using System.Linq;
using CodeWiki.Extraction;
using CodeWiki.Model;
using CodeWiki.Roslyn;
using Xunit;

namespace CodeWiki.Tests;

public class InterfaceImplementationExtractorTests
{
    [Fact]
    public void ImplMethodPointsToInterfaceMember()
    {
        var (c, _) = TestCompiler.Compile(
            "namespace N { public interface ISvc { void Do(); } public class Svc : ISvc { public void Do(){} } }");
        var g = new Graph();
        new InterfaceImplementationExtractor().Extract(new ExtractionContext(c, "/", "T"), g);
        var impl = g.Nodes.Single(n => n.FullName == "N.Svc.Do");
        var iface = g.Nodes.Single(n => n.FullName == "N.ISvc.Do");
        Assert.Contains(g.Edges, e => e.Type == Rel.ImplementsMethod && e.FromPk == impl.Pk && e.ToPk == iface.Pk);
    }

    [Fact]
    public void InterfaceMethodGetsDeterministicOperationProps()
    {
        var (c, _) = TestCompiler.Compile(@"namespace N {
            public interface IRepository<T> { void Update(T x); }
            public class Order {}
            public interface IOrderService { void Save(Order o); }
            public class OrderService : IOrderService {
                private IRepository<Order> _repo;
                public void Save(Order o){ _repo.Update(o); } } }");
        var g = new Graph();
        new InterfaceImplementationExtractor().Extract(new ExtractionContext(c, "/", "T"), g);
        var iface = g.Nodes.Single(n => n.FullName == "N.IOrderService.Save");
        Assert.Equal("true", iface.Props["mutatesState"]);
        Assert.Equal("command", iface.Props["operationType"]);
    }
}
