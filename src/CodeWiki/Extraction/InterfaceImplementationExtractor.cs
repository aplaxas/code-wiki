using System.Collections.Generic;
using System.Linq;
using CodeWiki.Model;
using CodeWiki.Roslyn;
using Microsoft.CodeAnalysis;
using OpKind = CodeWiki.Roslyn.OperationKind;

namespace CodeWiki.Extraction;

public sealed class InterfaceImplementationExtractor : IExtractor
{
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    public void Extract(ExtractionContext ctx, Graph graph)
    {
        foreach (var t in ctx.SourceTypes())
        {
            if (t.TypeKind != TypeKind.Class) continue;
            foreach (var iface in t.AllInterfaces)
            {
                foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
                {
                    if (t.FindImplementationForInterfaceMember(member) is not IMethodSymbol impl) continue;
                    if (!SymbolEqualityComparer.Default.Equals(impl.ContainingType, t)) continue;
                    var implNode = SymbolNodes.ForMethod(impl);
                    var ifaceNode = SymbolNodes.ForMethod(member);

                    foreach (var sr in impl.DeclaringSyntaxReferences)
                    {
                        var syntax = sr.GetSyntax();
                        var model = ctx.Compilation.GetSemanticModel(syntax.SyntaxTree);
                        if (OpKind.Classify(syntax, model) is { } k)
                        {
                            var props = new Dictionary<string, string>(ifaceNode.Props)
                            {
                                ["mutatesState"] = k.mutatesState,
                                ["operationType"] = k.operationType,
                            };
                            ifaceNode = ifaceNode with { Props = props };
                            break;
                        }
                    }

                    graph.AddNode(implNode);
                    graph.AddNode(ifaceNode);
                    graph.AddEdge(new Edge(Rel.ImplementsMethod, implNode.Pk, ifaceNode.Pk, Empty));
                }
            }
        }
    }
}
