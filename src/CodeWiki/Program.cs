using System;
using System.Net.Http;
using System.Threading.Tasks;
using CodeWiki.Cli;
using CodeWiki.Pipeline;
using CodeWiki.Semantic;
using CodeWiki.Storage;
using Neo4j.Driver;

var o = CliOptions.Parse(args);
switch (o.Verb)
{
    case "extract":
    {
        if (o.Solution == null || o.Output == null)
        {
            Console.Error.WriteLine("extract requires -s <sln> and -o <ndjson>");
            return;
        }
        var graph = new AnalysisPipeline(new WorkspaceBuilder()).Run(o.Solution);
        GraphSerializer.Write(graph, o.Output);
        Console.WriteLine($"extracted: {graph.Nodes.Count} nodes, {graph.Edges.Count} edges → {o.Output}");
        break;
    }
    case "load":
    {
        if (o.Credentials == null || o.Ndjson == null)
        {
            Console.Error.WriteLine("load requires -c <db:user:pass> and --ndjson <path>");
            return;
        }
        var parts = o.Credentials.Split(':');
        var graph = GraphSerializer.Read(o.Ndjson);
        await using var loader = new Neo4jLoader("bolt://localhost:7687", parts[^2], parts[^1]);
        await loader.LoadAsync(graph, o.Wipe);
        if (o.Semantic != null && System.IO.File.Exists(o.Semantic))
        {
            var recs = SemanticSerializer.Read(o.Semantic);
            await loader.ApplySemanticAsync(recs);
            Console.WriteLine($"  + semantic replayed: {recs.Count} records");
        }
        Console.WriteLine($"loaded: {graph.Nodes.Count} nodes, {graph.Edges.Count} edges (wipe={o.Wipe})");
        break;
    }
    case "enrich":
    {
        if (o.Credentials == null || o.Semantic == null || (o.Vm == null && o.Iface == null))
        {
            Console.Error.WriteLine("enrich requires -c <db:user:pass> --semantic <out> and (--vm <name> | --iface <method>)");
            return;
        }
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrEmpty(apiKey)) { Console.Error.WriteLine("ANTHROPIC_API_KEY not set"); return; }
        var model = o.Model ?? "claude-haiku-4-5-20251001";
        var parts = o.Credentials.Split(':');
        var vanuatuRoot = Environment.GetEnvironmentVariable("VANUATU_ROOT")
            ?? @"C:\develop\baw\phase2\baw-phase2-platform\Vanuatu";

        var driver = GraphDatabase.Driver("bolt://localhost:7687", AuthTokens.Basic(parts[^2], parts[^1]));
        await using var reader = new Neo4jGraphReader(driver);
        await using var loader = new Neo4jLoader("bolt://localhost:7687", parts[^2], parts[^1]);
        var llm = new AnthropicClient(apiKey, model, new HttpClient());

        var existing = System.IO.File.Exists(o.Semantic)
            ? SemanticSerializer.Read(o.Semantic)
            : new System.Collections.Generic.List<SemanticRecord>();
        var existingHash = new System.Collections.Generic.Dictionary<string, string>();
        foreach (var r in existing) existingHash[r.Pk] = r.SummaryHash;

        System.Collections.Generic.List<SemanticRecord> fresh;
        if (o.Vm != null)
        {
            var input = reader.ReadVmDossier(o.Vm);
            var combined = System.IO.Path.Combine(vanuatuRoot, input.VmCsPath);
            if (string.IsNullOrEmpty(input.VmCsPath) || !System.IO.File.Exists(combined))
            {
                Console.Error.WriteLine($"VM.cs not found for '{o.Vm}' (path '{combined}'). Re-run extract with L0 props or check VANUATU_ROOT.");
                return;
            }
            var hash = SummaryHash.Of(SourceSlicer.WholeFile(combined));
            var input2 = input with { VmCsPath = combined };
            existingHash.TryGetValue(input.VmPk, out var stored);
            fresh = await new VmEnricher(llm, model).EnrichAsync(input2, hash, stored);
        }
        else
        {
            var unit = reader.ReadIfaceUnit(o.Iface!) with { RootDir = vanuatuRoot };
            existingHash.TryGetValue(unit.IfacePk, out var stored);
            fresh = await new IfaceEnricher(llm, model).EnrichAsync(unit, o.Iface!, stored);
        }

        // 병합 저장(기존 + 신규, pk 기준 신규 우선) + Neo4j upsert
        var merged = new System.Collections.Generic.Dictionary<string, SemanticRecord>();
        foreach (var r in existing) merged[r.Pk] = r;
        foreach (var r in fresh) merged[r.Pk] = r;
        SemanticSerializer.Write(merged.Values, o.Semantic);
        await loader.ApplySemanticAsync(fresh);
        Console.WriteLine($"enriched: {fresh.Count} records (skipped if 0) → {o.Semantic}");
        break;
    }
    default:
        Console.Error.WriteLine("usage: codewiki extract -s <sln> -o <ndjson> | load -c <db:user:pass> --ndjson <f> [--wipe] [--semantic <path>] | enrich -c <db:user:pass> --semantic <out> (--vm <name> | --iface <method>) [--model <id>]");
        break;
}
