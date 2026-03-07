using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ASTral.Storage;
using ASTral.Utils;
using ModelContextProtocol.Server;

namespace ASTral.Tools;

/// <summary>
/// MCP tool that performs full-text search across indexed file contents
/// with case-insensitive substring matching and optional glob filtering.
/// </summary>
[McpServerToolType]
public static class SearchTextTool
{
    [McpServerTool(Name = "search_text"), Description("Full-text search across indexed file contents.")]
    public static string SearchText(
        IndexStore store,
        TokenTracker tracker,
        [Description("Repository identifier (owner/repo or just repo name)")] string repo,
        [Description("Text to search for (case-insensitive substring match)")] string query,
        [Description("Glob pattern to filter files")] string? filePattern = null,
        [Description("Maximum number of matching lines to return (1-100)")] int maxResults = 20)
    {
        var sw = Stopwatch.StartNew();
        maxResults = Math.Clamp(maxResults, 1, 100);

        var resolved = ToolUtils.ResolveRepoOrError(repo, store, out var resolveError);
        if (resolved is null) return resolveError!;
        var (owner, name) = resolved.Value;

        var index = store.LoadIndex(owner, name);
        if (index is null)
            return JsonSerializer.Serialize(new { error = $"Repository not indexed: {owner}/{name}" });

        // Filter files by glob pattern
        var files = index.SourceFiles;
        if (filePattern is not null)
        {
            files = files
                .Where(f => GlobMatcher.MatchesSimpleExpression(filePattern, f, ignoreCase: true)
                         || GlobMatcher.MatchesSimpleExpression($"*/{filePattern}", f, ignoreCase: true))
                .ToList();
        }

        var contentDir = store.GetContentDir(owner, name);
        var matches = new List<Dictionary<string, object>>();
        var filesSearched = 0;

        foreach (var filePath in files)
        {
            var fullPath = Path.Combine(contentDir, filePath);
            if (!File.Exists(fullPath))
                continue;

            string content;
            try
            {
                content = File.ReadAllText(fullPath, Encoding.UTF8);
            }
            catch
            {
                continue;
            }

            filesSearched++;
            var lines = content.Split('\n');

            for (var lineNum = 0; lineNum < lines.Length; lineNum++)
            {
                if (lines[lineNum].Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    var text = lines[lineNum].TrimEnd();
                    if (text.Length > 200)
                        text = text[..200];

                    matches.Add(new Dictionary<string, object>
                    {
                        ["file"] = filePath,
                        ["line"] = lineNum + 1,
                        ["text"] = text,
                    });

                    if (matches.Count >= maxResults)
                        break;
                }
            }

            if (matches.Count >= maxResults)
                break;
        }

        // Token savings: raw bytes of searched files vs matched lines returned
        var rawBytes = 0;
        foreach (var filePath in files.Take(filesSearched))
        {
            try
            {
                var fullPath = Path.Combine(contentDir, filePath);
                rawBytes += (int)new FileInfo(fullPath).Length;
            }
            catch
            {
                // Ignore
            }
        }

        var responseBytes = matches.Sum(m => Encoding.UTF8.GetByteCount((string)m["text"]));
        var tokensSaved = TokenTracker.EstimateSavings(rawBytes, responseBytes);
        var totalSaved = tracker.RecordSaving(tokensSaved);

        sw.Stop();

        var meta = ToolUtils.BuildMeta(sw.Elapsed.TotalMilliseconds, tokensSaved, totalSaved);
        meta["files_searched"] = filesSearched;
        meta["truncated"] = matches.Count >= maxResults;

        var result = new Dictionary<string, object>
        {
            ["repo"] = $"{owner}/{name}",
            ["query"] = query,
            ["result_count"] = matches.Count,
            ["results"] = matches,
            ["_meta"] = meta,
        };

        return JsonSerializer.Serialize(result);
    }
}
