using System;
using System.Collections.Generic;
using System.Linq;
using CodeWiki.Model;
using CodeWiki.Roslyn;

namespace CodeWiki.Extraction;

public sealed class StructureExtractor : IExtractor
{
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();
    private static readonly IReadOnlyList<string> NoRoles = Array.Empty<string>();

    public void Extract(ExtractionContext ctx, Graph graph)
    {
        var sln = new Node(Labels.Solution, Pk.Of("sln:" + ctx.SolutionName), ctx.SolutionName, ctx.SolutionName, Empty, NoRoles);
        graph.AddNode(sln);

        var asm = ctx.Compilation.AssemblyName ?? "unknown";
        var proj = new Node(Labels.Project, Pk.Of("proj:" + asm), asm, asm, Empty, NoRoles);
        graph.AddNode(proj);

        graph.AddEdge(new Edge(Rel.Contains, sln.Pk, proj.Pk, Empty));

        foreach (var tree in ctx.Compilation.SyntaxTrees.Where(t => !string.IsNullOrEmpty(t.FilePath)))
        {
            var file = FileNodes.ForPath(tree.FilePath, ctx.SolutionRoot);
            graph.AddNode(file);
            graph.AddEdge(new Edge(Rel.IncludedIn, file.Pk, proj.Pk, Empty));
        }

        foreach (var r in ctx.Compilation.ReferencedAssemblyNames)
        {
            var pkg = new Node(Labels.Package, Pk.Of("pkg:" + r.Name), r.Name, r.Name, Empty, NoRoles);
            graph.AddNode(pkg);
            graph.AddEdge(new Edge(Rel.DependsOn, proj.Pk, pkg.Pk, Empty));
        }
    }
}
