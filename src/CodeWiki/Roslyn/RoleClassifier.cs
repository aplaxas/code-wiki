using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace CodeWiki.Roslyn;

public class RoleClassifier
{
    public IReadOnlyList<string> Classify(INamedTypeSymbol t) => System.Array.Empty<string>();
}
