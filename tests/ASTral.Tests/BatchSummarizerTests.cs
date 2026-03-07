using ASTral.Models;
using ASTral.Summarizer;

namespace ASTral.Tests;

public class BatchSummarizerTests
{
    private static Symbol MakeSymbol(
        string name,
        string kind,
        string signature = "",
        string docstring = "",
        string summary = "") => new()
    {
        Id = $"test.py::{name}#{kind}",
        File = "test.py",
        Name = name,
        QualifiedName = name,
        Kind = kind,
        Language = "python",
        Signature = signature,
        Docstring = docstring,
        Summary = summary,
    };

    // --- ExtractSummaryFromDocstring ---

    [Fact]
    public void ExtractSummaryFromDocstring_FirstSentence()
    {
        var result = BatchSummarizer.ExtractSummaryFromDocstring("Does something cool. More details.");
        Assert.Equal("Does something cool.", result);
    }

    [Fact]
    public void ExtractSummaryFromDocstring_NoDocstring_ReturnsEmpty()
    {
        Assert.Equal("", BatchSummarizer.ExtractSummaryFromDocstring(""));
    }

    [Fact]
    public void ExtractSummaryFromDocstring_LongLine_Truncates()
    {
        var longString = new string('a', 200);
        var result = BatchSummarizer.ExtractSummaryFromDocstring(longString);
        Assert.Equal(120, result.Length);
    }

    [Fact]
    public void ExtractSummaryFromDocstring_MultiLine_TakesFirst()
    {
        var result = BatchSummarizer.ExtractSummaryFromDocstring("First line\nSecond line");
        Assert.Equal("First line", result);
    }

    // --- SignatureFallback ---

    [Fact]
    public void SignatureFallback_Class_ReturnsClassName()
    {
        var sym = MakeSymbol("Foo", "class");
        Assert.Equal("Class Foo", BatchSummarizer.SignatureFallback(sym));
    }

    [Fact]
    public void SignatureFallback_Constant_ReturnsConstantName()
    {
        var sym = MakeSymbol("MAX", "constant");
        Assert.Equal("Constant MAX", BatchSummarizer.SignatureFallback(sym));
    }

    [Fact]
    public void SignatureFallback_Type_ReturnsTypeName()
    {
        var sym = MakeSymbol("Config", "type");
        Assert.Equal("Type definition Config", BatchSummarizer.SignatureFallback(sym));
    }

    [Fact]
    public void SignatureFallback_FunctionWithSignature_ReturnsSignature()
    {
        var sym = MakeSymbol("foo", "function", signature: "def foo(x)");
        Assert.Equal("def foo(x)", BatchSummarizer.SignatureFallback(sym));
    }

    [Fact]
    public void SignatureFallback_FunctionNoSignature_ReturnsFallback()
    {
        var sym = MakeSymbol("bar", "function", signature: "");
        Assert.Equal("function bar", BatchSummarizer.SignatureFallback(sym));
    }

    [Fact]
    public void SignatureFallback_LongSignature_Truncates()
    {
        var longSig = new string('x', 200);
        var sym = MakeSymbol("foo", "function", signature: longSig);
        var result = BatchSummarizer.SignatureFallback(sym);
        Assert.Equal(120, result.Length);
    }

    // --- SummarizeSymbolsSimple ---

    [Fact]
    public void SummarizeSymbolsSimple_AppliesDocstringAndFallback()
    {
        var withDoc = MakeSymbol("foo", "function", signature: "def foo()", docstring: "Does stuff. More.");
        var withoutDoc = MakeSymbol("Bar", "class");

        var result = BatchSummarizer.SummarizeSymbolsSimple([withDoc, withoutDoc]);

        Assert.Equal("Does stuff.", result[0].Summary);
        Assert.Equal("Class Bar", result[1].Summary);
    }

    [Fact]
    public void SummarizeSymbolsSimple_SkipsExistingSummary()
    {
        var sym = MakeSymbol("foo", "function", docstring: "New doc.", summary: "Existing summary");

        var result = BatchSummarizer.SummarizeSymbolsSimple([sym]);

        Assert.Equal("Existing summary", result[0].Summary);
    }

    // --- ParseResponse ---

    [Fact]
    public void ParseResponse_ValidNumberedList_ParsesCorrectly()
    {
        var text = "1. Summary one\n2. Summary two";
        var result = BatchSummarizer.ParseResponse(text, 2);

        Assert.Equal("Summary one", result[0]);
        Assert.Equal("Summary two", result[1]);
    }

    [Fact]
    public void ParseResponse_InvalidFormat_ReturnsEmpty()
    {
        var result = BatchSummarizer.ParseResponse("no numbers here", 1);
        Assert.Equal("", result[0]);
    }

    [Fact]
    public void ParseResponse_OutOfRange_IgnoresLine()
    {
        var result = BatchSummarizer.ParseResponse("5. Too high", 2);
        Assert.Equal("", result[0]);
        Assert.Equal("", result[1]);
    }
}
