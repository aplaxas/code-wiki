using System.Linq;
using CodeWiki.Extraction;
using CodeWiki.Model;
using CodeWiki.Roslyn;
using Xunit;

namespace CodeWiki.Tests;

public class TypeExtractorTests
{
    static Graph Run(string src)
    {
        var (c, _) = TestCompiler.Compile(src);
        var g = new Graph();
        new TypeExtractor(new RoleClassifier()).Extract(new ExtractionContext(c, "/", "T"), g);
        return g;
    }

    [Fact]
    public void EmitsClassAndInterfaceImplementsEdge()
    {
        var g = Run("namespace N { public interface IFoo {} public class Foo : IFoo {} }");
        var foo = g.Nodes.Single(n => n.Name == "Foo");
        var ifoo = g.Nodes.Single(n => n.Name == "IFoo");
        Assert.Equal(Labels.Class, foo.Label);
        Assert.Equal(Labels.Interface, ifoo.Label);
        Assert.Contains(g.Edges, e => e.Type == Rel.Implements && e.FromPk == foo.Pk && e.ToPk == ifoo.Pk);
    }

    [Fact]
    public void InheritsEdge()
    {
        var g = Run("namespace N { public class B {} public class D : B {} }");
        var d = g.Nodes.Single(n => n.Name == "D");
        var b = g.Nodes.Single(n => n.Name == "B");
        Assert.Contains(g.Edges, e => e.Type == Rel.Inherits && e.FromPk == d.Pk && e.ToPk == b.Pk);
    }

    [Fact]
    public void UnresolvedBaseSkipped()
    {
        // BindableBase 미참조 - 불변식 #3
        var g = Run("namespace N { public class Vm : BindableBase {} }");
        Assert.DoesNotContain(g.Edges, e => e.Type == Rel.Inherits);
    }
}
