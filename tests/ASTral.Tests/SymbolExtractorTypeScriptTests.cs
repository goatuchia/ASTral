using ASTral.Parser;

namespace ASTral.Tests;

public class SymbolExtractorTypeScriptTests
{
    private readonly SymbolExtractor _extractor = new();

    [Fact]
    public void ExtractSymbols_Interface_ExtractsAsType()
    {
        const string code = """
            interface User {
                name: string;
                age: number;
            }
            """;

        var symbols = _extractor.ExtractSymbols(code, "test.ts", "typescript");

        var iface = Assert.Single(symbols, s => s.Name == "User");
        Assert.Equal("type", iface.Kind);
    }

    [Fact]
    public void ExtractSymbols_TypeAlias_ExtractsAsType()
    {
        const string code = """
            type Result<T> = { ok: true; value: T } | { ok: false; error: string };
            """;

        var symbols = _extractor.ExtractSymbols(code, "test.ts", "typescript");

        var typeAlias = Assert.Single(symbols, s => s.Name == "Result");
        Assert.Equal("type", typeAlias.Kind);
    }

    [Fact]
    public void ExtractSymbols_Function_ExtractsCorrectly()
    {
        const string code = """
            function processUser(user: User): string {
                return user.name;
            }
            """;

        var symbols = _extractor.ExtractSymbols(code, "test.ts", "typescript");

        var func = Assert.Single(symbols, s => s.Name == "processUser");
        Assert.Equal("function", func.Kind);
        Assert.Contains("function processUser", func.Signature);
    }

    [Fact]
    public void ExtractSymbols_ClassWithMethod_ExtractsHierarchy()
    {
        const string code = """
            class Service {
                getData(): string {
                    return "data";
                }
            }
            """;

        var symbols = _extractor.ExtractSymbols(code, "test.ts", "typescript");

        var cls = Assert.Single(symbols, s => s.Kind == "class");
        Assert.Equal("Service", cls.Name);

        var method = Assert.Single(symbols, s => s.Kind == "method");
        Assert.Equal("getData", method.Name);
        Assert.Equal(cls.Id, method.Parent);
    }

    [Fact]
    public void ExtractSymbols_Enum_ExtractsAsType()
    {
        const string code = """
            enum Color { Red, Green, Blue }
            """;

        var symbols = _extractor.ExtractSymbols(code, "test.ts", "typescript");

        var enumSym = Assert.Single(symbols, s => s.Name == "Color");
        Assert.Equal("type", enumSym.Kind);
    }
}
