using System;
using System.IO;
using CodeWiki.Extraction;
using CodeWiki.Model;
using CodeWiki.Roslyn;

namespace CodeWiki.Pipeline;

public sealed class AnalysisPipeline
{
    private readonly IWorkspaceBuilder _workspace;

    public AnalysisPipeline(IWorkspaceBuilder workspace) => _workspace = workspace;

    public Graph Run(string slnPath)
    {
        var graph = new Graph();
        var roles = new RoleClassifier();
        var extractors = new IExtractor[]
        {
            new TypeExtractor(roles),
            new InterfaceImplementationExtractor(),
            new CommandExtractor(roles),
            new TypeUsageExtractor(roles),
            new RepositoryUsageExtractor(roles),
            new StructureExtractor()
        };

        var root = Path.GetDirectoryName(Path.GetFullPath(slnPath)) ?? ".";
        var slnName = Path.GetFileNameWithoutExtension(slnPath);

        foreach (var comp in _workspace.Build(slnPath))
        {
            var ctx = new ExtractionContext(comp, root, slnName);
            foreach (var ex in extractors)
                try
                {
                    ex.Extract(ctx, graph);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"WARN: {ex.GetType().Name} on {comp.AssemblyName}: {e.Message}");
                }
        }

        new ViewModelLinker().Link(graph);
        return graph;
    }
}
