using System.Linq;
using CodeWiki.Extraction;
using CodeWiki.Model;
using CodeWiki.Roslyn;
using Xunit;

namespace CodeWiki.Tests;

public class TypeUsageExtractorTests
{
    [Fact]
    public void MethodUsesParameterAndReturnType()
    {
        var (c, _) = TestCompiler.Compile(@"namespace N {
            public class Filter {} public class Result {}
            public class Svc { public Result Search(Filter f)=>new Result(); } }");
        var g = new Graph();
        new TypeUsageExtractor(new RoleClassifier()).Extract(new ExtractionContext(c, "/", "T"), g);
        var m = g.Nodes.Single(n => n.Name == "Search");
        var filter = g.Nodes.Single(n => n.Name == "Filter");
        var result = g.Nodes.Single(n => n.Name == "Result");
        Assert.Contains(g.Edges, e => e.Type == Rel.UsesType && e.FromPk == m.Pk && e.ToPk == filter.Pk);
        Assert.Contains(g.Edges, e => e.Type == Rel.UsesType && e.FromPk == m.Pk && e.ToPk == result.Pk);
    }

    [Fact]
    public void SkipsFrameworkTypes()
    {
        var (c, _) = TestCompiler.Compile("namespace N { public class Svc { public string M(int x)=>\"\"; } }");
        var g = new Graph();
        new TypeUsageExtractor(new RoleClassifier()).Extract(new ExtractionContext(c, "/", "T"), g);
        Assert.DoesNotContain(g.Edges, e => e.Type == Rel.UsesType);
    }
}
