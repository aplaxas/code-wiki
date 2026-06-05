using System.Collections.Generic;
using System.Linq;
using Strazh.Analysis;
using Strazh.Domain;
using Xunit;

namespace Strazh.Tests;

public class DiRegistrationTests
{
    [Fact]
    public void Extracts_interface_impl_and_lifetime()
    {
        var src = @"
namespace N {
  public interface IOrderService { }
  public class OrderService : IOrderService { }
  public interface IServiceCollection { }
  public static class Reg {
    public static void AddScoped<TI, TImpl>(this IServiceCollection s) { }
    public static void Configure(IServiceCollection services) { services.AddScoped<IOrderService, OrderService>(); }
  }
}";
        var (tree, model) = TestCompiler.Compile(src);
        var triples = new List<Triple>();

        Extractor.GetDiRegistrations(triples, tree, model);

        var t = Assert.Single(triples.Where(x => x.Relationship is RegistersRelationship));
        Assert.Equal("N.IOrderService", t.NodeA.FullName);
        Assert.Equal("N.OrderService", t.NodeB.FullName);
        Assert.Equal("Scoped", ((RegistersRelationship)t.Relationship).Lifetime);
    }
}
