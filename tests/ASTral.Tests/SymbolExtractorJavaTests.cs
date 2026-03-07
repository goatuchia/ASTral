using ASTral.Parser;

namespace ASTral.Tests;

public class SymbolExtractorJavaTests
{
    private readonly SymbolExtractor _extractor = new();

    [Fact]
    public void ExtractSymbols_ClassAndMethods_ExtractsHierarchy()
    {
        const string code = """
            public class Calculator {
                public int add(int a, int b) {
                    return a + b;
                }
            }
            """;

        var symbols = _extractor.ExtractSymbols(code, "test.java", "java");

        var cls = Assert.Single(symbols, s => s.Kind == "class");
        Assert.Equal("Calculator", cls.Name);

        var method = Assert.Single(symbols, s => s.Kind == "method");
        Assert.Equal("add", method.Name);
        Assert.Equal(cls.Id, method.Parent);
    }

    [Fact]
    public void ExtractSymbols_Interface_ExtractsAsType()
    {
        const string code = """
            public interface Processor {
                void process();
            }
            """;

        var symbols = _extractor.ExtractSymbols(code, "test.java", "java");

        var iface = Assert.Single(symbols, s => s.Name == "Processor");
        Assert.Equal("type", iface.Kind);
    }

    [Fact]
    public void ExtractSymbols_Constructor_ExtractsAsMethod()
    {
        const string code = """
            public class Calculator {
                public Calculator() {}
            }
            """;

        var symbols = _extractor.ExtractSymbols(code, "test.java", "java");

        var ctor = Assert.Single(symbols, s => s.Name == "Calculator" && s.Kind == "method");
        Assert.NotNull(ctor);
    }
}
