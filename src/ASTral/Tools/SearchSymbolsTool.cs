using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ASTral.Models;
using ASTral.Storage;
using ModelContextProtocol.Server;

namespace ASTral.Tools;

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

        string owner, name;
        try
        {
            (owner, name) = ToolUtils.ResolveRepo(repo, store);
        }
        catch (ArgumentException ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }

        var index = store.LoadIndex(owner, name);
        if (index is null)
            return JsonSerializer.Serialize(new { error = $"Repository not indexed: {owner}/{name}" });

        // Search with weighted scoring
        var results = index.Search(query, kind, filePattern);

        // Post-filter by language
        if (language is not null)
        {
            results = results
                .Where(s => CodeIndex.GetStringValue(s, "language") == language)
                .ToList();
        }

        // Build scored results and compute token savings in a single pass
        var queryLower = query.ToLowerInvariant();
        var queryWords = new HashSet<string>(
            queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        var scoredResults = new List<object>();
        var rawBytes = 0;
        var responseBytes = 0;
        var seenFiles = new HashSet<string>();
        var contentDir = store.GetContentDir(owner, name);

        foreach (var sym in results.Take(maxResults))
        {
            var score = CodeIndex.ScoreSymbol(sym, queryLower, queryWords);
            scoredResults.Add(new
            {
                id = CodeIndex.GetStringValue(sym, "id"),
                kind = CodeIndex.GetStringValue(sym, "kind"),
                name = CodeIndex.GetStringValue(sym, "name"),
                file = CodeIndex.GetStringValue(sym, "file"),
                line = CodeIndex.GetIntValue(sym, "line"),
                signature = CodeIndex.GetStringValue(sym, "signature"),
                summary = CodeIndex.GetStringValue(sym, "summary"),
                score,
            });

            // Token savings accounting
            var file = CodeIndex.GetStringValue(sym, "file");
            if (seenFiles.Add(file))
            {
                try
                {
                    rawBytes += (int)new FileInfo(Path.Combine(contentDir, file)).Length;
                }
                catch
                {
                    // Ignore missing or inaccessible files
                }
            }

            responseBytes += CodeIndex.GetIntValue(sym, "byte_length");
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
                truncated = results.Count > maxResults,
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
