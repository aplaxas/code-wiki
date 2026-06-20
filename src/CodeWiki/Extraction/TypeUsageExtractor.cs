using System.Collections.Generic;
using System.Linq;
using CodeWiki.Model;
using CodeWiki.Roslyn;
using Microsoft.CodeAnalysis;

namespace CodeWiki.Extraction;

public sealed class TypeUsageExtractor : IExtractor
{
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();
    private readonly RoleClassifier _roles;

    public TypeUsageExtractor(RoleClassifier roles) => _roles = roles;

    private static bool IsDomain(ITypeSymbol? s) =>
        s is INamedTypeSymbol n && n.SpecialType == SpecialType.None && n.TypeKind != TypeKind.Error
        && !(n.ContainingNamespace?.ToDisplayString() ?? "").StartsWith("System")
        && !(n.ContainingNamespace?.ToDisplayString() ?? "").StartsWith("Microsoft");

    public void Extract(ExtractionContext ctx, Graph graph)
    {
        foreach (var t in ctx.SourceTypes())
        foreach (var m in t.GetMembers().OfType<IMethodSymbol>())
        {
            if (m.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet) continue;
            var mNode = SymbolNodes.ForMethod(m);
            foreach (var used in m.Parameters.Select(p => p.Type).Append(m.ReturnType).Distinct(SymbolEqualityComparer.Default))
            {
                if (used is not INamedTypeSymbol u || !IsDomain(u)) continue;
                var un = SymbolNodes.ForType(u, _roles);
                if (un == null) continue;
                graph.AddNode(mNode);
                graph.AddNode(un);
                graph.AddEdge(new Edge(Rel.UsesType, mNode.Pk, un.Pk, Empty));
            }
        }
    }
}
