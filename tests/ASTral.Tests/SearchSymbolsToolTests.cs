using System.Text.Json;
using ASTral.Models;
using ASTral.Storage;
using ASTral.Tools;

namespace ASTral.Tests;

public class SearchSymbolsToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IndexStore _store;
    private readonly TokenTracker _tracker;

    public SearchSymbolsToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "astral-searchsym-tests-" + Guid.NewGuid().ToString("N"));
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
        var symbols = new List<Symbol>
        {
            MakeSymbol("hello", kind: "function"),
            MakeSymbol("Calculator", file: "src/calc.py", kind: "class"),
        };
        var rawFiles = new Dictionary<string, string>
        {
            ["src/main.py"] = "def hello(): pass",
            ["src/calc.py"] = "class Calculator: pass",
        };
        var languages = new Dictionary<string, int> { ["python"] = 2 };
        _store.SaveIndex("testowner", "testrepo", ["src/main.py", "src/calc.py"], symbols, rawFiles, languages);
    }

    private static Symbol MakeSymbol(string name, string file = "src/main.py", string kind = "function", string? parent = null)
    {
        var content = kind == "class" ? $"class {name}: pass" : $"def {name}(): pass";
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return new Symbol
        {
            Id = Symbol.MakeSymbolId(file, name, kind),
            File = file,
            Name = name,
            QualifiedName = name,
            Kind = kind,
            Language = "python",
            Signature = kind == "class" ? $"class {name}:" : $"def {name}():",
            Line = 1,
            EndLine = 1,
            ByteOffset = System.Text.Encoding.UTF8.GetPreamble().Length,
            ByteLength = bytes.Length,
            ContentHash = Symbol.ComputeContentHash(bytes),
            Parent = parent,
        };
    }

    [Fact]
    public void SearchSymbols_ValidQuery_ReturnsResults()
    {
        IndexSampleRepo();

        var result = SearchSymbolsTool.SearchSymbols(
            _store, _tracker,
            repo: "testowner/testrepo",
            query: "hello");
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("result_count").GetInt32() > 0);
        var first = root.GetProperty("results")[0];
        Assert.Equal("hello", first.GetProperty("name").GetString());
    }

    [Fact]
    public void SearchSymbols_NoMatch_ReturnsEmptyResults()
    {
        IndexSampleRepo();

        var result = SearchSymbolsTool.SearchSymbols(
            _store, _tracker,
            repo: "testowner/testrepo",
            query: "nonexistent");
        var doc = JsonDocument.Parse(result);

        Assert.Equal(0, doc.RootElement.GetProperty("result_count").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("results").GetArrayLength());
    }

    [Fact]
    public void SearchSymbols_WithKindFilter_FiltersResults()
    {
        IndexSampleRepo();

        var result = SearchSymbolsTool.SearchSymbols(
            _store, _tracker,
            repo: "testowner/testrepo",
            query: "hello",
            kind: "function");
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("result_count").GetInt32() > 0);
        foreach (var r in root.GetProperty("results").EnumerateArray())
            Assert.Equal("function", r.GetProperty("kind").GetString());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(200, 100)]
    public void SearchSymbols_MaxResultsClamped_RespectsLimits(int input, int expectedClamped)
    {
        IndexSampleRepo();

        // We can't directly observe the clamped value, but we can verify
        // no crash and result_count <= expectedClamped
        var result = SearchSymbolsTool.SearchSymbols(
            _store, _tracker,
            repo: "testowner/testrepo",
            query: "hello",
            maxResults: input);
        var doc = JsonDocument.Parse(result);

        Assert.True(doc.RootElement.GetProperty("result_count").GetInt32() <= expectedClamped);
    }

    [Fact]
    public void SearchSymbols_RepoNotFound_ReturnsError()
    {
        // No repos indexed, search by short name triggers ArgumentException
        var result = SearchSymbolsTool.SearchSymbols(
            _store, _tracker,
            repo: "nonexistent",
            query: "hello");
        var doc = JsonDocument.Parse(result);

        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public void SearchSymbols_RepoNotIndexed_ReturnsError()
    {
        // Use owner/name format so ResolveRepo succeeds but LoadIndex returns null
        var result = SearchSymbolsTool.SearchSymbols(
            _store, _tracker,
            repo: "someowner/somerepo",
            query: "hello");
        var doc = JsonDocument.Parse(result);

        Assert.Contains("not indexed", doc.RootElement.GetProperty("error").GetString());
    }
}
