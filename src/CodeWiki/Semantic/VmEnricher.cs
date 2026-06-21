using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CodeWiki.Semantic;

public sealed class VmEnricher
{
    private readonly ILlmClient _client;
    private readonly string _model;

    public VmEnricher(ILlmClient client, string model)
    {
        _client = client;
        _model = model;
    }

    public async Task<List<SemanticRecord>> EnrichAsync(
        VmDossierInput input, string currentVmHash, string? storedVmHash)
    {
        if (storedVmHash == currentVmHash) return new List<SemanticRecord>();

        var content = SourceSlicer.WholeFile(input.VmCsPath);
        var req = VmPromptBuilder.Build(content, input.Handlers.Select(h => h.Name).ToList());
        var fields = await _client.EnrichAsync(req);

        var pkByName = input.Handlers.ToDictionary(h => h.Name, h => h.Pk);
        var records = new List<SemanticRecord>();
        foreach (var f in fields)
        {
            string? pk = f.Key == VmPromptBuilder.ViewModelKey
                ? input.VmPk
                : (pkByName.TryGetValue(f.Key, out var p) ? p : null);
            if (pk is null) continue;
            records.Add(new SemanticRecord(pk, f.Summary, f.Effects, f.Caveats, currentVmHash, _model));
        }
        return records;
    }
}
