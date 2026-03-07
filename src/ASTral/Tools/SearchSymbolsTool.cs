using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ASTral.Models;
using ASTral.Storage;
using ModelContextProtocol.Server;

namespace ASTral.Tools;

/// <summary>
/// MCP tool that searches for symbols matching a query with weighted scoring,
/// supporting filters by kind, file pattern, and language.
/// </summary>
[McpServerToolType]
public static class SearchSymbolsTool
{
    [McpServerTool(Name = "search_symbols"), Description("Search for symbols matching a query.")]
    public static string SearchSymbols(
        IndexStore store,
        TokenTracker tracker,
        [Description("Repository identifier (owner/repo or repo name)")] string repo,
        [Description("Search query")] string query,
        [Description("Filter by symbol kind")] string? kind = null,
        [Description("Glob pattern to filter files")] string? filePattern = null,
        [Description("Filter by language")] string? language = null,
        [Description("Maximum results (1-100)")] int maxResults = 10)
    {
        var sw = Stopwatch.StartNew();
        maxResults = Math.Clamp(maxResults, 1, 100);

        var resolved = ToolUtils.ResolveRepoOrError(repo, store, out var resolveError);
        if (resolved is null) return resolveError!;
        var (owner, name) = resolved.Value;

        var index = store.LoadIndex(owner, name);
        if (index is null)
            return JsonSerializer.Serialize(new { error = $"Repository not indexed: {owner}/{name}" });

        // Search with weighted scoring
        var scoredSearch = index.SearchWithScores(query, kind, filePattern);

        // Post-filter by language
        if (language is not null)
        {
            scoredSearch = scoredSearch
                .Where(s => s.Sym.Language == language)
                .ToList();
        }

        // Build results and compute token savings in a single pass
        var scoredResults = new List<object>();
        var rawBytes = 0;
        var responseBytes = 0;
        var seenFiles = new HashSet<string>();
        var contentDir = store.GetContentDir(owner, name);

        foreach (var (score, sym) in scoredSearch.Take(maxResults))
        {
            scoredResults.Add(new
            {
                id = sym.Id,
                kind = sym.Kind,
                name = sym.Name,
                file = sym.File,
                line = sym.Line,
                signature = sym.Signature,
                summary = sym.Summary,
                score,
            });

            // Token savings accounting
            if (seenFiles.Add(sym.File))
            {
                try
                {
                    rawBytes += (int)new FileInfo(Path.Combine(contentDir, sym.File)).Length;
                }
                catch
                {
                    // Ignore missing or inaccessible files
                }
            }

            responseBytes += sym.ByteLength;
        }

        var tokensSaved = TokenTracker.EstimateSavings(rawBytes, responseBytes);
        var totalSaved = tracker.RecordSaving(tokensSaved);
        var costs = TokenTracker.CostAvoided(tokensSaved, totalSaved);

        sw.Stop();

        var response = new
        {
            repo = $"{owner}/{name}",
            query,
            result_count = scoredResults.Count,
            results = scoredResults,
            _meta = new
            {
                timing_ms = Math.Round(sw.Elapsed.TotalMilliseconds, 1),
                total_symbols = index.Symbols.Count,
                truncated = scoredSearch.Count > maxResults,
                tokens_saved = tokensSaved,
                total_tokens_saved = totalSaved,
                cost_avoided = costs["cost_avoided"],
                total_cost_avoided = costs["total_cost_avoided"],
            },
        };

        return JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = false,
        });
    }
}
