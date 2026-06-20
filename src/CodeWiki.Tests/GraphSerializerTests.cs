using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWiki.Model;
using CodeWiki.Storage;
using Xunit;

public class GraphSerializerTests
{
    [Fact]
    public void RoundTrip()
    {
        var g = new Graph();
        g.AddNode(new Node(Labels.Class,"1","Foo","N.Foo", new Dictionary<string,string>{["k"]="v"}, new[]{Labels.ViewModel}));
        g.AddNode(new Node(Labels.Method,"2","Bar","N.Foo.Bar", new Dictionary<string,string>(), new string[0]));
        g.AddEdge(new Edge(Rel.Declares,"1","2", new Dictionary<string,string>()));
        var path = Path.GetTempFileName();
        GraphSerializer.Write(g, path);
        var g2 = GraphSerializer.Read(path);
        Assert.Equal(2, g2.Nodes.Count);
        Assert.Single(g2.Edges);
        Assert.Equal("v", g2.Nodes.Single(n=>n.Pk=="1").Props["k"]);
        Assert.Contains(Labels.ViewModel, g2.Nodes.Single(n=>n.Pk=="1").Roles);
    }
}
