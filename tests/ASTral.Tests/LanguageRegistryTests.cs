using ASTral.Parser;

namespace ASTral.Tests;

public class LanguageRegistryTests
{
    [Theory]
    [InlineData(".py", "python")]
    [InlineData(".js", "javascript")]
    [InlineData(".jsx", "javascript")]
    [InlineData(".ts", "typescript")]
    [InlineData(".tsx", "typescript")]
    [InlineData(".go", "go")]
    [InlineData(".rs", "rust")]
    [InlineData(".java", "java")]
    [InlineData(".cs", "csharp")]
    [InlineData(".c", "c")]
    [InlineData(".cpp", "cpp")]
    [InlineData(".swift", "swift")]
    [InlineData(".rb", "ruby")]
    [InlineData(".ex", "elixir")]
    [InlineData(".pl", "perl")]
    [InlineData(".php", "php")]
    [InlineData(".dart", "dart")]
    [InlineData(".kt", "kotlin")]
    [InlineData(".kts", "kotlin")]
    public void GetLanguageForFile_MapsExtensionToLanguage(string extension, string expectedLanguage)
    {
        var result = LanguageRegistry.GetLanguageForFile($"file{extension}");
        Assert.Equal(expectedLanguage, result);
    }

    [Theory]
    [InlineData(".xyz")]
    [InlineData(".unknown")]
    [InlineData(".randomext")]
    [InlineData("")]
    public void GetLanguageForFile_ReturnsNullForUnknownExtensions(string extension)
    {
        var fileName = string.IsNullOrEmpty(extension) ? "noextension" : $"file{extension}";
        var result = LanguageRegistry.GetLanguageForFile(fileName);
        Assert.Null(result);
    }

    [Fact]
    public void GetAllLanguages_ReturnsAtLeast15Languages()
    {
        var languages = LanguageRegistry.GetAllLanguages();
        Assert.True(languages.Count >= 16, $"Expected at least 16 languages, got {languages.Count}");
    }

    [Fact]
    public void GetAllLanguages_ContainsExpectedLanguages()
    {
        var languages = LanguageRegistry.GetAllLanguages();

        Assert.Contains("python", languages);
        Assert.Contains("javascript", languages);
        Assert.Contains("typescript", languages);
        Assert.Contains("go", languages);
        Assert.Contains("rust", languages);
        Assert.Contains("java", languages);
        Assert.Contains("csharp", languages);
        Assert.Contains("ruby", languages);
        Assert.Contains("perl", languages);
        Assert.Contains("kotlin", languages);
    }

    [Fact]
    public void AllRegisteredExtensions_AreUnique()
    {
        var extensions = LanguageRegistry.LanguageExtensions.Keys.ToList();
        var uniqueExtensions = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(uniqueExtensions.Count, extensions.Count);
    }

    [Fact]
    public void Registry_HasSpecForEveryLanguageInGetAllLanguages()
    {
        var languages = LanguageRegistry.GetAllLanguages();
        foreach (var lang in languages)
        {
            Assert.True(LanguageRegistry.Registry.ContainsKey(lang),
                $"Language '{lang}' is in GetAllLanguages but has no spec in Registry");
        }
    }

    [Fact]
    public void ApplyExtraExtensions_ValidMapping_AddsExtension()
    {
        try
        {
            LanguageRegistry.ApplyExtraExtensions(".vue:javascript");
            var result = LanguageRegistry.GetLanguageForFile("app.vue");
            Assert.Equal("javascript", result);
        }
        finally
        {
            LanguageRegistry.LanguageExtensions.Remove(".vue");
        }
    }

    [Fact]
    public void ApplyExtraExtensions_InvalidLanguage_IgnoresMapping()
    {
        var countBefore = LanguageRegistry.LanguageExtensions.Count;
        LanguageRegistry.ApplyExtraExtensions(".xyz:nonexistent");
        Assert.Equal(countBefore, LanguageRegistry.LanguageExtensions.Count);
    }

    [Fact]
    public void ApplyExtraExtensions_EmptyString_DoesNothing()
    {
        var countBefore = LanguageRegistry.LanguageExtensions.Count;
        LanguageRegistry.ApplyExtraExtensions("");
        Assert.Equal(countBefore, LanguageRegistry.LanguageExtensions.Count);
    }

    [Fact]
    public void ApplyExtraExtensions_NoColon_IgnoresEntry()
    {
        var countBefore = LanguageRegistry.LanguageExtensions.Count;
        LanguageRegistry.ApplyExtraExtensions("invalid");
        Assert.Equal(countBefore, LanguageRegistry.LanguageExtensions.Count);
    }

    [Fact]
    public void ApplyExtraExtensions_MultipleEntries_ProcessesAll()
    {
        try
        {
            LanguageRegistry.ApplyExtraExtensions(".vue:javascript,.svelte:javascript");
            Assert.Equal("javascript", LanguageRegistry.GetLanguageForFile("app.vue"));
            Assert.Equal("javascript", LanguageRegistry.GetLanguageForFile("app.svelte"));
        }
        finally
        {
            LanguageRegistry.LanguageExtensions.Remove(".vue");
            LanguageRegistry.LanguageExtensions.Remove(".svelte");
        }
    }
}
