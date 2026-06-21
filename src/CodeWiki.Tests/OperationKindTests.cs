using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;
using OpKind = CodeWiki.Roslyn.OperationKind;

namespace CodeWiki.Tests;

public class OperationKindTests
{
    static (SyntaxNode body, SemanticModel model) Method(string body)
    {
        var src = @"namespace N {
            public interface IRepository<T> { void Update(T x); System.Collections.Generic.List<T> Table { get; } }
            public class Order {}
            public class Svc { private IRepository<Order> _repo;
                public void M(){ " + body + @" } } }";
        var (c, m) = TestCompiler.Compile(src);
        var svc = (INamedTypeSymbol)c.GetSymbolsWithName("Svc").Single();
        var method = svc.GetMembers("M").OfType<IMethodSymbol>().Single();
        var syntax = method.DeclaringSyntaxReferences[0].GetSyntax();
        return (syntax, c.GetSemanticModel(syntax.SyntaxTree));
    }

    [Fact]
    public void RepoMutationIsCommand()
    {
        var (b, m) = Method("_repo.Update(new Order());");
        Assert.Equal(("true", "command"), OpKind.Classify(b, m));
    }

    [Fact]
    public void RepoReadOnlyIsQuery()
    {
        var (b, m) = Method("var x = _repo.Table;");
        Assert.Equal(("false", "query"), OpKind.Classify(b, m));
    }

    [Fact]
    public void NoRepoReturnsNull()
    {
        var (b, m) = Method("System.Console.WriteLine(1);");
        Assert.Null(OpKind.Classify(b, m));
    }
}
