# enrich 대상 선택 TUI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `enrich`를 TUI 전용으로 전환 — 프로젝트→ViewModel 다중선택, 폴더별 인터페이스→메서드 다중선택을 콘솔에서 골라 시맨틱 생성.

**Architecture:** 기존 `Program.cs` enrich 인라인 로직을 재사용 코어 `EnrichRunner`로 추출하고, 파일시스템 목록 리더 `VanuatuLayout` + 그래프 메서드 리스터 `Neo4jGraphReader.ListIfaceMethods` + Spectre.Console TUI `EnrichPicker`를 붙인다. `--vm`/`--iface` CLI 옵션은 제거하고 `enrich -c ... --semantic ...`가 곧 TUI를 띄운다. 시맨틱 생성 로직(프롬프트·해시·델타-스킵·사이드카)은 변경 없음.

**Tech Stack:** C# net10.0, Neo4j.Driver, Spectre.Console(신규), xUnit.

설계 정본: [docs/superpowers/specs/2026-06-20-enrich-tui-design.md](../specs/2026-06-20-enrich-tui-design.md).

## Global Constraints

- TargetFramework `net10.0`. `Node`/DTO는 불변 record.
- 기존 enrich 불변식 유지: 결정론/LLM 경계, 사이드카(`semantic.ndjson`) 분리, `summaryHash` 델타-스킵, 루트 조인은 해시/슬라이스 **전에**(`Path.Combine(VANUATU_ROOT, sourcePath)`).
- 비밀: API 키는 `AppSettings.AnthropicApiKey`(env > appsettings.json). **로그·커밋 금지.**
- 인터페이스는 `Domain/Vanuatu.Service/<folder>/I*.cs`에 있다(Torba.Service는 구현체). enrich 가능한 메서드 = 그 인터페이스가 선언하고 `Torba.Service` 구현이 있는 것.
- 빌드 `dotnet build src/CodeWiki/CodeWiki.csproj -c Release`, 테스트 `dotnet test`. 기존 64 테스트 깨지 말 것.
- 모델 기본값 `claude-haiku-4-5-20251001`. Vanuatu 루트 기본값 `C:\develop\baw\phase2\baw-phase2-platform\Vanuatu`.

---

## File Structure

| 파일 | 책임 |
|---|---|
| `src/CodeWiki/Semantic/IGraphReader.cs` (수정) | `ListIfaceMethods(string)` 추가 |
| `src/CodeWiki/Semantic/Neo4jGraphReader.cs` (수정) | `ListIfaceMethods` 구현(그래프) |
| `src/CodeWiki/Semantic/ISemanticSink.cs` (신규) | `ApplySemanticAsync` 추상화(테스트용 시임) |
| `src/CodeWiki/Storage/Neo4jLoader.cs` (수정) | `ISemanticSink` 구현 선언 추가 |
| `src/CodeWiki/Semantic/EnrichRunner.cs` (신규) | enrich 실행 코어(VM/iface), Program에서 추출 |
| `src/CodeWiki/Cli/VanuatuLayout.cs` (신규) | FS 목록(프로젝트/ViewModel/인터페이스) |
| `src/CodeWiki/Semantic/EnrichPicker.cs` (신규) | Spectre.Console TUI |
| `src/CodeWiki/Cli/CliOptions.cs` (수정) | `Vm`/`Iface` 제거 |
| `src/CodeWiki/Program.cs` (수정) | enrich → picker, usage 갱신 |
| `src/CodeWiki/CodeWiki.csproj` (수정) | Spectre.Console 패키지 |
| `README.md` (수정) | §6.1 enrich = TUI |
| 대응 `*Tests.cs` | EnrichRunner, VanuatuLayout, CliOptions |

---

## Task 1: `IGraphReader.ListIfaceMethods` (그래프 — enrich 가능 메서드)

**Files:**
- Modify: `src/CodeWiki/Semantic/IGraphReader.cs`
- Modify: `src/CodeWiki/Semantic/Neo4jGraphReader.cs`

**Interfaces:**
- Produces: `IReadOnlyList<string> IGraphReader.ListIfaceMethods(string interfaceName)` — 그 인터페이스가 선언하고 `Torba.Service` 구현이 있는 메서드 이름(중복 제거, 정렬). 통합(그래프 의존), 단위테스트 없음 → 빌드 + E2E(Task 6)로 검증.

- [ ] **Step 1: 인터페이스에 메서드 추가**

`src/CodeWiki/Semantic/IGraphReader.cs`의 `interface IGraphReader { ... }`에 한 줄 추가:

```csharp
    IReadOnlyList<string> ListIfaceMethods(string interfaceName);
```
(파일 상단에 `using System.Collections.Generic;`가 이미 있는지 확인, 없으면 추가.)

- [ ] **Step 2: `Neo4jGraphReader`에 구현 추가**

`src/CodeWiki/Semantic/Neo4jGraphReader.cs`에 public 동기 래퍼 + private async 추가(기존 `ReadVmDossier` 패턴과 동일하게):

```csharp
    public IReadOnlyList<string> ListIfaceMethods(string interfaceName) =>
        ListIfaceMethodsAsync(interfaceName).GetAwaiter().GetResult();

    private async Task<IReadOnlyList<string>> ListIfaceMethodsAsync(string interfaceName)
    {
        await using var s = _driver.AsyncSession();
        var cur = await s.RunAsync(@"
            MATCH (i:Interface {name:$n})-[:DECLARES]->(m:Method)
            WHERE EXISTS { (m)<-[:IMPLEMENTS_METHOD]-(impl:Method)
                           WHERE impl.fullName STARTS WITH 'Torba.Service' }
            RETURN DISTINCT m.name AS name ORDER BY name",
            new { n = interfaceName });
        var rows = await cur.ToListAsync();
        return rows.Select(r => r["name"].As<string>()).ToList();
    }
```
(상단 using에 `System.Collections.Generic`, `System.Linq`, `System.Threading.Tasks`가 있는지 확인.)

- [ ] **Step 3: 빌드 + 회귀 확인**

Run: `dotnet build src/CodeWiki/CodeWiki.csproj -c Release` → 0 errors.
Run: `dotnet test` → 기존 64 그대로 통과.

- [ ] **Step 4: 커밋**

```bash
git add src/CodeWiki/Semantic/IGraphReader.cs src/CodeWiki/Semantic/Neo4jGraphReader.cs
git commit -m "feat(codewiki): IGraphReader.ListIfaceMethods — enrich 가능 인터페이스 메서드 조회"
```

---

## Task 2: `EnrichRunner` + `ISemanticSink` (실행 코어 추출)

**Files:**
- Create: `src/CodeWiki/Semantic/ISemanticSink.cs`
- Create: `src/CodeWiki/Semantic/EnrichRunner.cs`
- Modify: `src/CodeWiki/Storage/Neo4jLoader.cs` (인터페이스 구현 선언)
- Test: `src/CodeWiki.Tests/EnrichRunnerTests.cs`

**Interfaces:**
- Consumes: `IGraphReader`(Task 1, `ReadVmDossier`/`ReadIfaceUnit`/`ListIfaceMethods`), `ILlmClient`, `VmEnricher`, `IfaceEnricher`, `SummaryHash`, `SourceSlicer`, `SemanticSerializer`.
- Produces:
  - `interface ISemanticSink { Task ApplySemanticAsync(IEnumerable<SemanticRecord> records); }`
  - `EnrichRunner(IGraphReader reader, ILlmClient llm, ISemanticSink sink, string model, string semanticPath, string vanuatuRoot)`
  - `Task<int> RunVmAsync(string vmName)` / `Task<int> RunIfaceAsync(string methodName)` — fresh 레코드 수 반환. 사이드카 병합 기록 + sink 적용. delta-skip 시 0(쓰기/적용 없음). VM.cs 없으면 `FileNotFoundException`.

- [ ] **Step 1: 실패 테스트 작성**

`src/CodeWiki.Tests/EnrichRunnerTests.cs`:

```csharp
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
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test --filter EnrichRunnerTests`
Expected: FAIL — `EnrichRunner`/`ISemanticSink` 없음.

- [ ] **Step 3: `ISemanticSink.cs` 작성**

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CodeWiki.Semantic;

public interface ISemanticSink
{
    Task ApplySemanticAsync(IEnumerable<SemanticRecord> records);
}
```

- [ ] **Step 4: `Neo4jLoader`가 `ISemanticSink` 구현 선언**

`src/CodeWiki/Storage/Neo4jLoader.cs`의 클래스 선언을 교체(메서드 시그니처는 이미 일치):

```csharp
public sealed class Neo4jLoader : System.IAsyncDisposable, CodeWiki.Semantic.ISemanticSink
```

- [ ] **Step 5: `EnrichRunner.cs` 작성**

```csharp
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
```

- [ ] **Step 6: 테스트 통과 + 회귀**

Run: `dotnet test`
Expected: PASS (64 + 3 = 67).

- [ ] **Step 7: 커밋**

```bash
git add src/CodeWiki/Semantic/ISemanticSink.cs src/CodeWiki/Semantic/EnrichRunner.cs src/CodeWiki/Storage/Neo4jLoader.cs src/CodeWiki.Tests/EnrichRunnerTests.cs
git commit -m "feat(codewiki): EnrichRunner 실행 코어 추출 + ISemanticSink"
```

---

## Task 3: `VanuatuLayout` (파일시스템 목록)

**Files:**
- Create: `src/CodeWiki/Cli/VanuatuLayout.cs`
- Test: `src/CodeWiki.Tests/VanuatuLayoutTests.cs`

**Interfaces:**
- Produces:
  - `IReadOnlyList<string> VanuatuLayout.ListClientModuleProjects(string root)` — `root/Client/Module/*` 디렉터리 이름(정렬). 없으면 빈 목록.
  - `IReadOnlyList<string> VanuatuLayout.ListViewModels(string projectDir)` — `projectDir/ViewModels/*ViewModel.cs` 파일명(확장자 제거, 정렬).
  - `IReadOnlyList<(string Folder, string Name)> VanuatuLayout.ListServiceInterfaces(string root)` — `root/Domain/Vanuatu.Service/<folder>/I*.cs`, `bin`/`obj` 폴더 제외.

- [ ] **Step 1: 실패 테스트 작성**

`src/CodeWiki.Tests/VanuatuLayoutTests.cs`:

```csharp
using System.IO;
using System.Linq;
using CodeWiki.Cli;
using Xunit;

namespace CodeWiki.Tests;

public class VanuatuLayoutTests
{
    static string MakeRoot()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        Directory.CreateDirectory(Path.Combine(root, "Client", "Module", "Shefa.Module.Order", "ViewModels"));
        Directory.CreateDirectory(Path.Combine(root, "Client", "Module", "Shefa.Module.Customer"));
        File.WriteAllText(Path.Combine(root, "Client", "Module", "Shefa.Module.Order", "ViewModels", "SearchOrderViewModel.cs"), "");
        File.WriteAllText(Path.Combine(root, "Client", "Module", "Shefa.Module.Order", "ViewModels", "EditOrderViewModel.cs"), "");
        File.WriteAllText(Path.Combine(root, "Client", "Module", "Shefa.Module.Order", "ViewModels", "Helper.cs"), "");
        Directory.CreateDirectory(Path.Combine(root, "Domain", "Vanuatu.Service", "Order"));
        Directory.CreateDirectory(Path.Combine(root, "Domain", "Vanuatu.Service", "obj"));
        File.WriteAllText(Path.Combine(root, "Domain", "Vanuatu.Service", "Order", "IOrderService.cs"), "");
        File.WriteAllText(Path.Combine(root, "Domain", "Vanuatu.Service", "Order", "OrderHelper.cs"), "");
        File.WriteAllText(Path.Combine(root, "Domain", "Vanuatu.Service", "obj", "IGenerated.cs"), "");
        return root;
    }

    [Fact]
    public void ListsProjects()
    {
        var p = VanuatuLayout.ListClientModuleProjects(MakeRoot());
        Assert.Contains("Shefa.Module.Order", p);
        Assert.Contains("Shefa.Module.Customer", p);
    }

    [Fact]
    public void ListsViewModelsByName_excludingNonViewModelCs()
    {
        var root = MakeRoot();
        var vms = VanuatuLayout.ListViewModels(Path.Combine(root, "Client", "Module", "Shefa.Module.Order"));
        Assert.Equal(new[] { "EditOrderViewModel", "SearchOrderViewModel" }, vms.ToArray());
        Assert.DoesNotContain("Helper", vms);
    }

    [Fact]
    public void ListsInterfaces_byFolder_excludingObjAndNonInterface()
    {
        var ifaces = VanuatuLayout.ListServiceInterfaces(MakeRoot());
        Assert.Contains(("Order", "IOrderService"), ifaces);
        Assert.DoesNotContain(ifaces, x => x.Name == "OrderHelper");      // I*.cs 아님
        Assert.DoesNotContain(ifaces, x => x.Folder == "obj");            // obj 제외
    }

    [Fact]
    public void MissingDirsReturnEmpty()
    {
        var empty = Directory.CreateTempSubdirectory().FullName;
        Assert.Empty(VanuatuLayout.ListClientModuleProjects(empty));
        Assert.Empty(VanuatuLayout.ListViewModels(empty));
        Assert.Empty(VanuatuLayout.ListServiceInterfaces(empty));
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test --filter VanuatuLayoutTests`
Expected: FAIL — `VanuatuLayout` 없음.

- [ ] **Step 3: 구현**

`src/CodeWiki/Cli/VanuatuLayout.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeWiki.Cli;

public static class VanuatuLayout
{
    public static IReadOnlyList<string> ListClientModuleProjects(string root)
    {
        var dir = Path.Combine(root, "Client", "Module");
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        return Directory.GetDirectories(dir)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    public static IReadOnlyList<string> ListViewModels(string projectDir)
    {
        var dir = Path.Combine(projectDir, "ViewModels");
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        return Directory.GetFiles(dir, "*ViewModel.cs")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    public static IReadOnlyList<(string Folder, string Name)> ListServiceInterfaces(string root)
    {
        var baseDir = Path.Combine(root, "Domain", "Vanuatu.Service");
        var result = new List<(string, string)>();
        if (!Directory.Exists(baseDir)) return result;
        foreach (var folder in Directory.GetDirectories(baseDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var folderName = Path.GetFileName(folder);
            if (folderName is "bin" or "obj") continue;
            foreach (var f in Directory.GetFiles(folder, "I*.cs").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                result.Add((folderName, Path.GetFileNameWithoutExtension(f)));
        }
        return result;
    }
}
```

- [ ] **Step 4: 테스트 통과 + 회귀**

Run: `dotnet test`
Expected: PASS (67 + 4 = 71).

- [ ] **Step 5: 커밋**

```bash
git add src/CodeWiki/Cli/VanuatuLayout.cs src/CodeWiki.Tests/VanuatuLayoutTests.cs
git commit -m "feat(codewiki): VanuatuLayout — 프로젝트/ViewModel/인터페이스 FS 목록"
```

---

## Task 4: Spectre.Console + `EnrichPicker` (TUI)

**Files:**
- Modify: `src/CodeWiki/CodeWiki.csproj`
- Create: `src/CodeWiki/Semantic/EnrichPicker.cs`

**Interfaces:**
- Consumes: `EnrichRunner`(Task 2), `IGraphReader.ListIfaceMethods`(Task 1), `VanuatuLayout`(Task 3), `Spectre.Console`.
- Produces: `EnrichPicker(EnrichRunner runner, IGraphReader reader, string vanuatuRoot)` + `Task RunAsync()`. 대화형이라 단위테스트 없음 → 빌드 + 수동 E2E(Task 6).

- [ ] **Step 1: Spectre.Console 패키지 추가**

Run: `dotnet add src/CodeWiki/CodeWiki.csproj package Spectre.Console`
→ `CodeWiki.csproj`의 `<ItemGroup>`에 `<PackageReference Include="Spectre.Console" Version="..." />` 추가됨(최신 안정, 0.49+).

- [ ] **Step 2: `EnrichPicker.cs` 작성**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CodeWiki.Cli;
using Spectre.Console;

namespace CodeWiki.Semantic;

public sealed class EnrichPicker
{
    private readonly EnrichRunner _runner;
    private readonly IGraphReader _reader;
    private readonly string _root;

    public EnrichPicker(EnrichRunner runner, IGraphReader reader, string vanuatuRoot)
    {
        _runner = runner; _reader = reader; _root = vanuatuRoot;
    }

    public async Task RunAsync()
    {
        var top = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("enrich 대상 종류")
            .AddChoices("화면 ViewModel", "서버 인터페이스", "종료"));
        if (top == "화면 ViewModel") await RunVmFlow();
        else if (top == "서버 인터페이스") await RunIfaceFlow();
    }

    private async Task RunVmFlow()
    {
        var projects = VanuatuLayout.ListClientModuleProjects(_root);
        if (projects.Count == 0) { Warn("Client/Module 프로젝트가 없습니다."); return; }
        var project = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("프로젝트").PageSize(20).AddChoices(projects));
        var projectDir = System.IO.Path.Combine(_root, "Client", "Module", project);
        var vms = VanuatuLayout.ListViewModels(projectDir);
        if (vms.Count == 0) { Warn("ViewModel이 없습니다."); return; }
        var picked = AnsiConsole.Prompt(new MultiSelectionPrompt<string>()
            .Title($"{project} — ViewModel 선택 (space 토글, enter 확정)")
            .PageSize(20).Required(false).AddChoices(vms));
        if (picked.Count == 0) { Warn("선택 없음."); return; }
        await RunEach(picked, _runner.RunVmAsync);
    }

    private async Task RunIfaceFlow()
    {
        var ifaces = VanuatuLayout.ListServiceInterfaces(_root);
        if (ifaces.Count == 0) { Warn("인터페이스가 없습니다."); return; }
        var prompt = new SelectionPrompt<string>().Title("인터페이스 (폴더별)").PageSize(20);
        foreach (var g in ifaces.GroupBy(x => x.Folder))
            prompt.AddChoiceGroup(g.Key, g.Select(x => x.Name));
        var iface = AnsiConsole.Prompt(prompt);
        var methods = _reader.ListIfaceMethods(iface);
        if (methods.Count == 0) { Warn($"{iface}: enrich 가능한 메서드가 없습니다(Torba 구현 없음)."); return; }
        var picked = AnsiConsole.Prompt(new MultiSelectionPrompt<string>()
            .Title($"{iface} — 메서드 선택").PageSize(20).Required(false).AddChoices(methods));
        if (picked.Count == 0) { Warn("선택 없음."); return; }
        await RunEach(picked, _runner.RunIfaceAsync);
    }

    private static async Task RunEach(IReadOnlyList<string> items, Func<string, Task<int>> run)
    {
        int enriched = 0, skipped = 0, failed = 0;
        foreach (var item in items)
        {
            try
            {
                var n = await run(item);
                if (n > 0) { enriched += n; AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(item)}: {n} records"); }
                else { skipped++; AnsiConsole.MarkupLine($"[grey]•[/] {Markup.Escape(item)}: skipped"); }
            }
            catch (Exception e)
            {
                failed++; AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(item)}: {Markup.Escape(e.Message)}");
            }
        }
        AnsiConsole.MarkupLine($"[bold]done — enriched {enriched} / skipped {skipped} / failed {failed}[/]");
    }

    private static void Warn(string msg) => AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(msg)}[/]");
}
```

- [ ] **Step 3: 빌드 + 회귀 확인**

Run: `dotnet build src/CodeWiki/CodeWiki.csproj -c Release` → 0 errors.
Run: `dotnet test` → 71 그대로 통과(신규 테스트 없음).

- [ ] **Step 4: 커밋**

```bash
git add src/CodeWiki/CodeWiki.csproj src/CodeWiki/Semantic/EnrichPicker.cs
git commit -m "feat(codewiki): EnrichPicker TUI(Spectre.Console) — 프로젝트/VM·인터페이스/메서드 선택"
```

---

## Task 5: Program 재배선 + `--vm`/`--iface` 제거 + README

**Files:**
- Modify: `src/CodeWiki/Cli/CliOptions.cs`
- Modify: `src/CodeWiki/Program.cs`
- Modify: `src/CodeWiki.Tests/CliOptionsTests.cs`
- Modify: `README.md`

**Interfaces:**
- Consumes: `EnrichRunner`, `EnrichPicker`, `Neo4jGraphReader`, `Neo4jLoader`, `AnthropicClient`, `AppSettings`.
- Produces: `enrich -c <db:user:pass> --semantic <out> [--model <id>]` → TUI. `CliOptions`에서 `Vm`/`Iface` 제거.

- [ ] **Step 1: CliOptions 테스트 교체(실패 유도)**

`src/CodeWiki.Tests/CliOptionsTests.cs`에서 `ParsesEnrichVmOptions` 테스트를 찾아 **삭제**하고 아래로 교체(같은 클래스 내):

```csharp
    [Fact]
    public void ParsesEnrichOptions()
    {
        var o = CliOptions.Parse(new[]
        {
            "enrich", "-c", "neo4j:neo4j:pw",
            "--semantic", "out/semantic.ndjson", "--model", "claude-haiku-4-5-20251001"
        });
        Assert.Equal("enrich", o.Verb);
        Assert.Equal("neo4j:neo4j:pw", o.Credentials);
        Assert.Equal("out/semantic.ndjson", o.Semantic);
        Assert.Equal("claude-haiku-4-5-20251001", o.Model);
    }
```
(`ParsesLoadSemantic` 등 다른 테스트는 그대로 둔다.)

- [ ] **Step 2: 테스트 실패 확인(컴파일 에러)**

Run: `dotnet test --filter CliOptionsTests`
Expected: FAIL — 아직 `CliOptions`에 `Vm`/`Iface` 참조가 남아 컴파일 깨짐(다음 스텝에서 제거). 또는 교체 전 `ParsesEnrichVmOptions`가 `o.Vm` 참조로 깨짐.

- [ ] **Step 3: `CliOptions`에서 `Vm`/`Iface` 제거**

`src/CodeWiki/Cli/CliOptions.cs` 전체 교체:

```csharp
namespace CodeWiki.Cli;

public sealed record CliOptions(string Verb, string? Solution, string? Output,
    string? Credentials, string? Ndjson, bool Wipe, string? Semantic, string? Model)
{
    public static CliOptions Parse(string[] args)
    {
        string verb = args.Length > 0 ? args[0] : "";
        string? sln = null, o = null, c = null, ndjson = null, semantic = null, model = null;
        bool wipe = false;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-s": case "--solution": if (++i < args.Length) sln = args[i]; break;
                case "-o": case "--output": if (++i < args.Length) o = args[i]; break;
                case "-c": case "--credentials": if (++i < args.Length) c = args[i]; break;
                case "--ndjson": if (++i < args.Length) ndjson = args[i]; break;
                case "--wipe": wipe = true; break;
                case "--semantic": if (++i < args.Length) semantic = args[i]; break;
                case "--model": if (++i < args.Length) model = args[i]; break;
            }
        }
        return new CliOptions(verb, sln, o, c, ndjson, wipe, semantic, model);
    }
}
```

- [ ] **Step 4: `Program.cs` enrich 케이스 교체**

`src/CodeWiki/Program.cs`의 `case "enrich": { ... }` 블록 전체(현재 약 45~100행)를 아래로 교체:

```csharp
    case "enrich":
    {
        if (o.Credentials == null || o.Semantic == null)
        {
            Console.Error.WriteLine("enrich requires -c <db:user:pass> --semantic <out>");
            return;
        }
        var apiKey = CodeWiki.AppSettings.AnthropicApiKey;
        if (string.IsNullOrEmpty(apiKey)) { Console.Error.WriteLine("ANTHROPIC_API_KEY not set (env 또는 appsettings.json Anthropic:ApiKey)"); return; }
        var model = o.Model ?? "claude-haiku-4-5-20251001";
        var parts = o.Credentials.Split(':');
        var vanuatuRoot = CodeWiki.AppSettings.VanuatuRoot ?? @"C:\develop\baw\phase2\baw-phase2-platform\Vanuatu";

        var driver = GraphDatabase.Driver("bolt://localhost:7687", AuthTokens.Basic(parts[^2], parts[^1]));
        await using var reader = new Neo4jGraphReader(driver);
        await using var loader = new Neo4jLoader("bolt://localhost:7687", parts[^2], parts[^1]);
        using var http = new HttpClient();
        var llm = new AnthropicClient(apiKey, model, http);
        var runner = new EnrichRunner(reader, llm, loader, model, o.Semantic, vanuatuRoot);
        await new EnrichPicker(runner, reader, vanuatuRoot).RunAsync();
        break;
    }
```

그리고 `default:` 케이스의 usage 문자열에서 enrich 부분을 교체:

```csharp
        Console.Error.WriteLine("usage: codewiki extract -s <sln> -o <ndjson> | load -c <db:user:pass> --ndjson <f> [--wipe] [--semantic <path>] | enrich -c <db:user:pass> --semantic <out> [--model <id>]");
```

- [ ] **Step 5: 빌드 + 전체 테스트**

Run: `dotnet build src/CodeWiki/CodeWiki.csproj -c Release` → 0 errors.
Run: `dotnet test`
Expected: PASS (71 — vm 테스트 제거 +enrich 테스트 추가로 동일 수준).

- [ ] **Step 6: README §6.1 갱신**

`README.md`의 `### 6.1 실행` 본문(현재 `enrich --vm`/`--iface` 예시)을 아래로 교체:

```markdown
### 6.1 실행

`enrich`는 대화형 TUI다. 실행하면 ① 화면 ViewModel / 서버 인터페이스를 고르고, ② 화면은 프로젝트→ViewModel 다중·전체 선택, ③ 인터페이스는 폴더별 인터페이스→메서드 다중 선택으로 대상을 정한다.

```powershell
# (appsettings.json을 안 쓸 경우에만) 환경변수로 주입
$env:ANTHROPIC_API_KEY = "sk-ant-..."     # 커밋 금지

dotnet run --project src/CodeWiki -c Release -- `
  enrich -c "neo4j:neo4j:strazhpass" --semantic out/semantic.ndjson
```
선택분이 각각 처리되고 `enriched N / skipped M / failed K`로 요약된다. 변경 없는 입자는 자동 건너뜀(델타-스킵).
```

- [ ] **Step 7: 커밋**

```bash
git add src/CodeWiki/Cli/CliOptions.cs src/CodeWiki/Program.cs src/CodeWiki.Tests/CliOptionsTests.cs README.md
git commit -m "feat(codewiki): enrich를 TUI 전용으로 전환(--vm/--iface 제거)"
```

---

## Task 6: 수동 E2E (TUI 검증)

**Files:** 없음(실행 검증).

전제: Neo4j 기동 + L0 포함 그래프 적재됨(README §3~§4) + `appsettings.json` 또는 `ANTHROPIC_API_KEY`.

- [ ] **Step 1: TUI 실행**

Run:
```powershell
dotnet run --project src/CodeWiki -c Release -- enrich -c "neo4j:neo4j:strazhpass" --semantic out/semantic.ndjson
```

- [ ] **Step 2: VM 경로 확인**
`화면 ViewModel` → `Shefa.Module.Order` → `SearchOrderViewModel` 등 다중 선택 → enter.
Expected: `✓ SearchOrderViewModel: N records` 또는 `• ...: skipped`, 끝에 `done — enriched.../skipped.../failed...`.

- [ ] **Step 3: 인터페이스 경로 확인**
재실행 → `서버 인터페이스` → 폴더별 목록에서 `IOrderService` → 메서드 목록에서 `SearchOrdersAsync` 등 다중 선택.
Expected: 메서드별 `✓`/`•` + 요약.

- [ ] **Step 4: 사이드카 확인**
`out/semantic.ndjson`에 선택분 레코드가 누적됐는지 확인. 재실행 시 동일 선택은 `skipped`(델타-스킵).

- [ ] **Step 5: 검증 기록(선택)**
`docs/graphDoc/`에 한 줄 기록 또는 ledger 갱신.

---

## Self-Review

**Spec coverage:**
- TUI 전용 전환(--vm/--iface 제거) → Task 5. ✅
- 프로젝트→VM 다중/전체 → Task 3(목록)+Task 4(MultiSelectionPrompt)+Task 2(RunVmAsync). ✅
- 폴더별 인터페이스 단일→메서드 다중 → Task 3(ListServiceInterfaces)+Task 1(ListIfaceMethods)+Task 4(AddChoiceGroup+MultiSelect). ✅
- 인터페이스는 Vanuatu.Service에서 읽음 → Task 3. ✅
- 기존 enrich 코어 재사용 → Task 2(EnrichRunner). ✅
- Spectre.Console 의존성 → Task 4. ✅
- 항목별 try/catch + 요약 → Task 4(RunEach). ✅
- 테스트 전략(VanuatuLayout/EnrichRunner 단위, TUI 수동) → Task 2/3/6. ✅

**Placeholder scan:** 모든 코드 스텝에 실제 코드 포함. 인터랙티브(Task 4)·통합(Task 1)·E2E(Task 6)는 빌드/수동 검증으로 명시.

**Type consistency:** `EnrichRunner(IGraphReader, ILlmClient, ISemanticSink, string, string, string)` — Task 2 정의, Task 5 호출 일치. `ISemanticSink.ApplySemanticAsync(IEnumerable<SemanticRecord>)` — Neo4jLoader 기존 시그니처와 일치(Task 2 Step 4). `IGraphReader.ListIfaceMethods(string)` — Task 1 정의, Task 2 fake·Task 4 사용. `VanuatuLayout` 3메서드 — Task 3 정의, Task 4 사용. `RunVmAsync`/`RunIfaceAsync : Task<int>` — Task 2 정의, Task 4 `RunEach(Func<string,Task<int>>)` 일치.
