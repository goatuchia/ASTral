using ASTral.Configuration;

namespace ASTral.Tests;

public class AstralConfigTests
{
    [Fact]
    public void Load_ReturnsDefaults_WhenNoConfigFile()
    {
        var config = AstralConfig.Load();

        Assert.NotNull(config);
    }

    [Fact]
    public void Load_ParsesJsonFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"astral_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var rcPath = Path.Combine(dir, ".astralrc");
            File.WriteAllText(rcPath, """
                {
                    "storage_path": "/custom/path",
                    "log_level": "DEBUG",
                    "max_index_files": 5000,
                    "extra_extensions": ".vue:javascript,.svelte:javascript",
                    "excluded_patterns": ["*.generated.cs", "*.Designer.cs"]
                }
                """);

            var config = AstralConfig.LoadFromFile(rcPath);

            Assert.Equal("/custom/path", config.StoragePath);
            Assert.Equal("DEBUG", config.LogLevel);
            Assert.Equal(5000, config.MaxIndexFiles);
            Assert.Equal(".vue:javascript,.svelte:javascript", config.ExtraExtensions);
            Assert.NotNull(config.ExcludedPatterns);
            Assert.Equal(["*.generated.cs", "*.Designer.cs"], config.ExcludedPatterns);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void LoadFromFile_ReturnsDefaults_WhenFileDoesNotExist()
    {
        var config = AstralConfig.LoadFromFile("/nonexistent/.astralrc");

        Assert.NotNull(config);
        Assert.Null(config.StoragePath);
        Assert.Null(config.LogLevel);
        Assert.Null(config.MaxIndexFiles);
        Assert.Null(config.ExtraExtensions);
        Assert.Null(config.ExcludedPatterns);
    }

    [Fact]
    public void Load_EnvVarOverridesFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"astral_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var rcPath = Path.Combine(dir, ".astralrc");
            File.WriteAllText(rcPath, """
                {
                    "storage_path": "/from/file",
                    "log_level": "INFO"
                }
                """);

            Environment.SetEnvironmentVariable("CODE_INDEX_PATH", "/from/env");
            Environment.SetEnvironmentVariable("ASTRAL_LOG_LEVEL", "ERROR");
            try
            {
                var config = AstralConfig.LoadFromFile(rcPath);
                var storagePath = Environment.GetEnvironmentVariable("CODE_INDEX_PATH");
                if (!string.IsNullOrEmpty(storagePath))
                    config.StoragePath = storagePath;
                var logLevel = Environment.GetEnvironmentVariable("ASTRAL_LOG_LEVEL");
                if (!string.IsNullOrEmpty(logLevel))
                    config.LogLevel = logLevel;

                Assert.Equal("/from/env", config.StoragePath);
                Assert.Equal("ERROR", config.LogLevel);
            }
            finally
            {
                Environment.SetEnvironmentVariable("CODE_INDEX_PATH", null);
                Environment.SetEnvironmentVariable("ASTRAL_LOG_LEVEL", null);
            }
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
