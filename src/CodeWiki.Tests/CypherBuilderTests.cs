using System.Collections.Generic;
using System.Linq;
using CodeWiki.Model;
using CodeWiki.Storage;
using Xunit;

public class CypherBuilderTests
{
    [Fact]
    public void NodeCypherHasMultiLabelAndRows()
    {
        var g = new Graph();
        g.AddNode(new Node(Labels.Class, "1", "Vm", "N.Vm", new Dictionary<string, string>(), new[] { Labels.ViewModel }));
        var (cypher, param) = CypherBuilder.NodeStatements(g).Single();
        Assert.Contains("MERGE (n:Class:ViewModel {pk: row.pk})", cypher);
        Assert.Single((List<Dictionary<string, object>>)param["rows"]);
    }

    [Fact]
    public void EdgeCypherByType()
    {
        var g = new Graph();
        g.AddEdge(new Edge(Rel.Calls, "1", "2", new Dictionary<string, string>()));
        var (cypher, _) = CypherBuilder.EdgeStatements(g).Single();
        Assert.Contains("MERGE (a)-[r:CALLS]->(b)", cypher);
    }
}
