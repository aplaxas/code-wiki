using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CodeWiki.Semantic;

public static class SemanticSerializer
{
    public static void Write(IEnumerable<SemanticRecord> records, string path)
    {
        using var w = new StreamWriter(path, false);
        foreach (var r in records)
            w.WriteLine(JsonSerializer.Serialize(r));
    }

    public static List<SemanticRecord> Read(string path)
    {
        var list = new List<SemanticRecord>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            list.Add(JsonSerializer.Deserialize<SemanticRecord>(line)!);
        }
        return list;
    }
}
