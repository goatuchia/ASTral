using ASTral.Utils;

namespace ASTral.Models;

/// <summary>
/// Index for a repository's source code.
/// </summary>
public sealed class CodeIndex
{
    public const int CurrentIndexVersion = 3;

    /// <summary>"owner/repo"</summary>
    public required string Repo { get; init; }

    public required string Owner { get; init; }
    public required string Name { get; init; }

    /// <summary>ISO timestamp</summary>
    public required string IndexedAt { get; init; }

    /// <summary>All indexed file paths</summary>
    public required List<string> SourceFiles { get; init; }

    /// <summary>Language -> file count</summary>
    public required Dictionary<string, int> Languages { get; init; }

    /// <summary>Typed symbol records</summary>
    public required List<Symbol> Symbols { get; init; }

    public int IndexVersion { get; init; } = CurrentIndexVersion;

    /// <summary>file_path -> sha256</summary>
    public Dictionary<string, string> FileHashes { get; init; } = new();

    /// <summary>HEAD commit hash at index time</summary>
    public string GitHead { get; init; } = "";

    /// <summary>file_path -> summary</summary>
    public Dictionary<string, string> FileSummaries { get; init; } = new();

    /// <summary>Find a symbol by ID.</summary>
    public Symbol? GetSymbol(string symbolId)
    {
        foreach (var sym in Symbols)
        {
            if (sym.Id == symbolId)
                return sym;
        }

        return null;
    }

    /// <summary>Search symbols with weighted scoring, returning scores.</summary>
    public List<(int Score, Symbol Sym)> SearchWithScores(
        string query,
        string? kind = null,
        string? filePattern = null)
    {
        var queryLower = query.ToLowerInvariant();
        var queryWords = new HashSet<string>(queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        var scored = new List<(int Score, Symbol Sym)>();
        foreach (var sym in Symbols)
        {
            if (kind is not null && sym.Kind != kind)
                continue;
            if (filePattern is not null && !MatchPattern(sym.File, filePattern))
                continue;

            var score = ScoreSymbol(sym, queryLower, queryWords);
            if (score > 0)
            {
                scored.Add((score, sym));
            }
        }

        scored.Sort((a, b) => b.Score.CompareTo(a.Score));
        return scored;
    }

    /// <summary>Search symbols with weighted scoring.</summary>
    public List<Symbol> Search(
        string query,
        string? kind = null,
        string? filePattern = null)
    {
        return SearchWithScores(query, kind, filePattern)
            .Select(s => s.Sym)
            .ToList();
    }

    private static bool MatchPattern(string filePath, string pattern)
    {
        return GlobMatcher.MatchesSimpleExpression(pattern, filePath, ignoreCase: true)
               || GlobMatcher.MatchesSimpleExpression($"*/{pattern}", filePath, ignoreCase: true);
    }

    internal static int ScoreSymbol(
        Symbol sym,
        string queryLower,
        HashSet<string> queryWords)
    {
        var score = 0;

        // 1. Exact name match (highest weight)
        var nameLower = sym.Name.ToLowerInvariant();
        if (queryLower == nameLower)
            score += 20;
        else if (nameLower.Contains(queryLower, StringComparison.Ordinal))
            score += 10;

        // 2. Name word overlap
        foreach (var word in queryWords)
        {
            if (nameLower.Contains(word, StringComparison.Ordinal))
                score += 5;
        }

        // 3. Signature match
        var sigLower = sym.Signature.ToLowerInvariant();
        if (sigLower.Contains(queryLower, StringComparison.Ordinal))
            score += 8;
        foreach (var word in queryWords)
        {
            if (sigLower.Contains(word, StringComparison.Ordinal))
                score += 2;
        }

        // 4. Summary match
        var summaryLower = sym.Summary.ToLowerInvariant();
        if (summaryLower.Contains(queryLower, StringComparison.Ordinal))
            score += 5;
        foreach (var word in queryWords)
        {
            if (summaryLower.Contains(word, StringComparison.Ordinal))
                score += 1;
        }

        // 5. Keyword match
        var keywordSet = new HashSet<string>(sym.Keywords, StringComparer.OrdinalIgnoreCase);
        foreach (var word in queryWords)
        {
            if (keywordSet.Contains(word))
                score += 3;
        }

        // 6. Docstring match
        var docLower = sym.Docstring.ToLowerInvariant();
        foreach (var word in queryWords)
        {
            if (docLower.Contains(word, StringComparison.Ordinal))
                score += 1;
        }

        return score;
    }

}
