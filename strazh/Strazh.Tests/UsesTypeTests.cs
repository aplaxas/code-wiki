using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Strazh.Analysis;
using Strazh.Domain;
using Xunit;

namespace Strazh.Tests;

public class UsesTypeTests
{
    [Fact]
    public void Links_method_to_parameter_type()
    {
        var src = @"
namespace N {
  public class FilterDTO { }
  public class Svc { public void Do(FilterDTO f) { } }
}";
        var (tree, model) = TestCompiler.Compile(src);
        var svc = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(c => c.Identifier.Text == "Svc");
        var triples = new List<Triple>();

        Extractor.GetTypeUsages(triples, svc, model);

        Assert.Contains(triples, t =>
            t.Relationship is UsesTypeRelationship &&
            t.NodeA.FullName == "N.Svc.Do" &&
            t.NodeB.FullName == "N.FilterDTO");
    }

    [Fact]
    public void Skips_framework_types()
    {
        var src = @"namespace N { public class Svc { public void Do(string s) { } } }";
        var (tree, model) = TestCompiler.Compile(src);
        var svc = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var triples = new List<Triple>();

        Extractor.GetTypeUsages(triples, svc, model);

        Assert.DoesNotContain(triples, t => t.Relationship is UsesTypeRelationship);
    }
}
