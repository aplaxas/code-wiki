using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CodeWiki.Model;

namespace CodeWiki.Storage;

public static class GraphSerializer
{
    private sealed record NodeLine(string kind, string label, string pk, string name, string fullName,
        Dictionary<string,string> props, List<string> roles);
    private sealed record EdgeLine(string kind, string type, string from, string to, Dictionary<string,string> props);

    public static void Write(Graph g, string path)
    {
        using var w = new StreamWriter(path, false);
        foreach (var n in g.Nodes)
            w.WriteLine(JsonSerializer.Serialize(new NodeLine("node", n.Label, n.Pk, n.Name, n.FullName,
                new(n.Props), n.Roles.ToList())));
        foreach (var e in g.Edges)
            w.WriteLine(JsonSerializer.Serialize(new EdgeLine("edge", e.Type, e.FromPk, e.ToPk, new(e.Props))));
    }

    public static Graph Read(string path)
    {
        var g = new Graph();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            var kind = doc.RootElement.GetProperty("kind").GetString();
            if (kind == "node")
            {
                var n = JsonSerializer.Deserialize<NodeLine>(line)!;
                g.AddNode(new Node(n.label, n.pk, n.name, n.fullName, n.props ?? new(), n.roles ?? new()));
            }
            else
            {
                var e = JsonSerializer.Deserialize<EdgeLine>(line)!;
                g.AddEdge(new Edge(e.type, e.from, e.to, e.props ?? new()));
            }
        }
        return g;
    }
}
