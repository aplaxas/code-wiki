using System.Linq;
using CodeWiki.Model;
using CodeWiki.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace CodeWiki.Tests;

public class SymbolNodesTests
{
    static Compilation Compile(string src) => CSharpCompilation.Create("t",
        new[] { CSharpSyntaxTree.ParseText(src) },
        new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    [Fact]
    public void TypeNodeHasFullNameAndLabel()
    {
        var c = Compile("namespace N { public class Foo {} }");
        var foo = (INamedTypeSymbol)c.GetSymbolsWithName("Foo").Single();
        var n = SymbolNodes.ForType(foo, null);
        Assert.Equal(Labels.Class, n!.Label);
        Assert.Equal("N.Foo", n.FullName);
    }

    [Fact]
    public void MethodPkIncludesSignature()
    {
        var c = Compile("namespace N { public class Foo { public int Bar(string s)=>1; public int Bar()=>2; } }");
        var foo = (INamedTypeSymbol)c.GetSymbolsWithName("Foo").Single();
        var ms = foo.GetMembers("Bar").OfType<IMethodSymbol>().Select(SymbolNodes.ForMethod).ToList();
        Assert.NotEqual(ms[0].Pk, ms[1].Pk);   // 오버로드 구분
    }
}
