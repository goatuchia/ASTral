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
        string owner, name;
        try
        {
            (owner, name) = ToolUtils.ResolveRepo(repo, store);
        }
        catch (ArgumentException ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }

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
