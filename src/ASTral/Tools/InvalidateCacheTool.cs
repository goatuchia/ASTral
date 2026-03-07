using System.ComponentModel;
using System.Text.Json;
using ASTral.Storage;
using ModelContextProtocol.Server;

namespace ASTral.Tools;

/// <summary>
/// MCP tool that deletes the index and cached files for a repository.
/// </summary>
[McpServerToolType]
public static class InvalidateCacheTool
{
    [McpServerTool(Name = "invalidate_cache"), Description("Delete the index and cached files for a repository.")]
    public static string InvalidateCache(
        IndexStore store,
        [Description("Repository identifier (owner/repo or just repo name)")] string repo)
    {
        var resolved = ToolUtils.ResolveRepoOrError(repo, store, out var resolveError);
        if (resolved is null) return resolveError!;
        var (owner, name) = resolved.Value;

        var deleted = store.DeleteIndex(owner, name);

        return deleted
            ? JsonSerializer.Serialize(new
            {
                success = true,
                repo = $"{owner}/{name}",
                message = $"Index and cached files deleted for {owner}/{name}",
            })
            : JsonSerializer.Serialize(new
            {
                success = false,
                error = $"No index found for {owner}/{name}",
            });
    }
}
