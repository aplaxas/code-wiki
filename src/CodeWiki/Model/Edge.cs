namespace CodeWiki.Model;

public sealed record Edge(string Type, string FromPk, string ToPk,
    System.Collections.Generic.IReadOnlyDictionary<string, string> Props);
