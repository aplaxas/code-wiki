using System.Linq;
using CodeWiki.Extraction;
using CodeWiki.Model;
using Xunit;

namespace CodeWiki.Tests;

public class StructureExtractorTests
{
    [Fact]
    public void EmitsSolutionProjectContains()
    {
        var (c, _) = TestCompiler.Compile("namespace N { public class Foo {} }"); // AssemblyName="Test"
        var g = new Graph();
        new StructureExtractor().Extract(new ExtractionContext(c, "/", "MySln"), g);
        var sln = g.Nodes.Single(n => n.Label == Labels.Solution);
        var proj = g.Nodes.Single(n => n.Label == Labels.Project);
        Assert.Equal("MySln", sln.Name);
        Assert.Contains(g.Edges, e => e.Type == Rel.Contains && e.FromPk == sln.Pk && e.ToPk == proj.Pk);
    }
}
