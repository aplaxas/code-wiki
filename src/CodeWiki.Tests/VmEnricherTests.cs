using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeWiki.Semantic;
using Xunit;

namespace CodeWiki.Tests;

public class VmEnricherTests
{
    sealed class FakeLlm : ILlmClient
    {
        public Task<IReadOnlyList<LlmField>> EnrichAsync(LlmRequest req)
            => Task.FromResult<IReadOnlyList<LlmField>>(new List<LlmField>
            {
                new(VmPromptBuilder.ViewModelKey, "주문 검색 화면", null, null),
                new("SearchOrderAsync", "필터로 검색", null, "페이징 필수"),
                new("Unknown", "버려질 것", null, null),
            });
    }

    static VmDossierInput Input(string vmCsPath) => new(
        "vmpk", vmCsPath,
        new List<HandlerRef> { new("hpk", "SearchOrderAsync") });

    [Fact]
    public async Task MapsKeysToPksAndAttachesHashAndModel()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "class VM {}");
        var hash = SummaryHash.Of(SourceSlicer.WholeFile(path));
        var recs = await new VmEnricher(new FakeLlm(), "m1")
            .EnrichAsync(Input(path), hash, storedVmHash: null);

        Assert.Equal(2, recs.Count);                                   // Unknown key 제외
        var vm = recs.Single(r => r.Pk == "vmpk");
        Assert.Equal("주문 검색 화면", vm.Summary);
        var h = recs.Single(r => r.Pk == "hpk");
        Assert.Equal("페이징 필수", h.Caveats);
        Assert.All(recs, r => Assert.Equal(hash, r.SummaryHash));
        Assert.All(recs, r => Assert.Equal("m1", r.SummaryModel));
        File.Delete(path);
    }

    [Fact]
    public async Task SkipsWhenHashUnchanged()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "class VM {}");
        var hash = SummaryHash.Of(SourceSlicer.WholeFile(path));
        var recs = await new VmEnricher(new FakeLlm(), "m1")
            .EnrichAsync(Input(path), hash, storedVmHash: hash);
        Assert.Empty(recs);
        File.Delete(path);
    }
}
