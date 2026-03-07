using System.Text.Json;
using ASTral.Models;
using ASTral.Storage;

namespace ASTral.Tools;

/// <summary>
/// Shared helpers for MCP tool modules: repository resolution,
/// file skip patterns, priority sorting, and JSON serialization.
/// </summary>
internal static class ToolUtils
{
    internal static readonly string[] SkipPatterns =
    [
        "node_modules/", "vendor/", "venv/", ".venv/", "__pycache__/",
        "dist/", "build/", ".git/", ".tox/", ".mypy_cache/",
        "target/", ".gradle/",
        "test_data/", "testdata/", "fixtures/", "snapshots/",
        "migrations/",
        ".min.js", ".min.ts", ".bundle.js",
        "package-lock.json", "yarn.lock", "go.sum",
        "generated/", "proto/",
    ];

    internal static readonly string[] PriorityDirs =
        ["src/", "lib/", "pkg/", "cmd/", "internal/"];

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>
    /// Parse "owner/repo" or look up a single repo name.
    /// Returns (Owner, Name).
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the repository is not found.</exception>
    public static (string Owner, string Name) ResolveRepo(string repo, IndexStore store)
    {
        if (repo.Contains('/'))
        {
            var parts = repo.Split('/', 2);
            return (parts[0], parts[1]);
        }

        var repos = store.ListRepos();
        var matching = repos
            .Where(r => r.TryGetValue("repo", out var repoVal)
                        && repoVal is string repoStr
                        && repoStr.EndsWith($"/{repo}", StringComparison.Ordinal))
            .ToList();

        if (matching.Count == 0)
            throw new ArgumentException($"Repository not found: {repo}");

        var fullRepo = (string)matching[0]["repo"];
        var repoParts = fullRepo.Split('/', 2);
        return (repoParts[0], repoParts[1]);
    }

    internal static bool ShouldSkipFile(string path)
    {
        var normalized = path.Replace('\\', '/');
        foreach (var pattern in SkipPatterns)
        {
            if (pattern.EndsWith('/'))
            {
                if (normalized.StartsWith(pattern, StringComparison.Ordinal)
                    || normalized.Contains("/" + pattern, StringComparison.Ordinal))
                    return true;
            }
            else
            {
                if (normalized.Contains(pattern, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    internal static (int Priority, int Depth, string Path) PriorityKey(string path)
    {
        for (var i = 0; i < PriorityDirs.Length; i++)
        {
            if (path.StartsWith(PriorityDirs[i], StringComparison.Ordinal))
                return (i, path.Count(c => c == '/'), path);
        }

        return (PriorityDirs.Length, path.Count(c => c == '/'), path);
    }

    internal static Dictionary<string, List<Symbol>> GroupSymbolsByFile(List<Symbol> symbols)
    {
        var map = new Dictionary<string, List<Symbol>>();
        foreach (var s in symbols)
        {
            if (!map.TryGetValue(s.File, out var list))
            {
                list = [];
                map[s.File] = list;
            }
            list.Add(s);
        }
        return map;
    }

    /// <summary>
    /// Try to resolve a repo identifier, returning an error JSON string on failure.
    /// </summary>
    internal static (string Owner, string Name)? ResolveRepoOrError(
        string repo, IndexStore store, out string? errorJson)
    {
        try
        {
            var resolved = ResolveRepo(repo, store);
            errorJson = null;
            return resolved;
        }
        catch (ArgumentException ex)
        {
            errorJson = JsonSerializer.Serialize(new { error = ex.Message });
            return null;
        }
    }

    /// <summary>
    /// Build a standard _meta dictionary with timing, token savings, and cost avoided.
    /// </summary>
    internal static Dictionary<string, object> BuildMeta(
        double timingMs, int tokensSaved, int totalSaved)
    {
        var meta = new Dictionary<string, object>
        {
            ["timing_ms"] = Math.Round(timingMs, 1),
            ["tokens_saved"] = tokensSaved,
            ["total_tokens_saved"] = totalSaved,
        };

        var costAvoided = TokenTracker.CostAvoided(tokensSaved, totalSaved);
        foreach (var (key, value) in costAvoided)
            meta[key] = value;

        return meta;
    }

    internal static string Serialize(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }
}
