using Microsoft.CodeAnalysis;

namespace CodeWiki.Roslyn;

public static class Names
{
    private static readonly SymbolDisplayFormat Fmt = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

    public static string Full(ISymbol s) => s.ToDisplayString(Fmt);
}
