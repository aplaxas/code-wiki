using System.Linq;
using CodeWiki.Model;
using CodeWiki.Roslyn;
using Microsoft.CodeAnalysis;
using Xunit;

namespace CodeWiki.Tests;

public class RoleClassifierTests
{
    static INamedTypeSymbol T(string src, string name)
    {
        var (c, _) = TestCompiler.Compile(src);
        return (INamedTypeSymbol)c.GetSymbolsWithName(name).Single();
    }

    [Fact]
    public void ViewModelByName()
    {
        var t = T("public class FooViewModel {}", "FooViewModel");
        Assert.Contains(Labels.ViewModel, new RoleClassifier().Classify(t));
    }

    [Fact]
    public void ViewByName_NotViewModel()
    {
        var t = T("public class FooView {}", "FooView");
        var roles = new RoleClassifier().Classify(t);
        Assert.Contains(Labels.View, roles);
        Assert.DoesNotContain(Labels.ViewModel, roles);
    }

    [Fact]
    public void PlainClassNoRole() =>
        Assert.Empty(new RoleClassifier().Classify(T("public class Foo {}", "Foo")));
}
