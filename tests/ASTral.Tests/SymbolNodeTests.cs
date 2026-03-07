using ASTral.Models;

namespace ASTral.Tests;

public class SymbolNodeTests
{
    private static Symbol MakeSymbol(
        string id, string name, string kind,
        string? parent = null)
    {
        return new Symbol
        {
            Id = id,
            Name = name,
            Kind = kind,
            File = "src/main.py",
            Signature = $"def {name}():",
            Summary = "",
            Docstring = "",
            Keywords = [],
            QualifiedName = name,
            Language = "python",
            Decorators = [],
            Parent = parent,
            Line = 1,
            EndLine = 10,
            ByteOffset = 0,
            ByteLength = 50,
            ContentHash = "",
        };
    }

    [Fact]
    public void BuildTree_WithNoSymbols_ReturnsEmpty()
    {
        var result = SymbolNode.BuildTree([]);

        Assert.Empty(result);
    }

    [Fact]
    public void BuildTree_WithStandaloneSymbols_ReturnsAllAsRoots()
    {
        var sym1 = MakeSymbol("s1", "func_a", "function");
        var sym2 = MakeSymbol("s2", "func_b", "function");

        var roots = SymbolNode.BuildTree([sym1, sym2]);

        Assert.Equal(2, roots.Count);
        Assert.All(roots, r => Assert.Empty(r.Children));
    }

    [Fact]
    public void BuildTree_WithParentChild_NestsCorrectly()
    {
        var cls = MakeSymbol("c1", "MyClass", "class");
        var method = MakeSymbol("m1", "my_method", "method", parent: "c1");

        var roots = SymbolNode.BuildTree([cls, method]);

        Assert.Single(roots);
        Assert.Equal("MyClass", roots[0].Symbol.Name);
        Assert.Single(roots[0].Children);
        Assert.Equal("my_method", roots[0].Children[0].Symbol.Name);
    }

    [Fact]
    public void BuildTree_WithOrphanChild_TreatsAsRoot()
    {
        var method = MakeSymbol("m1", "orphan_method", "method", parent: "nonexistent");

        var roots = SymbolNode.BuildTree([method]);

        Assert.Single(roots);
        Assert.Equal("orphan_method", roots[0].Symbol.Name);
    }

    [Fact]
    public void FlattenTree_ReturnsCorrectDepths()
    {
        var cls = MakeSymbol("c1", "MyClass", "class");
        var method = MakeSymbol("m1", "my_method", "method", parent: "c1");

        var tree = SymbolNode.BuildTree([cls, method]);
        var flat = SymbolNode.FlattenTree(tree);

        Assert.Equal(2, flat.Count);
        Assert.Equal("MyClass", flat[0].Symbol.Name);
        Assert.Equal(0, flat[0].Depth);
        Assert.Equal("my_method", flat[1].Symbol.Name);
        Assert.Equal(1, flat[1].Depth);
    }

    [Fact]
    public void FlattenTree_EmptyTree_ReturnsEmpty()
    {
        var flat = SymbolNode.FlattenTree([]);

        Assert.Empty(flat);
    }
}
