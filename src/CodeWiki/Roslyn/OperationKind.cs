using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeWiki.Roslyn;

public static class OperationKind
{
    private static readonly string[] MutationVerbs =
        { "Insert", "Add", "Update", "Delete", "Remove", "Save", "SaveChanges", "Create" };
    private static readonly string[] RawSqlMarkers =
        { "CallRawSQL", "ExecuteSqlRaw", "FromSqlRaw" };

    // 리포지토리를 만지지 않으면 null (클라 프록시·순수 UI 등은 분류 대상 아님).
    public static (string mutatesState, string operationType)? Classify(SyntaxNode body, SemanticModel model)
    {
        if (!UsesRepository(body, model)) return null;
        bool rawSql = false, mutates = false;
        foreach (var inv in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var name = InvokedName(inv);
            if (name is null) continue;
            if (RawSqlMarkers.Any(name.Contains)) rawSql = true;
            var bare = name.EndsWith("Async") ? name[..^5] : name;
            if (MutationVerbs.Contains(bare)) mutates = true;
        }
        if (rawSql) return ("unknown", "unknown");
        return mutates ? ("true", "command") : ("false", "query");
    }

    private static bool UsesRepository(SyntaxNode body, SemanticModel model) =>
        body.DescendantNodes().OfType<IdentifierNameSyntax>().Any(id =>
            model.GetSymbolInfo(id).Symbol is IFieldSymbol f &&
            f.Type is INamedTypeSymbol ft && ft.IsGenericType && ft.Name.Contains("Repository"));

    private static string? InvokedName(InvocationExpressionSyntax inv) => inv.Expression switch
    {
        MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
        IdentifierNameSyntax id => id.Identifier.Text,
        _ => null
    };
}
