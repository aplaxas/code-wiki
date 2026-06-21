using System.Collections.Generic;
using System.Threading.Tasks;

namespace CodeWiki.Semantic;

public sealed record LlmRequest(string System, string User);
public sealed record LlmField(string Key, string Summary, string? Effects, string? Caveats);

public interface ILlmClient
{
    Task<IReadOnlyList<LlmField>> EnrichAsync(LlmRequest req);
}
