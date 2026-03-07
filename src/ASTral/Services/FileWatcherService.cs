using System.Collections.Concurrent;
using System.Threading.Channels;
using ASTral.Parser;
using ASTral.Storage;
using ASTral.Summarizer;
using ASTral.Tools;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ASTral.Services;

public sealed class FileWatcherService : BackgroundService
{
    private readonly IndexStore _store;
    private readonly SymbolExtractor _extractor;
    private readonly BatchSummarizer _summarizer;
    private readonly ILogger<FileWatcherService> _logger;
    private readonly Channel<string> _changeChannel;
    private readonly SemaphoreSlim _indexLock = new(1, 1);
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new();

    /// <summary>
    /// Static registry mapping "owner/repoName" to absolute folder paths.
    /// Called by IndexFolderTool after indexing a local folder.
    /// </summary>
    private static readonly ConcurrentDictionary<string, string> FolderRegistry = new();

    public static void RegisterFolder(string repoKey, string folderPath)
        => FolderRegistry[repoKey] = folderPath;

    public FileWatcherService(
        IndexStore store,
        SymbolExtractor extractor,
        BatchSummarizer summarizer,
        ILogger<FileWatcherService> logger)
    {
        _store = store;
        _extractor = extractor;
        _summarizer = summarizer;
        _logger = logger;
        _changeChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("ASTRAL_WATCH"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("ASTRAL_WATCH is not set to true; file watcher disabled");
            return;
        }

        _logger.LogInformation("File watcher service starting");

        // Discover local repos and set up watchers
        SetupWatchers();

        // Process change events with debouncing
        try
        {
            while (await _changeChannel.Reader.WaitToReadAsync(stoppingToken))
            {
                var changedPaths = new HashSet<string>();
                while (_changeChannel.Reader.TryRead(out var path))
                    changedPaths.Add(path);

                // Debounce: wait 500ms for additional changes
                await Task.Delay(500, stoppingToken);

                // Drain any additional changes that arrived during debounce
                while (_changeChannel.Reader.TryRead(out var path2))
                    changedPaths.Add(path2);

                await ReindexFolders(changedPaths, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        finally
        {
            DisposeAllWatchers();
        }
    }

    private void SetupWatchers()
    {
        // Find all local repos from the store
        var repos = _store.ListRepos();
        foreach (var repo in repos)
        {
            if (repo.TryGetValue("repo", out var repoObj) && repoObj is string repoKey
                && repoKey.StartsWith("local/", StringComparison.Ordinal))
            {
                // Check the static registry for the folder path
                if (FolderRegistry.TryGetValue(repoKey, out var folderPath)
                    && Directory.Exists(folderPath))
                {
                    CreateWatcher(repoKey, folderPath);
                }
            }
        }

        _logger.LogInformation("Watching {Count} local folder(s)", _watchers.Count);
    }

    private void CreateWatcher(string repoKey, string folderPath)
    {
        if (_watchers.ContainsKey(folderPath))
            return;

        try
        {
            var watcher = new FileSystemWatcher(folderPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size
                    | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
                InternalBufferSize = 64 * 1024,
            };

            watcher.Changed += OnFileChanged;
            watcher.Created += OnFileChanged;
            watcher.Deleted += OnFileChanged;
            watcher.Renamed += OnFileRenamed;
            watcher.Error += (_, args) => OnWatcherError(repoKey, folderPath, args);

            _watchers[folderPath] = watcher;
            _logger.LogInformation("Watching folder: {Folder} ({Repo})", folderPath, repoKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create watcher for {Folder}", folderPath);
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!IsSourceFile(e.FullPath))
            return;

        _changeChannel.Writer.TryWrite(e.FullPath);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (IsSourceFile(e.OldFullPath))
            _changeChannel.Writer.TryWrite(e.OldFullPath);
        if (IsSourceFile(e.FullPath))
            _changeChannel.Writer.TryWrite(e.FullPath);
    }

    private void OnWatcherError(string repoKey, string folderPath, ErrorEventArgs args)
    {
        var ex = args.GetException();

        if (ex is InternalBufferOverflowException)
        {
            _logger.LogWarning("FileSystemWatcher buffer overflow for {Folder}; queuing full re-scan", folderPath);
            _changeChannel.Writer.TryWrite(folderPath);
            return;
        }

        _logger.LogError(ex, "FileSystemWatcher error for {Folder}; recreating watcher", folderPath);

        // Dispose old watcher and recreate
        if (_watchers.TryGetValue(folderPath, out var oldWatcher))
        {
            oldWatcher.Dispose();
            _watchers.Remove(folderPath);
        }

        if (Directory.Exists(folderPath))
        {
            CreateWatcher(repoKey, folderPath);
        }
        else
        {
            _logger.LogWarning("Folder {Folder} no longer exists; stopping watch", folderPath);
        }
    }

    private async Task ReindexFolders(HashSet<string> changedPaths, CancellationToken ct)
    {
        // Map changed file paths to their watched root folders
        var foldersToReindex = new HashSet<string>();
        foreach (var changedPath in changedPaths)
        {
            foreach (var watchedFolder in _watchers.Keys)
            {
                if (changedPath.StartsWith(watchedFolder, StringComparison.Ordinal))
                {
                    foldersToReindex.Add(watchedFolder);
                    break;
                }
            }
        }

        foreach (var folder in foldersToReindex)
        {
            if (!Directory.Exists(folder))
            {
                _logger.LogWarning("Folder {Folder} was deleted; disposing watcher", folder);
                if (_watchers.TryGetValue(folder, out var watcher))
                {
                    watcher.Dispose();
                    _watchers.Remove(folder);
                }

                continue;
            }

            await _indexLock.WaitAsync(ct);
            try
            {
                _logger.LogInformation("Re-indexing folder: {Folder}", folder);
                var result = await IndexFolderTool.IndexFolder(
                    _store, _extractor, _summarizer,
                    path: folder,
                    useAiSummaries: false,
                    incremental: true);
                _logger.LogDebug("Re-index result for {Folder}: {Result}", folder, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to re-index {Folder}", folder);
            }
            finally
            {
                _indexLock.Release();
            }
        }
    }

    private static bool IsSourceFile(string path)
    {
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && LanguageRegistry.LanguageExtensions.ContainsKey(ext);
    }

    private void DisposeAllWatchers()
    {
        foreach (var watcher in _watchers.Values)
            watcher.Dispose();
        _watchers.Clear();
    }

    public override void Dispose()
    {
        DisposeAllWatchers();
        _indexLock.Dispose();
        base.Dispose();
    }
}
