using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Strazh.Analysis;
using Xunit;

namespace Strazh.Tests;

public class RoleClassifierTests
{
    [Fact]
    public void Classifies_entity_by_IBaseEntity()
    {
        var src = @"
namespace N {
  public interface IBaseEntity { }
  public class Order : IBaseEntity { }
}";
        var (tree, model) = TestCompiler.Compile(src);
        var decl = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var symbol = model.GetDeclaredSymbol(decl)!;

        var roles = RoleClassifier.Classify(symbol);

        Assert.Contains("Entity", roles);
    }

    [Fact]
    public void Classifies_controller_by_base_type_name()
    {
        var src = @"
namespace N {
  public class ControllerBase { }
  public class OrderController : ControllerBase { }
}";
        var (tree, model) = TestCompiler.Compile(src);
        var decl = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(c => c.Identifier.Text == "OrderController");
        var symbol = model.GetDeclaredSymbol(decl)!;

        Assert.Contains("Controller", RoleClassifier.Classify(symbol));
    }
}
