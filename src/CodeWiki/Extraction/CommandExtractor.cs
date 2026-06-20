using System.Collections.Generic;
using System.Linq;
using CodeWiki.Model;
using CodeWiki.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeWiki.Extraction;

public sealed class CommandExtractor : IExtractor
{
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();
    private readonly RoleClassifier _roles;

    public CommandExtractor(RoleClassifier roles) => _roles = roles;

    public void Extract(ExtractionContext ctx, Graph graph)
    {
        foreach (var t in ctx.SourceTypes())
        {
            var owner = SymbolNodes.ForType(t, _roles);
            if (owner == null) continue;

            foreach (var sr in t.DeclaringSyntaxReferences)
            {
                var syntax = sr.GetSyntax();
                var model = ctx.Compilation.GetSemanticModel(syntax.SyntaxTree);

                foreach (var oc in syntax.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
                {
                    var typeName = (oc.Type as GenericNameSyntax)?.Identifier.Text
                                ?? (oc.Type as IdentifierNameSyntax)?.Identifier.Text;
                    if (typeName is null || !typeName.StartsWith("DelegateCommand")) continue;

                    var cmdName = AssignedName(oc);
                    if (cmdName is null) continue;

                    var ownerFull = owner.FullName;
                    var cmd = new Node(Labels.Command, Pk.Of(ownerFull, cmdName), cmdName,
                        ownerFull + "." + cmdName, Empty, System.Array.Empty<string>());

                    graph.AddNode(owner);
                    graph.AddNode(cmd);
                    graph.AddEdge(new Edge(Rel.DefinesCommand, owner.Pk, cmd.Pk, Empty));

                    var arg = oc.ArgumentList?.Arguments.FirstOrDefault();
                    if (arg != null && model.GetSymbolInfo(arg.Expression).Symbol is IMethodSymbol handler)
                    {
                        var hn = SymbolNodes.ForMethod(handler);
                        graph.AddNode(hn);
                        graph.AddEdge(new Edge(Rel.Executes, cmd.Pk, hn.Pk, Empty));
                    }
                }
            }
        }
    }

    private static string? AssignedName(ObjectCreationExpressionSyntax oc)
    {
        // Walk up the syntax tree, ignoring chains like .ObservesCanExecute(...)
        // until we find an assignment, variable declarator, or property declaration
        SyntaxNode? node = oc;
        while (node is not null
               && node is not AssignmentExpressionSyntax
               && node is not VariableDeclaratorSyntax
               && node is not PropertyDeclarationSyntax)
        {
            node = node.Parent;
        }

        return node switch
        {
            AssignmentExpressionSyntax a => (a.Left as IdentifierNameSyntax)?.Identifier.Text
                                          ?? (a.Left as MemberAccessExpressionSyntax)?.Name.Identifier.Text,
            VariableDeclaratorSyntax v => v.Identifier.Text,
            PropertyDeclarationSyntax p => p.Identifier.Text,
            _ => null
        };
    }
}
