using ASTral.Parser;

namespace ASTral.Tests;

public class SymbolExtractorPythonTests
{
    private readonly SymbolExtractor _extractor = new();

    [Fact]
    public void ExtractSymbols_Function_ExtractsCorrectly()
    {
        var code = "def hello(name: str) -> str:\n    \"\"\"Greet someone.\"\"\"\n    return f\"Hello, {name}!\"";

        var symbols = _extractor.ExtractSymbols(code, "test.py", "python");

        var func = Assert.Single(symbols, s => s.Kind == "function");
        Assert.Equal("hello", func.Name);
        Assert.Contains("def hello", func.Signature);
    }

    [Fact]
    public void ExtractSymbols_ClassWithMethods_ExtractsHierarchy()
    {
        var code = "class Calculator:\n    \"\"\"A calculator class.\"\"\"\n    def add(self, a: int, b: int) -> int:\n        return a + b\n    def subtract(self, a, b):\n        return a - b";

        var symbols = _extractor.ExtractSymbols(code, "test.py", "python");

        var cls = Assert.Single(symbols, s => s.Kind == "class");
        Assert.Equal("Calculator", cls.Name);

        var methods = symbols.Where(s => s.Kind == "method").ToList();
        Assert.Equal(2, methods.Count);
        Assert.Contains(methods, m => m.Name == "add");
        Assert.Contains(methods, m => m.Name == "subtract");
        Assert.All(methods, m => Assert.Equal(cls.Id, m.Parent));
    }

    [Fact]
    public void ExtractSymbols_Docstring_Extracted()
    {
        var code = "def hello(name: str) -> str:\n    \"\"\"Greet someone.\"\"\"\n    return f\"Hello, {name}!\"";

        var symbols = _extractor.ExtractSymbols(code, "test.py", "python");

        var func = Assert.Single(symbols, s => s.Name == "hello");
        Assert.Contains("Greet someone", func.Docstring);
    }

    [Fact]
    public void ExtractSymbols_Decorator_Extracted()
    {
        var code = "@app.route(\"/login\")\ndef login():\n    pass";

        var symbols = _extractor.ExtractSymbols(code, "test.py", "python");

        var func = Assert.Single(symbols, s => s.Name == "login");
        Assert.NotEmpty(func.Decorators);
    }

    [Fact]
    public void ExtractSymbols_EmptyContent_ReturnsEmpty()
    {
        var symbols = _extractor.ExtractSymbols("", "test.py", "python");

        Assert.Empty(symbols);
    }

    [Fact]
    public void ExtractSymbols_UnknownLanguage_ReturnsEmpty()
    {
        var symbols = _extractor.ExtractSymbols("code", "test.xyz", "unknown");

        Assert.Empty(symbols);
    }
}
