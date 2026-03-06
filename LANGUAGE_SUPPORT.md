# Language Support

## Supported Languages

| Language   | Extensions    | Parser                 | Symbol Types                                       | Decorators    | Docstrings                 | Notes / Limitations                                              |
| ---------- | ------------- | ---------------------- | -------------------------------------------------- | ------------- | -------------------------- | ---------------------------------------------------------------- |
| Python     | `.py`         | tree-sitter-python     | function, class, method, constant, type            | `@decorator`  | Triple-quoted strings      | Type aliases require Python 3.12+ syntax for full fidelity       |
| JavaScript | `.js`, `.jsx` | tree-sitter-javascript | function, class, method, constant                  | —             | `//` and `/** */` comments | Anonymous arrow functions without assigned names are not indexed |
| TypeScript | `.ts`, `.tsx` | tree-sitter-typescript | function, class, method, constant, type            | `@decorator`  | `//` and `/** */` comments | Decorator extraction depends on Stage-3 decorator syntax         |
| Go         | `.go`         | tree-sitter-go         | function, method, type, constant                   | —             | `//` comments              | No class hierarchy (language limitation)                         |
| Rust       | `.rs`         | tree-sitter-rust       | function, type (struct/enum/trait), impl, constant | `#[attr]`     | `///` and `//!` comments   | Macro-generated symbols are not visible to the parser            |
| Java       | `.java`       | tree-sitter-java       | method, class, type (interface/enum), constant     | `@Annotation` | `/** */` Javadoc           | Deep inner-class nesting may be flattened                        |
| PHP        | `.php`        | tree-sitter-php        | function, class, method, type (interface/trait/enum), constant | `#[Attribute]` | `/** */` PHPDoc | PHP 8+ attributes supported; language-file `<?php` tag required  |
| Dart       | `.dart`       | tree-sitter-dart       | function, class (class/mixin/extension), method, type (enum/typedef) | `@annotation` | `///` doc comments | Constructors and top-level constants are not indexed               |
| C#         | `.cs`         | tree-sitter-csharp     | class (class/record), method (method/constructor), type (interface/enum/struct/delegate) | `[Attribute]` | `/// <summary>` XML doc comments | Properties and `const` fields not indexed                          |
| C          | `.c`          | tree-sitter-c          | function, type (struct/enum/union), constant | —             | `/* */` and `//` comments | `#define` macros extracted as constants; no class/method hierarchy |
| C++        | `.cpp`, `.cc`, `.cxx`, `.hpp`, `.hh`, `.hxx`, `.h`* | tree-sitter-cpp | function, class, method, type (struct/enum/union/alias), constant | — | `/* */` and `//` comments | Namespace symbols are used for qualification but not emitted as standalone symbols |
| Swift      | `.swift`      | tree-sitter-swift      | function, class, type                              | —             | `//` and `///` comments    | Minimal support                                                   |
| Elixir     | `.ex`, `.exs` | tree-sitter-elixir | class (defmodule/defimpl), type (defprotocol/@type/@callback), method (def/defp/defmacro/defguard inside module), function (top-level def) | — | `@doc`/`@moduledoc` strings | Homoiconic grammar; custom walker required. `defstruct`, `use`, `import`, `alias` not indexed |
| Ruby       | `.rb`, `.rake` | tree-sitter-ruby  | class, type (module), method (instance + `self.` singleton), function (top-level def) | — | `#` preceding comments | `attr_accessor`, constants, and `include`/`extend` not indexed |
| Perl       | `.pl`, `.pm`  | tree-sitter-perl       | function, class (package)                          | —             | `#` comments               | Limited support                                                   |

\* `.h` uses C++ parsing first, then falls back to C when no C++ symbols are extracted.

---

## Parser Engine

All language parsing is powered by **tree-sitter** via the `TreeSitter.DotNet` NuGet package, providing:

* Incremental, error-tolerant parsing
* Uniform AST representation across languages
* Pre-compiled grammars for supported languages

**Dependency:** `TreeSitter.DotNet` (pinned in `ASTral.csproj`)

---

## Adding a New Language

1. **Define a `LanguageSpec`** in `src/ASTral/Parser/LanguageRegistry.cs`:

```csharp
var newLangSpec = new LanguageSpec(
    TsLanguage: "new_language",
    SymbolNodeTypes: new Dictionary<string, string>
    {
        ["function_definition"] = "function",
        ["class_definition"] = "class"
    },
    NameFields: new Dictionary<string, string>
    {
        ["function_definition"] = "name",
        ["class_definition"] = "name"
    },
    ParamFields: new Dictionary<string, string>
    {
        ["function_definition"] = "parameters"
    },
    ReturnTypeFields: new Dictionary<string, string>(),
    DocstringStrategy: "preceding_comment",
    DecoratorNodeType: null,
    ContainerNodeTypes: ["class_definition"],
    ConstantPatterns: [],
    TypePatterns: []
);
```

2. **Register the language**:

```csharp
LanguageRegistry["new_language"] = newLangSpec;
```

3. **Map file extensions**:

```csharp
LanguageExtensions[".ext"] = "new_language";
```

4. **Verify parser availability** — ensure `TreeSitter.DotNet` includes a grammar for the language.

5. **Add parser tests** in `tests/ASTral.Tests/`.

---

## Inspecting AST Node Types

To inspect the node types produced by tree-sitter for a source file, use the tree-sitter CLI or write a small test:

```csharp
// In a test or scratch program
var parser = new TreeSitter.Parser();
parser.SetLanguage(/* language reference */);
var tree = parser.Parse("def foo(): pass");

void PrintTree(TreeSitter.Node node, int indent = 0)
{
    Console.WriteLine(new string(' ', indent) + $"{node.Type} [{node.StartPoint}-{node.EndPoint}]");
    for (int i = 0; i < node.ChildCount; i++)
        PrintTree(node.Child(i)!, indent + 2);
}

PrintTree(tree.RootNode);
```

This inspection process helps identify the correct `SymbolNodeTypes`, `NameFields`, and extraction rules when adding support for a new language.


## Configuration

### `JCODEMUNCH_EXTRA_EXTENSIONS`

Map additional file extensions to languages at startup without modifying source:

```
JCODEMUNCH_EXTRA_EXTENSIONS=".cgi:perl,.psgi:perl,.mjs:javascript"
```

- Comma-separated `.ext:lang` pairs
- Overrides built-in mappings on collision
- Unknown languages and malformed entries are skipped with a warning
- Valid language names: `python`, `javascript`, `typescript`, `go`, `rust`, `java`, `php`, `dart`, `csharp`, `c`, `cpp`, `swift`, `elixir`, `ruby`, `perl`

Set via MCP server `env` block or any environment mechanism supported by your MCP client.
