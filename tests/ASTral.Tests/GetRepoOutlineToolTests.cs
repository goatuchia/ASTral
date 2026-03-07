using System.Text.Json;
using ASTral.Models;
using ASTral.Storage;
using ASTral.Tools;

namespace ASTral.Tests;

public class GetRepoOutlineToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IndexStore _store;
    private readonly TokenTracker _tracker;

    public GetRepoOutlineToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "astral-repooutline-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _store = new IndexStore(_tempDir);
        _tracker = new TokenTracker(_tempDir);
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
        var symbols = new List<Symbol>
        {
            new()
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
            },
        };
        var rawFiles = new Dictionary<string, string> { ["src/main.py"] = content };
        var languages = new Dictionary<string, int> { ["python"] = 1 };
        _store.SaveIndex("testowner", "testrepo", ["src/main.py"], symbols, rawFiles, languages);
    }

    [Fact]
    public void GetRepoOutline_ValidRepo_ReturnsSummary()
    {
        IndexSampleRepo();

        var result = GetRepoOutlineTool.GetRepoOutline(
            _store, _tracker,
            repo: "testowner/testrepo");
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        Assert.Equal("testowner/testrepo", root.GetProperty("repo").GetString());
        Assert.True(root.GetProperty("file_count").GetInt32() > 0);
        Assert.True(root.GetProperty("symbol_count").GetInt32() > 0);
        Assert.True(root.TryGetProperty("directories", out _));
        Assert.True(root.TryGetProperty("symbol_kinds", out _));
        Assert.True(root.TryGetProperty("languages", out _));
    }

    [Fact]
    public void GetRepoOutline_RepoNotIndexed_ReturnsError()
    {
        var result = GetRepoOutlineTool.GetRepoOutline(
            _store, _tracker,
            repo: "someowner/somerepo");
        var doc = JsonDocument.Parse(result);

        Assert.Contains("not indexed", doc.RootElement.GetProperty("error").GetString());
    }
}
