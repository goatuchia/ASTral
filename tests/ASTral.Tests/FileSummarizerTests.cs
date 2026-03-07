using ASTral.Models;
using ASTral.Summarizer;

namespace ASTral.Tests;

public class FileSummarizerTests
{
    private static Symbol MakeSymbol(string name, string kind, string? parent = null) => new()
    {
        Id = $"test.py::{name}#{kind}",
        File = "test.py",
        Name = name,
        QualifiedName = name,
        Kind = kind,
        Language = "python",
        Signature = $"def {name}()",
        Parent = parent,
    };

    [Fact]
    public void GenerateFileSummaries_EmptySymbols_ReturnsEmptyString()
    {
        var input = new Dictionary<string, List<Symbol>>
        {
            ["test.py"] = [],
        };

        var result = FileSummarizer.GenerateFileSummaries(input);

        Assert.Equal("", result["test.py"]);
    }

    [Fact]
    public void GenerateFileSummaries_ClassWithMethods_ReturnsClassSummary()
    {
        var classSymbol = MakeSymbol("UserService", "class");
        var m1 = MakeSymbol("get", "method", "test.py::UserService#class");
        var m2 = MakeSymbol("update", "method", "test.py::UserService#class");
        var m3 = MakeSymbol("delete", "method", "test.py::UserService#class");

        var input = new Dictionary<string, List<Symbol>>
        {
            ["test.py"] = [classSymbol, m1, m2, m3],
        };

        var result = FileSummarizer.GenerateFileSummaries(input);

        Assert.Contains("Defines UserService class (3 methods)", result["test.py"]);
    }

    [Fact]
    public void GenerateFileSummaries_FewFunctions_ListsAllNames()
    {
        var f1 = MakeSymbol("login", "function");
        var f2 = MakeSymbol("logout", "function");

        var input = new Dictionary<string, List<Symbol>>
        {
            ["test.py"] = [f1, f2],
        };

        var result = FileSummarizer.GenerateFileSummaries(input);

        Assert.Contains("Contains 2 functions: login, logout", result["test.py"]);
    }

    [Fact]
    public void GenerateFileSummaries_ManyFunctions_TruncatesNames()
    {
        var symbols = Enumerable.Range(1, 5)
            .Select(i => MakeSymbol($"fn{i}", "function"))
            .ToList();

        var input = new Dictionary<string, List<Symbol>>
        {
            ["test.py"] = symbols,
        };

        var result = FileSummarizer.GenerateFileSummaries(input);
        var summary = result["test.py"];

        Assert.Contains("Contains 5 functions: fn1, fn2, fn3, ...", summary);
    }

    [Fact]
    public void GenerateFileSummaries_TypesOnly_ReturnsTypesSummary()
    {
        var t1 = MakeSymbol("Config", "type");
        var t2 = MakeSymbol("Options", "type");

        var input = new Dictionary<string, List<Symbol>>
        {
            ["test.py"] = [t1, t2],
        };

        var result = FileSummarizer.GenerateFileSummaries(input);

        Assert.Contains("Defines types:", result["test.py"]);
    }

    [Fact]
    public void GenerateFileSummaries_ConstantsOnly_ReturnsConstantsSummary()
    {
        var c1 = MakeSymbol("MAX", "constant");
        var c2 = MakeSymbol("MIN", "constant");
        var c3 = MakeSymbol("DEFAULT", "constant");

        var input = new Dictionary<string, List<Symbol>>
        {
            ["test.py"] = [c1, c2, c3],
        };

        var result = FileSummarizer.GenerateFileSummaries(input);

        Assert.Contains("Defines 3 constants", result["test.py"]);
    }
}
