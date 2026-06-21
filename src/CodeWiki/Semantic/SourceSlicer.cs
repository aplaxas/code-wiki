using System;
using System.IO;
using System.Linq;

namespace CodeWiki.Semantic;

public static class SourceSlicer
{
    public static string WholeFile(string absPath) => File.ReadAllText(absPath);

    public static string Slice(string absPath, int startLine, int endLine)
    {
        var lines = File.ReadAllLines(absPath);
        var from = Math.Max(1, startLine);
        var to = Math.Min(lines.Length, endLine);
        return string.Join("\n", lines.Skip(from - 1).Take(to - from + 1));
    }
}
