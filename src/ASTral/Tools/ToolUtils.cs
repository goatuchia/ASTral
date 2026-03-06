using System.Text.Json;
using ASTral.Models;
using ASTral.Storage;

namespace ASTral.Tools;

/// <summary>
/// Shared helpers for tool modules.
/// Port of Python tools/_utils.py.
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

    /// <summary>
    /// Compute the content directory path for a repository.
    /// Mirrors IndexStore.ContentDir: basePath/{owner}-{name}
    /// </summary>
    public static string GetContentDir(string? storagePath, string owner, string name)
    {
        var basePath = storagePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".code-index");

        return Path.Combine(basePath, $"{owner}-{name}");
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

    internal static string Serialize(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }
}
