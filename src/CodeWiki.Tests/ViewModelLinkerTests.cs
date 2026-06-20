using System;
using System.Collections.Generic;
using System.Linq;
using CodeWiki.Extraction;
using CodeWiki.Model;
using Xunit;

namespace CodeWiki.Tests;

public class ViewModelLinkerTests
{
    static Node N(string name, string role) => new(Labels.Class, name, name, "N." + name,
        new Dictionary<string, string>(), new[] { role });

    [Fact]
    public void LinksViewToViewModelByName()
    {
        var g = new Graph();
        g.AddNode(N("SearchOrderView", Labels.View));
        g.AddNode(N("SearchOrderViewModel", Labels.ViewModel));
        new ViewModelLinker().Link(g);
        var v = g.Nodes.Single(n => n.Name == "SearchOrderView");
        var vm = g.Nodes.Single(n => n.Name == "SearchOrderViewModel");
        Assert.Contains(g.Edges, e => e.Type == Rel.BindsTo && e.FromPk == v.Pk && e.ToPk == vm.Pk);
    }
}
