# Architecture

## Directory Structure

```
ASTral/
├── ASTral.sln
├── global.json                         # .NET SDK version (10.0)
├── README.md
├── docs/
│   ├── ARCHITECTURE.md
│   ├── LANGUAGE_SUPPORT.md
│   ├── SECURITY.md
│   ├── SPEC.md
│   ├── TOKEN_SAVINGS.md
│   └── USER_GUIDE.md
│
├── src/ASTral/
│   ├── ASTral.csproj
│   ├── Program.cs                      # Entry point: DI setup + MCP server bootstrap
│   │
│   ├── Configuration/
│   │   └── AstralConfig.cs             # .astralrc JSON config file loading
│   │
│   ├── Models/
│   │   ├── CodeIndex.cs                # Repository index: search, scoring, pattern matching
│   │   ├── JsonElementHelpers.cs       # JSON deserialization helpers
│   │   ├── Symbol.cs                   # Sealed record: ID generation, content hashing
│   │   └── SymbolNode.cs               # Hierarchical tree building for outlines
│   │
│   ├── Parser/
│   │   ├── LanguageRegistry.cs         # LanguageSpec registry for 16 languages
│   │   └── SymbolExtractor.cs          # tree-sitter AST walking + symbol extraction
│   │
│   ├── Security/
│   │   └── SecurityValidator.cs        # Path traversal, symlink, secret, binary detection
│   │
│   ├── Services/
│   │   └── FileWatcherService.cs       # Background file watcher for auto re-indexing
│   │
│   ├── Storage/
│   │   ├── IndexStore.cs               # Save/load indexes, incremental indexing, byte-offset retrieval
│   │   └── TokenTracker.cs             # Persistent token savings counter (~/.code-index/_savings.json)
│   │
│   ├── Summarizer/
│   │   ├── BatchSummarizer.cs          # Docstring > AI > signature fallback
│   │   └── FileSummarizer.cs           # File-level heuristic summaries
│   │
│   └── Tools/
│       ├── ToolUtils.cs                # Shared tool helpers
│       ├── IndexRepoTool.cs            # GitHub repository indexing
│       ├── IndexFolderTool.cs          # Local folder indexing
│       ├── ListReposTool.cs
│       ├── GetFileTreeTool.cs
│       ├── GetFileOutlineTool.cs
│       ├── GetSymbolTool.cs
│       ├── GetSymbolsTool.cs
│       ├── SearchSymbolsTool.cs
│       ├── SearchTextTool.cs
│       ├── GetRepoOutlineTool.cs
│       └── InvalidateCacheTool.cs
│
└── tests/ASTral.Tests/
    ├── ASTral.Tests.csproj
    ├── AstralConfigTests.cs            # Config file loading, env var overrides
    ├── CodeIndexTests.cs               # Search algorithm, symbol retrieval
    ├── IndexStoreTests.cs              # Storage, incremental indexing, versioning
    ├── LanguageRegistryTests.cs        # Language spec validation
    ├── SecurityValidatorTests.cs       # Path validation, secret detection
    ├── SymbolTests.cs                  # Symbol ID generation, hashing, equality
    ├── TokenTrackerTests.cs            # Token tracking, cost calculations
    └── ToolIntegrationTests.cs         # End-to-end tool workflow tests
```

---

## Data Flow

```
Source code (GitHub API or local folder)
    |
    v
Security filters (path traversal, symlinks, secrets, binary, size)
    |
    v
tree-sitter parsing (language-specific grammars via LanguageSpec)
    |
    v
Symbol extraction (functions, classes, methods, constants, types)
    |
    v
Post-processing (overload disambiguation, content hashing)
    |
    v
Summarization (docstring > AI batch > signature fallback)
    |
    v
Storage (JSON index + raw files, atomic writes)
    |
    v
MCP tools (discovery, search, retrieval)
```

---

## Parser Design

The parser follows a **language registry pattern**. Each of the 16 supported languages defines a `LanguageSpec` describing how symbols are extracted from its AST.

```csharp
public record LanguageSpec(
    string TsLanguage,
    Dictionary<string, string> SymbolNodeTypes,
    Dictionary<string, string> NameFields,
    Dictionary<string, string> ParamFields,
    Dictionary<string, string> ReturnTypeFields,
    string DocstringStrategy,
    string? DecoratorNodeType,
    List<string> ContainerNodeTypes,
    List<string> ConstantPatterns,
    List<string> TypePatterns
);
```

The generic extractor performs two post-processing passes:

1. **Overload disambiguation**
   Duplicate symbol IDs receive numeric suffixes (`~1`, `~2`, etc.)

2. **Content hashing**
   SHA-256 hashes of symbol source content enable change detection.

---

## Symbol ID Scheme

```
{file_path}::{qualified_name}#{kind}
```

Examples:

* `src/main.py::UserService.login#method`
* `src/utils.py::authenticate#function`
* `config.py::MAX_RETRIES#constant`

IDs remain stable across re-indexing as long as the file path, qualified name, and symbol kind remain unchanged.

---

## Storage

Indexes are stored at `~/.code-index/` (configurable via `CODE_INDEX_PATH`):

* `{owner}-{name}.json` — metadata, file hashes, symbol metadata, file summaries
* `{owner}-{name}/` — cached raw source files

Each symbol records byte offsets, allowing **O(1)** retrieval via `seek()` + `read()` without re-parsing.

Incremental indexing compares stored file hashes with current hashes, reprocessing only changed files. Writes are atomic (temporary file + rename).

---

## Security

All file operations pass through `SecurityValidator`:

* Path traversal protection via validated resolved paths
* Symlink target validation
* Secret-file exclusion using predefined patterns
* Binary file detection (extension-based + null-byte content sniffing)
* Safe encoding reads using replacement characters

---

## Response Envelope

All tool responses include metadata:

```json
{
  "result": "...",
  "_meta": {
    "timing_ms": 42,
    "repo": "owner/repo",
    "symbol_count": 387,
    "truncated": false,
    "tokens_saved": 2450,
    "total_tokens_saved": 184320,
    "cost_avoided": { "claude_opus": 0.0368, "gpt5_latest": 0.0245 },
    "total_cost_avoided": { "claude_opus": 2.76, "gpt5_latest": 1.84 }
  }
}
```

`tokens_saved` and `total_tokens_saved` are included on all retrieval and search tools. The running total is persisted to `~/.code-index/_savings.json` across sessions.

---

## Search Algorithm

`search_symbols` uses weighted scoring:

| Match type              | Weight                |
| ----------------------- | --------------------- |
| Exact name match        | +20                   |
| Name substring          | +10                   |
| Name word overlap       | +5 per word           |
| Signature match         | +8 (full) / +2 (word) |
| Summary match           | +5 (full) / +1 (word) |
| Docstring/keyword match | +3 / +1 per word      |

Filters (kind, language, file_pattern) are applied before scoring. Results scoring zero are excluded.

---

## Dependency Injection

ASTral uses `Microsoft.Extensions.Hosting` for dependency injection and server lifecycle:

```csharp
builder.Services.AddSingleton<IndexStore>();
builder.Services.AddSingleton<TokenTracker>();
builder.Services.AddSingleton<SymbolExtractor>();
builder.Services.AddSingleton<BatchSummarizer>();
```

MCP tools are auto-discovered from the assembly via `[McpServerTool]` attributes and receive dependencies as method parameters.

---

## Dependencies

| Package                          | Purpose                                   |
| -------------------------------- | ----------------------------------------- |
| `ModelContextProtocol`           | MCP server framework                      |
| `Microsoft.Extensions.Hosting`   | Dependency injection and hosting           |
| `TreeSitter.DotNet`             | tree-sitter AST parsing                    |
| `MAB.DotIgnore`                 | `.gitignore` pattern matching              |
| `Anthropic`                     | AI summarization via Claude Haiku           |
| `xunit.v3`                      | Unit testing (test project)                |
| `Microsoft.NET.Test.Sdk`        | Test infrastructure (test project)         |
