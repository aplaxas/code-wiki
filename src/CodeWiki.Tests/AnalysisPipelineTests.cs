using System.Collections.Generic;
using System.Linq;
using CodeWiki.Model;
using CodeWiki.Pipeline;
using Microsoft.CodeAnalysis;
using Xunit;

namespace CodeWiki.Tests;

public class AnalysisPipelineTests
{
    sealed class Stub : IWorkspaceBuilder
    {
        private readonly Compilation _c;
        public Stub(Compilation c) => _c = c;
        public IEnumerable<Compilation> Build(string slnPath) { yield return _c; }
    }

    [Fact]
    public void RunsExtractorsAndLinker()
    {
        var (c, _) = TestCompiler.Compile(@"namespace N {
            public class SearchOrderView {} public class SearchOrderViewModel {} }");
        var g = new AnalysisPipeline(new Stub(c)).Run("x.sln");
        var v = g.Nodes.Single(n => n.Name == "SearchOrderView");
        var vm = g.Nodes.Single(n => n.Name == "SearchOrderViewModel");
        Assert.Contains(g.Edges, e => e.Type == Rel.BindsTo && e.FromPk == v.Pk && e.ToPk == vm.Pk);
    }
}
