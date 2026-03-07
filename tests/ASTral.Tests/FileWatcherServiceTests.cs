using ASTral.Parser;
using ASTral.Services;
using ASTral.Storage;
using ASTral.Summarizer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ASTral.Tests;

public class FileWatcherServiceTests : IDisposable
{
    private readonly string _tempDir;

    public FileWatcherServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "astral-fwatcher-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void RegisterFolder_StoresPath()
    {
        var ex = Record.Exception(() =>
            FileWatcherService.RegisterFolder("local/test-fws", "/tmp/test-fws"));
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var store = new IndexStore(_tempDir);
        var extractor = new SymbolExtractor();
        var summarizer = new BatchSummarizer();
        var logger = NullLogger<FileWatcherService>.Instance;

        var service = new FileWatcherService(store, extractor, summarizer, logger);
        var ex = Record.Exception(() => service.Dispose());
        Assert.Null(ex);
    }
}
