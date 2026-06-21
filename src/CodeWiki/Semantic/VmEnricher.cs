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

        // 핸들러 이름은 유일하지 않다(오버로드·공유 핸들러). 이름→pk 목록으로 묶어
        // 한 이름의 LLM 요약을 같은 이름의 모든 핸들러 pk에 부착한다.
        var pksByName = input.Handlers
            .GroupBy(h => h.Name)
            .ToDictionary(g => g.Key, g => g.Select(h => h.Pk).ToList());

        var content = SourceSlicer.WholeFile(input.VmCsPath);
        var req = VmPromptBuilder.Build(content, pksByName.Keys.ToList());
        var fields = await _client.EnrichAsync(req);

        var records = new List<SemanticRecord>();
        foreach (var f in fields)
        {
            IReadOnlyList<string> pks = f.Key == VmPromptBuilder.ViewModelKey
                ? new[] { input.VmPk }
                : (pksByName.TryGetValue(f.Key, out var p) ? p : System.Array.Empty<string>());
            foreach (var pk in pks)
                records.Add(new SemanticRecord(pk, f.Summary, f.Effects, f.Caveats, currentVmHash, _model));
        }
        return records;
    }
}
