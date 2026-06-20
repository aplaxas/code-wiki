using System.Collections.Generic;
using System.Linq;
using CodeWiki.Model;
using CodeWiki.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeWiki.Extraction;

public sealed class TypeExtractor : IExtractor
{
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();
    private readonly RoleClassifier _roles;

    public TypeExtractor(RoleClassifier roles) => _roles = roles;

    public void Extract(ExtractionContext ctx, Graph graph)
    {
        foreach (var t in ctx.SourceTypes())
        {
            var node = SymbolNodes.ForType(t, _roles);
            if (node == null) continue;
            graph.AddNode(node);

            // DECLARES + CALLS + INSTANTIATES edges
            foreach (var m in t.GetMembers().OfType<IMethodSymbol>())
            {
                if (m.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet
                    or MethodKind.EventAdd or MethodKind.EventRemove) continue;
                var mNode = SymbolNodes.ForMethod(m);
                graph.AddNode(mNode);
                graph.AddEdge(new Edge(Rel.Declares, node.Pk, mNode.Pk, Empty));
                foreach (var sr in m.DeclaringSyntaxReferences)
                {
                    var syntax = sr.GetSyntax();
                    var model = ctx.Compilation.GetSemanticModel(syntax.SyntaxTree);
                    foreach (var inv in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
                        if (model.GetSymbolInfo(inv).Symbol is IMethodSymbol callee)
                        {
                            var cn = SymbolNodes.ForMethod(callee.OriginalDefinition);
                            graph.AddNode(cn);
                            graph.AddEdge(new Edge(Rel.Calls, mNode.Pk, cn.Pk, Empty));
                        }
                    foreach (var oc in syntax.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
                        if (model.GetSymbolInfo(oc).Symbol is IMethodSymbol ctor && ctor.ContainingType is INamedTypeSymbol created)
                        {
                            var crn = SymbolNodes.ForType(created, _roles);
                            if (crn != null)
                            {
                                graph.AddNode(crn);
                                graph.AddEdge(new Edge(Rel.Instantiates, mNode.Pk, crn.Pk, Empty));
                            }
                        }
                }
            }

            // DECLARED_IN edges
            foreach (var loc in t.Locations.Where(l => l.IsInSource))
            {
                var path = loc.SourceTree?.FilePath;
                if (string.IsNullOrEmpty(path)) continue;
                var file = FileNodes.ForPath(path, ctx.SolutionRoot);
                graph.AddNode(file);
                graph.AddEdge(new Edge(Rel.DeclaredIn, node.Pk, file.Pk, Empty));
            }

            // INHERITS edge
            if (t.BaseType is { TypeKind: TypeKind.Class, SpecialType: SpecialType.None } bt)
            {
                var bn = SymbolNodes.ForType(bt, _roles);
                if (bn != null)
                {
                    graph.AddNode(bn);
                    graph.AddEdge(new Edge(Rel.Inherits, node.Pk, bn.Pk, Empty));
                }
            }

            // IMPLEMENTS edges
            foreach (var iface in t.Interfaces)
            {
                var inode = SymbolNodes.ForType(iface, _roles);
                if (inode == null) continue;
                graph.AddNode(inode);
                graph.AddEdge(new Edge(Rel.Implements, node.Pk, inode.Pk, Empty));
            }
        }
    }
}
