using System.Linq;
using CodeWiki.Extraction;
using CodeWiki.Model;
using Xunit;

namespace CodeWiki.Tests;

public class SourceLocationExtractorTests
{
    [Fact]
    public void MethodGetsRelativeSourcePathAndLines()
    {
        var (c, _) = TestCompiler.Compile(
            "namespace N { public class Foo {\n  public int Bar()=>1;\n} }",
            path: @"C:\sln\Mod\Foo.cs");
        var g = new Graph();
        new SourceLocationExtractor().Extract(new ExtractionContext(c, @"C:\sln", "T"), g);
        var bar = g.Nodes.Single(n => n.Name == "Bar");
        Assert.Equal("Mod/Foo.cs", bar.Props["sourcePath"]);
        Assert.Equal("2", bar.Props["startLine"]);
        Assert.Equal("2", bar.Props["endLine"]);
    }

    [Fact]
    public void PropertyAccessorsSkipped()
    {
        var (c, _) = TestCompiler.Compile(
            "namespace N { public class Foo { public int P { get; set; } } }",
            path: @"C:\sln\Foo.cs");
        var g = new Graph();
        new SourceLocationExtractor().Extract(new ExtractionContext(c, @"C:\sln", "T"), g);
        Assert.DoesNotContain(g.Nodes, n => n.Name is "get_P" or "set_P");
    }
}
