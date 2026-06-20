using System.Collections.Generic;
using System.Linq;

namespace CodeWiki.Model;

public sealed class Graph
{
    private readonly Dictionary<string, Node> _nodes = new();
    private readonly Dictionary<string, Edge> _edges = new();

    public IReadOnlyCollection<Node> Nodes => _nodes.Values;
    public IReadOnlyCollection<Edge> Edges => _edges.Values;

    public void AddNode(Node n) => _nodes[n.Pk] = _nodes.TryGetValue(n.Pk, out var e) ? Merge(e, n) : n;

    public void AddEdge(Edge e)
    {
        var k = $"{e.FromPk}|{e.ToPk}|{e.Type}";
        if (!_edges.ContainsKey(k)) _edges[k] = e;
    }

    private static Node Merge(Node a, Node b)
    {
        var props = new Dictionary<string, string>(a.Props);
        foreach (var (k, v) in b.Props) if (!string.IsNullOrEmpty(v)) props[k] = v;
        var roles = a.Roles.Concat(b.Roles).Distinct().ToList();
        return a with
        {
            Name = string.IsNullOrEmpty(a.Name) ? b.Name : a.Name,
            FullName = string.IsNullOrEmpty(a.FullName) ? b.FullName : a.FullName,
            Props = props,
            Roles = roles
        };
    }
}
