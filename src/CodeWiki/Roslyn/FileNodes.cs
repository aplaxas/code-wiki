using System;
using System.Collections.Generic;
using CodeWiki.Model;

namespace CodeWiki.Roslyn;

public static class FileNodes
{
    public static Node ForPath(string abs, string root)
    {
        var rel = System.IO.Path.GetRelativePath(root, abs).Replace('\\', '/');
        return new Node(Labels.File, Pk.Of(rel), System.IO.Path.GetFileName(rel), rel,
            new Dictionary<string, string>(), Array.Empty<string>());
    }
}
