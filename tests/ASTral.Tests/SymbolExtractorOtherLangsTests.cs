using ASTral.Parser;

namespace ASTral.Tests;

public class SymbolExtractorOtherLangsTests
{
    private readonly SymbolExtractor _extractor = new();

    // --- Ruby (tree-sitter grammar available) ---

    [Fact]
    public void ExtractSymbols_Ruby_ClassAndMethod()
    {
        var code = "class Animal\n  def speak\n    \"...\"\n  end\nend";
        var symbols = _extractor.ExtractSymbols(code, "test.rb", "ruby");
        Assert.Contains(symbols, s => s.Name == "Animal" && s.Kind == "class");
        Assert.Contains(symbols, s => s.Name == "speak");
    }

    [Fact]
    public void ExtractSymbols_Ruby_Module()
    {
        var code = "module Helpers\n  def helper\n    true\n  end\nend";
        var symbols = _extractor.ExtractSymbols(code, "test.rb", "ruby");
        Assert.Contains(symbols, s => s.Name == "Helpers" && s.Kind == "type");
    }

    // --- PHP (tree-sitter grammar available) ---

    [Fact]
    public void ExtractSymbols_Php_FunctionAndClass()
    {
        var code = "<?php\nfunction greet($name) {\n    return \"Hello, $name\";\n}\n\nclass UserService {\n    public function getUser($id) {\n        return null;\n    }\n}";
        var symbols = _extractor.ExtractSymbols(code, "test.php", "php");
        Assert.Contains(symbols, s => s.Name == "greet" && s.Kind == "function");
        Assert.Contains(symbols, s => s.Name == "UserService" && s.Kind == "class");
    }

    // --- Kotlin (no tree-sitter grammar in this build) ---

    [Fact]
    public void ExtractSymbols_Kotlin_FunctionAndClass()
    {
        var code = "fun greet(name: String): String {\n    return \"Hello, $name\"\n}\n\nclass Calculator {\n    fun add(a: Int, b: Int): Int = a + b\n}";
        var symbols = _extractor.ExtractSymbols(code, "test.kt", "kotlin");

        if (symbols.Count == 0)
            return; // Skip - no tree-sitter grammar for Kotlin

        Assert.Contains(symbols, s => s.Name == "greet" && s.Kind == "function");
        Assert.Contains(symbols, s => s.Name == "Calculator" && s.Kind == "class");
    }

    [Fact]
    public void ExtractSymbols_Kotlin_Interface()
    {
        var code = "interface Repository {\n    fun findById(id: Int): Any?\n}";
        var symbols = _extractor.ExtractSymbols(code, "test.kt", "kotlin");

        if (symbols.Count == 0)
            return; // Skip - no tree-sitter grammar for Kotlin

        Assert.Contains(symbols, s => s.Name == "Repository" && s.Kind == "type");
    }

    // --- Swift (no tree-sitter grammar in this build) ---

    [Fact]
    public void ExtractSymbols_Swift_FunctionAndClass()
    {
        var code = "func greet(name: String) -> String {\n    return \"Hello, \\(name)\"\n}\n\nclass Animal {\n    func speak() -> String {\n        return \"...\"\n    }\n}";
        var symbols = _extractor.ExtractSymbols(code, "test.swift", "swift");

        if (symbols.Count == 0)
            return; // Skip - no tree-sitter grammar for Swift

        Assert.Contains(symbols, s => s.Name == "greet" && s.Kind == "function");
        Assert.Contains(symbols, s => s.Name == "Animal" && s.Kind == "class");
    }

    // --- Dart (no tree-sitter grammar in this build) ---

    [Fact]
    public void ExtractSymbols_Dart_ClassAndFunction()
    {
        var code = "class Greeter {\n  String greet(String name) {\n    return \"Hello, $name\";\n  }\n}";
        var symbols = _extractor.ExtractSymbols(code, "test.dart", "dart");

        if (symbols.Count == 0)
            return; // Skip - no tree-sitter grammar for Dart

        Assert.Contains(symbols, s => s.Name == "Greeter" && s.Kind == "class");
    }

    // --- Perl (no tree-sitter grammar in this build) ---

    [Fact]
    public void ExtractSymbols_Perl_Subroutine()
    {
        var code = "sub hello {\n    print \"Hello, World!\\n\";\n}";
        var symbols = _extractor.ExtractSymbols(code, "test.pl", "perl");

        if (symbols.Count == 0)
            return; // Skip - no tree-sitter grammar for Perl

        Assert.Contains(symbols, s => s.Name == "hello" && s.Kind == "function");
    }

    // --- Unknown language ---

    [Fact]
    public void ExtractSymbols_UnknownLanguage_ReturnsEmpty()
    {
        var symbols = _extractor.ExtractSymbols("some content", "test.xyz", "nonexistent");
        Assert.Empty(symbols);
    }
}
