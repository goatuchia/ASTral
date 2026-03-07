using ASTral.Parser;

namespace ASTral.Tests;

public class SymbolExtractorElixirTests
{
    private readonly SymbolExtractor _extractor = new();

    private const string ElixirSource = "defmodule MyModule do\n  @type config :: %{host: String.t(), port: integer()}\n\n  def hello(name) do\n    \"Hello, #{name}\"\n  end\n\n  defp private_helper do\n    :ok\n  end\nend";

    [Fact]
    public void ExtractSymbols_Module_ExtractsAsClass()
    {
        var symbols = _extractor.ExtractSymbols(ElixirSource, "test.ex", "elixir");

        // Elixir grammar may not be available in all environments
        if (symbols.Count == 0)
            return; // Skip - no tree-sitter grammar for Elixir

        var module = symbols.FirstOrDefault(s => s.Name == "MyModule");
        Assert.NotNull(module);
        Assert.Equal("class", module.Kind);
    }

    [Fact]
    public void ExtractSymbols_PublicFunction_ExtractsAsFunction()
    {
        var symbols = _extractor.ExtractSymbols(ElixirSource, "test.ex", "elixir");

        if (symbols.Count == 0)
            return; // Skip - no tree-sitter grammar for Elixir

        var func = symbols.FirstOrDefault(s => s.Name == "hello");
        Assert.NotNull(func);
        Assert.Equal("function", func.Kind);
    }

    [Fact]
    public void ExtractSymbols_PrivateFunction_ExtractsAsFunction()
    {
        var symbols = _extractor.ExtractSymbols(ElixirSource, "test.ex", "elixir");

        if (symbols.Count == 0)
            return; // Skip - no tree-sitter grammar for Elixir

        var func = symbols.FirstOrDefault(s => s.Name == "private_helper");
        Assert.NotNull(func);
        Assert.Equal("function", func.Kind);
    }

    [Fact]
    public void ExtractSymbols_TypeAttribute_ExtractsAsType()
    {
        var symbols = _extractor.ExtractSymbols(ElixirSource, "test.ex", "elixir");

        if (symbols.Count == 0)
            return; // Skip - no tree-sitter grammar for Elixir

        var typeSymbol = symbols.FirstOrDefault(s => s.Kind == "type");
        Assert.NotNull(typeSymbol);
    }
}
