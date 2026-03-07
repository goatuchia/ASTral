namespace ASTral.Utils;

/// <summary>
/// Shared glob/wildcard matching supporting * and ? wildcards.
/// </summary>
internal static class GlobMatcher
{
    /// <summary>
    /// Simple glob match supporting * and ? wildcards.
    /// </summary>
    public static bool MatchesSimpleExpression(string pattern, string text, bool ignoreCase = false)
    {
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return MatchWildcard(pattern, text, comparison);
    }

    private static bool MatchWildcard(string pattern, string text, StringComparison comparison)
    {
        var pIdx = 0;
        var tIdx = 0;
        var starPIdx = -1;
        var starTIdx = -1;

        while (tIdx < text.Length)
        {
            if (pIdx < pattern.Length && (pattern[pIdx] == '?' ||
                                          string.Compare(pattern, pIdx, text, tIdx, 1, comparison) == 0))
            {
                pIdx++;
                tIdx++;
            }
            else if (pIdx < pattern.Length && pattern[pIdx] == '*')
            {
                starPIdx = pIdx;
                starTIdx = tIdx;
                pIdx++;
            }
            else if (starPIdx >= 0)
            {
                pIdx = starPIdx + 1;
                starTIdx++;
                tIdx = starTIdx;
            }
            else
            {
                return false;
            }
        }

        while (pIdx < pattern.Length && pattern[pIdx] == '*')
            pIdx++;

        return pIdx == pattern.Length;
    }
}
