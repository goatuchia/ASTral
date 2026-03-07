using System.Text.Json;
using ASTral.Models;
using ASTral.Storage;
using ASTral.Tools;

namespace ASTral.Tests;

public class GetSymbolsToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IndexStore _store;
    private readonly TokenTracker _tracker;

    public GetSymbolsToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "astral-getsyms-tests-" + Guid.NewGuid().ToString("N"));
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
        var content1 = "def hello(): pass";
        var bytes1 = System.Text.Encoding.UTF8.GetBytes(content1);
        var content2 = "def greet(): pass";
        var bytes2 = System.Text.Encoding.UTF8.GetBytes(content2);

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
                ByteLength = bytes1.Length,
                ContentHash = Symbol.ComputeContentHash(bytes1),
            },
            new()
            {
                Id = Symbol.MakeSymbolId("src/main.py", "greet", "function"),
                File = "src/main.py",
                Name = "greet",
                QualifiedName = "greet",
                Kind = "function",
                Language = "python",
                Signature = "def greet():",
                Line = 2,
                EndLine = 2,
                ByteOffset = bytes1.Length + 1, // after newline
                ByteLength = bytes2.Length,
                ContentHash = Symbol.ComputeContentHash(bytes2),
            },
        };
        var rawFiles = new Dictionary<string, string>
        {
            ["src/main.py"] = content1 + "\n" + content2,
        };
        var languages = new Dictionary<string, int> { ["python"] = 1 };
        _store.SaveIndex("testowner", "testrepo", ["src/main.py"], symbols, rawFiles, languages);
    }

    [Fact]
    public void GetSymbols_ValidIds_ReturnsMultiple()
    {
        IndexSampleRepo();
        var id1 = Symbol.MakeSymbolId("src/main.py", "hello", "function");
        var id2 = Symbol.MakeSymbolId("src/main.py", "greet", "function");

        var result = GetSymbolsTool.GetSymbols(
            _store, _tracker,
            repo: "testowner/testrepo",
            symbolIds: [id1, id2]);
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        Assert.Equal(2, root.GetProperty("symbols").GetArrayLength());
        Assert.Equal(0, root.GetProperty("errors").GetArrayLength());
    }

    [Fact]
    public void GetSymbols_MixedValidInvalid_ReturnsResultsAndErrors()
    {
        IndexSampleRepo();
        var validId = Symbol.MakeSymbolId("src/main.py", "hello", "function");
        var invalidId = "src/main.py::nonexistent#function";

        var result = GetSymbolsTool.GetSymbols(
            _store, _tracker,
            repo: "testowner/testrepo",
            symbolIds: [validId, invalidId]);
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        Assert.Equal(1, root.GetProperty("symbols").GetArrayLength());
        Assert.Equal(1, root.GetProperty("errors").GetArrayLength());
        Assert.Contains("not found", root.GetProperty("errors")[0].GetProperty("error").GetString());
    }

    [Fact]
    public void GetSymbols_RepoNotIndexed_ReturnsError()
    {
        var result = GetSymbolsTool.GetSymbols(
            _store, _tracker,
            repo: "someowner/somerepo",
            symbolIds: ["any-id"]);
        var doc = JsonDocument.Parse(result);

        Assert.Contains("not indexed", doc.RootElement.GetProperty("error").GetString());
    }
}
