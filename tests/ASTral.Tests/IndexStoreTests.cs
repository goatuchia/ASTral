using ASTral.Models;
using ASTral.Storage;

namespace ASTral.Tests;

public class IndexStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IndexStore _store;

    public IndexStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "astral-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _store = new IndexStore(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static Symbol MakeSymbol(string name, string file = "src/main.py", string kind = "function")
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
    public void SaveIndex_And_LoadIndex_Roundtrip()
    {
        var symbol = MakeSymbol("login");
        var rawFiles = new Dictionary<string, string> { ["src/main.py"] = "def login(): pass" };
        var languages = new Dictionary<string, int> { ["python"] = 1 };

        _store.SaveIndex("testowner", "testrepo", ["src/main.py"], [symbol], rawFiles, languages);

        var loaded = _store.LoadIndex("testowner", "testrepo");

        Assert.NotNull(loaded);
        Assert.Equal("testowner/testrepo", loaded.Repo);
        Assert.Equal("testowner", loaded.Owner);
        Assert.Equal("testrepo", loaded.Name);
        Assert.Single(loaded.SourceFiles);
        Assert.Single(loaded.Symbols);
        Assert.Equal("login", loaded.Symbols[0].Name);
    }

    [Fact]
    public void LoadIndex_ReturnsNull_WhenNotFound()
    {
        var result = _store.LoadIndex("nonexistent", "repo");
        Assert.Null(result);
    }

    [Fact]
    public void DetectChanges_IdentifiesNewFiles()
    {
        // No existing index, so all files should be "new"
        var currentFiles = new Dictionary<string, string>
        {
            ["src/main.py"] = "def login(): pass",
            ["src/utils.py"] = "def helper(): pass",
        };

        var (changed, newFiles, deleted) = _store.DetectChanges("testowner", "testrepo", currentFiles);

        Assert.Empty(changed);
        Assert.Equal(2, newFiles.Count);
        Assert.Empty(deleted);
    }

    [Fact]
    public void DetectChanges_IdentifiesChangedFiles()
    {
        var symbol = MakeSymbol("login");
        var rawFiles = new Dictionary<string, string> { ["src/main.py"] = "def login(): pass" };
        var languages = new Dictionary<string, int> { ["python"] = 1 };

        _store.SaveIndex("testowner", "testrepo", ["src/main.py"], [symbol], rawFiles, languages);

        // Now change the content
        var currentFiles = new Dictionary<string, string>
        {
            ["src/main.py"] = "def login(): return True",
        };

        var (changed, newFiles, deleted) = _store.DetectChanges("testowner", "testrepo", currentFiles);

        Assert.Single(changed);
        Assert.Equal("src/main.py", changed[0]);
        Assert.Empty(newFiles);
        Assert.Empty(deleted);
    }

    [Fact]
    public void DetectChanges_IdentifiesDeletedFiles()
    {
        var symbol = MakeSymbol("login");
        var rawFiles = new Dictionary<string, string> { ["src/main.py"] = "def login(): pass" };
        var languages = new Dictionary<string, int> { ["python"] = 1 };

        _store.SaveIndex("testowner", "testrepo", ["src/main.py"], [symbol], rawFiles, languages);

        // Empty current files means the original file was deleted
        var currentFiles = new Dictionary<string, string>();

        var (changed, newFiles, deleted) = _store.DetectChanges("testowner", "testrepo", currentFiles);

        Assert.Empty(changed);
        Assert.Empty(newFiles);
        Assert.Single(deleted);
        Assert.Equal("src/main.py", deleted[0]);
    }

    [Fact]
    public void DeleteIndex_RemovesFiles()
    {
        var symbol = MakeSymbol("login");
        var rawFiles = new Dictionary<string, string> { ["src/main.py"] = "def login(): pass" };
        var languages = new Dictionary<string, int> { ["python"] = 1 };

        _store.SaveIndex("testowner", "testrepo", ["src/main.py"], [symbol], rawFiles, languages);

        var deleted = _store.DeleteIndex("testowner", "testrepo");
        Assert.True(deleted);

        var loaded = _store.LoadIndex("testowner", "testrepo");
        Assert.Null(loaded);
    }

    [Fact]
    public void DeleteIndex_ReturnsFalse_WhenNothingToDelete()
    {
        var result = _store.DeleteIndex("nonexistent", "repo");
        Assert.False(result);
    }

    [Fact]
    public void ListRepos_FindsSavedIndexes()
    {
        var symbol = MakeSymbol("login");
        var rawFiles = new Dictionary<string, string> { ["src/main.py"] = "def login(): pass" };
        var languages = new Dictionary<string, int> { ["python"] = 1 };

        _store.SaveIndex("owner1", "repo1", ["src/main.py"], [symbol], rawFiles, languages);
        _store.SaveIndex("owner2", "repo2", ["src/main.py"], [symbol], rawFiles, languages);

        var repos = _store.ListRepos();

        Assert.Equal(2, repos.Count);
        var repoNames = repos.Select(r => (string)r["repo"]).OrderBy(r => r).ToList();
        Assert.Contains("owner1/repo1", repoNames);
        Assert.Contains("owner2/repo2", repoNames);
    }

    [Fact]
    public void SafeContentPath_RejectsPathTraversal()
    {
        // SaveIndex should throw for path-traversal file paths
        var symbol = MakeSymbol("login", file: "../../../etc/passwd");
        var rawFiles = new Dictionary<string, string> { ["../../../etc/passwd"] = "malicious" };
        var languages = new Dictionary<string, int> { ["python"] = 1 };

        Assert.Throws<ArgumentException>(() =>
            _store.SaveIndex("testowner", "testrepo", ["../../../etc/passwd"], [symbol], rawFiles, languages));
    }

    [Fact]
    public void SaveIndex_WithFileSummaries_PersistsSummaries()
    {
        var symbol = MakeSymbol("login");
        var rawFiles = new Dictionary<string, string> { ["src/main.py"] = "def login(): pass" };
        var languages = new Dictionary<string, int> { ["python"] = 1 };
        var summaries = new Dictionary<string, string> { ["src/main.py"] = "Login function" };

        _store.SaveIndex("testowner", "testrepo", ["src/main.py"], [symbol], rawFiles, languages,
            fileSummaries: summaries);

        var loaded = _store.LoadIndex("testowner", "testrepo");
        Assert.NotNull(loaded);
        Assert.True(loaded.FileSummaries.ContainsKey("src/main.py"));
        Assert.Equal("Login function", loaded.FileSummaries["src/main.py"]);
    }

    [Fact]
    public void SaveIndex_WithGitHead_PersistsGitHead()
    {
        var symbol = MakeSymbol("login");
        var rawFiles = new Dictionary<string, string> { ["src/main.py"] = "def login(): pass" };
        var languages = new Dictionary<string, int> { ["python"] = 1 };

        _store.SaveIndex("testowner", "testrepo", ["src/main.py"], [symbol], rawFiles, languages,
            gitHead: "abc123");

        var loaded = _store.LoadIndex("testowner", "testrepo");
        Assert.NotNull(loaded);
        Assert.Equal("abc123", loaded.GitHead);
    }

    [Fact]
    public void GetContentDir_ReturnsValidPath()
    {
        var contentDir = _store.GetContentDir("myowner", "myrepo");
        Assert.Contains("myowner", contentDir);
        Assert.Contains("myrepo", contentDir);
    }

    [Fact]
    public void GetGitHead_NonGitDir_ReturnsNull()
    {
        var result = IndexStore.GetGitHead(_tempDir);
        Assert.Null(result);
    }

    [Fact]
    public void IncrementalSave_UpdatesChangedFiles()
    {
        var symbol = MakeSymbol("login");
        var rawFiles = new Dictionary<string, string> { ["src/main.py"] = "def login(): pass" };
        var languages = new Dictionary<string, int> { ["python"] = 1 };

        _store.SaveIndex("testowner", "testrepo", ["src/main.py"], [symbol], rawFiles, languages);

        var newSymbol = MakeSymbol("login_v2", file: "src/main.py");
        var newRawFiles = new Dictionary<string, string> { ["src/main.py"] = "def login_v2(): pass" };

        var updated = _store.IncrementalSave("testowner", "testrepo",
            changedFiles: ["src/main.py"],
            newFiles: [],
            deletedFiles: [],
            newSymbols: [newSymbol],
            rawFiles: newRawFiles,
            languages: languages);

        Assert.NotNull(updated);
        Assert.Contains(updated.Symbols, s => s.Name == "login_v2");
        Assert.DoesNotContain(updated.Symbols, s => s.Name == "login");
    }

    [Fact]
    public void GetSymbolContent_MissingFile_ReturnsNull()
    {
        var result = _store.GetSymbolContent("nonexistent", "repo", "fake-symbol-id");
        Assert.Null(result);
    }

    [Fact]
    public void LoadIndex_CorruptJson_Throws()
    {
        // Write corrupt JSON to the expected index file path
        var indexPath = Path.Combine(_tempDir, "testowner-testrepo.json");
        File.WriteAllText(indexPath, "{ this is not valid json }}}}");

        Assert.ThrowsAny<System.Text.Json.JsonException>(() => _store.LoadIndex("testowner", "testrepo"));
    }

    [Fact]
    public void ListRepos_EmptyStore_ReturnsEmptyList()
    {
        var emptyDir = Path.Combine(Path.GetTempPath(), "astral-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyDir);
        try
        {
            var emptyStore = new IndexStore(emptyDir);
            var repos = emptyStore.ListRepos();
            Assert.Empty(repos);
        }
        finally
        {
            Directory.Delete(emptyDir, recursive: true);
        }
    }

    [Fact]
    public void GetSymbolContent_ReadsByByteOffset()
    {
        // File.WriteAllText with Encoding.UTF8 writes a 3-byte BOM prefix.
        // Account for the BOM when computing byte offsets for stored content.
        var bomLength = System.Text.Encoding.UTF8.GetPreamble().Length; // 3

        var fileContent = "def login(): pass\ndef logout(): pass";
        var loginSource = "def login(): pass";
        var loginBytes = System.Text.Encoding.UTF8.GetBytes(loginSource);

        var symbol = new Symbol
        {
            Id = Symbol.MakeSymbolId("src/main.py", "login", "function"),
            File = "src/main.py",
            Name = "login",
            QualifiedName = "login",
            Kind = "function",
            Language = "python",
            Signature = "def login():",
            Line = 1,
            EndLine = 1,
            ByteOffset = bomLength, // skip BOM
            ByteLength = loginBytes.Length,
            ContentHash = Symbol.ComputeContentHash(loginBytes),
        };

        var rawFiles = new Dictionary<string, string> { ["src/main.py"] = fileContent };
        var languages = new Dictionary<string, int> { ["python"] = 1 };

        _store.SaveIndex("testowner", "testrepo", ["src/main.py"], [symbol], rawFiles, languages);

        var content = _store.GetSymbolContent("testowner", "testrepo", symbol.Id);

        Assert.Equal(loginSource, content);
    }
}
