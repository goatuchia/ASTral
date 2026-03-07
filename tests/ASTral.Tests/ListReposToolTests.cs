using System.Text.Json;
using ASTral.Models;
using ASTral.Storage;
using ASTral.Tools;

namespace ASTral.Tests;

public class ListReposToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IndexStore _store;

    public ListReposToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "astral-listrepos-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _store = new IndexStore(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void IndexRepo(string owner, string name)
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
        _store.SaveIndex(owner, name, ["src/main.py"], [symbol], rawFiles, languages);
    }

    [Fact]
    public void ListRepos_EmptyStore_ReturnsZero()
    {
        var result = ListReposTool.ListRepos(_store);
        var doc = JsonDocument.Parse(result);

        Assert.Equal(0, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("repos").GetArrayLength());
    }

    [Fact]
    public void ListRepos_MultipleRepos_ReturnsAll()
    {
        IndexRepo("owner1", "repo1");
        IndexRepo("owner2", "repo2");

        var result = ListReposTool.ListRepos(_store);
        var doc = JsonDocument.Parse(result);

        Assert.Equal(2, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("repos").GetArrayLength());
    }
}
