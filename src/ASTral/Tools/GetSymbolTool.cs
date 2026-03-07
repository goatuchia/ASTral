using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ASTral.Models;
using ASTral.Storage;
using ModelContextProtocol.Server;

namespace ASTral.Tools;

/// <summary>
/// MCP tool that retrieves the full source code of a specific symbol by ID,
/// with optional context lines and content-hash verification.
/// </summary>
[McpServerToolType]
public static class GetSymbolTool
{
    [McpServerTool(Name = "get_symbol"), Description("Get the full source code of a specific symbol by its ID.")]
    public static string GetSymbol(
        IndexStore store,
        TokenTracker tracker,
        [Description("Repository identifier (owner/repo or just repo name)")] string repo,
        [Description("Symbol ID from get_file_outline or search_symbols")] string symbolId,
        [Description("If true, re-read source and verify content hash matches")] bool verify = false,
        [Description("Number of lines of context before/after the symbol (0-50)")] int contextLines = 0)
    {
        var sw = Stopwatch.StartNew();
        contextLines = Math.Clamp(contextLines, 0, 50);

        var resolved = ToolUtils.ResolveRepoOrError(repo, store, out var resolveError);
        if (resolved is null) return resolveError!;
        var (owner, name) = resolved.Value;

        var index = store.LoadIndex(owner, name);
        if (index is null)
            return JsonSerializer.Serialize(new { error = $"Repository not indexed: {owner}/{name}" });

        var symbol = index.GetSymbol(symbolId);
        if (symbol is null)
            return JsonSerializer.Serialize(new { error = $"Symbol not found: {symbolId}" });

        // Get source via byte-offset read
        var source = store.GetSymbolContent(owner, name, symbolId);

        // Context lines
        string contextBefore = "";
        string contextAfter = "";

        if (contextLines > 0 && source is not null)
        {
            var contentDir = store.GetContentDir(owner, name);
            var filePath = Path.Combine(contentDir, symbol.File);

            if (File.Exists(filePath))
            {
                try
                {
                    var allLines = File.ReadAllText(filePath, Encoding.UTF8).Split('\n');
                    var startLine = symbol.Line - 1;   // 0-indexed
                    var endLine = symbol.EndLine;      // exclusive

                    var beforeStart = Math.Max(0, startLine - contextLines);
                    var afterEnd = Math.Min(allLines.Length, endLine + contextLines);

                    if (beforeStart < startLine)
                        contextBefore = string.Join("\n", allLines[beforeStart..startLine]);
                    if (endLine < afterEnd)
                        contextAfter = string.Join("\n", allLines[endLine..afterEnd]);
                }
                catch
                {
                    // Ignore context extraction failures
                }
            }
        }

        // Build meta
        var meta = new Dictionary<string, object>();

        // Verify content hash
        if (verify && source is not null)
        {
            var actualHash = Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(source)));
            var storedHash = symbol.ContentHash;

            meta["content_verified"] = string.IsNullOrEmpty(storedHash)
                ? (object)false
                : actualHash == storedHash;
        }

        // Token savings: raw file size vs symbol byte length
        var rawBytes = 0;
        try
        {
            var rawFilePath = Path.Combine(store.GetContentDir(owner, name), symbol.File);
            if (File.Exists(rawFilePath))
                rawBytes = (int)new FileInfo(rawFilePath).Length;
        }
        catch
        {
            // Ignore
        }

        var byteLength = symbol.ByteLength;
        var tokensSaved = TokenTracker.EstimateSavings(rawBytes, byteLength);
        var totalSaved = tracker.RecordSaving(tokensSaved);

        sw.Stop();
        var baseMeta = ToolUtils.BuildMeta(sw.Elapsed.TotalMilliseconds, tokensSaved, totalSaved);
        foreach (var (k, v) in baseMeta)
            meta[k] = v;

        // Build result
        var result = new Dictionary<string, object>
        {
            ["id"] = symbol.Id,
            ["kind"] = symbol.Kind,
            ["name"] = symbol.Name,
            ["file"] = symbol.File,
            ["line"] = symbol.Line,
            ["end_line"] = symbol.EndLine,
            ["signature"] = symbol.Signature,
            ["decorators"] = symbol.Decorators,
            ["docstring"] = symbol.Docstring,
            ["content_hash"] = symbol.ContentHash,
            ["source"] = source ?? "",
            ["_meta"] = meta,
        };

        if (!string.IsNullOrEmpty(contextBefore))
            result["context_before"] = contextBefore;
        if (!string.IsNullOrEmpty(contextAfter))
            result["context_after"] = contextAfter;

        return JsonSerializer.Serialize(result);
    }

}
