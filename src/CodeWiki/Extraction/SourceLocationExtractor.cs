using System.Collections.Generic;
using System.Linq;
using CodeWiki.Model;
using CodeWiki.Roslyn;
using Microsoft.CodeAnalysis;

namespace CodeWiki.Extraction;

public sealed class SourceLocationExtractor : IExtractor
{
    public void Extract(ExtractionContext ctx, Graph graph)
    {
        foreach (var t in ctx.SourceTypes())
        foreach (var m in t.GetMembers().OfType<IMethodSymbol>())
        {
            if (m.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet
                or MethodKind.EventAdd or MethodKind.EventRemove) continue;
            var loc = m.Locations.FirstOrDefault(l => l.IsInSource);
            if (loc is null) continue;
            var span = loc.GetLineSpan();
            if (string.IsNullOrEmpty(span.Path)) continue;
            var rel = System.IO.Path.GetRelativePath(ctx.SolutionRoot, span.Path).Replace('\\', '/');
            var baseNode = SymbolNodes.ForMethod(m);
            var props = new Dictionary<string, string>(baseNode.Props)
            {
                ["sourcePath"] = rel,
                ["startLine"] = (span.StartLinePosition.Line + 1).ToString(),
                ["endLine"] = (span.EndLinePosition.Line + 1).ToString(),
            };
            graph.AddNode(baseNode with { Props = props });
        }
    }
}
