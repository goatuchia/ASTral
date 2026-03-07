using ASTral.Parser;
using ASTral.Storage;
using ASTral.Summarizer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

LanguageRegistry.ApplyExtraExtensions();

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Logging.SetMinimumLevel(
    Environment.GetEnvironmentVariable("ASTRAL_LOG_LEVEL") switch
    {
        "DEBUG" => LogLevel.Debug,
        "INFO" => LogLevel.Information,
        "WARNING" => LogLevel.Warning,
        "ERROR" => LogLevel.Error,
        _ => LogLevel.Warning,
    });

var storagePath = Environment.GetEnvironmentVariable("CODE_INDEX_PATH");

builder.Services.AddSingleton(sp => new IndexStore(storagePath, sp.GetService<ILogger<IndexStore>>()));
builder.Services.AddSingleton(_ => new TokenTracker(storagePath));
builder.Services.AddSingleton(sp => new SymbolExtractor(sp.GetService<ILogger<SymbolExtractor>>()));
builder.Services.AddSingleton(sp => new BatchSummarizer(logger: sp.GetService<ILogger<BatchSummarizer>>()));

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
