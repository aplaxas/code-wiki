using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace CodeWiki.Semantic;

public sealed class EnrichRunner
{
    private readonly IGraphReader _reader;
    private readonly ILlmClient _llm;
    private readonly ISemanticSink _sink;
    private readonly string _model;
    private readonly string _semanticPath;
    private readonly string _vanuatuRoot;

    public EnrichRunner(IGraphReader reader, ILlmClient llm, ISemanticSink sink,
        string model, string semanticPath, string vanuatuRoot)
    {
        _reader = reader; _llm = llm; _sink = sink;
        _model = model; _semanticPath = semanticPath; _vanuatuRoot = vanuatuRoot;
    }

    public async Task<int> RunVmAsync(string vmName)
    {
        var input = _reader.ReadVmDossier(vmName);
        var combined = Path.Combine(_vanuatuRoot, input.VmCsPath);
        if (string.IsNullOrEmpty(input.VmCsPath) || !File.Exists(combined))
            throw new FileNotFoundException($"VM.cs not found for '{vmName}' (path '{combined}').");
        var hash = SummaryHash.Of(SourceSlicer.WholeFile(combined));
        var fresh = await new VmEnricher(_llm, _model)
            .EnrichAsync(input with { VmCsPath = combined }, hash, ReadStoredHash(input.VmPk));
        await PersistAsync(fresh);
        return fresh.Count;
    }

    public async Task<int> RunIfaceAsync(string methodName)
    {
        var unit = _reader.ReadIfaceUnit(methodName) with { RootDir = _vanuatuRoot };
        var fresh = await new IfaceEnricher(_llm, _model)
            .EnrichAsync(unit, methodName, ReadStoredHash(unit.IfacePk));
        await PersistAsync(fresh);
        return fresh.Count;
    }

    private string? ReadStoredHash(string pk)
    {
        if (!File.Exists(_semanticPath)) return null;
        foreach (var r in SemanticSerializer.Read(_semanticPath))
            if (r.Pk == pk) return r.SummaryHash;
        return null;
    }

    private async Task PersistAsync(List<SemanticRecord> fresh)
    {
        if (fresh.Count == 0) return;
        var merged = new Dictionary<string, SemanticRecord>();
        if (File.Exists(_semanticPath))
            foreach (var r in SemanticSerializer.Read(_semanticPath)) merged[r.Pk] = r;
        foreach (var r in fresh) merged[r.Pk] = r;
        SemanticSerializer.Write(merged.Values, _semanticPath);
        await _sink.ApplySemanticAsync(fresh);
    }
}
