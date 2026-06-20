using System.Collections.Generic;
using System.Linq;
using CodeWiki.Model;

namespace CodeWiki.Extraction;

public sealed class ViewModelLinker
{
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    public void Link(Graph graph)
    {
        var vms = graph.Nodes.Where(n => n.Roles.Contains(Labels.ViewModel))
            .GroupBy(n => n.Name).ToDictionary(grp => grp.Key, grp => grp.First());
        foreach (var v in graph.Nodes.Where(n => n.Roles.Contains(Labels.View)).ToList())
            if (vms.TryGetValue(v.Name + "Model", out var vm))
                graph.AddEdge(new Edge(Rel.BindsTo, v.Pk, vm.Pk, Empty));
    }
}
