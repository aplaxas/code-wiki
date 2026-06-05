using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Strazh.Analysis;
using Strazh.Domain;
using Xunit;

namespace Strazh.Tests;

public class RepositoryUsesTests
{
    [Fact]
    public void Links_method_to_entity_via_repository_field()
    {
        var src = @"
namespace N {
  public interface IRepository<T> { }
  public class Order { }
  public class OrderService {
    private readonly IRepository<Order> _orders;
    public OrderService(IRepository<Order> orders) { _orders = orders; }
    public void Search() { var x = _orders; }
  }
}";
        var (tree, model) = TestCompiler.Compile(src);
        var svc = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(c => c.Identifier.Text == "OrderService");
        var triples = new List<Triple>();

        Extractor.GetRepositoryUsages(triples, svc, model);

        Assert.Contains(triples, t =>
            t.Relationship is UsesRelationship &&
            t.NodeA.FullName == "N.OrderService.Search" &&
            t.NodeB.FullName == "N.Order");
    }
}
