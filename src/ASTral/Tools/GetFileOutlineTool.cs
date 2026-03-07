using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ASTral.Models;
using ASTral.Storage;
using ModelContextProtocol.Server;

namespace ASTral.Tools;

/// <summary>
/// MCP tool that returns a hierarchical outline of all symbols in a file,
/// with signatures, summaries, and parent-child relationships.
/// </summary>
[McpServerToolType]
public static class GetFileOutlineTool
{
    [McpServerTool(Name = "get_file_outline"),
     Description("Get all symbols in a file with signatures and summaries.")]
    public static string GetFileOutline(
        IndexStore store,
        TokenTracker tracker,
        [Description("Repository identifier (owner/repo or just repo name)")]
        string repo,
        [Description("Path to file within the repository")]
        string filePath)
    {
        var sw = Stopwatch.StartNew();

        var resolved = ToolUtils.ResolveRepoOrError(repo, store, out var resolveError);
        if (resolved is null) return resolveError!;
        var (owner, name) = resolved.Value;

        // Load index
        var index = store.LoadIndex(owner, name);
        if (index is null)
        {
            return JsonSerializer.Serialize(new { error = $"Repository not indexed: {owner}/{name}" });
        }

        // Filter symbols to this file
        var fileSymbols = index.Symbols
            .Where(s => s.File == filePath)
            .ToList();

        if (fileSymbols.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                repo = $"{owner}/{name}",
                file = filePath,
                language = "",
                symbols = Array.Empty<object>(),
            });
        }

        // Build hierarchical tree
        var tree = SymbolNode.BuildTree(fileSymbols);

        // Convert tree to output format
        var symbolsOutput = tree.Select(NodeToDict).ToList();

        // Get language from first symbol
        var language = fileSymbols[0].Language;

        sw.Stop();
        var elapsedMs = Math.Round(sw.Elapsed.TotalMilliseconds, 1);

        // Token savings: raw file size vs outline response size
        var rawBytes = 0;
        try
        {
            var rawFile = Path.Combine(store.GetContentDir(owner, name), filePath);
            if (File.Exists(rawFile))
                rawBytes = (int)new FileInfo(rawFile).Length;
        }
        catch
        {
            // Ignore file access errors
        }

        var responseBytes = fileSymbols.Sum(s => s.ByteLength);

        var tokensSaved = TokenTracker.EstimateSavings(rawBytes, responseBytes);
        var totalSaved = tracker.RecordSaving(tokensSaved);

        var fileSummary = index.FileSummaries.GetValueOrDefault(filePath, "");

        var meta = ToolUtils.BuildMeta(elapsedMs, tokensSaved, totalSaved);
        meta["symbol_count"] = symbolsOutput.Count;

        var result = new Dictionary<string, object>
        {
            ["repo"] = $"{owner}/{name}",
            ["file"] = filePath,
            ["language"] = language,
            ["file_summary"] = fileSummary,
            ["symbols"] = symbolsOutput,
            ["_meta"] = meta,
        };

        return JsonSerializer.Serialize(result);
    }

    private static Dictionary<string, object> NodeToDict(SymbolNode node)
    {
        var result = new Dictionary<string, object>
        {
            ["id"] = node.Symbol.Id,
            ["kind"] = node.Symbol.Kind,
            ["name"] = node.Symbol.Name,
            ["signature"] = node.Symbol.Signature,
            ["summary"] = node.Symbol.Summary,
            ["line"] = node.Symbol.Line,
        };

        if (node.Children.Count > 0)
        {
            result["children"] = node.Children.Select(NodeToDict).ToList();
        }

        return result;
    }

}
