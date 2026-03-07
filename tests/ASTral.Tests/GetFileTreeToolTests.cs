using System.Text.Json;
using ASTral.Models;
using ASTral.Storage;
using ASTral.Tools;

namespace ASTral.Tests;

public class GetFileTreeToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IndexStore _store;
    private readonly TokenTracker _tracker;

    public GetFileTreeToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "astral-filetree-tests-" + Guid.NewGuid().ToString("N"));
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
            MakeSymbol("hello", "src/main.py"),
            MakeSymbol("greet", "src/utils/helpers.py"),
            MakeSymbol("Config", "config/settings.py", kind: "class"),
        };
        var rawFiles = new Dictionary<string, string>
        {
            ["src/main.py"] = "def hello(): pass",
            ["src/utils/helpers.py"] = "def greet(): pass",
            ["config/settings.py"] = "class Config: pass",
        };
        var languages = new Dictionary<string, int> { ["python"] = 3 };
        _store.SaveIndex("testowner", "testrepo",
            ["src/main.py", "src/utils/helpers.py", "config/settings.py"],
            symbols, rawFiles, languages);
    }

    private static Symbol MakeSymbol(string name, string file, string kind = "function")
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
            Language = "python",
            Signature = $"def {name}():",
            Line = 1,
            EndLine = 1,
            ByteOffset = 0,
            ByteLength = bytes.Length,
            ContentHash = Symbol.ComputeContentHash(bytes),
        };
    }

    [Fact]
    public void GetFileTree_ValidRepo_ReturnsTree()
    {
        IndexSampleRepo();

        var result = GetFileTreeTool.GetFileTree(
            _store, _tracker,
            repo: "testowner/testrepo");
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        Assert.Equal("testowner/testrepo", root.GetProperty("repo").GetString());
        Assert.True(root.GetProperty("tree").GetArrayLength() > 0);
    }

    [Fact]
    public void GetFileTree_WithPathPrefix_FiltersFiles()
    {
        IndexSampleRepo();

        var result = GetFileTreeTool.GetFileTree(
            _store, _tracker,
            repo: "testowner/testrepo",
            pathPrefix: "src/");
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        Assert.Equal("src/", root.GetProperty("path_prefix").GetString());
        // Tree should only have src/ content, not config/
        var treeJson = root.GetProperty("tree").GetRawText();
        Assert.DoesNotContain("config/", treeJson);
    }

    [Fact]
    public void GetFileTree_RepoNotIndexed_ReturnsError()
    {
        var result = GetFileTreeTool.GetFileTree(
            _store, _tracker,
            repo: "someowner/somerepo");
        var doc = JsonDocument.Parse(result);

        Assert.Contains("not indexed", doc.RootElement.GetProperty("error").GetString());
    }
}
