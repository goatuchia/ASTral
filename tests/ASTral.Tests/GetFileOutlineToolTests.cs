using System.Text.Json;
using ASTral.Models;
using ASTral.Storage;
using ASTral.Tools;

namespace ASTral.Tests;

public class GetFileOutlineToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IndexStore _store;
    private readonly TokenTracker _tracker;

    public GetFileOutlineToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "astral-outline-tests-" + Guid.NewGuid().ToString("N"));
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
        var classContent = "class Calculator: pass";
        var classBytes = System.Text.Encoding.UTF8.GetBytes(classContent);
        var methodContent = "def add(self, a, b): return a + b";
        var methodBytes = System.Text.Encoding.UTF8.GetBytes(methodContent);

        var classId = Symbol.MakeSymbolId("src/calc.py", "Calculator", "class");
        var symbols = new List<Symbol>
        {
            new()
            {
                Id = classId,
                File = "src/calc.py",
                Name = "Calculator",
                QualifiedName = "Calculator",
                Kind = "class",
                Language = "python",
                Signature = "class Calculator:",
                Line = 1,
                EndLine = 3,
                ByteOffset = 0,
                ByteLength = classBytes.Length,
                ContentHash = Symbol.ComputeContentHash(classBytes),
            },
            new()
            {
                Id = Symbol.MakeSymbolId("src/calc.py", "Calculator.add", "method"),
                File = "src/calc.py",
                Name = "add",
                QualifiedName = "Calculator.add",
                Kind = "method",
                Language = "python",
                Signature = "def add(self, a, b):",
                Line = 2,
                EndLine = 3,
                ByteOffset = classBytes.Length + 1,
                ByteLength = methodBytes.Length,
                ContentHash = Symbol.ComputeContentHash(methodBytes),
                Parent = classId,
            },
        };
        var rawFiles = new Dictionary<string, string>
        {
            ["src/calc.py"] = classContent + "\n" + methodContent,
        };
        var languages = new Dictionary<string, int> { ["python"] = 1 };
        _store.SaveIndex("testowner", "testrepo", ["src/calc.py"], symbols, rawFiles, languages);
    }

    [Fact]
    public void GetFileOutline_ValidFile_ReturnsSymbols()
    {
        IndexSampleRepo();

        var result = GetFileOutlineTool.GetFileOutline(
            _store, _tracker,
            repo: "testowner/testrepo",
            filePath: "src/calc.py");
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        Assert.Equal("src/calc.py", root.GetProperty("file").GetString());
        Assert.Equal("python", root.GetProperty("language").GetString());
        Assert.True(root.GetProperty("symbols").GetArrayLength() > 0);
    }

    [Fact]
    public void GetFileOutline_FileNotFound_ReturnsEmptySymbols()
    {
        IndexSampleRepo();

        var result = GetFileOutlineTool.GetFileOutline(
            _store, _tracker,
            repo: "testowner/testrepo",
            filePath: "nonexistent.py");
        var doc = JsonDocument.Parse(result);

        Assert.Equal(0, doc.RootElement.GetProperty("symbols").GetArrayLength());
    }

    [Fact]
    public void GetFileOutline_RepoNotIndexed_ReturnsError()
    {
        var result = GetFileOutlineTool.GetFileOutline(
            _store, _tracker,
            repo: "someowner/somerepo",
            filePath: "any.py");
        var doc = JsonDocument.Parse(result);

        Assert.Contains("not indexed", doc.RootElement.GetProperty("error").GetString());
    }
}
