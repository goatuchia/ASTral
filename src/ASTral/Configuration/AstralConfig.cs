using System.Text.Json;
using System.Text.Json.Serialization;

namespace ASTral.Configuration;

public sealed class AstralConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    [JsonPropertyName("storage_path")]
    public string? StoragePath { get; set; }

    [JsonPropertyName("log_level")]
    public string? LogLevel { get; set; }

    [JsonPropertyName("max_index_files")]
    public int? MaxIndexFiles { get; set; }

    [JsonPropertyName("extra_extensions")]
    public string? ExtraExtensions { get; set; }

    [JsonPropertyName("excluded_patterns")]
    public string[]? ExcludedPatterns { get; set; }

    public static AstralConfig Load()
    {
        var home = LoadFromFile(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".astralrc"));
        var local = LoadFromFile(Path.Combine(Directory.GetCurrentDirectory(), ".astralrc"));

        var merged = Merge(home, local);
        ApplyEnvironmentOverrides(merged);
        return merged;
    }

    public static AstralConfig LoadFromFile(string path)
    {
        if (!File.Exists(path))
            return new AstralConfig();

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AstralConfig>(json, JsonOptions) ?? new AstralConfig();
    }

    private static AstralConfig Merge(AstralConfig home, AstralConfig local)
    {
        return new AstralConfig
        {
            StoragePath = local.StoragePath ?? home.StoragePath,
            LogLevel = local.LogLevel ?? home.LogLevel,
            MaxIndexFiles = local.MaxIndexFiles ?? home.MaxIndexFiles,
            ExtraExtensions = local.ExtraExtensions ?? home.ExtraExtensions,
            ExcludedPatterns = local.ExcludedPatterns ?? home.ExcludedPatterns,
        };
    }

    private static void ApplyEnvironmentOverrides(AstralConfig config)
    {
        var storagePath = Environment.GetEnvironmentVariable("CODE_INDEX_PATH");
        if (!string.IsNullOrEmpty(storagePath))
            config.StoragePath = storagePath;

        var logLevel = Environment.GetEnvironmentVariable("ASTRAL_LOG_LEVEL");
        if (!string.IsNullOrEmpty(logLevel))
            config.LogLevel = logLevel;

        var extraExtensions = Environment.GetEnvironmentVariable("ASTRAL_EXTRA_EXTENSIONS");
        if (!string.IsNullOrEmpty(extraExtensions))
            config.ExtraExtensions = extraExtensions;
    }
}
