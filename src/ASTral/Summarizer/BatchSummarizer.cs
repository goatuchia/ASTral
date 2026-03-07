using Anthropic;
using Anthropic.Models.Messages;
using ASTral.Models;
using Microsoft.Extensions.Logging;

namespace ASTral.Summarizer;

/// <summary>
/// Three-tier symbol summarization pipeline.
/// <list type="number">
///   <item><description>Tier 1 — Docstring extraction (free, instant).</description></item>
///   <item><description>Tier 2 — AI batch summarization via Claude Haiku (requires <c>ANTHROPIC_API_KEY</c>).</description></item>
///   <item><description>Tier 3 — Signature fallback (always available).</description></item>
/// </list>
/// </summary>
public sealed class BatchSummarizer
{
    private readonly string _model;
    private readonly int _maxTokensPerBatch;
    private readonly AnthropicClient? _client; // Anthropic client, if available
    private readonly ILogger<BatchSummarizer>? _logger;

    public BatchSummarizer(string model = "claude-haiku-4-5-20251001", int maxTokensPerBatch = 500, ILogger<BatchSummarizer>? logger = null)
    {
        _model = model;
        _maxTokensPerBatch = maxTokensPerBatch;
        _client = InitClient();
        _logger = logger;
    }

    /// <summary>Whether an AI client is available for Tier 2 summarization.</summary>
    public bool HasClient => _client is not null;

    /// <summary>
    /// Full three-tier summarization pipeline.
    /// Tier 1: Docstring extraction (free).
    /// Tier 2: AI batch summarization (requires ANTHROPIC_API_KEY).
    /// Tier 3: Signature fallback (always works).
    /// </summary>
    public async Task<List<Symbol>> SummarizeSymbols(List<Symbol> symbols, bool useAi = true)
    {
        // Tier 1: Extract from docstrings
        foreach (var sym in symbols)
        {
            if (!string.IsNullOrEmpty(sym.Docstring) && string.IsNullOrEmpty(sym.Summary))
            {
                sym.Summary = ExtractSummaryFromDocstring(sym.Docstring);
            }
        }

        // Tier 2: AI summarization for remaining symbols
        if (useAi && _client is not null)
        {
            await SummarizeBatch(symbols);
        }

        // Tier 3: Signature fallback for any still missing
        foreach (var sym in symbols)
        {
            if (string.IsNullOrEmpty(sym.Summary))
            {
                sym.Summary = SignatureFallback(sym);
            }
        }

        return symbols;
    }

    /// <summary>
    /// Tier 1: Extract first sentence from docstring.
    /// </summary>
    public static string ExtractSummaryFromDocstring(string docstring)
    {
        if (string.IsNullOrEmpty(docstring))
            return "";

        var firstLine = docstring.Trim().Split('\n')[0].Trim();

        // Truncate at first period if present
        var dotIndex = firstLine.IndexOf('.');
        if (dotIndex >= 0)
            firstLine = firstLine[..(dotIndex + 1)];

        return firstLine.Length > 120 ? firstLine[..120] : firstLine;
    }

    /// <summary>
    /// Tier 3: Generate summary from signature when all else fails.
    /// </summary>
    public static string SignatureFallback(Symbol symbol)
    {
        return symbol.Kind switch
        {
            "class" => $"Class {symbol.Name}",
            "constant" => $"Constant {symbol.Name}",
            "type" => $"Type definition {symbol.Name}",
            _ => !string.IsNullOrEmpty(symbol.Signature)
                ? (symbol.Signature.Length > 120 ? symbol.Signature[..120] : symbol.Signature)
                : $"{symbol.Kind} {symbol.Name}",
        };
    }

    /// <summary>
    /// Tier 1 + Tier 3 only: Docstring extraction + signature fallback. No AI required.
    /// </summary>
    public static List<Symbol> SummarizeSymbolsSimple(List<Symbol> symbols)
    {
        foreach (var sym in symbols)
        {
            if (!string.IsNullOrEmpty(sym.Summary))
                continue;

            if (!string.IsNullOrEmpty(sym.Docstring))
                sym.Summary = ExtractSummaryFromDocstring(sym.Docstring);

            if (string.IsNullOrEmpty(sym.Summary))
                sym.Summary = SignatureFallback(sym);
        }

        return symbols;
    }

    // --- Private helpers ---

    private static AnthropicClient? InitClient()
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
            return null;

        // AnthropicClient reads ANTHROPIC_API_KEY and ANTHROPIC_BASE_URL from env automatically
        return new AnthropicClient();
    }

    private async Task SummarizeBatch(List<Symbol> symbols, int batchSize = 10)
    {
        if (_client is null) return;

        var toSummarize = symbols
            .Where(s => string.IsNullOrEmpty(s.Summary) && string.IsNullOrEmpty(s.Docstring))
            .ToList();

        if (toSummarize.Count == 0) return;

        _logger?.LogInformation("Summarizing {Count} symbols with AI", toSummarize.Count);

        for (var i = 0; i < toSummarize.Count; i += batchSize)
        {
            var batch = toSummarize.Skip(i).Take(batchSize).ToList();
            await SummarizeOneBatch(batch);
        }
    }

    private async Task SummarizeOneBatch(List<Symbol> batch)
    {
        var prompt = BuildPrompt(batch);

        try
        {
            var response = await _client!.Messages.Create(new MessageCreateParams
            {
                Messages =
                [
                    new()
                    {
                        Role = Role.User,
                        Content = prompt,
                    },
                ],
                MaxTokens = _maxTokensPerBatch,
                Model = _model,
            });

            var text = response.ToString();
            var summaries = ParseResponse(text, batch.Count);

            for (var i = 0; i < batch.Count; i++)
            {
                if (!string.IsNullOrEmpty(summaries[i]))
                    batch[i].Summary = summaries[i];
                else if (string.IsNullOrEmpty(batch[i].Summary))
                    batch[i].Summary = SignatureFallback(batch[i]);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("AI summarization failed for batch: {Error}", ex.Message);
            foreach (var sym in batch)
            {
                if (string.IsNullOrEmpty(sym.Summary))
                    sym.Summary = SignatureFallback(sym);
            }
        }
    }

    private static string BuildPrompt(List<Symbol> symbols)
    {
        var lines = new List<string>
        {
            "Summarize each code symbol in ONE short sentence (max 15 words).",
            "Focus on what it does, not how.",
            "",
            "Input:",
        };

        for (var i = 0; i < symbols.Count; i++)
        {
            lines.Add($"{i + 1}. {symbols[i].Kind}: {symbols[i].Signature}");
        }

        lines.AddRange([
            "",
            "Output format: NUMBER. SUMMARY",
            "Example: 1. Authenticates users with username and password.",
            "",
            "Summaries:",
        ]);

        return string.Join("\n", lines);
    }

    /// <summary>Parse numbered summaries from AI response.</summary>
    internal static List<string> ParseResponse(string text, int expectedCount)
    {
        var summaries = new string[expectedCount];

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            var dotIdx = trimmed.IndexOf('.');
            if (dotIdx < 0) continue;

            if (int.TryParse(trimmed[..dotIdx].Trim(), out var num) && num >= 1 && num <= expectedCount)
            {
                summaries[num - 1] = trimmed[(dotIdx + 1)..].Trim();
            }
        }

        return summaries.Select(s => s ?? "").ToList();
    }
}
