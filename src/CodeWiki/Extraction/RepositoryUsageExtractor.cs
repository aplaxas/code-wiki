using System.Collections.Generic;
using System.Linq;
using CodeWiki.Model;
using CodeWiki.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeWiki.Extraction;

public sealed class RepositoryUsageExtractor : IExtractor
{
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();
    private readonly RoleClassifier _roles;

    public RepositoryUsageExtractor(RoleClassifier roles) => _roles = roles;

    public void Extract(ExtractionContext ctx, Graph graph)
    {
        foreach (var t in ctx.SourceTypes())
        foreach (var m in t.GetMembers().OfType<IMethodSymbol>())
        {
            var mNode = SymbolNodes.ForMethod(m);
            foreach (var sr in m.DeclaringSyntaxReferences)
            {
                var syntax = sr.GetSyntax();
                var model = ctx.Compilation.GetSemanticModel(syntax.SyntaxTree);
                foreach (var id in syntax.DescendantNodes().OfType<IdentifierNameSyntax>())
                {
                    if (model.GetSymbolInfo(id).Symbol is not IFieldSymbol f) continue;
                    if (f.Type is not INamedTypeSymbol ft || !ft.IsGenericType || !ft.Name.Contains("Repository")) continue;
                    if (ft.TypeArguments.FirstOrDefault() is not INamedTypeSymbol entity) continue;
                    var en = SymbolNodes.ForType(entity, _roles);
                    if (en == null) continue;
                    graph.AddNode(mNode);
                    graph.AddNode(en);
                    graph.AddEdge(new Edge(Rel.Uses, mNode.Pk, en.Pk, Empty));
                }
            }
        }
    }
}
