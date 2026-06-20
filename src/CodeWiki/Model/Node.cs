namespace CodeWiki.Model;

public sealed record Node(string Label, string Pk, string Name, string FullName,
    System.Collections.Generic.IReadOnlyDictionary<string, string> Props,
    System.Collections.Generic.IReadOnlyList<string> Roles);
