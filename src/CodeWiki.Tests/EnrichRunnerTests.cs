using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CodeWiki.Semantic;
using Xunit;

namespace CodeWiki.Tests;

public class EnrichRunnerTests
{
    sealed class FakeReader : IGraphReader
    {
        public VmDossierInput Vm = new("vmpk", "VM.cs",
            new List<HandlerRef> { new("hpk", "SearchOrderAsync") });
        public IfaceUnitInput Iface = new("ipk", "",
            new List<SliceRef> { new("Svc.cs", 1, 2) });
        public VmDossierInput ReadVmDossier(string n) => Vm;
        public IfaceUnitInput ReadIfaceUnit(string n) => Iface;
        public IReadOnlyList<string> ListIfaceMethods(string n) => new[] { "SearchOrdersAsync" };
    }
    sealed class FakeLlm : ILlmClient
    {
        public Task<IReadOnlyList<LlmField>> EnrichAsync(LlmRequest req)
            => Task.FromResult<IReadOnlyList<LlmField>>(new List<LlmField>
            {
                new(VmPromptBuilder.ViewModelKey, "화면", null, null),
                new("SearchOrderAsync", "검색", null, null),
                new("SearchOrdersAsync", "서버 검색", null, null),
            });
    }
    sealed class FakeSink : ISemanticSink
    {
        public int Applied;
        public Task ApplySemanticAsync(IEnumerable<SemanticRecord> records)
        { foreach (var _ in records) Applied++; return Task.CompletedTask; }
    }

    [Fact]
    public async Task RunVmAsync_writes_sidecar_and_applies()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(root, "VM.cs"), "class VM {}");
        var sidecar = Path.Combine(root, "semantic.ndjson");
        var sink = new FakeSink();
        var runner = new EnrichRunner(new FakeReader(), new FakeLlm(), sink, "m1", sidecar, root);

        var n = await runner.RunVmAsync("SearchOrderViewModel");

        Assert.Equal(2, n);                                  // ViewModelKey + SearchOrderAsync (Unknown 없음)
        Assert.Equal(2, sink.Applied);
        Assert.Equal(2, SemanticSerializer.Read(sidecar).Count);
    }

    [Fact]
    public async Task RunVmAsync_delta_skips_when_unchanged()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(root, "VM.cs"), "class VM {}");
        var sidecar = Path.Combine(root, "semantic.ndjson");
        var sink = new FakeSink();
        var runner = new EnrichRunner(new FakeReader(), new FakeLlm(), sink, "m1", sidecar, root);

        await runner.RunVmAsync("X");
        sink.Applied = 0;
        var n2 = await runner.RunVmAsync("X");             // VM.cs 불변 → 스킵

        Assert.Equal(0, n2);
        Assert.Equal(0, sink.Applied);
    }

    [Fact]
    public async Task RunIfaceAsync_writes_single_record()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(root, "Svc.cs"), "a\nb\nc\n");
        var sidecar = Path.Combine(root, "semantic.ndjson");
        var sink = new FakeSink();
        var runner = new EnrichRunner(new FakeReader(), new FakeLlm(), sink, "m1", sidecar, root);

        var n = await runner.RunIfaceAsync("SearchOrdersAsync");

        Assert.Equal(1, n);
        Assert.Equal("ipk", SemanticSerializer.Read(sidecar)[0].Pk);
    }
}
