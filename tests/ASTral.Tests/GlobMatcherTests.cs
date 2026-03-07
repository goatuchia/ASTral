using ASTral.Utils;

namespace ASTral.Tests;

public class GlobMatcherTests
{
    [Fact]
    public void MatchesSimpleExpression_ExactMatch_ReturnsTrue()
    {
        Assert.True(GlobMatcher.MatchesSimpleExpression("hello", "hello"));
    }

    [Fact]
    public void MatchesSimpleExpression_NoMatch_ReturnsFalse()
    {
        Assert.False(GlobMatcher.MatchesSimpleExpression("hello", "world"));
    }

    [Fact]
    public void MatchesSimpleExpression_StarWildcard_MatchesExtension()
    {
        Assert.True(GlobMatcher.MatchesSimpleExpression("*.py", "main.py"));
    }

    [Fact]
    public void MatchesSimpleExpression_StarWildcard_NoMatch()
    {
        Assert.False(GlobMatcher.MatchesSimpleExpression("*.py", "main.js"));
    }

    [Fact]
    public void MatchesSimpleExpression_QuestionMark_MatchesSingleChar()
    {
        Assert.True(GlobMatcher.MatchesSimpleExpression("h?llo", "hello"));
    }

    [Fact]
    public void MatchesSimpleExpression_QuestionMark_DoesNotMatchEmpty()
    {
        Assert.False(GlobMatcher.MatchesSimpleExpression("h?llo", "hllo"));
    }

    [Fact]
    public void MatchesSimpleExpression_StarInMiddle_Matches()
    {
        Assert.True(GlobMatcher.MatchesSimpleExpression("src/*.py", "src/main.py"));
    }

    [Fact]
    public void MatchesSimpleExpression_MultipleStars_Matches()
    {
        Assert.True(GlobMatcher.MatchesSimpleExpression("*/*/*.py", "a/b/c.py"));
    }

    [Fact]
    public void MatchesSimpleExpression_CaseInsensitive_Matches()
    {
        Assert.True(GlobMatcher.MatchesSimpleExpression("*.PY", "main.py", ignoreCase: true));
    }

    [Fact]
    public void MatchesSimpleExpression_CaseSensitiveDefault_DoesNotMatch()
    {
        Assert.False(GlobMatcher.MatchesSimpleExpression("*.PY", "main.py"));
    }

    [Fact]
    public void MatchesSimpleExpression_EmptyPatternAndText_ReturnsTrue()
    {
        Assert.True(GlobMatcher.MatchesSimpleExpression("", ""));
    }

    [Fact]
    public void MatchesSimpleExpression_EmptyPattern_NonEmptyText_ReturnsFalse()
    {
        Assert.False(GlobMatcher.MatchesSimpleExpression("", "hello"));
    }

    [Fact]
    public void MatchesSimpleExpression_StarMatchesEmpty_ReturnsTrue()
    {
        Assert.True(GlobMatcher.MatchesSimpleExpression("*", ""));
    }

    [Fact]
    public void MatchesSimpleExpression_PatternLongerThanText_ReturnsFalse()
    {
        Assert.False(GlobMatcher.MatchesSimpleExpression("abcdef", "abc"));
    }

    [Theory]
    [InlineData("*", "anything", true)]
    [InlineData("a*b", "ab", true)]
    [InlineData("a*b", "aXYZb", true)]
    [InlineData("a*b", "aXYZc", false)]
    public void MatchesSimpleExpression_Theory_VariousPatterns(string pattern, string text, bool expected)
    {
        Assert.Equal(expected, GlobMatcher.MatchesSimpleExpression(pattern, text));
    }
}
