using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CodeWiki.Semantic;

public sealed class IfaceEnricher
{
    private readonly ILlmClient _client;
    private readonly string _model;

    public IfaceEnricher(ILlmClient client, string model)
    {
        _client = client;
        _model = model;
    }

    public async Task<List<SemanticRecord>> EnrichAsync(
        IfaceUnitInput input, string methodName, string? storedHash)
    {
        var bundle = string.Join("\n\n",
            input.Slices.Select(s =>
                SourceSlicer.Slice(Path.Combine(input.RootDir, s.SourcePath), s.StartLine, s.EndLine)));
        var hash = SummaryHash.Of(bundle);
        if (storedHash == hash) return new List<SemanticRecord>();

        var req = IfacePromptBuilder.Build(bundle, methodName);
        var fields = await _client.EnrichAsync(req);
        var f = fields.FirstOrDefault();
        if (f is null) return new List<SemanticRecord>();
        return new List<SemanticRecord>
        {
            new(input.IfacePk, f.Summary, f.Effects, f.Caveats, hash, _model)
        };
    }
}
