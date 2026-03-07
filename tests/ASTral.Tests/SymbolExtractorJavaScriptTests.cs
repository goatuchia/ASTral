using ASTral.Parser;

namespace ASTral.Tests;

public class SymbolExtractorJavaScriptTests
{
    private readonly SymbolExtractor _extractor = new();

    [Fact]
    public void ExtractSymbols_Function_ExtractsCorrectly()
    {
        const string code = """
            function greet(name) {
                return "Hello, " + name;
            }
            """;

        var symbols = _extractor.ExtractSymbols(code, "test.js", "javascript");

        var func = Assert.Single(symbols, s => s.Name == "greet");
        Assert.Equal("function", func.Kind);
        Assert.Contains("function greet", func.Signature);
    }

    [Fact]
    public void ExtractSymbols_ArrowFunction_ExtractsAsFunction()
    {
        const string code = """
            const add = (a, b) => a + b;
            """;

        var symbols = _extractor.ExtractSymbols(code, "test.js", "javascript");

        var func = Assert.Single(symbols, s => s.Name == "add");
        Assert.Equal("function", func.Kind);
    }

    [Fact]
    public void ExtractSymbols_Class_ExtractsWithMethods()
    {
        const string code = """
            class Animal {
                constructor(name) { this.name = name; }
                speak() { return this.name; }
            }
            """;

        var symbols = _extractor.ExtractSymbols(code, "test.js", "javascript");

        var cls = Assert.Single(symbols, s => s.Kind == "class");
        Assert.Equal("Animal", cls.Name);

        var methods = symbols.Where(s => s.Kind == "method").ToList();
        Assert.Contains(methods, m => m.Name == "constructor");
        Assert.Contains(methods, m => m.Name == "speak");
    }

    [Fact]
    public void ExtractSymbols_EmptyContent_ReturnsEmpty()
    {
        var symbols = _extractor.ExtractSymbols("", "test.js", "javascript");

        Assert.Empty(symbols);
    }
}
