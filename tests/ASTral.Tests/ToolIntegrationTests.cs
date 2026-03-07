using System.Text.Json;
using ASTral.Parser;
using ASTral.Storage;
using ASTral.Summarizer;
using ASTral.Tools;

namespace ASTral.Tests;

public class ToolIntegrationTests : IDisposable
{
    private readonly string _storeDir;
    private readonly string _sourceDir;
    private readonly IndexStore _store;
    private readonly SymbolExtractor _extractor;
    private readonly BatchSummarizer _summarizer;
    private readonly TokenTracker _tracker;

    private const string SamplePython = """"
        def hello(name: str) -> str:
            """Greet someone by name."""
            return f"Hello, {name}!"

        class Calculator:
            """A simple calculator."""
            def add(self, a: int, b: int) -> int:
                return a + b
        """";

    public ToolIntegrationTests()
    {
        _storeDir = Path.Combine(Path.GetTempPath(), "astral-tool-tests-" + Guid.NewGuid().ToString("N"));
        _sourceDir = Path.Combine(Path.GetTempPath(), "astral-tool-src-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_storeDir);
        Directory.CreateDirectory(_sourceDir);

        _store = new IndexStore(_storeDir);
        _extractor = new SymbolExtractor();
        _summarizer = new BatchSummarizer();
        _tracker = new TokenTracker(_storeDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_storeDir))
            Directory.Delete(_storeDir, recursive: true);
        if (Directory.Exists(_sourceDir))
            Directory.Delete(_sourceDir, recursive: true);
    }

    private void WriteSamplePythonFile()
    {
        File.WriteAllText(Path.Combine(_sourceDir, "sample.py"), SamplePython);
    }

    private async Task<string> IndexSampleFolder()
    {
        WriteSamplePythonFile();
        return await IndexFolderTool.IndexFolder(
            _store, _extractor, _summarizer,
            path: _sourceDir,
            useAiSummaries: false,
            incremental: false);
    }

    [Fact]
    public async Task IndexFolder_IndexesPythonFiles()
    {
        var result = await IndexSampleFolder();
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.True(root.GetProperty("symbol_count").GetInt32() > 0);
    }

    [Fact]
    public async Task SearchSymbols_FindsIndexedSymbol()
    {
        await IndexSampleFolder();
        var repoName = new DirectoryInfo(_sourceDir).Name;

        var result = SearchSymbolsTool.SearchSymbols(
            _store, _tracker,
            repo: $"local/{repoName}",
            query: "hello");
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("result_count").GetInt32() > 0);
        var firstResult = root.GetProperty("results")[0];
        Assert.Equal("hello", firstResult.GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetSymbol_ReturnsSymbolDetails()
    {
        await IndexSampleFolder();
        var repoName = new DirectoryInfo(_sourceDir).Name;

        // First search to get a symbol ID
        var searchResult = SearchSymbolsTool.SearchSymbols(
            _store, _tracker,
            repo: $"local/{repoName}",
            query: "hello");
        var searchDoc = JsonDocument.Parse(searchResult);
        var symbolId = searchDoc.RootElement.GetProperty("results")[0].GetProperty("id").GetString()!;

        var result = GetSymbolTool.GetSymbol(
            _store, _tracker,
            repo: $"local/{repoName}",
            symbolId: symbolId);
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        Assert.Equal("hello", root.GetProperty("name").GetString());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("source").GetString()));
    }

    [Fact]
    public async Task GetFileOutline_ReturnsFileStructure()
    {
        await IndexSampleFolder();
        var repoName = new DirectoryInfo(_sourceDir).Name;

        var result = GetFileOutlineTool.GetFileOutline(
            _store, _tracker,
            repo: $"local/{repoName}",
            filePath: "sample.py");
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        Assert.Equal("sample.py", root.GetProperty("file").GetString());
        var symbols = root.GetProperty("symbols");
        Assert.True(symbols.GetArrayLength() > 0);
    }

    [Fact]
    public async Task GetRepoOutline_ReturnsRepoStructure()
    {
        await IndexSampleFolder();
        var repoName = new DirectoryInfo(_sourceDir).Name;

        var result = GetRepoOutlineTool.GetRepoOutline(
            _store, _tracker,
            repo: $"local/{repoName}");
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        Assert.Equal($"local/{repoName}", root.GetProperty("repo").GetString());
        Assert.True(root.GetProperty("symbol_count").GetInt32() > 0);
        Assert.True(root.GetProperty("file_count").GetInt32() > 0);
    }

    [Fact]
    public async Task ListRepos_ShowsIndexedRepo()
    {
        await IndexSampleFolder();
        var repoName = new DirectoryInfo(_sourceDir).Name;

        var result = ListReposTool.ListRepos(_store);
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("count").GetInt32() > 0);
        var repos = root.GetProperty("repos");
        var repoNames = new List<string>();
        foreach (var repo in repos.EnumerateArray())
            repoNames.Add(repo.GetProperty("repo").GetString()!);

        Assert.Contains($"local/{repoName}", repoNames);
    }

    [Fact]
    public async Task InvalidateCache_RemovesIndex()
    {
        await IndexSampleFolder();
        var repoName = new DirectoryInfo(_sourceDir).Name;

        var result = InvalidateCacheTool.InvalidateCache(
            _store,
            repo: $"local/{repoName}");
        var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());

        // Verify repo no longer listed
        var listResult = ListReposTool.ListRepos(_store);
        var listDoc = JsonDocument.Parse(listResult);
        Assert.Equal(0, listDoc.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task IndexFolder_HandlesEmptyFolder()
    {
        var emptyDir = Path.Combine(Path.GetTempPath(), "astral-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyDir);

        try
        {
            var result = await IndexFolderTool.IndexFolder(
                _store, _extractor, _summarizer,
                path: emptyDir,
                useAiSummaries: false,
                incremental: false);
            var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;

            Assert.False(root.GetProperty("success").GetBoolean());
            Assert.True(root.TryGetProperty("error", out _));
        }
        finally
        {
            if (Directory.Exists(emptyDir))
                Directory.Delete(emptyDir, recursive: true);
        }
    }
}
