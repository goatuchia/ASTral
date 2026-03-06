using System.ComponentModel;
using System.Text;
using System.Text.Json;
using MAB.DotIgnore;
using ASTral.Models;
using ASTral.Parser;
using ASTral.Security;
using ASTral.Storage;
using ASTral.Summarizer;
using ModelContextProtocol.Server;

namespace ASTral.Tools;

/// <summary>
/// MCP tool that indexes a local folder containing source code.
/// Port of Python tools/index_folder.py.
/// </summary>
[McpServerToolType]
public static class IndexFolderTool
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>File/directory patterns to skip (synced with index_repo).</summary>
    private static readonly string[] SkipPatterns =
    [
        "node_modules/", "vendor/", "venv/", ".venv/", "__pycache__/",
        "dist/", "build/", ".git/", ".tox/", ".mypy_cache/",
        "target/",
        ".gradle/",
        "test_data/", "testdata/", "fixtures/", "snapshots/",
        "migrations/",
        ".min.js", ".min.ts", ".bundle.js",
        "package-lock.json", "yarn.lock", "go.sum",
        "generated/", "proto/",
    ];

    /// <summary>Priority directories for file-limit sorting.</summary>
    private static readonly string[] PriorityDirs = ["src/", "lib/", "pkg/", "cmd/", "internal/"];

    [McpServerTool(Name = "index_folder"), Description("Index a local folder containing source code.")]
    public static async Task<string> IndexFolder(
        IndexStore store,
        SymbolExtractor extractor,
        BatchSummarizer summarizer,
        [Description("Path to local folder")] string path,
        [Description("Use AI for symbol summaries")] bool useAiSummaries = true,
        [Description("Additional gitignore-style patterns to exclude")] string[]? extraIgnorePatterns = null,
        [Description("Whether to follow symlinks")] bool followSymlinks = false,
        [Description("Only re-index changed files")] bool incremental = true)
    {
        // Resolve folder path (support ~ expansion)
        var folderPath = Path.GetFullPath(
            path.StartsWith('~')
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[1..].TrimStart('/'))
                : path);

        if (!Directory.Exists(folderPath))
            return JsonSerializer.Serialize(new { success = false, error = $"Folder not found: {path}" }, JsonOptions);

        var warnings = new List<string>();
        var maxFiles = SecurityValidator.GetMaxIndexFiles();

        try
        {
            // Discover source files with security filtering
            var (sourceFiles, discoverWarnings, skipCounts) = DiscoverLocalFiles(
                folderPath, maxFiles, SecurityValidator.DefaultMaxFileSize, extraIgnorePatterns, followSymlinks);

            warnings.AddRange(discoverWarnings);

            if (sourceFiles.Count == 0)
                return JsonSerializer.Serialize(new { success = false, error = "No source files found" }, JsonOptions);

            // Create repo identifier from folder name
            var repoName = new DirectoryInfo(folderPath).Name;
            const string owner = "local";

            // Read all files to build current_files map
            var currentFiles = new Dictionary<string, string>();
            foreach (var filePath in sourceFiles)
            {
                if (!SecurityValidator.ValidatePath(folderPath, filePath))
                    continue;

                string content;
                try
                {
                    content = File.ReadAllText(filePath);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to read {filePath}: {ex.Message}");
                    continue;
                }

                string relPath;
                try
                {
                    relPath = Path.GetRelativePath(folderPath, filePath).Replace('\\', '/');
                }
                catch
                {
                    continue;
                }

                var ext = Path.GetExtension(filePath);
                if (!LanguageRegistry.LanguageExtensions.ContainsKey(ext))
                    continue;

                currentFiles[relPath] = content;
            }

            // Incremental path
            if (incremental && store.LoadIndex(owner, repoName) is not null)
            {
                return await HandleIncremental(store, extractor, summarizer, owner, repoName,
                    folderPath, currentFiles, skipCounts, warnings, useAiSummaries);
            }

            // Full index path
            return await HandleFullIndex(store, extractor, summarizer, owner, repoName,
                folderPath, currentFiles, skipCounts, warnings, maxFiles, useAiSummaries);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { success = false, error = $"Indexing failed: {ex.Message}" }, JsonOptions);
        }
    }

    // ---- Private: incremental index ----

    private static async Task<string> HandleIncremental(
        IndexStore store,
        SymbolExtractor extractor,
        BatchSummarizer summarizer,
        string owner,
        string repoName,
        string folderPath,
        Dictionary<string, string> currentFiles,
        Dictionary<string, int> skipCounts,
        List<string> warnings,
        bool useAiSummaries)
    {
        var (changed, newFiles, deleted) = store.DetectChanges(owner, repoName, currentFiles);

        if (changed.Count == 0 && newFiles.Count == 0 && deleted.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                success = true,
                message = "No changes detected",
                repo = $"{owner}/{repoName}",
                folder_path = folderPath,
                changed = 0,
                @new = 0,
                deleted = 0,
            }, JsonOptions);
        }

        var filesToParse = new HashSet<string>(changed);
        filesToParse.UnionWith(newFiles);

        var newSymbols = new List<Symbol>();
        var rawFilesSubset = new Dictionary<string, string>();
        var incrementalNoSymbols = new List<string>();

        foreach (var relPath in filesToParse)
        {
            var content = currentFiles[relPath];
            rawFilesSubset[relPath] = content;

            var language = LanguageRegistry.GetLanguageForFile(relPath);
            if (language is null)
                continue;

            try
            {
                var symbols = extractor.ExtractSymbols(content, relPath, language);
                if (symbols.Count > 0)
                    newSymbols.AddRange(symbols);
                else
                    incrementalNoSymbols.Add(relPath);
            }
            catch (Exception ex)
            {
                warnings.Add($"Failed to parse {relPath}: {ex.Message}");
            }
        }

        newSymbols = await summarizer.SummarizeSymbols(newSymbols, useAiSummaries);

        // Generate file summaries for changed/new files
        var incrFileSummaries = FileSummarizer.GenerateFileSummaries(GroupSymbolsByFile(newSymbols));

        var gitHead = IndexStore.GetGitHead(folderPath) ?? "";

        var updated = store.IncrementalSave(
            owner: owner,
            name: repoName,
            changedFiles: changed,
            newFiles: newFiles,
            deletedFiles: deleted,
            newSymbols: newSymbols,
            rawFiles: rawFilesSubset,
            languages: new Dictionary<string, int>(),
            gitHead: gitHead,
            fileSummaries: incrFileSummaries);

        var result = new Dictionary<string, object?>
        {
            ["success"] = true,
            ["repo"] = $"{owner}/{repoName}",
            ["folder_path"] = folderPath,
            ["incremental"] = true,
            ["changed"] = changed.Count,
            ["new"] = newFiles.Count,
            ["deleted"] = deleted.Count,
            ["symbol_count"] = updated?.Symbols.Count ?? 0,
            ["indexed_at"] = updated?.IndexedAt ?? "",
            ["discovery_skip_counts"] = skipCounts,
            ["no_symbols_count"] = incrementalNoSymbols.Count,
            ["no_symbols_files"] = incrementalNoSymbols.Take(50).ToList(),
        };

        if (warnings.Count > 0)
            result["warnings"] = warnings;

        return JsonSerializer.Serialize(result, JsonOptions);
    }

    // ---- Private: full index ----

    private static async Task<string> HandleFullIndex(
        IndexStore store,
        SymbolExtractor extractor,
        BatchSummarizer summarizer,
        string owner,
        string repoName,
        string folderPath,
        Dictionary<string, string> currentFiles,
        Dictionary<string, int> skipCounts,
        List<string> warnings,
        int maxFiles,
        bool useAiSummaries)
    {
        var allSymbols = new List<Symbol>();
        var languages = new Dictionary<string, int>();
        var rawFiles = new Dictionary<string, string>();
        var parsedFiles = new List<string>();
        var noSymbolsFiles = new List<string>();

        foreach (var (relPath, content) in currentFiles)
        {
            var language = LanguageRegistry.GetLanguageForFile(relPath);
            if (language is null)
                continue;

            try
            {
                var symbols = extractor.ExtractSymbols(content, relPath, language);
                if (symbols.Count > 0)
                {
                    allSymbols.AddRange(symbols);
                    var fileLanguage = symbols[0].Language ?? language;
                    languages[fileLanguage] = languages.GetValueOrDefault(fileLanguage) + 1;
                    rawFiles[relPath] = content;
                    parsedFiles.Add(relPath);
                }
                else
                {
                    noSymbolsFiles.Add(relPath);
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Failed to parse {relPath}: {ex.Message}");
            }
        }

        if (allSymbols.Count == 0)
            return JsonSerializer.Serialize(new { success = false, error = "No symbols extracted from files" }, JsonOptions);

        // Generate summaries
        allSymbols = await summarizer.SummarizeSymbols(allSymbols, useAiSummaries);

        // Generate file-level summaries
        var fileSummaries = FileSummarizer.GenerateFileSummaries(GroupSymbolsByFile(allSymbols));

        // Compute file hashes for all current files (including no-symbol files)
        var fileHashes = currentFiles.ToDictionary(
            kv => kv.Key,
            kv => Symbol.ComputeContentHash(Encoding.UTF8.GetBytes(kv.Value)));

        store.SaveIndex(
            owner: owner,
            name: repoName,
            sourceFiles: parsedFiles,
            symbols: allSymbols,
            rawFiles: rawFiles,
            languages: languages,
            fileHashes: fileHashes,
            fileSummaries: fileSummaries);

        var savedIndex = store.LoadIndex(owner, repoName);

        var result = new Dictionary<string, object?>
        {
            ["success"] = true,
            ["repo"] = $"{owner}/{repoName}",
            ["folder_path"] = folderPath,
            ["indexed_at"] = savedIndex?.IndexedAt ?? "",
            ["file_count"] = parsedFiles.Count,
            ["symbol_count"] = allSymbols.Count,
            ["file_summary_count"] = fileSummaries.Values.Count(v => !string.IsNullOrEmpty(v)),
            ["languages"] = languages,
            ["files"] = parsedFiles.Take(20).ToList(),
            ["discovery_skip_counts"] = skipCounts,
            ["no_symbols_count"] = noSymbolsFiles.Count,
            ["no_symbols_files"] = noSymbolsFiles.Take(50).ToList(),
        };

        if (warnings.Count > 0)
            result["warnings"] = warnings;

        if (skipCounts.GetValueOrDefault("file_limit") > 0)
            result["note"] = $"Folder has many files; indexed first {maxFiles}";

        return JsonSerializer.Serialize(result, JsonOptions);
    }

    // ---- Private: file discovery ----

    /// <summary>
    /// Discover source files in a local folder with security filtering.
    /// </summary>
    private static (List<string> Files, List<string> Warnings, Dictionary<string, int> SkipCounts) DiscoverLocalFiles(
        string folderPath,
        int maxFiles,
        int maxSize,
        string[]? extraIgnorePatterns,
        bool followSymlinks)
    {
        var files = new List<string>();
        var warnings = new List<string>();
        var root = Path.GetFullPath(folderPath);

        var skipCounts = new Dictionary<string, int>
        {
            ["symlink"] = 0,
            ["symlink_escape"] = 0,
            ["path_traversal"] = 0,
            ["skip_pattern"] = 0,
            ["gitignore"] = 0,
            ["extra_ignore"] = 0,
            ["secret"] = 0,
            ["wrong_extension"] = 0,
            ["too_large"] = 0,
            ["unreadable"] = 0,
            ["binary"] = 0,
            ["file_limit"] = 0,
        };

        // Load .gitignore
        var gitignoreList = LoadGitignore(root);

        // Build extra ignore list if provided
        IgnoreList? extraIgnoreList = null;
        if (extraIgnorePatterns is { Length: > 0 })
        {
            try
            {
                extraIgnoreList = new IgnoreList(extraIgnorePatterns);
            }
            catch
            {
                // Ignore invalid patterns
            }
        }

        foreach (var filePath in Directory.EnumerateFiles(root, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System,
        }))
        {
            var fileInfo = new FileInfo(filePath);

            // Symlink protection
            if (!followSymlinks && fileInfo.LinkTarget is not null)
            {
                skipCounts["symlink"]++;
                continue;
            }

            if (fileInfo.LinkTarget is not null && !SecurityValidator.ValidateSymlinks(filePath, root))
            {
                skipCounts["symlink_escape"]++;
                warnings.Add($"Skipped symlink escape: {filePath}");
                continue;
            }

            // Path traversal check
            if (!SecurityValidator.ValidatePath(root, filePath))
            {
                skipCounts["path_traversal"]++;
                warnings.Add($"Skipped path traversal: {filePath}");
                continue;
            }

            // Get relative path for pattern matching
            string relPath;
            try
            {
                relPath = Path.GetRelativePath(root, filePath).Replace('\\', '/');
            }
            catch
            {
                skipCounts["path_traversal"]++;
                continue;
            }

            // Skip patterns
            if (ShouldSkipFile(relPath))
            {
                skipCounts["skip_pattern"]++;
                continue;
            }

            // .gitignore matching
            if (gitignoreList is not null && gitignoreList.IsIgnored(relPath, pathIsDirectory: false))
            {
                skipCounts["gitignore"]++;
                continue;
            }

            // Extra ignore patterns
            if (extraIgnoreList is not null && extraIgnoreList.IsIgnored(relPath, pathIsDirectory: false))
            {
                skipCounts["extra_ignore"]++;
                continue;
            }

            // Secret detection
            if (SecurityValidator.IsSecretFile(relPath))
            {
                skipCounts["secret"]++;
                warnings.Add($"Skipped secret file: {relPath}");
                continue;
            }

            // Extension filter
            var ext = Path.GetExtension(filePath);
            if (!LanguageRegistry.LanguageExtensions.ContainsKey(ext))
            {
                skipCounts["wrong_extension"]++;
                continue;
            }

            // Size limit
            try
            {
                if (fileInfo.Length > maxSize)
                {
                    skipCounts["too_large"]++;
                    continue;
                }
            }
            catch
            {
                skipCounts["unreadable"]++;
                continue;
            }

            // Binary detection (content sniffing)
            if (SecurityValidator.IsBinaryFile(filePath))
            {
                skipCounts["binary"]++;
                warnings.Add($"Skipped binary file: {relPath}");
                continue;
            }

            files.Add(filePath);
        }

        // File count limit with prioritization
        if (files.Count > maxFiles)
        {
            skipCounts["file_limit"] = files.Count - maxFiles;

            // Pre-compute relative paths and priority keys to avoid redundant work during sort
            var keyed = files
                .Select(f => (File: f, Key: PriorityKey(Path.GetRelativePath(root, f).Replace('\\', '/'))))
                .OrderBy(x => x.Key)
                .Select(x => x.File)
                .Take(maxFiles)
                .ToList();

            files = keyed;
        }

        return (files, warnings, skipCounts);
    }

    /// <summary>
    /// Check if a file path matches any skip pattern.
    /// </summary>
    private static bool ShouldSkipFile(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        foreach (var pattern in SkipPatterns)
        {
            if (pattern.EndsWith('/'))
            {
                // Directory pattern: match only complete path segments
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

    /// <summary>
    /// Load .gitignore from the folder root if it exists.
    /// </summary>
    private static IgnoreList? LoadGitignore(string folderPath)
    {
        try
        {
            return new IgnoreList(Path.Combine(folderPath, ".gitignore"));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Group symbols by their source file path.
    /// </summary>
    private static Dictionary<string, List<Symbol>> GroupSymbolsByFile(List<Symbol> symbols)
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
    /// Priority key for file-limit sorting. Lower = higher priority.
    /// </summary>
    private static (int DirPriority, int Depth, string Path) PriorityKey(string relPath)
    {
        for (var i = 0; i < PriorityDirs.Length; i++)
        {
            if (relPath.StartsWith(PriorityDirs[i], StringComparison.Ordinal))
                return (i, relPath.Count(c => c == '/'), relPath);
        }

        return (PriorityDirs.Length, relPath.Count(c => c == '/'), relPath);
    }
}
