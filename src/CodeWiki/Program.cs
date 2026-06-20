using System;
using System.Threading.Tasks;
using CodeWiki.Cli;
using CodeWiki.Pipeline;
using CodeWiki.Storage;

var o = CliOptions.Parse(args);
switch (o.Verb)
{
    case "extract":
    {
        var graph = new AnalysisPipeline(new WorkspaceBuilder()).Run(o.Solution!);
        GraphSerializer.Write(graph, o.Output!);
        Console.WriteLine($"extracted: {graph.Nodes.Count} nodes, {graph.Edges.Count} edges → {o.Output}");
        break;
    }
    case "load":
    {
        var parts = o.Credentials!.Split(':');
        var graph = GraphSerializer.Read(o.Ndjson!);
        await using var loader = new Neo4jLoader("bolt://localhost:7687", parts[^2], parts[^1]);
        await loader.LoadAsync(graph, o.Wipe);
        Console.WriteLine($"loaded: {graph.Nodes.Count} nodes, {graph.Edges.Count} edges (wipe={o.Wipe})");
        break;
    }
    default:
        Console.Error.WriteLine("usage: codewiki extract -s <sln> -o <ndjson> | load -c <db:user:pass> --ndjson <f> [--wipe]");
        break;
}
