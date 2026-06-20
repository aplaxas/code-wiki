using System.Linq;
using CodeWiki.Extraction;
using CodeWiki.Model;
using CodeWiki.Roslyn;
using Xunit;

namespace CodeWiki.Tests;

public class RepositoryUsageExtractorTests
{
    [Fact]
    public void MethodUsesEntityViaRepositoryField()
    {
        var (c, _) = TestCompiler.Compile(@"namespace N {
            public interface IRepository<T> {}
            public class Order {}
            public class Svc { private IRepository<Order> _repo;
                public void Do(){ var x = _repo; } } }");
        var g = new Graph();
        new RepositoryUsageExtractor(new RoleClassifier()).Extract(new ExtractionContext(c, "/", "T"), g);
        var m = g.Nodes.Single(n => n.Name == "Do");
        var order = g.Nodes.Single(n => n.Name == "Order");
        Assert.Contains(g.Edges, e => e.Type == Rel.Uses && e.FromPk == m.Pk && e.ToPk == order.Pk);
    }
}
