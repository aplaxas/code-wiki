using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Strazh.Tests;

public class TestCompilerTests
{
    [Fact]
    public void Resolves_class_symbol_from_in_memory_compilation()
    {
        var (tree, model) = TestCompiler.Compile("namespace N { public class Foo { } }");
        var decl = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var symbol = model.GetDeclaredSymbol(decl);
        Assert.NotNull(symbol);
        Assert.Equal("N.Foo", symbol!.ToString());
    }
}
