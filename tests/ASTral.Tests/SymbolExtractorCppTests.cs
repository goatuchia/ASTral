using ASTral.Parser;

namespace ASTral.Tests;

public class SymbolExtractorCppTests
{
    private readonly SymbolExtractor _extractor = new();

    [Fact]
    public void ExtractSymbols_Function_ExtractsCorrectly()
    {
        const string code = """
            #include <string>

            void hello(const std::string& name) {
                // ...
            }
            """;

        var symbols = _extractor.ExtractSymbols(code, "test.cpp", "cpp");

        var func = Assert.Single(symbols, s => s.Name == "hello");
        Assert.Equal("function", func.Kind);
    }

    [Fact]
    public void ExtractSymbols_Struct_ExtractsAsType()
    {
        const string code = """
            struct Config {
                std::string host;
                int port;
            };
            """;

        var symbols = _extractor.ExtractSymbols(code, "test.cpp", "cpp");

        var structSym = Assert.Single(symbols, s => s.Name == "Config");
        Assert.Equal("type", structSym.Kind);
    }

    [Fact]
    public void ExtractSymbols_Class_ExtractsCorrectly()
    {
        const string code = """
            class Service {
            public:
                void run();
            };
            """;

        var symbols = _extractor.ExtractSymbols(code, "test.cpp", "cpp");

        var cls = Assert.Single(symbols, s => s.Name == "Service");
        Assert.Equal("class", cls.Kind);
    }
}
