using System.Collections.Generic;
using CodeWiki.Model;
using System.Linq;
using Xunit;

namespace CodeWiki.Tests;

public class GraphTests
{
    static Node N(string pk, IReadOnlyDictionary<string, string> p) =>
        new("Class", pk, "n", "full", p, new[] { "ViewModel" });

    [Fact]
    public void DedupNodeByPk()
    {
        var g = new Graph();
        g.AddNode(N("1", new Dictionary<string, string>()));
        g.AddNode(N("1", new Dictionary<string, string>()));
        Assert.Single(g.Nodes);
    }

    [Fact]
    public void NonEmptyPropWinsOverEmpty()
    {
        var g = new Graph();
        g.AddNode(N("1", new Dictionary<string, string> { ["k"] = "" }));
        g.AddNode(N("1", new Dictionary<string, string> { ["k"] = "v" }));
        Assert.Equal("v", g.Nodes.Single().Props["k"]);
    }

    [Fact]
    public void DedupEdgeByFromToType()
    {
        var g = new Graph();
        var e = new Edge("CALLS", "1", "2", new Dictionary<string, string>());
        g.AddEdge(e);
        g.AddEdge(e);
        Assert.Single(g.Edges);
    }
}
