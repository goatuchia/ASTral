using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ASTral.Models;
using ASTral.Storage;
using ModelContextProtocol.Server;

namespace ASTral.Tools;

/// <summary>
/// MCP tool that retrieves the full source code of multiple symbols in a single call.
/// </summary>
[McpServerToolType]
public static class GetSymbolsTool
{
    [McpServerTool(Name = "get_symbols"), Description("Get the full source code of multiple symbols by their IDs.")]
    public static string GetSymbols(
        IndexStore store,
        TokenTracker tracker,
        [Description("Repository identifier (owner/repo or just repo name)")] string repo,
        [Description("List of symbol IDs")] string[] symbolIds)
    {
        var sw = Stopwatch.StartNew();

        var resolved = ToolUtils.ResolveRepoOrError(repo, store, out var resolveError);
        if (resolved is null) return resolveError!;
        var (owner, name) = resolved.Value;

        var index = store.LoadIndex(owner, name);
        if (index is null)
            return JsonSerializer.Serialize(new { error = $"Repository not indexed: {owner}/{name}" });

        var symbols = new List<Dictionary<string, object>>();
        var errors = new List<Dictionary<string, string>>();
        var resolvedSymbols = new List<(string Id, Symbol Sym)>();

        foreach (var symbolId in symbolIds)
        {
            var symbol = index.GetSymbol(symbolId);
            if (symbol is null)
            {
                errors.Add(new Dictionary<string, string>
                {
                    ["id"] = symbolId,
                    ["error"] = $"Symbol not found: {symbolId}",
                });
                continue;
            }

            resolvedSymbols.Add((symbolId, symbol));

            var source = store.GetSymbolContent(index, owner, name, symbolId);

            symbols.Add(new Dictionary<string, object>
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
            });
        }

        // Token savings: unique file sizes vs sum of symbol byte_lengths
        var rawBytes = 0;
        var seenFiles = new HashSet<string>();
        var responseBytes = 0;

        foreach (var (_, symbol) in resolvedSymbols)
        {
            if (seenFiles.Add(symbol.File))
            {
                try
                {
                    var filePath = Path.Combine(store.GetContentDir(owner, name), symbol.File);
                    if (File.Exists(filePath))
                        rawBytes += (int)new FileInfo(filePath).Length;
                }
                catch
                {
                    // Ignore
                }
            }

            responseBytes += symbol.ByteLength;
        }

        var tokensSaved = TokenTracker.EstimateSavings(rawBytes, responseBytes);
        var totalSaved = tracker.RecordSaving(tokensSaved);

        sw.Stop();

        var meta = ToolUtils.BuildMeta(sw.Elapsed.TotalMilliseconds, tokensSaved, totalSaved);
        meta["symbol_count"] = symbols.Count;

        var result = new Dictionary<string, object>
        {
            ["symbols"] = symbols,
            ["errors"] = errors,
            ["_meta"] = meta,
        };

        return JsonSerializer.Serialize(result);
    }

}
