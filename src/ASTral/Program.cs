using ASTral.Configuration;
using ASTral.Parser;
using ASTral.Storage;
using ASTral.Summarizer;
using ASTral.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var config = AstralConfig.Load();

LanguageRegistry.ApplyExtraExtensions(config.ExtraExtensions ?? "");

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var logLevel = config.LogLevel switch
{
    "DEBUG" => LogLevel.Debug,
    "INFO" => LogLevel.Information,
    "WARNING" => LogLevel.Warning,
    "ERROR" => LogLevel.Error,
    _ => LogLevel.Warning,
};
builder.Logging.SetMinimumLevel(logLevel);

var storagePath = config.StoragePath;

builder.Services.AddSingleton(config);
builder.Services.AddSingleton(sp => new IndexStore(storagePath, sp.GetService<ILogger<IndexStore>>()));
builder.Services.AddSingleton(_ => new TokenTracker(storagePath));
builder.Services.AddSingleton(sp => new SymbolExtractor(sp.GetService<ILogger<SymbolExtractor>>()));
builder.Services.AddSingleton(sp => new BatchSummarizer(logger: sp.GetService<ILogger<BatchSummarizer>>()));

if (string.Equals(Environment.GetEnvironmentVariable("ASTRAL_WATCH"), "true", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddHostedService<FileWatcherService>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
