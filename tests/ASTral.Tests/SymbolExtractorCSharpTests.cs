using ASTral.Parser;

namespace ASTral.Tests;

public class SymbolExtractorCSharpTests
{
    private readonly SymbolExtractor _extractor = new();

    [Fact]
    public void ExtractSymbols_Class_ExtractsCorrectly()
    {
        const string code = """
            public class UserService
            {
                public string GetUser(int id)
                {
                    return "user";
                }
            }
            """;

        var symbols = _extractor.ExtractSymbols(code, "test.cs", "csharp");

        var cls = Assert.Single(symbols, s => s.Kind == "class");
        Assert.Equal("UserService", cls.Name);
    }

    [Fact]
    public void ExtractSymbols_Method_HasParent()
    {
        const string code = """
            public class UserService
            {
                public string GetUser(int id)
                {
                    return "user";
                }
            }
            """;

        var symbols = _extractor.ExtractSymbols(code, "test.cs", "csharp");

        var cls = Assert.Single(symbols, s => s.Kind == "class");
        var method = Assert.Single(symbols, s => s.Kind == "method");
        Assert.Equal("GetUser", method.Name);
        Assert.Equal(cls.Id, method.Parent);
    }

    [Fact]
    public void ExtractSymbols_Interface_ExtractsAsType()
    {
        const string code = """
            public interface IRepository
            {
                void Save();
            }
            """;

        var symbols = _extractor.ExtractSymbols(code, "test.cs", "csharp");

        var iface = Assert.Single(symbols, s => s.Name == "IRepository");
        Assert.Equal("type", iface.Kind);
    }

    [Fact]
    public void ExtractSymbols_Record_ExtractsAsClass()
    {
        const string code = """
            public record UserDto(string Name, int Age);
            """;

        var symbols = _extractor.ExtractSymbols(code, "test.cs", "csharp");

        var record = Assert.Single(symbols, s => s.Name == "UserDto");
        Assert.Equal("class", record.Kind);
    }
}
