using ASTral.Parser;

namespace ASTral.Tests;

public class SymbolExtractorGoTests
{
    private readonly SymbolExtractor _extractor = new();

    [Fact]
    public void ExtractSymbols_Function_ExtractsCorrectly()
    {
        const string code = """
            package main

            func hello(name string) string {
                return "Hello, " + name
            }
            """;

        var symbols = _extractor.ExtractSymbols(code, "test.go", "go");

        var func = Assert.Single(symbols, s => s.Name == "hello");
        Assert.Equal("function", func.Kind);
        Assert.Contains("func hello", func.Signature);
    }

    [Fact]
    public void ExtractSymbols_Struct_ExtractsAsType()
    {
        const string code = """
            package main

            type Config struct {
                Host string
                Port int
            }
            """;

        var symbols = _extractor.ExtractSymbols(code, "test.go", "go");

        var structSym = Assert.Single(symbols, s => s.Name == "Config");
        Assert.Equal("type", structSym.Kind);
    }

    [Fact]
    public void ExtractSymbols_Interface_ExtractsAsType()
    {
        const string code = """
            package main

            type Handler interface {
                Handle() error
            }
            """;

        var symbols = _extractor.ExtractSymbols(code, "test.go", "go");

        var iface = Assert.Single(symbols, s => s.Name == "Handler");
        Assert.Equal("type", iface.Kind);
    }

    [Fact]
    public void ExtractSymbols_MethodDeclaration_ExtractsAsMethod()
    {
        const string code = """
            package main

            type Config struct {
                Host string
                Port int
            }

            func (c *Config) Validate() error {
                return nil
            }
            """;

        var symbols = _extractor.ExtractSymbols(code, "test.go", "go");

        var method = Assert.Single(symbols, s => s.Name == "Validate");
        Assert.Equal("method", method.Kind);
    }
}
