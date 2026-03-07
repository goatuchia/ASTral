using ASTral.Parser;

namespace ASTral.Tests;

public class SymbolExtractorRustTests
{
    private readonly SymbolExtractor _extractor = new();

    [Fact]
    public void ExtractSymbols_Function_ExtractsCorrectly()
    {
        const string code = """
            fn hello(name: &str) -> String {
                format!("Hello, {}", name)
            }
            """;

        var symbols = _extractor.ExtractSymbols(code, "test.rs", "rust");

        var func = Assert.Single(symbols, s => s.Name == "hello");
        Assert.Equal("function", func.Kind);
        Assert.Contains("fn hello", func.Signature);
    }

    [Fact]
    public void ExtractSymbols_Struct_ExtractsAsType()
    {
        const string code = """
            struct Config {
                host: String,
                port: u16,
            }
            """;

        var symbols = _extractor.ExtractSymbols(code, "test.rs", "rust");

        var structSym = Assert.Single(symbols, s => s.Name == "Config");
        Assert.Equal("type", structSym.Kind);
    }

    [Fact]
    public void ExtractSymbols_Trait_ExtractsAsType()
    {
        const string code = """
            trait Handler {
                fn handle(&self) -> Result<(), String>;
            }
            """;

        var symbols = _extractor.ExtractSymbols(code, "test.rs", "rust");

        var traitSym = Assert.Single(symbols, s => s.Name == "Handler");
        Assert.Equal("type", traitSym.Kind);
    }

    [Fact]
    public void ExtractSymbols_ImplBlock_ExtractsFunctions()
    {
        const string code = """
            struct Config {
                host: String,
                port: u16,
            }

            impl Config {
                fn new(host: String, port: u16) -> Self {
                    Config { host, port }
                }
            }
            """;

        var symbols = _extractor.ExtractSymbols(code, "test.rs", "rust");

        Assert.Contains(symbols, s => s.Name == "Config" && s.Kind == "type");
        // The "new" function should exist inside the impl block
        var newFn = Assert.Single(symbols, s => s.Name == "new");
        Assert.NotNull(newFn);
    }
}
