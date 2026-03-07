using System.Text.Json;
using ASTral.Models;
using ASTral.Storage;
using ASTral.Tools;

namespace ASTral.Tests;

public class GetSymbolToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IndexStore _store;
    private readonly TokenTracker _tracker;

    public GetSymbolToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "astral-getsym-tests-" + Guid.NewGuid().ToString("N"));
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
        // File.WriteAllText with UTF8 writes a 3-byte BOM prefix
        var bomLength = System.Text.Encoding.UTF8.GetPreamble().Length;
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
            ByteOffset = bomLength,
            ByteLength = bytes.Length,
            ContentHash = Symbol.ComputeContentHash(bytes),
        };
        var rawFiles = new Dictionary<string, string> { ["src/main.py"] = content };
        var languages = new Dictionary<string, int> { ["python"] = 1 };
        _store.SaveIndex("testowner", "testrepo", ["src/main.py"], [symbol], rawFiles, languages);
    }

    [Fact]
    public void GetSymbol_ValidId_ReturnsSource()
    {
        IndexSampleRepo();
        var symbolId = Symbol.MakeSymbolId("src/main.py", "hello", "function");

        var result = GetSymbolTool.GetSymbol(
            _store, _tracker,
            repo: "testowner/testrepo",
            symbolId: symbolId);
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        Assert.Equal("hello", root.GetProperty("name").GetString());
        Assert.Equal("def hello(): pass", root.GetProperty("source").GetString());
    }

    [Fact]
    public void GetSymbol_NotFound_ReturnsError()
    {
        IndexSampleRepo();

        var result = GetSymbolTool.GetSymbol(
            _store, _tracker,
            repo: "testowner/testrepo",
            symbolId: "src/main.py::nonexistent#function");
        var doc = JsonDocument.Parse(result);

        Assert.Contains("not found", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void GetSymbol_RepoNotIndexed_ReturnsError()
    {
        var result = GetSymbolTool.GetSymbol(
            _store, _tracker,
            repo: "someowner/somerepo",
            symbolId: "any-id");
        var doc = JsonDocument.Parse(result);

        Assert.Contains("not indexed", doc.RootElement.GetProperty("error").GetString());
    }
}
