using System.Collections.Generic;
using System.Linq;
using CodeWiki.Model;
using Microsoft.CodeAnalysis;

namespace CodeWiki.Roslyn;

public sealed class RoleClassifier
{
    public IReadOnlyList<string> Classify(INamedTypeSymbol t)
    {
        var roles = new List<string>();
        var name = t.Name;
        var ns = t.ContainingNamespace?.ToDisplayString() ?? "";

        bool Inherits(string baseName)
        {
            for (var b = t.BaseType; b != null; b = b.BaseType)
                if (b.Name == baseName)
                    return true;
            return false;
        }

        bool ImplementsName(string ifaceName) => t.AllInterfaces.Any(i => i.Name == ifaceName);

        if (ImplementsName("IBaseEntity")) roles.Add(Labels.Entity);
        if (name.EndsWith("ViewModel") || Inherits("BindableBase")) roles.Add(Labels.ViewModel);
        if (name.EndsWith("Controller") || Inherits("ControllerBase")) roles.Add(Labels.Controller);
        if (t.AllInterfaces.Any(i => i.Name.StartsWith("I") && i.Name.EndsWith("Service")
            && (i.ContainingNamespace?.ToDisplayString() ?? "").StartsWith("Vanuatu.Service"))) roles.Add(Labels.Service);
        if (name.Contains("Repository")) roles.Add(Labels.Repository);
        if (ns.Contains(".DTO") || name.EndsWith("DTO")) roles.Add(Labels.Dto);
        if (name.EndsWith("View") && !name.EndsWith("ViewModel")) roles.Add(Labels.View);

        return roles;
    }
}
