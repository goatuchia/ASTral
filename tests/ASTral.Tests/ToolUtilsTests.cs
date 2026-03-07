using System.Text.Json;
using ASTral.Models;
using ASTral.Storage;
using ASTral.Tools;

namespace ASTral.Tests;

public class ToolUtilsTests : IDisposable
{
    private readonly string _tempDir;

    public ToolUtilsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "astral_tooltest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static Symbol MakeSymbol(string file, string name) => new()
    {
        Id = $"{file}::{name}#function",
        File = file,
        Name = name,
        QualifiedName = name,
        Kind = "function",
        Language = "python",
        Signature = $"def {name}()",
    };

    // --- ResolveRepo ---

    [Fact]
    public void ResolveRepo_WithSlash_SplitsCorrectly()
    {
        var store = new IndexStore(Path.Combine(_tempDir, "store1"));
        var (owner, name) = ToolUtils.ResolveRepo("owner/repo", store);

        Assert.Equal("owner", owner);
        Assert.Equal("repo", name);
    }

    [Fact]
    public void ResolveRepo_WithoutSlash_LooksUpStore()
    {
        var storePath = Path.Combine(_tempDir, "store2");
        var store = new IndexStore(storePath);

        // Index a repo so it can be found
        store.SaveIndex("owner", "myrepo", ["src/main.py"], [],
            new Dictionary<string, string> { ["src/main.py"] = "print('hi')" },
            new Dictionary<string, int> { ["python"] = 1 });

        var (owner, name) = ToolUtils.ResolveRepo("myrepo", store);

        Assert.Equal("owner", owner);
        Assert.Equal("myrepo", name);
    }

    [Fact]
    public void ResolveRepo_NotFound_ThrowsArgumentException()
    {
        var store = new IndexStore(Path.Combine(_tempDir, "store3"));

        Assert.Throws<ArgumentException>(() => ToolUtils.ResolveRepo("unknown", store));
    }

    // --- ShouldSkipFile ---

    [Theory]
    [InlineData("node_modules/foo.js", true)]
    [InlineData(".git/config", true)]
    [InlineData("bundle.min.js", true)]
    [InlineData("src/main.py", false)]
    [InlineData("node_modules\\foo.js", true)]
    public void ShouldSkipFile_VariousPaths(string path, bool expected)
    {
        Assert.Equal(expected, ToolUtils.ShouldSkipFile(path));
    }

    // --- PriorityKey ---

    [Fact]
    public void PriorityKey_SrcDir_ReturnsPriority0()
    {
        var (priority, _, _) = ToolUtils.PriorityKey("src/main.py");
        Assert.Equal(0, priority);
    }

    [Fact]
    public void PriorityKey_LibDir_ReturnsPriority1()
    {
        var (priority, _, _) = ToolUtils.PriorityKey("lib/utils.py");
        Assert.Equal(1, priority);
    }

    [Fact]
    public void PriorityKey_NonPriorityDir_ReturnsMaxPriority()
    {
        var (priority, _, _) = ToolUtils.PriorityKey("docs/readme.md");
        Assert.Equal(ToolUtils.PriorityDirs.Length, priority);
    }

    // --- GroupSymbolsByFile ---

    [Fact]
    public void GroupSymbolsByFile_GroupsCorrectly()
    {
        var symbols = new List<Symbol>
        {
            MakeSymbol("a.py", "foo"),
            MakeSymbol("b.py", "bar"),
            MakeSymbol("a.py", "baz"),
        };

        var groups = ToolUtils.GroupSymbolsByFile(symbols);

        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups["a.py"].Count);
        Assert.Single(groups["b.py"]);
    }

    // --- ResolveRepoOrError ---

    [Fact]
    public void ResolveRepoOrError_ValidRepo_ReturnsResolved()
    {
        var store = new IndexStore(Path.Combine(_tempDir, "store4"));
        var result = ToolUtils.ResolveRepoOrError("owner/repo", store, out var errorJson);

        Assert.NotNull(result);
        Assert.Null(errorJson);
        Assert.Equal("owner", result.Value.Owner);
        Assert.Equal("repo", result.Value.Name);
    }

    [Fact]
    public void ResolveRepoOrError_InvalidRepo_ReturnsNullAndError()
    {
        var store = new IndexStore(Path.Combine(_tempDir, "store5"));
        var result = ToolUtils.ResolveRepoOrError("unknown", store, out var errorJson);

        Assert.Null(result);
        Assert.NotNull(errorJson);
        Assert.Contains("error", errorJson);
    }

    // --- BuildMeta ---

    [Fact]
    public void BuildMeta_ReturnsExpectedKeys()
    {
        var meta = ToolUtils.BuildMeta(123.4, 1000, 5000);

        Assert.True(meta.ContainsKey("timing_ms"));
        Assert.True(meta.ContainsKey("tokens_saved"));
        Assert.True(meta.ContainsKey("total_tokens_saved"));
        Assert.True(meta.ContainsKey("cost_avoided"));
    }

    // --- Serialize ---

    [Fact]
    public void Serialize_UsesSnakeCaseAndIndented()
    {
        var obj = new { MyProperty = "value", AnotherOne = 42 };
        var json = ToolUtils.Serialize(obj);

        Assert.Contains("my_property", json);
        Assert.Contains("another_one", json);
        Assert.Contains("\n", json); // indented
    }
}
