using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ASTral.Storage;
using ModelContextProtocol.Server;

namespace ASTral.Tools;

/// <summary>
/// MCP tool that returns a high-level overview of an indexed repository,
/// including directory file counts, symbol kind breakdown, and language statistics.
/// </summary>
[McpServerToolType]
public static class GetRepoOutlineTool
{
    [McpServerTool(Name = "get_repo_outline"), Description("Get a high-level overview of an indexed repository.")]
    public static string GetRepoOutline(
        IndexStore store,
        TokenTracker tracker,
        [Description("Repository identifier (owner/repo or repo name)")] string repo)
    {
        var sw = Stopwatch.StartNew();

        var resolved = ToolUtils.ResolveRepoOrError(repo, store, out var resolveError);
        if (resolved is null) return resolveError!;
        var (owner, name) = resolved.Value;

        var index = store.LoadIndex(owner, name);
        if (index is null)
            return JsonSerializer.Serialize(new { error = $"Repository not indexed: {owner}/{name}" });

        // Directory-level file counts
        var dirCounts = new Dictionary<string, int>();
        foreach (var file in index.SourceFiles)
        {
            var parts = file.Split('/');
            var dir = parts.Length > 1 ? parts[0] + "/" : "(root)";
            dirCounts[dir] = dirCounts.GetValueOrDefault(dir) + 1;
        }

        var sortedDirs = dirCounts
            .OrderByDescending(kv => kv.Value)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        // Symbol kind breakdown
        var kindCounts = new Dictionary<string, int>();
        foreach (var sym in index.Symbols)
        {
            var kind = sym.Kind;
            kindCounts[kind] = kindCounts.GetValueOrDefault(kind) + 1;
        }

        var sortedKinds = kindCounts
            .OrderByDescending(kv => kv.Value)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        // Token savings: sum of all raw file sizes
        var contentDir = store.GetContentDir(owner, name);

        var rawBytes = 0L;
        var contentDirFull = Path.GetFullPath(contentDir);
        foreach (var file in index.SourceFiles)
        {
            try
            {
                var fullPath = Path.GetFullPath(Path.Combine(contentDir, file));
                if (fullPath.StartsWith(contentDirFull, StringComparison.Ordinal)
                    && File.Exists(fullPath))
                {
                    rawBytes += new FileInfo(fullPath).Length;
                }
            }
            catch
            {
                // Skip inaccessible files
            }
        }

        var tokensSaved = TokenTracker.EstimateSavings((int)Math.Min(rawBytes, int.MaxValue), 0);
        var totalSaved = tracker.RecordSaving(tokensSaved);

        var meta = ToolUtils.BuildMeta(sw.Elapsed.TotalMilliseconds, tokensSaved, totalSaved);

        var result = new Dictionary<string, object>
        {
            ["repo"] = $"{owner}/{name}",
            ["indexed_at"] = index.IndexedAt,
            ["file_count"] = index.SourceFiles.Count,
            ["symbol_count"] = index.Symbols.Count,
            ["languages"] = index.Languages,
            ["directories"] = sortedDirs,
            ["symbol_kinds"] = sortedKinds,
            ["_meta"] = meta,
        };

        return JsonSerializer.Serialize(result);
    }
}
