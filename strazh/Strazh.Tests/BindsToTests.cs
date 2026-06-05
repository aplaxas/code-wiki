using System.Collections.Generic;
using System.Linq;
using Strazh.Analysis;
using Strazh.Domain;
using Xunit;

namespace Strazh.Tests;

public class BindsToTests
{
    [Fact]
    public void Links_view_to_viewmodel_by_naming_convention()
    {
        var classes = new List<ClassNode>
        {
            new("App.Views.SearchOrderView", "SearchOrderView"),
            new("App.ViewModels.SearchOrderViewModel", "SearchOrderViewModel"),
            new("App.Other.Unrelated", "Unrelated"),
        };
        var triples = new List<Triple>();

        Extractor.LinkViewsToViewModels(triples, classes);

        Assert.Single(triples);
        var t = triples[0];
        Assert.True(t.Relationship is BindsToRelationship);
        Assert.Equal("App.Views.SearchOrderView", t.NodeA.FullName);
        Assert.Equal("App.ViewModels.SearchOrderViewModel", t.NodeB.FullName);
    }
}
