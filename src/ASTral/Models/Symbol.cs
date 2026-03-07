using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace ASTral.Models;

/// <summary>
/// A code symbol extracted from source via tree-sitter.
/// </summary>
public sealed record Symbol
{
    /// <summary>Unique ID: "file_path::QualifiedName#kind"</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Source file path (e.g., "src/main.py")</summary>
    [JsonPropertyName("file")]
    public required string File { get; init; }

    /// <summary>Symbol name (e.g., "login")</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Fully qualified name (e.g., "MyClass.login")</summary>
    [JsonPropertyName("qualified_name")]
    public required string QualifiedName { get; init; }

    /// <summary>"function" | "class" | "method" | "constant" | "type"</summary>
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    /// <summary>Language identifier (e.g., "python", "csharp")</summary>
    [JsonPropertyName("language")]
    public required string Language { get; init; }

    /// <summary>Full signature line(s)</summary>
    [JsonPropertyName("signature")]
    public required string Signature { get; init; }

    /// <summary>Extracted docstring (language-specific)</summary>
    [JsonPropertyName("docstring")]
    public string Docstring { get; init; } = "";

    /// <summary>One-line summary</summary>
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    /// <summary>Decorators/attributes</summary>
    [JsonPropertyName("decorators")]
    public List<string> Decorators { get; init; } = [];

    /// <summary>Extracted search keywords</summary>
    [JsonPropertyName("keywords")]
    public List<string> Keywords { get; init; } = [];

    /// <summary>Parent symbol ID (for methods -> class)</summary>
    [JsonPropertyName("parent")]
    public string? Parent { get; init; }

    /// <summary>Start line number (1-indexed)</summary>
    [JsonPropertyName("line")]
    public int Line { get; init; }

    /// <summary>End line number (1-indexed)</summary>
    [JsonPropertyName("end_line")]
    public int EndLine { get; init; }

    /// <summary>Start byte in raw file</summary>
    [JsonPropertyName("byte_offset")]
    public int ByteOffset { get; init; }

    /// <summary>Byte length of full source</summary>
    [JsonPropertyName("byte_length")]
    public int ByteLength { get; init; }

    /// <summary>SHA-256 of symbol source bytes (for drift detection)</summary>
    [JsonPropertyName("content_hash")]
    public string ContentHash { get; init; } = "";

    /// <summary>
    /// Generate unique symbol ID.
    /// Format: {filePath}::{qualifiedName}#{kind}
    /// </summary>
    public static string MakeSymbolId(string filePath, string qualifiedName, string kind = "")
    {
        return string.IsNullOrEmpty(kind)
            ? $"{filePath}::{qualifiedName}"
            : $"{filePath}::{qualifiedName}#{kind}";
    }

    /// <summary>
    /// Compute SHA-256 hash of symbol source bytes for drift detection.
    /// </summary>
    public static string ComputeContentHash(byte[] sourceBytes)
    {
        var hash = SHA256.HashData(sourceBytes);
        return Convert.ToHexStringLower(hash);
    }
}
