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

    [Fact]
    public void ConstructorInjectionDoesNotEmitUses()
    {
        var (c, _) = TestCompiler.Compile(@"namespace N {
            public interface IRepository<T> {}
            public class Order {}
            public class Svc { private IRepository<Order> _repo;
                public Svc(IRepository<Order> repo){ _repo = repo; } } }");
        var g = new Graph();
        new RepositoryUsageExtractor(new RoleClassifier()).Extract(new ExtractionContext(c, "/", "T"), g);
        Assert.DoesNotContain(g.Edges, e => e.Type == Rel.Uses);
    }

    [Fact]
    public void PropertyAccessorDoesNotEmitUses()
    {
        var (c, _) = TestCompiler.Compile(@"namespace N {
            public interface IRepository<T> {}
            public class Order {}
            public class Svc { private IRepository<Order> _repo;
                public IRepository<Order> Repo { get { return _repo; } set { _repo = value; } } } }");
        var g = new Graph();
        new RepositoryUsageExtractor(new RoleClassifier()).Extract(new ExtractionContext(c, "/", "T"), g);
        Assert.DoesNotContain(g.Edges, e => e.Type == Rel.Uses);
    }
}
