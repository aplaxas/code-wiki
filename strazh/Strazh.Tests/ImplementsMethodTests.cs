using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Strazh.Analysis;
using Strazh.Domain;
using Xunit;

namespace Strazh.Tests;

public class ImplementsMethodTests
{
    [Fact]
    public void Links_implementing_method_to_interface_member()
    {
        var src = @"
namespace N {
  public interface IOrderService { int Search(string f); }
  public class OrderService : IOrderService { public int Search(string f) => 0; }
}";
        var (tree, model) = TestCompiler.Compile(src);
        var classDecl = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var triples = new List<Triple>();

        Extractor.GetInterfaceImplementations(triples, classDecl, model);

        Assert.Contains(triples, t =>
            t.Relationship is ImplementsMethodRelationship &&
            t.NodeA.FullName == "N.OrderService.Search" &&
            t.NodeB.FullName == "N.IOrderService.Search");
    }
}
