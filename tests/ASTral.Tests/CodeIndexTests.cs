using ASTral.Models;

namespace ASTral.Tests;

public class CodeIndexTests
{
    private static Symbol MakeSymbol(
        string id, string name, string kind, string file,
        string signature = "", string summary = "", string docstring = "",
        List<string>? keywords = null)
    {
        return new Symbol
        {
            Id = id,
            Name = name,
            Kind = kind,
            File = file,
            Signature = signature,
            Summary = summary,
            Docstring = docstring,
            Keywords = keywords ?? [],
            QualifiedName = name,
            Language = "python",
            Decorators = [],
            Parent = null,
            Line = 1,
            EndLine = 10,
            ByteOffset = 0,
            ByteLength = 50,
            ContentHash = "",
        };
    }

    private static CodeIndex MakeTestIndex(params Symbol[] symbols)
    {
        return new CodeIndex
        {
            Repo = "test/repo",
            Owner = "test",
            Name = "repo",
            IndexedAt = "2025-01-01T00:00:00Z",
            SourceFiles = ["src/main.py"],
            Languages = new Dictionary<string, int> { ["python"] = 1 },
            Symbols = [..symbols],
        };
    }

    [Fact]
    public void GetSymbol_FindsById()
    {
        var sym = MakeSymbol("src/main.py::login#function", "login", "function", "src/main.py");
        var index = MakeTestIndex(sym);

        var found = index.GetSymbol("src/main.py::login#function");

        Assert.NotNull(found);
        Assert.Equal("login", found.Name);
    }

    [Fact]
    public void GetSymbol_ReturnsNullForMissingId()
    {
        var sym = MakeSymbol("src/main.py::login#function", "login", "function", "src/main.py");
        var index = MakeTestIndex(sym);

        var found = index.GetSymbol("nonexistent::id#function");

        Assert.Null(found);
    }

    [Fact]
    public void Search_ReturnsScoredResults()
    {
        var sym1 = MakeSymbol("s1", "login", "function", "src/main.py", signature: "def login():");
        var sym2 = MakeSymbol("s2", "logout", "function", "src/main.py", signature: "def logout():");
        var index = MakeTestIndex(sym1, sym2);

        var results = index.Search("login");

        Assert.NotEmpty(results);
        // "login" should be the top result (exact name match)
        Assert.Equal("login", results[0].Name);
    }

    [Fact]
    public void Search_WithKindFilter()
    {
        var func = MakeSymbol("s1", "login", "function", "src/main.py");
        var cls = MakeSymbol("s2", "LoginService", "class", "src/main.py",
            summary: "login service");
        var index = MakeTestIndex(func, cls);

        var results = index.Search("login", kind: "class");

        Assert.All(results, r => Assert.Equal("class", r.Kind));
    }

    [Fact]
    public void Search_WithFilePatternFilter()
    {
        var sym1 = MakeSymbol("s1", "login", "function", "src/main.py");
        var sym2 = MakeSymbol("s2", "login", "function", "tests/test_main.py");
        var index = MakeTestIndex(sym1, sym2);

        var results = index.Search("login", filePattern: "src/*.py");

        Assert.Single(results);
        Assert.Equal("src/main.py", results[0].File);
    }

    [Fact]
    public void Search_ScoreWeighting_ExactNameMatchHigherThanContains()
    {
        var exact = MakeSymbol("s1", "login", "function", "src/a.py");
        var contains = MakeSymbol("s2", "login_handler", "function", "src/b.py");
        var index = MakeTestIndex(exact, contains);

        var results = index.Search("login");

        Assert.True(results.Count >= 2);
        Assert.Equal("login", results[0].Name);
    }

    [Fact]
    public void Search_NoResults_ReturnsEmptyList()
    {
        var sym = MakeSymbol("s1", "login", "function", "src/main.py");
        var index = MakeTestIndex(sym);

        var results = index.Search("zzzznonexistent");

        Assert.Empty(results);
    }
}
