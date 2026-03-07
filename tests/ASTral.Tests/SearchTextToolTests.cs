using System.Text.Json;
using ASTral.Models;
using ASTral.Storage;
using ASTral.Tools;

namespace ASTral.Tests;

public class SearchTextToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IndexStore _store;
    private readonly TokenTracker _tracker;

    public SearchTextToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "astral-searchtxt-tests-" + Guid.NewGuid().ToString("N"));
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
            MakeSymbol("hello", file: "src/main.py"),
            MakeSymbol("greet", file: "src/utils.js", language: "javascript"),
        };
        var rawFiles = new Dictionary<string, string>
        {
            ["src/main.py"] = "def hello():\n    print('hello world')\n",
            ["src/utils.js"] = "function greet() { return 'hi there'; }\n",
        };
        var languages = new Dictionary<string, int> { ["python"] = 1, ["javascript"] = 1 };
        _store.SaveIndex("testowner", "testrepo",
            ["src/main.py", "src/utils.js"], symbols, rawFiles, languages);
    }

    private static Symbol MakeSymbol(string name, string file = "src/main.py", string kind = "function", string language = "python")
    {
        var content = $"def {name}(): pass";
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return new Symbol
        {
            Id = Symbol.MakeSymbolId(file, name, kind),
            File = file,
            Name = name,
            QualifiedName = name,
            Kind = kind,
            Language = language,
            Signature = $"def {name}():",
            Line = 1,
            EndLine = 1,
            ByteOffset = 0,
            ByteLength = bytes.Length,
            ContentHash = Symbol.ComputeContentHash(bytes),
        };
    }

    [Fact]
    public void SearchText_ValidQuery_FindsMatch()
    {
        IndexSampleRepo();

        var result = SearchTextTool.SearchText(
            _store, _tracker,
            repo: "testowner/testrepo",
            query: "hello");
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("result_count").GetInt32() > 0);
        var first = root.GetProperty("results")[0];
        Assert.Equal("src/main.py", first.GetProperty("file").GetString());
    }

    [Fact]
    public void SearchText_NoMatch_ReturnsEmptyResults()
    {
        IndexSampleRepo();

        var result = SearchTextTool.SearchText(
            _store, _tracker,
            repo: "testowner/testrepo",
            query: "nonexistent_xyz");
        var doc = JsonDocument.Parse(result);

        Assert.Equal(0, doc.RootElement.GetProperty("result_count").GetInt32());
    }

    [Fact]
    public void SearchText_FilePatternFilter_LimitsSearch()
    {
        IndexSampleRepo();

        var result = SearchTextTool.SearchText(
            _store, _tracker,
            repo: "testowner/testrepo",
            query: "hello",
            filePattern: "*.py");
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        // Should find match only in .py files
        Assert.True(root.GetProperty("result_count").GetInt32() > 0);
        foreach (var r in root.GetProperty("results").EnumerateArray())
            Assert.EndsWith(".py", r.GetProperty("file").GetString()!);
    }

    [Fact]
    public void SearchText_MaxResultsClamped_RespectsLimits()
    {
        IndexSampleRepo();

        // maxResults=0 should be clamped to 1
        var result = SearchTextTool.SearchText(
            _store, _tracker,
            repo: "testowner/testrepo",
            query: "hello",
            maxResults: 0);
        var doc = JsonDocument.Parse(result);

        Assert.True(doc.RootElement.GetProperty("result_count").GetInt32() <= 1);
    }

    [Fact]
    public void SearchText_RepoNotIndexed_ReturnsError()
    {
        var result = SearchTextTool.SearchText(
            _store, _tracker,
            repo: "someowner/somerepo",
            query: "hello");
        var doc = JsonDocument.Parse(result);

        Assert.Contains("not indexed", doc.RootElement.GetProperty("error").GetString());
    }
}
