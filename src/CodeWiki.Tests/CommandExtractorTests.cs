using System.Linq;
using CodeWiki.Extraction;
using CodeWiki.Model;
using CodeWiki.Roslyn;
using Xunit;

namespace CodeWiki.Tests;

public class CommandExtractorTests
{
    const string Src = @"namespace N {
        public class DelegateCommand { public DelegateCommand(System.Action e){} public DelegateCommand ObservesCanExecute(System.Func<bool> f)=>this; }
        public class Vm { public DelegateCommand SearchCommand { get; }
            public Vm(){ SearchCommand = new DelegateCommand(Search).ObservesCanExecute(()=>true); }
            public void Search(){} } }";

    static Graph Run()
    {
        var (c, _) = TestCompiler.Compile(Src);
        var g = new Graph();
        new CommandExtractor(new RoleClassifier()).Extract(new ExtractionContext(c, "/", "T"), g);
        return g;
    }

    [Fact]
    public void DefinesAndExecutes()
    {
        var g = Run();
        var vm = g.Nodes.Single(n => n.Name == "Vm");
        var cmd = g.Nodes.Single(n => n.Label == Labels.Command && n.Name == "SearchCommand");
        var handler = g.Nodes.Single(n => n.Name == "Search");

        Assert.Contains(g.Edges, e => e.Type == Rel.DefinesCommand && e.FromPk == vm.Pk && e.ToPk == cmd.Pk);
        Assert.Contains(g.Edges, e => e.Type == Rel.Executes && e.FromPk == cmd.Pk && e.ToPk == handler.Pk);
    }
}
