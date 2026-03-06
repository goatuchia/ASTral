# Technical Specification

## Overview

**ASTral** pre-indexes repository source code using tree-sitter AST parsing, extracting a structured catalog of every symbol (function, class, method, constant, type). Each symbol stores its **signature + one-line summary**, with full source retrievable on demand via O(1) byte-offset seeking.

Built with .NET 10 and C#, ASTral runs as an MCP server using the `ModelContextProtocol` framework.

### Token Savings

| Scenario                        | Raw dump        | ASTral        | Savings   |
| ------------------------------- | --------------- | ------------- | --------- |
| Explore 500-file repo structure | ~200,000 tokens | ~2,000 tokens | **99%**   |
| Find a specific function        | ~40,000 tokens  | ~200 tokens   | **99.5%** |
| Read one function body          | ~40,000 tokens  | ~500 tokens   | **98.7%** |
| Understand module API           | ~15,000 tokens  | ~800 tokens   | **94.7%** |

---

## MCP Tools (11)

### Indexing Tools

#### `index_repo` — Index a GitHub repository

```json
{
  "url": "owner/repo",
  "use_ai_summaries": true
}
```

Fetches source via `git/trees?recursive=1` (single API call), filters through the security pipeline, parses with tree-sitter, summarizes, and saves the index plus raw files. Uses concurrent file fetching (10-thread limit).

#### `index_folder` — Index a local folder

```json
{
  "path": "/path/to/project",
  "extra_ignore_patterns": ["*.generated.*"],
  "follow_symlinks": false
}
```

Walks the local directory with full security controls: path traversal prevention, symlink escape protection, secret detection, binary filtering, and `.gitignore` respect (via MAB.DotIgnore).

#### `invalidate_cache` — Delete index for a repository

```json
{
  "repo": "owner/repo"
}
```

Deletes both the index JSON and raw content directory.

---

### Discovery Tools

#### `list_repos` — List indexed repositories

No input required. Returns all indexed repositories with symbol counts, file counts, languages, and index version.

#### `get_file_tree` — Get file structure

```json
{
  "repo": "owner/repo",
  "path_prefix": "src/"
}
```

Returns a nested directory tree with per-file language and symbol count annotations.

#### `get_file_outline` — Get symbols in a file

```json
{
  "repo": "owner/repo",
  "file_path": "src/main.py"
}
```

Returns a hierarchical symbol tree (classes contain methods) with signatures and summaries. Includes file-level heuristic summary. Source code is not included; use `get_symbol` for that.

#### `get_repo_outline` — High-level repository overview

```json
{
  "repo": "owner/repo"
}
```

Returns directory file counts, language breakdown, and symbol kind distribution. Lighter than `get_file_tree`.

---

### Retrieval Tools

#### `get_symbol` — Get full source of a symbol

```json
{
  "repo": "owner/repo",
  "symbol_id": "src/main.py::MyClass.login#method",
  "verify": true,
  "context_lines": 3
}
```

Retrieves source via byte-offset seeking (O(1)). Optional `verify` re-hashes the source and compares it to the stored `content_hash`. Optional `context_lines` (0-50) includes surrounding lines.

#### `get_symbols` — Batch retrieve multiple symbols

```json
{
  "repo": "owner/repo",
  "symbol_ids": ["id1", "id2", "id3"]
}
```

Returns a list of symbols plus an error list for any IDs not found.

---

### Search Tools

#### `search_symbols` — Search across all symbols

```json
{
  "repo": "owner/repo",
  "query": "authenticate",
  "kind": "function",
  "language": "python",
  "file_pattern": "src/**/*.py",
  "max_results": 10
}
```

Weighted scoring search across name, signature, summary, keywords, and docstring. All filters are optional. Maximum 100 results.

#### `search_text` — Full-text search across file contents

```json
{
  "repo": "owner/repo",
  "query": "TODO",
  "file_pattern": "*.py",
  "max_results": 20
}
```

Case-insensitive substring search across indexed file contents. Returns matching lines with file, line number, and surrounding context. Maximum 100 matches. Use when symbol search misses (string literals, comments, config values).

---

## Data Models

### Symbol

```csharp
public sealed record Symbol
{
    public string Id { get; init; }              // "{file_path}::{qualified_name}#{kind}"
    public string File { get; init; }            // Relative file path
    public string Name { get; init; }            // Symbol name
    public string QualifiedName { get; init; }   // Dot-separated with parent context
    public string Kind { get; init; }            // function | class | method | constant | type
    public string Language { get; init; }        // python | javascript | typescript | go | rust | java | php | dart | csharp | c | cpp | swift | elixir | ruby | perl
    public string Signature { get; init; }       // Full signature line(s)
    public string ContentHash { get; init; }     // SHA-256 of source bytes (drift detection)
    public string Docstring { get; init; }
    public string Summary { get; init; }
    public string[] Decorators { get; init; }    // Decorators/attributes
    public string[] Keywords { get; init; }      // Search keywords
    public string? Parent { get; init; }         // Parent symbol ID (methods > class)
    public int Line { get; init; }               // Start line (1-indexed)
    public int EndLine { get; init; }            // End line (1-indexed)
    public int ByteOffset { get; init; }         // Start byte in raw file
    public int ByteLength { get; init; }         // Byte length of source
}
```

### CodeIndex

```csharp
public sealed record CodeIndex
{
    public string Repo { get; init; }                         // "owner/repo"
    public string Owner { get; init; }
    public string Name { get; init; }
    public string IndexedAt { get; init; }                    // ISO timestamp
    public int IndexVersion { get; init; }                    // Schema version (current: 3)
    public string[] SourceFiles { get; init; }
    public Dictionary<string, int> Languages { get; init; }   // language > file count
    public List<Dictionary<string, JsonElement>> Symbols { get; init; }
    public Dictionary<string, string> FileHashes { get; init; }  // file_path > SHA-256
    public string GitHead { get; init; }                      // HEAD commit hash
    public Dictionary<string, string> FileSummaries { get; init; } // file > heuristic summary
}
```

---

## File Discovery

### GitHub Repositories

Single API call:
`GET /repos/{owner}/{repo}/git/trees/HEAD?recursive=1`

### Local Folders

Recursive directory walk with the full security pipeline, using MAB.DotIgnore for `.gitignore` pattern matching.

### Filtering Pipeline (Both Paths)

1. **Extension filter** — must be in `LanguageExtensions` (.py, .js, .jsx, .ts, .tsx, .go, .rs, .java, .php, .dart, .cs, .c, .h, .cpp, .cc, .cxx, .hpp, .hh, .hxx, .swift, .ex, .exs, .rb, .rake, .pl, .pm)
2. **Skip patterns** — `node_modules/`, `vendor/`, `.git/`, `build/`, `dist/`, lock files, minified files, etc.
3. **`.gitignore`** — respected via the `MAB.DotIgnore` library
4. **Secret detection** — `.env`, `*.pem`, `*.key`, `*.p12`, credentials files excluded
5. **Binary detection** — extension-based + null-byte content sniffing
6. **Size limit** — 500 KB per file (configurable)
7. **File count limit** — 10,000 files max (configurable via `JCODEMUNCH_MAX_INDEX_FILES`), prioritized: `src/` > `lib/` > `pkg/` > `cmd/` > `internal/` > remainder

---

## Response Envelope

All tools return a `_meta` object with timing, context, and token savings:

```json
{
  "_meta": {
    "timing_ms": 42,
    "repo": "owner/repo",
    "symbol_count": 387,
    "truncated": false,
    "content_verified": true,
    "tokens_saved": 2450,
    "total_tokens_saved": 184320,
    "cost_avoided": { "claude_opus": 0.0368, "gpt5_latest": 0.0245 },
    "total_cost_avoided": { "claude_opus": 2.76, "gpt5_latest": 1.84 }
  }
}
```

- **`tokens_saved`**: Tokens saved by this specific call (raw file bytes vs response bytes, divided by 4)
- **`total_tokens_saved`**: Cumulative tokens saved across all tool calls, persisted to `~/.code-index/_savings.json`
- **`cost_avoided`**: Per-model cost savings (Claude Opus at $15/1M tokens, GPT-5 at $10/1M tokens)

Present on: `get_file_outline`, `get_symbol`, `get_symbols`, `get_repo_outline`, `search_symbols`.

---

## Error Handling

All errors return:

```json
{
  "error": "Human-readable message",
  "_meta": { "timing_ms": 1 }
}
```

| Scenario                          | Behavior                                              |
| --------------------------------- | ----------------------------------------------------- |
| Repository not found (GitHub 404) | Error with message                                    |
| Rate limited (GitHub 403)         | Error with reset time; suggest setting `GITHUB_TOKEN` |
| File fetch fails                  | File skipped; indexing continues                      |
| Parse fails (single file)         | File skipped; indexing continues                      |
| No source files found             | Error message returned                                |
| Symbol ID not found               | Error in response                                     |
| Repository not indexed            | Error suggesting indexing first                       |
| AI summarization fails            | Falls back to docstring or signature                  |
| Index version mismatch            | Old index ignored; full reindex required              |

---

## Environment Variables

| Variable                       | Purpose                                                  | Required |
| ------------------------------ | -------------------------------------------------------- | -------- |
| `GITHUB_TOKEN`                 | GitHub API authentication (higher limits, private repos) | No       |
| `ANTHROPIC_API_KEY`            | AI summarization via Claude Haiku                        | No       |
| `CODE_INDEX_PATH`              | Custom storage path (default: `~/.code-index/`)          | No       |
| `JCODEMUNCH_LOG_LEVEL`         | Log verbosity (DEBUG, INFO, WARNING, ERROR)              | No       |
| `JCODEMUNCH_MAX_INDEX_FILES`   | Maximum files to index (default: 10,000)                 | No       |
| `JCODEMUNCH_EXTRA_EXTENSIONS`  | Custom extension-to-language mappings                    | No       |
