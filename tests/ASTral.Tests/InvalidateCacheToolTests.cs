using System.Text.Json;
using ASTral.Models;
using ASTral.Storage;
using ASTral.Tools;

namespace ASTral.Tests;

public class InvalidateCacheToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IndexStore _store;

    public InvalidateCacheToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "astral-invalidate-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _store = new IndexStore(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void IndexSampleRepo()
    {
        var content = "def hello(): pass";
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var symbol = new Symbol
        {
            Id = Symbol.MakeSymbolId("src/main.py", "hello", "function"),
            File = "src/main.py",
            Name = "hello",
            QualifiedName = "hello",
            Kind = "function",
            Language = "python",
            Signature = "def hello():",
            Line = 1,
            EndLine = 1,
            ByteOffset = 0,
            ByteLength = bytes.Length,
            ContentHash = Symbol.ComputeContentHash(bytes),
        };
        var rawFiles = new Dictionary<string, string> { ["src/main.py"] = content };
        var languages = new Dictionary<string, int> { ["python"] = 1 };
        _store.SaveIndex("testowner", "testrepo", ["src/main.py"], [symbol], rawFiles, languages);
    }

    [Fact]
    public void InvalidateCache_ExistingRepo_ReturnsSuccess()
    {
        IndexSampleRepo();

        var result = InvalidateCacheTool.InvalidateCache(
            _store,
            repo: "testowner/testrepo");
        var doc = JsonDocument.Parse(result);

        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());

        // Verify repo is gone
        var listResult = ListReposTool.ListRepos(_store);
        var listDoc = JsonDocument.Parse(listResult);
        Assert.Equal(0, listDoc.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public void InvalidateCache_NonExistentRepo_ReturnsFailure()
    {
        var result = InvalidateCacheTool.InvalidateCache(
            _store,
            repo: "testowner/nonexistent");
        var doc = JsonDocument.Parse(result);

        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
    }
}
