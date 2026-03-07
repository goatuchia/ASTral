using System.Text;
using TreeSitter;
using ASTral.Models;
using Microsoft.Extensions.Logging;

namespace ASTral.Parser;

/// <summary>
/// Language-agnostic AST symbol extractor powered by tree-sitter.
/// Parses source code, walks the syntax tree, and produces <see cref="Symbol"/>
/// records for functions, classes, methods, constants, and type definitions.
/// </summary>
public sealed class SymbolExtractor
{
    private readonly ILogger<SymbolExtractor>? _logger;

    public SymbolExtractor(ILogger<SymbolExtractor>? logger = null)
    {
        _logger = logger;
    }

    // JS/TS variable_declarator value types that represent functions
    private static readonly HashSet<string> VariableFunctionTypes =
    [
        "arrow_function",
        "function_expression",
        "generator_function",
    ];

    // Elixir keyword sets
    private static readonly HashSet<string> ElixirModuleKw =
        ["defmodule", "defprotocol", "defimpl"];

    private static readonly HashSet<string> ElixirFunctionKw =
        ["def", "defp", "defmacro", "defmacrop", "defguard", "defguardp"];

    private static readonly HashSet<string> ElixirTypeAttrs =
        ["type", "typep", "opaque"];

    private static readonly HashSet<string> ElixirSkipAttrs =
        ["spec", "impl"];

    /// <summary>
    /// Parse source code and extract symbols using tree-sitter.
    /// </summary>
    public List<Symbol> ExtractSymbols(string content, string filePath, string language)
    {
        if (!LanguageRegistry.Registry.ContainsKey(language))
            return [];

        var sourceBytes = Encoding.UTF8.GetBytes(content);

        List<Symbol> symbols;
        if (language == "cpp")
        {
            symbols = ParseCppSymbols(content, sourceBytes, filePath);
        }
        else if (language == "elixir")
        {
            symbols = ParseElixirSymbols(content, sourceBytes, filePath);
        }
        else
        {
            var spec = LanguageRegistry.Registry[language];
            symbols = ParseWithSpec(content, sourceBytes, filePath, language, spec);
        }

        symbols = DisambiguateOverloads(symbols);
        _logger?.LogDebug("Extracted {Count} symbols from {FilePath}", symbols.Count, filePath);
        return symbols;
    }

    // -----------------------------------------------------------------------
    // Core parsing
    // -----------------------------------------------------------------------

    private List<Symbol> ParseWithSpec(
        string content,
        byte[] sourceBytes,
        string filename,
        string language,
        LanguageSpec spec)
    {
        try
        {
            var parser = new TreeSitter.Parser { Language = new Language(spec.TsLanguage) };
            var tree = parser.Parse(content);
            if (tree?.RootNode is null) return [];

            List<Symbol> symbols = [];
            WalkTree(tree.RootNode, spec, content, sourceBytes, filename, language, symbols, null, null, 0);
            return symbols;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Failed to parse {FilePath}: {Error}", filename, ex.Message);
            return [];
        }
    }

    // -----------------------------------------------------------------------
    // AST walker (_walk_tree)
    // -----------------------------------------------------------------------

    private void WalkTree(
        Node node,
        LanguageSpec spec,
        string content,
        byte[] sourceBytes,
        string filename,
        string language,
        List<Symbol> symbols,
        Symbol? parentSymbol,
        List<string>? scopeParts,
        int classScopeDepth)
    {
        if (node == null)
            return;

        // Dart: function_signature inside method_signature is handled by method_signature
        if (node.Type == "function_signature" && node.Parent?.Type == "method_signature")
            return;

        var isCpp = language == "cpp";
        var localScopeParts = scopeParts ?? [];
        var nextParent = parentSymbol;
        var nextClassScopeDepth = classScopeDepth;

        if (isCpp && node.Type == "namespace_definition")
        {
            var nsName = ExtractCppNamespaceName(node, content);
            if (nsName != null)
            {
                localScopeParts = [..localScopeParts, nsName];
            }
        }

        // Check if this node is a symbol
        if (spec.SymbolNodeTypes.ContainsKey(node.Type))
        {
            // C++ declarations include non-function declarations. Filter those out.
            var shouldExtract = true;
            if (isCpp && node.Type is "declaration" or "field_declaration" && !IsCppFunctionDeclaration(node))
                shouldExtract = false;

            if (shouldExtract)
            {
                var symbol = ExtractSymbol(node, spec, content, sourceBytes, filename, language, parentSymbol, localScopeParts, classScopeDepth);
                if (symbol != null)
                {
                    symbols.Add(symbol);
                    if (isCpp)
                    {
                        if (IsCppTypeContainer(node))
                        {
                            nextParent = symbol;
                            nextClassScopeDepth = classScopeDepth + 1;
                        }
                    }
                    else
                    {
                        nextParent = symbol;
                    }
                }
            }
        }

        // Check for arrow/function-expression variable assignments in JS/TS
        if (node.Type == "variable_declarator" && language is "javascript" or "typescript")
        {
            var varFunc = ExtractVariableFunction(node, spec, content, sourceBytes, filename, language, parentSymbol);
            if (varFunc != null)
                symbols.Add(varFunc);
        }

        // Check for constant patterns (top-level assignments with UPPER_CASE names)
        if (spec.ConstantPatterns.Contains(node.Type) && parentSymbol == null)
        {
            var constSymbol = ExtractConstant(node, spec, content, sourceBytes, filename, language);
            if (constSymbol != null)
                symbols.Add(constSymbol);
        }

        // Recurse into children
        foreach (var child in node.Children)
        {
            WalkTree(child, spec, content, sourceBytes, filename, language, symbols,
                nextParent, localScopeParts, nextClassScopeDepth);
        }
    }

    // -----------------------------------------------------------------------
    // Symbol extraction (_extract_symbol)
    // -----------------------------------------------------------------------

    private Symbol? ExtractSymbol(
        Node node,
        LanguageSpec spec,
        string content,
        byte[] sourceBytes,
        string filename,
        string language,
        Symbol? parentSymbol,
        List<string>? scopeParts,
        int classScopeDepth)
    {
        var kind = spec.SymbolNodeTypes[node.Type];

        if (node.HasError)
            return null;

        var name = ExtractName(node, spec, content);
        if (string.IsNullOrEmpty(name))
            return null;

        string qualifiedName;
        if (language == "cpp")
        {
            if (parentSymbol != null)
                qualifiedName = $"{parentSymbol.QualifiedName}.{name}";
            else if (scopeParts != null && scopeParts.Count > 0)
                qualifiedName = string.Join(".", [..scopeParts, name]);
            else
                qualifiedName = name;

            if (kind == "function" && classScopeDepth > 0)
                kind = "method";
        }
        else
        {
            if (parentSymbol != null)
            {
                qualifiedName = $"{parentSymbol.Name}.{name}";
                if (kind == "function")
                    kind = "method";
            }
            else
            {
                qualifiedName = name;
            }
        }

        var signatureNode = node;
        if (language == "cpp")
        {
            var wrapper = NearestCppTemplateWrapper(node);
            if (wrapper != null)
                signatureNode = wrapper;
        }

        var signature = BuildSignature(signatureNode, content);
        var docstring = ExtractDocstring(signatureNode, spec, content);
        var decorators = ExtractDecorators(node, spec, content);

        var startNode = signatureNode;
        var endCharIndex = node.EndIndex;
        var endLineNum = node.EndPosition.Row + 1;

        // Dart: function_signature/method_signature have their body as a next sibling
        if (node.Type is "function_signature" or "method_signature")
        {
            var nextSib = node.NextNamedSibling;
            if (nextSib?.Type == "function_body")
            {
                endCharIndex = nextSib.EndIndex;
                endLineNum = nextSib.EndPosition.Row + 1;
            }
        }

        var startByteOffset = ByteOffsetOf(startNode.StartIndex, content);
        var endByteOffset = ByteOffsetOf(endCharIndex, content);
        var symbolBytes = sourceBytes[startByteOffset..endByteOffset];
        var cHash = Symbol.ComputeContentHash(symbolBytes);

        return new Symbol
        {
            Id = Symbol.MakeSymbolId(filename, qualifiedName, kind),
            File = filename,
            Name = name,
            QualifiedName = qualifiedName,
            Kind = kind,
            Language = language,
            Signature = signature,
            Docstring = docstring,
            Decorators = decorators,
            Parent = parentSymbol?.Id,
            Line = startNode.StartPosition.Row + 1,
            EndLine = endLineNum,
            ByteOffset = startByteOffset,
            ByteLength = endByteOffset - startByteOffset,
            ContentHash = cHash,
        };
    }

    // -----------------------------------------------------------------------
    // Name extraction (_extract_name)
    // -----------------------------------------------------------------------

    private static string? ExtractName(Node node, LanguageSpec spec, string content)
    {
        // Handle type_declaration in Go - name is in type_spec child
        if (node.Type == "type_declaration")
        {
            foreach (var child in node.Children)
            {
                if (child.Type == "type_spec")
                {
                    var nameNode = child.GetChildForField("name");
                    if (nameNode != null)
                        return GetText(nameNode, content);
                }
            }
            return null;
        }

        // Dart: mixin_declaration has identifier as direct child (no field name)
        if (node.Type == "mixin_declaration")
        {
            foreach (var child in node.Children)
            {
                if (child.Type == "identifier")
                    return GetText(child, content);
            }
            return null;
        }

        // Dart: method_signature wraps function_signature or getter_signature
        if (node.Type == "method_signature")
        {
            foreach (var child in node.Children)
            {
                if (child.Type is "function_signature" or "getter_signature")
                {
                    var nameNode = child.GetChildForField("name");
                    if (nameNode != null)
                        return GetText(nameNode, content);
                }
            }
            return null;
        }

        // Dart: type_alias name is the first type_identifier child
        if (node.Type == "type_alias")
        {
            foreach (var child in node.Children)
            {
                if (child.Type == "type_identifier")
                    return GetText(child, content);
            }
            return null;
        }

        if (!spec.NameFields.TryGetValue(node.Type, out var fieldName))
            return null;

        var nameFieldNode = node.GetChildForField(fieldName);
        if (nameFieldNode == null)
            return null;

        if (spec.TsLanguage == "cpp")
            return ExtractCppName(nameFieldNode, content);

        // C function_definition: declarator is a function_declarator,
        // which wraps the actual identifier. Unwrap recursively.
        var current = nameFieldNode;
        while (current.Type is "function_declarator" or "pointer_declarator" or "reference_declarator")
        {
            var inner = current.GetChildForField("declarator");
            if (inner != null)
                current = inner;
            else
                break;
        }

        return GetText(current, content);
    }

    // -----------------------------------------------------------------------
    // Signature building (_build_signature)
    // -----------------------------------------------------------------------

    private static string BuildSignature(Node node, string content)
    {
        int endCharIndex;

        if (node.Type == "template_declaration")
        {
            var inner = node.GetChildForField("declaration");
            if (inner == null)
            {
                // fallback: last named child
                Node? lastNamed = null;
                foreach (var child in node.Children)
                {
                    if (child.IsNamed)
                        lastNamed = child;
                }
                inner = lastNamed;
            }

            if (inner != null)
            {
                var body = inner.GetChildForField("body");
                endCharIndex = body?.StartIndex ?? inner.EndIndex;
            }
            else
            {
                endCharIndex = node.EndIndex;
            }
        }
        else
        {
            var body = node.GetChildForField("body");
            endCharIndex = body?.StartIndex ?? node.EndIndex;
        }

        var sigText = content[node.StartIndex..endCharIndex].Trim();

        // Clean up: remove trailing '{', ':', etc.
        sigText = sigText.TrimEnd('{', ':', ' ', '\n', '\t');

        return sigText;
    }

    // -----------------------------------------------------------------------
    // Docstring extraction (_extract_docstring)
    // -----------------------------------------------------------------------

    private static string ExtractDocstring(Node node, LanguageSpec spec, string content)
    {
        if (spec.DocstringStrategy == "next_sibling_string")
            return ExtractPythonDocstring(node, content);
        if (spec.DocstringStrategy == "preceding_comment")
            return ExtractPrecedingComments(node, content);
        return "";
    }

    private static string ExtractPythonDocstring(Node node, string content)
    {
        var body = node.GetChildForField("body");
        if (body == null || body.Children.Count == 0)
            return "";

        foreach (var child in body.Children)
        {
            if (child.Type == "expression_statement")
            {
                var expr = child.GetChildForField("expression");
                if (expr?.Type == "string")
                    return StripQuotes(GetText(expr, content));

                // Handle tree-sitter-python 0.21+ string format
                if (child.Children.Count > 0)
                {
                    var first = child.Children[0];
                    if (first.Type is "string" or "concatenated_string")
                        return StripQuotes(GetText(first, content));
                }
            }
            else if (child.Type == "string")
            {
                return StripQuotes(GetText(child, content));
            }
        }

        return "";
    }

    private static string ExtractPrecedingComments(Node node, string content)
    {
        var comments = new List<string>();

        // Walk backwards through siblings, skipping past annotations/decorators
        var prev = node.PreviousNamedSibling;
        while (prev?.Type is "annotation" or "marker_annotation")
            prev = prev.PreviousNamedSibling;

        while (prev?.Type is "comment" or "line_comment" or "block_comment" or "documentation_comment" or "pod")
        {
            comments.Insert(0, GetText(prev, content));
            prev = prev.PreviousNamedSibling;
        }

        if (comments.Count == 0)
            return "";

        var docstring = string.Join("\n", comments);
        return CleanCommentMarkers(docstring);
    }

    // -----------------------------------------------------------------------
    // Comment cleaning (_clean_comment_markers)
    // -----------------------------------------------------------------------

    private static string CleanCommentMarkers(string text)
    {
        // POD block: strip directive lines (=pod, =head1, =cut, etc.), keep content
        if (text.TrimStart().StartsWith('='))
        {
            var contentLines = new List<string>();
            foreach (var line in text.Split('\n'))
            {
                var stripped = line.Trim();
                if (stripped.StartsWith('='))
                    continue;
                contentLines.Add(stripped);
            }
            return string.Join("\n", contentLines).Trim();
        }

        var lines = text.Split('\n');
        var cleaned = new List<string>();
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            // Remove leading comment markers (order matters: longer prefixes first)
            if (line.StartsWith("/**"))
                line = line[3..];
            else if (line.StartsWith("//!"))
                line = line[3..];
            else if (line.StartsWith("///"))
                line = line[3..];
            else if (line.StartsWith("//"))
                line = line[2..];
            else if (line.StartsWith("/*"))
                line = line[2..];
            else if (line.StartsWith('*'))
                line = line[1..];
            else if (line.StartsWith('#'))
                line = line[1..];

            // Remove trailing */
            if (line.EndsWith("*/"))
                line = line[..^2];

            cleaned.Add(line.Trim());
        }

        return string.Join("\n", cleaned).Trim();
    }

    // -----------------------------------------------------------------------
    // Quote stripping (_strip_quotes)
    // -----------------------------------------------------------------------

    private static string StripQuotes(string text)
    {
        text = text.Trim();
        if (text.StartsWith("\"\"\"") && text.EndsWith("\"\"\"") && text.Length >= 6)
            return text[3..^3].Trim();
        if (text.StartsWith("'''") && text.EndsWith("'''") && text.Length >= 6)
            return text[3..^3].Trim();
        if (text.StartsWith('"') && text.EndsWith('"') && text.Length >= 2)
            return text[1..^1].Trim();
        if (text.StartsWith('\'') && text.EndsWith('\'') && text.Length >= 2)
            return text[1..^1].Trim();
        return text;
    }

    // -----------------------------------------------------------------------
    // Decorator extraction (_extract_decorators)
    // -----------------------------------------------------------------------

    private static List<string> ExtractDecorators(Node node, LanguageSpec spec, string content)
    {
        if (string.IsNullOrEmpty(spec.DecoratorNodeType))
            return [];

        var decorators = new List<string>();

        if (spec.DecoratorFromChildren)
        {
            // C#: attribute_list nodes are direct children of the declaration
            foreach (var child in node.Children)
            {
                if (child.Type == spec.DecoratorNodeType)
                    decorators.Add(GetText(child, content).Trim());
            }
        }
        else
        {
            // Other languages: decorators are preceding siblings
            var prev = node.PreviousNamedSibling;
            while (prev?.Type == spec.DecoratorNodeType)
            {
                decorators.Insert(0, GetText(prev, content).Trim());
                prev = prev.PreviousNamedSibling;
            }
        }

        return decorators;
    }

    // -----------------------------------------------------------------------
    // Variable function extraction (_extract_variable_function)
    // -----------------------------------------------------------------------

    private Symbol? ExtractVariableFunction(
        Node node,
        LanguageSpec spec,
        string content,
        byte[] sourceBytes,
        string filename,
        string language,
        Symbol? parentSymbol)
    {
        // node is a variable_declarator
        var nameNode = node.GetChildForField("name");
        if (nameNode?.Type != "identifier")
            return null;

        var valueNode = node.GetChildForField("value");
        if (valueNode == null || !VariableFunctionTypes.Contains(valueNode.Type))
            return null;

        var name = GetText(nameNode, content);

        var kind = "function";
        string qualifiedName;
        if (parentSymbol != null)
        {
            qualifiedName = $"{parentSymbol.Name}.{name}";
            kind = "method";
        }
        else
        {
            qualifiedName = name;
        }

        // Signature: use the full declaration statement (lexical_declaration parent)
        var sigNode = node;
        var parentNodeAst = node.Parent;
        if (parentNodeAst?.Type is "lexical_declaration" or "export_statement" or "variable_declaration")
            sigNode = parentNodeAst;
        // Walk up through export_statement wrapper if present
        if (sigNode.Parent?.Type == "export_statement")
            sigNode = sigNode.Parent;

        var signature = BuildSignature(sigNode, content);
        var docstring = ExtractDocstring(sigNode, spec, content);

        var startByteOffset = ByteOffsetOf(sigNode.StartIndex, content);
        var endByteOffset = ByteOffsetOf(sigNode.EndIndex, content);
        var symbolBytes = sourceBytes[startByteOffset..endByteOffset];
        var cHash = Symbol.ComputeContentHash(symbolBytes);

        return new Symbol
        {
            Id = Symbol.MakeSymbolId(filename, qualifiedName, kind),
            File = filename,
            Name = name,
            QualifiedName = qualifiedName,
            Kind = kind,
            Language = language,
            Signature = signature,
            Docstring = docstring,
            Parent = parentSymbol?.Id,
            Line = sigNode.StartPosition.Row + 1,
            EndLine = sigNode.EndPosition.Row + 1,
            ByteOffset = startByteOffset,
            ByteLength = endByteOffset - startByteOffset,
            ContentHash = cHash,
        };
    }

    // -----------------------------------------------------------------------
    // Constant extraction (_extract_constant)
    // -----------------------------------------------------------------------

    private Symbol? ExtractConstant(
        Node node,
        LanguageSpec spec,
        string content,
        byte[] sourceBytes,
        string filename,
        string language)
    {
        // Python: UPPER_CASE top-level assignment
        if (node.Type == "assignment")
        {
            var left = node.GetChildForField("left");
            if (left?.Type == "identifier")
            {
                var name = GetText(left, content);
                if (IsConstantName(name))
                    return MakeConstantSymbol(node, name, content, sourceBytes, filename, language);
            }
        }

        // C/C++ preprocessor #define macros
        if (node.Type == "preproc_def")
        {
            var nameNode = node.GetChildForField("name");
            if (nameNode != null)
            {
                var name = GetText(nameNode, content);
                if (IsConstantName(name))
                    return MakeConstantSymbol(node, name, content, sourceBytes, filename, language);
            }
        }

        // Perl: use constant NAME => value
        if (node.Type == "use_statement")
        {
            var children = node.Children;
            if (children.Count >= 3 && children[1].Type == "package")
            {
                var pkgName = GetText(children[1], content);
                if (pkgName == "constant")
                {
                    foreach (var child in children)
                    {
                        if (child.Type == "list_expression" && child.Children.Count >= 1)
                        {
                            var constNameNode = child.Children[0];
                            if (constNameNode.Type == "autoquoted_bareword")
                            {
                                var name = GetText(constNameNode, content);
                                if (IsConstantName(name))
                                    return MakeConstantSymbol(node, name, content, sourceBytes, filename, language);
                            }
                        }
                    }
                }
            }
        }

        // Swift: let MAX_SPEED = 100 (property_declaration with let binding)
        if (node.Type == "property_declaration")
        {
            Node? binding = null;
            foreach (var child in node.Children)
            {
                if (child.Type == "value_binding_pattern")
                {
                    binding = child;
                    break;
                }
            }
            if (binding == null)
                return null;

            var mutability = binding.GetChildForField("mutability");
            if (mutability == null)
                return null;
            var mutText = GetText(mutability, content);
            if (mutText != "let")
                return null;

            var pattern = node.GetChildForField("name");
            if (pattern == null)
                return null;

            var nameNode = pattern.GetChildForField("bound_identifier");
            if (nameNode == null)
            {
                // fallback: first simple_identifier in pattern
                foreach (var child in pattern.Children)
                {
                    if (child.Type == "simple_identifier")
                    {
                        nameNode = child;
                        break;
                    }
                }
            }

            if (nameNode == null)
                return null;

            var name = GetText(nameNode, content);
            if (!IsConstantName(name))
                return null;

            return MakeConstantSymbol(node, name, content, sourceBytes, filename, language);
        }

        return null;
    }

    private Symbol MakeConstantSymbol(Node node, string name, string content, byte[] sourceBytes, string filename, string language)
    {
        var sig = GetText(node, content).Trim();
        if (sig.Length > 100) sig = sig[..100];

        var startByteOffset = ByteOffsetOf(node.StartIndex, content);
        var endByteOffset = ByteOffsetOf(node.EndIndex, content);
        var constBytes = sourceBytes[startByteOffset..endByteOffset];
        var cHash = Symbol.ComputeContentHash(constBytes);

        return new Symbol
        {
            Id = Symbol.MakeSymbolId(filename, name, "constant"),
            File = filename,
            Name = name,
            QualifiedName = name,
            Kind = "constant",
            Language = language,
            Signature = sig,
            Line = node.StartPosition.Row + 1,
            EndLine = node.EndPosition.Row + 1,
            ByteOffset = startByteOffset,
            ByteLength = endByteOffset - startByteOffset,
            ContentHash = cHash,
        };
    }

    private static bool IsConstantName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        // UPPER_CASE or CamelCase with underscore
        return name.All(c => char.IsUpper(c) || c == '_' || char.IsDigit(c))
               || (name.Length > 1 && char.IsUpper(name[0]) && name.Contains('_'));
    }

    // -----------------------------------------------------------------------
    // C++ specific helpers
    // -----------------------------------------------------------------------

    private static string? ExtractCppName(Node nameNode, string content)
    {
        var current = nameNode;
        HashSet<string> wrapperTypes =
        [
            "function_declarator",
            "pointer_declarator",
            "reference_declarator",
            "array_declarator",
            "parenthesized_declarator",
            "attributed_declarator",
            "init_declarator",
        ];

        while (wrapperTypes.Contains(current.Type))
        {
            var inner = current.GetChildForField("declarator");
            if (inner == null)
                break;
            current = inner;
        }

        // Prefer typed name children where available
        if (current.Type is "qualified_identifier" or "scoped_identifier")
        {
            var nn = current.GetChildForField("name");
            if (nn != null)
            {
                var text = GetText(nn, content).Trim();
                if (!string.IsNullOrEmpty(text))
                    return text;
            }
        }

        var subtreeName = FindCppNameInSubtree(current, content);
        if (subtreeName != null)
            return subtreeName;

        var fallbackText = GetText(current, content).Trim();
        return string.IsNullOrEmpty(fallbackText) ? null : fallbackText;
    }

    private static string? FindCppNameInSubtree(Node node, string content)
    {
        HashSet<string> directTypes =
            ["identifier", "field_identifier", "operator_name", "destructor_name", "type_identifier"];

        if (directTypes.Contains(node.Type))
        {
            var text = GetText(node, content).Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        if (node.Type is "qualified_identifier" or "scoped_identifier")
        {
            var nn = node.GetChildForField("name");
            if (nn != null)
                return FindCppNameInSubtree(nn, content);
        }

        foreach (var child in node.Children)
        {
            if (!child.IsNamed)
                continue;
            var found = FindCppNameInSubtree(child, content);
            if (found != null)
                return found;
        }

        return null;
    }

    private List<Symbol> ParseCppSymbols(string content, byte[] sourceBytes, string filename)
    {
        var cppSpec = LanguageRegistry.Registry["cpp"];
        List<Symbol> cppSymbols = [];
        var cppErrorNodes = 0;

        try
        {
            var parser = new TreeSitter.Parser { Language = new Language(cppSpec.TsLanguage) };
            var tree = parser.Parse(content);
            if (tree?.RootNode is not null)
            {
                cppErrorNodes = CountErrorNodes(tree.RootNode);
                WalkTree(tree.RootNode, cppSpec, content, sourceBytes, filename, "cpp", cppSymbols, null, null, 0);
            }
        }
        catch
        {
            cppErrorNodes = int.MaxValue;
        }

        // Non-headers are always C++
        if (!filename.EndsWith(".h", StringComparison.OrdinalIgnoreCase))
            return cppSymbols;

        // Header auto-detection: parse both C++ and C, prefer better parse quality
        if (!LanguageRegistry.Registry.TryGetValue("c", out var cSpec))
            return cppSymbols;

        List<Symbol> cSymbols = [];
        var cErrorNodes = int.MaxValue;

        try
        {
            var cParser = new TreeSitter.Parser { Language = new Language(cSpec.TsLanguage) };
            var cTree = cParser.Parse(content);
            if (cTree?.RootNode is not null)
            {
                cErrorNodes = CountErrorNodes(cTree.RootNode);
                WalkTree(cTree.RootNode, cSpec, content, sourceBytes, filename, "c", cSymbols, null, null, 0);
            }
        }
        catch
        {
            cErrorNodes = int.MaxValue;
        }

        // If only one parser yields symbols, use that
        if (cppSymbols.Count > 0 && cSymbols.Count == 0)
            return cppSymbols;
        if (cSymbols.Count > 0 && cppSymbols.Count == 0)
            return cSymbols;
        if (cppSymbols.Count == 0 && cSymbols.Count == 0)
            return cppSymbols;

        // Both yielded symbols: choose fewer parse errors first
        if (cErrorNodes < cppErrorNodes)
            return cSymbols;
        if (cppErrorNodes < cErrorNodes)
            return cppSymbols;

        // Same error quality: use lexical signal to break ties for .h
        if (LooksLikeCppHeader(content))
        {
            if (cppSymbols.Count >= cSymbols.Count)
                return cppSymbols;
        }
        else
        {
            return cSymbols;
        }

        if (cSymbols.Count > cppSymbols.Count)
            return cSymbols;

        return cppSymbols;
    }

    private static bool IsCppFunctionDeclaration(Node node)
    {
        if (node.Type is not ("declaration" or "field_declaration"))
            return true;

        var declarator = node.GetChildForField("declarator");
        if (declarator == null)
            return false;

        return HasFunctionDeclarator(declarator);
    }

    private static bool HasFunctionDeclarator(Node node)
    {
        if (node.Type is "function_declarator" or "abstract_function_declarator")
            return true;

        foreach (var child in node.Children)
        {
            if (child.IsNamed && HasFunctionDeclarator(child))
                return true;
        }

        return false;
    }

    private static bool IsCppTypeContainer(Node node)
    {
        return node.Type is "class_specifier" or "struct_specifier" or "union_specifier";
    }

    private static Node? NearestCppTemplateWrapper(Node node)
    {
        var current = node;
        Node? wrapper = null;
        while (current.Parent?.Type == "template_declaration")
        {
            wrapper = current.Parent;
            current = current.Parent;
        }
        return wrapper;
    }

    private static string? ExtractCppNamespaceName(Node node, string content)
    {
        var nameNode = node.GetChildForField("name");
        if (nameNode == null)
        {
            foreach (var child in node.Children)
            {
                if (child.Type is "namespace_identifier" or "identifier")
                {
                    nameNode = child;
                    break;
                }
            }
        }

        if (nameNode == null)
            return null;

        var name = GetText(nameNode, content).Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    private static bool LooksLikeCppHeader(string content)
    {
        string[] cppMarkers =
        [
            "namespace ", "class ", "template<", "template <",
            "constexpr", "noexcept", "[[", "std::", "using ",
            "::", "public:", "private:", "protected:", "operator", "typename",
        ];
        foreach (var marker in cppMarkers)
        {
            if (content.Contains(marker, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static int CountErrorNodes(Node node)
    {
        if (node == null) return 0;
        var count = node.Type == "ERROR" ? 1 : 0;
        foreach (var child in node.Children)
            count += CountErrorNodes(child);
        return count;
    }

    // -----------------------------------------------------------------------
    // Elixir custom extractor
    // -----------------------------------------------------------------------

    private List<Symbol> ParseElixirSymbols(string content, byte[] sourceBytes, string filename)
    {
        var spec = LanguageRegistry.Registry["elixir"];
        try
        {
            var parser = new TreeSitter.Parser { Language = new Language(spec.TsLanguage) };
            var tree = parser.Parse(content);
            if (tree?.RootNode is null) return [];

            List<Symbol> symbols = [];
            WalkElixir(tree.RootNode, content, sourceBytes, filename, symbols, null);
            return symbols;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Failed to parse {FilePath}: {Error}", filename, ex.Message);
            return [];
        }
    }

    private void WalkElixir(Node node, string content, byte[] sourceBytes, string filename, List<Symbol> symbols, Symbol? parentSymbol)
    {
        if (node == null)
            return;

        if (node.Type == "call")
        {
            var target = node.GetChildForField("target");
            if (target == null)
            {
                WalkElixirChildren(node, content, sourceBytes, filename, symbols, parentSymbol);
                return;
            }

            var keyword = GetText(target, content);

            if (ElixirModuleKw.Contains(keyword))
            {
                var sym = ExtractElixirModule(node, keyword, content, sourceBytes, filename, parentSymbol);
                if (sym != null)
                {
                    symbols.Add(sym);
                    var doBlock = FindElixirDoBlock(node);
                    if (doBlock != null)
                        WalkElixirChildren(doBlock, content, sourceBytes, filename, symbols, sym);
                    return;
                }
            }

            if (ElixirFunctionKw.Contains(keyword))
            {
                var sym = ExtractElixirFunction(node, keyword, content, sourceBytes, filename, parentSymbol);
                if (sym != null)
                    symbols.Add(sym);
                return;
            }
        }
        else if (node.Type == "unary_operator")
        {
            var innerCall = node.FirstNamedChild;
            if (innerCall?.Type == "call")
            {
                var innerTarget = innerCall.GetChildForField("target");
                if (innerTarget != null)
                {
                    var attrName = GetText(innerTarget, content);
                    if (ElixirTypeAttrs.Contains(attrName) || attrName == "callback")
                    {
                        var sym = ExtractElixirTypeAttribute(node, attrName, innerCall, content, sourceBytes, filename, parentSymbol);
                        if (sym != null)
                            symbols.Add(sym);
                        return;
                    }
                }
            }
        }

        WalkElixirChildren(node, content, sourceBytes, filename, symbols, parentSymbol);
    }

    private void WalkElixirChildren(Node node, string content, byte[] sourceBytes, string filename, List<Symbol> symbols, Symbol? parentSymbol)
    {
        foreach (var child in node.Children)
            WalkElixir(child, content, sourceBytes, filename, symbols, parentSymbol);
    }

    private static Node? FindElixirDoBlock(Node callNode)
    {
        foreach (var child in callNode.Children)
        {
            if (child.Type == "do_block")
                return child;
        }
        return null;
    }

    private Symbol? ExtractElixirModule(Node node, string keyword, string content, byte[] sourceBytes, string filename, Symbol? parentSymbol)
    {
        var arguments = GetElixirArgs(node);
        if (arguments == null)
            return null;

        string? name;
        if (keyword == "defimpl")
            name = ExtractElixirDefimplName(arguments, content);
        else
            name = ExtractElixirAliasName(arguments, content);

        if (string.IsNullOrEmpty(name))
            return null;

        var kind = keyword == "defprotocol" ? "type" : "class";
        var qualifiedName = parentSymbol != null
            ? $"{parentSymbol.QualifiedName}.{name}"
            : name;

        var signature = BuildElixirSignature(node, content);

        var doBlock = FindElixirDoBlock(node);
        var docstring = doBlock != null ? ExtractElixirModuledoc(doBlock, content) : "";

        return MakeElixirSymbol(node, content, sourceBytes, filename, name, qualifiedName, kind, parentSymbol, signature, docstring);
    }

    private static string? ExtractElixirAliasName(Node arguments, string content)
    {
        foreach (var child in arguments.Children)
        {
            if (child.Type is "alias" or "identifier" or "atom")
                return GetText(child, content).Trim();
        }
        return null;
    }

    private static string? ExtractElixirDefimplName(Node arguments, string content)
    {
        string? protoName = null;
        string? forName = null;

        foreach (var child in arguments.Children)
        {
            if (child.Type == "alias" && protoName == null)
                protoName = GetText(child, content).Trim();

            if (child.Type == "keywords")
            {
                foreach (var pair in child.Children)
                {
                    if (pair.Type == "pair")
                    {
                        var keyNode = pair.GetChildForField("key");
                        var valNode = pair.GetChildForField("value");
                        if (keyNode != null && valNode != null)
                        {
                            var keyText = GetText(keyNode, content).Trim();
                            if (keyText is "for" or "for:")
                                forName = GetText(valNode, content).Trim();
                        }
                    }
                }
            }
        }

        if (protoName != null && forName != null)
            return $"{protoName}.{forName}";
        return protoName;
    }

    private Symbol? ExtractElixirFunction(Node node, string keyword, string content, byte[] sourceBytes, string filename, Symbol? parentSymbol)
    {
        var arguments = GetElixirArgs(node);
        if (arguments == null)
            return null;

        var funcCall = arguments.FirstNamedChild;
        if (funcCall == null)
            return null;

        var actualCall = funcCall;
        if (funcCall.Type == "binary_operator")
        {
            var left = funcCall.GetChildForField("left");
            if (left != null)
                actualCall = left;
        }

        var name = ExtractElixirCallName(actualCall, content);
        if (string.IsNullOrEmpty(name))
            return null;

        var kind = (parentSymbol != null && parentSymbol.Kind is "class" or "type") ? "method" : "function";
        var qualifiedName = parentSymbol != null
            ? $"{parentSymbol.QualifiedName}.{name}"
            : name;

        var signature = BuildElixirSignature(node, content);
        var docstring = ExtractElixirDoc(node, content);

        return MakeElixirSymbol(node, content, sourceBytes, filename, name, qualifiedName, kind, parentSymbol, signature, docstring);
    }

    private static string? ExtractElixirCallName(Node callNode, string content)
    {
        if (callNode.Type == "call")
        {
            var target = callNode.GetChildForField("target");
            if (target != null)
                return GetText(target, content).Trim();
        }
        if (callNode.Type == "identifier")
            return GetText(callNode, content).Trim();
        return null;
    }

    private static string BuildElixirSignature(Node node, string content)
    {
        var doBlock = FindElixirDoBlock(node);
        var endIdx = doBlock?.StartIndex ?? node.EndIndex;
        var sig = content[node.StartIndex..endIdx].Trim().TrimEnd(',').Trim();
        return sig;
    }

    private static string ExtractElixirDoc(Node node, string content)
    {
        var prev = node.PreviousNamedSibling;
        while (prev != null)
        {
            if (prev.Type == "unary_operator")
            {
                var attr = GetElixirAttrName(prev, content);
                if (attr == "doc")
                {
                    var inner = prev.FirstNamedChild;
                    if (inner != null)
                        return ExtractElixirStringArg(inner, content);
                    return "";
                }
                if (attr != null && ElixirSkipAttrs.Contains(attr))
                {
                    prev = prev.PreviousNamedSibling;
                    continue;
                }
                break;
            }
            else if (prev.Type == "comment")
            {
                prev = prev.PreviousNamedSibling;
                continue;
            }
            else
            {
                break;
            }
        }
        return "";
    }

    private static string ExtractElixirModuledoc(Node doBlock, string content)
    {
        foreach (var child in doBlock.Children)
        {
            if (child.Type == "unary_operator")
            {
                if (GetElixirAttrName(child, content) == "moduledoc")
                {
                    var inner = child.FirstNamedChild;
                    if (inner != null)
                        return ExtractElixirStringArg(inner, content);
                }
            }
        }
        return "";
    }

    private static string ExtractElixirStringArg(Node callNode, string content)
    {
        var arguments = GetElixirArgs(callNode);
        if (arguments == null)
            return "";

        foreach (var child in arguments.Children)
        {
            if (child.Type == "string")
                return StripQuotes(GetText(child, content));
        }
        return "";
    }

    private Symbol? ExtractElixirTypeAttribute(Node node, string attrName, Node innerCall, string content, byte[] sourceBytes, string filename, Symbol? parentSymbol)
    {
        var arguments = GetElixirArgs(innerCall);
        if (arguments == null)
            return null;

        foreach (var child in arguments.Children)
        {
            if (!child.IsNamed)
                continue;

            var name = ExtractElixirTypeName(child, content);
            if (string.IsNullOrEmpty(name))
                return null;

            var kind = "type";
            var qualifiedName = parentSymbol != null
                ? $"{parentSymbol.QualifiedName}.{name}"
                : name;

            var sig = GetText(node, content);
            return MakeElixirSymbol(node, content, sourceBytes, filename, name, qualifiedName, kind, parentSymbol, sig);
        }
        return null;
    }

    private static string? ExtractElixirTypeName(Node typeExprNode, string content)
    {
        if (typeExprNode.Type == "binary_operator")
        {
            var left = typeExprNode.GetChildForField("left");
            if (left != null)
                return ExtractElixirTypeName(left, content);
        }
        if (typeExprNode.Type == "call")
        {
            var target = typeExprNode.GetChildForField("target");
            if (target != null)
                return GetText(target, content).Trim();
        }
        if (typeExprNode.Type is "identifier" or "atom")
            return GetText(typeExprNode, content).Trim();
        return null;
    }

    private static Node? GetElixirArgs(Node node)
    {
        foreach (var child in node.NamedChildren)
        {
            if (child.Type == "arguments")
                return child;
        }
        return null;
    }

    private static string? GetElixirAttrName(Node node, string content)
    {
        var inner = node.FirstNamedChild;
        if (inner?.Type == "call")
        {
            var target = inner.GetChildForField("target");
            if (target != null)
                return GetText(target, content);
        }
        return null;
    }

    private Symbol MakeElixirSymbol(
        Node node, string content, byte[] sourceBytes, string filename,
        string name, string qualifiedName, string kind, Symbol? parentSymbol,
        string signature, string docstring = "")
    {
        var startByteOffset = ByteOffsetOf(node.StartIndex, content);
        var endByteOffset = ByteOffsetOf(node.EndIndex, content);
        var symbolBytes = sourceBytes[startByteOffset..endByteOffset];

        return new Symbol
        {
            Id = Symbol.MakeSymbolId(filename, qualifiedName, kind),
            File = filename,
            Name = name,
            QualifiedName = qualifiedName,
            Kind = kind,
            Language = "elixir",
            Signature = signature,
            Docstring = docstring,
            Parent = parentSymbol?.Id,
            Line = node.StartPosition.Row + 1,
            EndLine = node.EndPosition.Row + 1,
            ByteOffset = startByteOffset,
            ByteLength = endByteOffset - startByteOffset,
            ContentHash = Symbol.ComputeContentHash(symbolBytes),
        };
    }

    // -----------------------------------------------------------------------
    // Overload disambiguation
    // -----------------------------------------------------------------------

    private static List<Symbol> DisambiguateOverloads(List<Symbol> symbols)
    {
        var idCounts = new Dictionary<string, int>();
        var result = new List<Symbol>(symbols.Count);

        foreach (var sym in symbols)
        {
            if (idCounts.TryGetValue(sym.Id, out var count))
            {
                idCounts[sym.Id] = count + 1;
                result.Add(sym with
                {
                    Id = $"{sym.Id}#{count + 1}",
                    QualifiedName = $"{sym.QualifiedName}#{count + 1}",
                });
            }
            else
            {
                idCounts[sym.Id] = 1;
                result.Add(sym);
            }
        }

        return result;
    }

    // -----------------------------------------------------------------------
    // Utility helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Extract the text of a node from the source content string.
    /// </summary>
    private static string GetText(Node node, string content)
    {
        var start = node.StartIndex;
        var end = node.EndIndex;
        if (start < 0 || end > content.Length || start >= end)
            return "";
        return content[start..end];
    }

    /// <summary>
    /// Convert a character index (from TreeSitter.DotNet) to a UTF-8 byte offset.
    /// TreeSitter.DotNet uses character indices; Symbol needs byte offsets.
    /// </summary>
    private static int ByteOffsetOf(int charIndex, string content)
    {
        var idx = Math.Min(charIndex, content.Length);
        return Encoding.UTF8.GetByteCount(content.AsSpan(0, idx));
    }
}
