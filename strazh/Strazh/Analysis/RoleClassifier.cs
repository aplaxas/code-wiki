using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Strazh.Analysis;

public static class RoleClassifier
{
    public static IReadOnlyList<string> Classify(INamedTypeSymbol type)
    {
        var roles = new List<string>();
        var name = type.Name;
        var ns = type.ContainingNamespace?.ToString() ?? "";
        var allIfaces = type.AllInterfaces.Select(i => i.Name).ToHashSet();
        var baseNames = BaseChain(type).Select(b => b.Name).ToHashSet();

        if (allIfaces.Contains("IBaseEntity")) roles.Add("Entity");
        if (baseNames.Contains("BindableBase") || name.EndsWith("ViewModel")) roles.Add("ViewModel");
        if (baseNames.Contains("ControllerBase") || name.EndsWith("Controller")) roles.Add("Controller");
        if (allIfaces.Any(i => i.StartsWith("I") && i.EndsWith("Service"))) roles.Add("Service");
        if (name.Contains("Repository")) roles.Add("Repository");
        if (ns.Contains(".DTO") || name.EndsWith("DTO")) roles.Add("DTO");
        if (name.EndsWith("View") && !name.EndsWith("ViewModel")) roles.Add("View");
        return roles;
    }

    private static IEnumerable<INamedTypeSymbol> BaseChain(INamedTypeSymbol type)
    {
        for (var b = type.BaseType; b != null; b = b.BaseType)
            yield return b;
    }
}
