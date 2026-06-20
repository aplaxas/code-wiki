using System;
using System.Collections.Generic;
using System.Linq;
using CodeWiki.Model;
using Microsoft.CodeAnalysis;

namespace CodeWiki.Roslyn;

public static class SymbolNodes
{
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    public static Node? ForType(INamedTypeSymbol t, RoleClassifier? roles)
    {
        if (t.TypeKind != TypeKind.Class && t.TypeKind != TypeKind.Interface) return null;
        var label = t.TypeKind == TypeKind.Interface ? Labels.Interface : Labels.Class;
        var full = Names.Full(t);
        var roleList = roles?.Classify(t) ?? Array.Empty<string>();
        var props = t.DeclaredAccessibility != Accessibility.NotApplicable
            ? new Dictionary<string, string> { ["modifiers"] = t.DeclaredAccessibility.ToString().ToLowerInvariant() } : Empty;
        return new Node(label, Pk.Of(full), t.Name, full, props, roleList);
    }

    public static Node ForMethod(IMethodSymbol m)
    {
        var full = Names.Full(m);
        var args = string.Join(", ", m.Parameters.Select(p => Names.Full(p.Type)));
        var ret = Names.Full(m.ReturnType);
        return new Node(Labels.Method, Pk.Of(full, args, ret), m.Name, full,
            new Dictionary<string, string> { ["arguments"] = args, ["returnType"] = ret }, Array.Empty<string>());
    }
}
