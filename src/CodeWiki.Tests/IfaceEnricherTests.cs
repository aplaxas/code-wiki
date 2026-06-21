using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeWiki.Semantic;
using Xunit;

namespace CodeWiki.Tests;

public class IfaceEnricherTests
{
    sealed class FakeLlm : ILlmClient
    {
        public Task<IReadOnlyList<LlmField>> EnrichAsync(LlmRequest req)
            => Task.FromResult<IReadOnlyList<LlmField>>(new List<LlmField>
            {
                new("SearchOrdersAsync", "필터로 주문 조회", "없음", "페이징 필수"),
            });
    }

    [Fact]
    public async Task ProducesSingleRecordForIfacePk()
    {
        var root = Path.GetTempPath();
        var rel = "Svc.cs";
        File.WriteAllText(Path.Combine(root, rel), "a\nb\nc\n");
        var input = new IfaceUnitInput("ipk", root,
            new List<SliceRef> { new(rel, 1, 2) });

        var recs = await new IfaceEnricher(new FakeLlm(), "m1")
            .EnrichAsync(input, "SearchOrdersAsync", storedHash: null);

        var r = Assert.Single(recs);
        Assert.Equal("ipk", r.Pk);
        Assert.Equal("필터로 주문 조회", r.Summary);
        Assert.Equal("페이징 필수", r.Caveats);
    }
}
