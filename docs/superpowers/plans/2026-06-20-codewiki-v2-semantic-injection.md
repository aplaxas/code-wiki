# CodeWiki v2 Source 시맨틱 주입 — MVP 구현 계획 (M0+M1)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Phase 1 그래프의 노드에 "소스 위치(L0 결정론) + 소스 시맨틱(LLM 3필드)"을 붙이고, `SearchOrder` 수직 슬라이스로 검증한다.

**Architecture:** ① `extract` 단계에 결정론 props(sourcePath/startLine/endLine, mutatesState/operationType)를 추가한다. ② 신규 `enrich` 단계가 Neo4j에서 노드의 sourcePath를 읽어 디스크 슬라이스를 LLM(Haiku)에 보내 `summary`/`effects`/`caveats`를 받아 **사이드카 `semantic.ndjson`** 에 저장하고 Neo4j에 upsert한다. ③ `load`가 사이드카를 리플레이해 `--wipe` 후에도 복원한다. LLM 호출은 `ILlmClient` 뒤로 격리해 오케스트레이션을 가짜 클라이언트로 TDD한다.

**Tech Stack:** C# net10.0, Roslyn(Microsoft.CodeAnalysis 5.3), Neo4j.Driver 6.2, xUnit, Anthropic Messages API(HTTP, tool-use 구조화 출력 + prompt caching).

설계 정본: [docs/codewiki-v2-spec.md](../../codewiki-v2-spec.md). 이 계획은 그 PRD의 §9 MVP 게이트(M0+M1)까지를 구현한다. M2/M3(대량 `--l1` + 전 화면 + 동시성)는 게이트 통과 후 후속 계획.

## Global Constraints

- **TargetFramework:** `net10.0`. 신규 코드도 동일.
- **불변 Node 레코드:** `Node`는 `record`, `Props`는 `IReadOnlyDictionary<string,string>`. 노드 수정은 `n with { Props = ... }`. 그래프 머지는 [Graph.cs:14-34](../../../src/CodeWiki/Model/Graph.cs#L14-L34) — **같은 pk 재추가 시 비어있지 않은 props가 머지**된다(L0가 이 머지에 의존).
- **결정론↔LLM 경계 (PRD §4 원칙 1):** `keyEntities`는 절대 LLM에 묻지 않는다(`USES` 엣지 그대로). LLM은 `summary`/`effects`/`caveats` 3필드만.
- **시맨틱은 사이드카 분리(PRD §5.3, D5):** 구조 `graph.ndjson`과 별개 `semantic.ndjson`. 쓰기는 기존 적재 패턴 `MERGE (n {pk}) SET n += props` 재사용.
- **네임스페이스 규약:** 서버 구현 = `Torba.Service.*`, 클라 프록시 = `Shefa.Service.RestAPI.*`. 단 결정론 분류는 네임스페이스가 아니라 **"리포지토리 필드를 실제로 쓰는가"** 로 판별(테스트 가능·이식성).
- **비밀정보:** `ANTHROPIC_API_KEY`는 환경변수에서만 읽는다. 커밋·로그 금지.
- **모델 id:** `claude-haiku-4-5-20251001`(기본). 정확한 id·캐싱 헤더는 구현 시 `claude-api` 스킬로 재확인.
- **테스트:** 프로젝트 `src/CodeWiki.Tests`, xUnit. 기존 42/42 깨지 말 것. 빌드/테스트: `dotnet build src/CodeWiki/CodeWiki.csproj -c Release` / `dotnet test`.

---

## File Structure

신규/수정 파일과 책임:

| 파일 | 책임 |
|---|---|
| `src/CodeWiki/Extraction/SourceLocationExtractor.cs` (신규) | 모든 Method 노드에 `sourcePath`/`startLine`/`endLine` 결정론 부착 |
| `src/CodeWiki/Roslyn/OperationKind.cs` (신규) | impl 본문 → `(mutatesState, operationType)` 분류(순수) |
| `src/CodeWiki/Extraction/InterfaceImplementationExtractor.cs` (수정) | 인터페이스 메서드 노드에 `mutatesState`/`operationType` 부착 |
| `src/CodeWiki/Pipeline/AnalysisPipeline.cs` (수정) | `SourceLocationExtractor` 등록 |
| `src/CodeWiki/Semantic/SemanticRecord.cs` (신규) | 사이드카 레코드 + `SummaryHash` |
| `src/CodeWiki/Semantic/SemanticSerializer.cs` (신규) | `semantic.ndjson` 읽기/쓰기 |
| `src/CodeWiki/Semantic/ILlmClient.cs` (신규) | `LlmRequest`/`LlmField`/`ILlmClient` 경계 |
| `src/CodeWiki/Semantic/SourceSlicer.cs` (신규) | 파일 통째/라인 슬라이스 읽기 |
| `src/CodeWiki/Semantic/VmPromptBuilder.cs` (신규) | VM.cs + 핸들러 목록 → `LlmRequest`(순수) |
| `src/CodeWiki/Semantic/VmEnricher.cs` (신규) | `--vm` 오케스트레이션 + 델타-스킵 |
| `src/CodeWiki/Semantic/IfacePromptBuilder.cs` (신규) | impl 슬라이스 번들 → `LlmRequest`(순수) |
| `src/CodeWiki/Semantic/IfaceEnricher.cs` (신규) | 단일 인터페이스 메서드 오케스트레이션 |
| `src/CodeWiki/Semantic/IGraphReader.cs` (신규) | VM dossier / iface unit 입력 조회 경계 |
| `src/CodeWiki/Semantic/Neo4jGraphReader.cs` (신규) | `IGraphReader` Neo4j 구현(통합 검증) |
| `src/CodeWiki/Semantic/AnthropicClient.cs` (신규) | `ILlmClient` HTTP 구현(통합 검증) |
| `src/CodeWiki/Storage/Neo4jLoader.cs` (수정) | `ApplySemanticAsync` 추가 |
| `src/CodeWiki/Cli/CliOptions.cs` (수정) | `enrich` verb 옵션(`--vm`/`--iface`/`--semantic`/`--model`) |
| `src/CodeWiki/Program.cs` (수정) | `enrich` verb 배선 + `load --semantic` 리플레이 |
| `src/CodeWiki.Tests/TestCompiler.cs` (수정) | 소스 경로 지정 컴파일 지원 |

각 신규 클래스에 대응하는 `*Tests.cs`(순수/오케스트레이션 한정). `AnthropicClient`·`Neo4jGraphReader`는 단위테스트 없이 Task 13 실행으로 검증.

---

## Phase A — M0: L0 결정론 추출

### Task 1: 소스 위치 추출기 (`SourceLocationExtractor`)

**Files:**
- Modify: `src/CodeWiki/Tests/TestCompiler.cs` → 경로 지정 오버로드 (실제 경로: `src/CodeWiki.Tests/TestCompiler.cs`)
- Create: `src/CodeWiki/Extraction/SourceLocationExtractor.cs`
- Modify: `src/CodeWiki/Pipeline/AnalysisPipeline.cs:19-27`
- Test: `src/CodeWiki.Tests/SourceLocationExtractorTests.cs`

**Interfaces:**
- Consumes: `IExtractor.Extract(ExtractionContext, Graph)`; `SymbolNodes.ForMethod(IMethodSymbol)`; `ExtractionContext.SolutionRoot`; `Graph.AddNode` 머지 동작.
- Produces: Method 노드에 props `sourcePath`(root 상대, `/` 구분), `startLine`(1-based), `endLine`(1-based).

- [ ] **Step 1: TestCompiler에 경로 지원 추가 (실패 테스트 대상이 컴파일되게)**

`src/CodeWiki.Tests/TestCompiler.cs`의 `Compile`을 경로 오버로드로 교체:

```csharp
public static (Compilation, SemanticModel) Compile(string source, string? path = null)
{
    var tree = CSharpSyntaxTree.ParseText(source, path: path ?? "");
    var refs = AppDomain.CurrentDomain.GetAssemblies()
        .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
        .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location));
    var c = CSharpCompilation.Create("Test", new[] { tree }, refs,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    return (c, c.GetSemanticModel(tree));
}
```

- [ ] **Step 2: 실패 테스트 작성**

`src/CodeWiki.Tests/SourceLocationExtractorTests.cs`:

```csharp
using System.Linq;
using CodeWiki.Extraction;
using CodeWiki.Model;
using Xunit;

namespace CodeWiki.Tests;

public class SourceLocationExtractorTests
{
    [Fact]
    public void MethodGetsRelativeSourcePathAndLines()
    {
        var (c, _) = TestCompiler.Compile(
            "namespace N { public class Foo {\n  public int Bar()=>1;\n} }",
            path: @"C:\sln\Mod\Foo.cs");
        var g = new Graph();
        new SourceLocationExtractor().Extract(new ExtractionContext(c, @"C:\sln", "T"), g);
        var bar = g.Nodes.Single(n => n.Name == "Bar");
        Assert.Equal("Mod/Foo.cs", bar.Props["sourcePath"]);
        Assert.Equal("2", bar.Props["startLine"]);
        Assert.Equal("2", bar.Props["endLine"]);
    }

    [Fact]
    public void PropertyAccessorsSkipped()
    {
        var (c, _) = TestCompiler.Compile(
            "namespace N { public class Foo { public int P { get; set; } } }",
            path: @"C:\sln\Foo.cs");
        var g = new Graph();
        new SourceLocationExtractor().Extract(new ExtractionContext(c, @"C:\sln", "T"), g);
        Assert.DoesNotContain(g.Nodes, n => n.Name is "get_P" or "set_P");
    }
}
```

- [ ] **Step 3: 테스트 실패 확인**

Run: `dotnet test --filter SourceLocationExtractorTests`
Expected: FAIL — `SourceLocationExtractor` 형식 없음(컴파일 에러).

- [ ] **Step 4: 추출기 구현**

`src/CodeWiki/Extraction/SourceLocationExtractor.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using CodeWiki.Model;
using CodeWiki.Roslyn;
using Microsoft.CodeAnalysis;

namespace CodeWiki.Extraction;

public sealed class SourceLocationExtractor : IExtractor
{
    public void Extract(ExtractionContext ctx, Graph graph)
    {
        foreach (var t in ctx.SourceTypes())
        foreach (var m in t.GetMembers().OfType<IMethodSymbol>())
        {
            if (m.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet
                or MethodKind.EventAdd or MethodKind.EventRemove) continue;
            var loc = m.Locations.FirstOrDefault(l => l.IsInSource);
            if (loc is null) continue;
            var span = loc.GetLineSpan();
            if (string.IsNullOrEmpty(span.Path)) continue;
            var rel = System.IO.Path.GetRelativePath(ctx.SolutionRoot, span.Path).Replace('\\', '/');
            var baseNode = SymbolNodes.ForMethod(m);
            var props = new Dictionary<string, string>(baseNode.Props)
            {
                ["sourcePath"] = rel,
                ["startLine"] = (span.StartLinePosition.Line + 1).ToString(),
                ["endLine"] = (span.EndLinePosition.Line + 1).ToString(),
            };
            graph.AddNode(baseNode with { Props = props });
        }
    }
}
```

- [ ] **Step 5: 파이프라인에 등록**

`src/CodeWiki/Pipeline/AnalysisPipeline.cs`의 `extractors` 배열에 추가(`TypeExtractor` 다음 줄):

```csharp
            new TypeExtractor(roles),
            new SourceLocationExtractor(),
            new InterfaceImplementationExtractor(),
```

- [ ] **Step 6: 테스트 통과 확인**

Run: `dotnet test --filter SourceLocationExtractorTests`
Expected: PASS (2 tests).

- [ ] **Step 7: 전체 테스트 회귀 확인**

Run: `dotnet test`
Expected: PASS (기존 42 + 신규 2 = 44).

- [ ] **Step 8: 커밋**

```bash
git add src/CodeWiki/Extraction/SourceLocationExtractor.cs src/CodeWiki/Pipeline/AnalysisPipeline.cs src/CodeWiki.Tests/SourceLocationExtractorTests.cs src/CodeWiki.Tests/TestCompiler.cs
git commit -m "feat(codewiki): L0 소스 위치 props(sourcePath/startLine/endLine) 추출"
```

---

### Task 2: 결정론 operation 분류 (`OperationKind` + 인터페이스 메서드 props)

**Files:**
- Create: `src/CodeWiki/Roslyn/OperationKind.cs`
- Modify: `src/CodeWiki/Extraction/InterfaceImplementationExtractor.cs`
- Test: `src/CodeWiki.Tests/OperationKindTests.cs`

**Interfaces:**
- Consumes: `SemanticModel`, `SyntaxNode`(impl 본문); `RepositoryUsageExtractor`의 리포지토리 필드 판별 규칙(타입명에 "Repository" 포함, 제네릭).
- Produces: `OperationKind.Classify(SyntaxNode, SemanticModel) : (string mutatesState, string operationType)?` — 리포지토리 미사용이면 `null`. 인터페이스 Method 노드 props `mutatesState`(`true`/`false`/`unknown`), `operationType`(`command`/`query`/`unknown`).

- [ ] **Step 1: 실패 테스트 작성**

`src/CodeWiki.Tests/OperationKindTests.cs`:

```csharp
using CodeWiki.Roslyn;
using Microsoft.CodeAnalysis;
using Xunit;

namespace CodeWiki.Tests;

public class OperationKindTests
{
    static (SyntaxNode body, SemanticModel model) Method(string body)
    {
        var src = @"namespace N {
            public interface IRepository<T> { void Update(T x); System.Collections.Generic.List<T> Table { get; } }
            public class Order {}
            public class Svc { private IRepository<Order> _repo;
                public void M(){ " + body + @" } } }";
        var (c, m) = TestCompiler.Compile(src);
        var svc = (INamedTypeSymbol)c.GetSymbolsWithName("Svc").Single();
        var method = svc.GetMembers("M").OfType<IMethodSymbol>().Single();
        var syntax = method.DeclaringSyntaxReferences[0].GetSyntax();
        return (syntax, c.GetSemanticModel(syntax.SyntaxTree));
    }

    [Fact]
    public void RepoMutationIsCommand()
    {
        var (b, m) = Method("_repo.Update(new Order());");
        Assert.Equal(("true", "command"), OperationKind.Classify(b, m));
    }

    [Fact]
    public void RepoReadOnlyIsQuery()
    {
        var (b, m) = Method("var x = _repo.Table;");
        Assert.Equal(("false", "query"), OperationKind.Classify(b, m));
    }

    [Fact]
    public void NoRepoReturnsNull()
    {
        var (b, m) = Method("System.Console.WriteLine(1);");
        Assert.Null(OperationKind.Classify(b, m));
    }
}
```

(필요 using: `System.Linq`. 파일 상단에 추가.)

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test --filter OperationKindTests`
Expected: FAIL — `OperationKind` 없음.

- [ ] **Step 3: `OperationKind` 구현**

`src/CodeWiki/Roslyn/OperationKind.cs`:

```csharp
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeWiki.Roslyn;

public static class OperationKind
{
    private static readonly string[] MutationVerbs =
        { "Insert", "Add", "Update", "Delete", "Remove", "Save", "SaveChanges", "Create" };
    private static readonly string[] RawSqlMarkers =
        { "CallRawSQL", "ExecuteSqlRaw", "FromSqlRaw" };

    // 리포지토리를 만지지 않으면 null (클라 프록시·순수 UI 등은 분류 대상 아님).
    public static (string mutatesState, string operationType)? Classify(SyntaxNode body, SemanticModel model)
    {
        if (!UsesRepository(body, model)) return null;
        bool rawSql = false, mutates = false;
        foreach (var inv in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var name = InvokedName(inv);
            if (name is null) continue;
            if (RawSqlMarkers.Any(name.Contains)) rawSql = true;
            var bare = name.EndsWith("Async") ? name[..^5] : name;
            if (MutationVerbs.Contains(bare)) mutates = true;
        }
        if (rawSql) return ("unknown", "unknown");
        return mutates ? ("true", "command") : ("false", "query");
    }

    private static bool UsesRepository(SyntaxNode body, SemanticModel model) =>
        body.DescendantNodes().OfType<IdentifierNameSyntax>().Any(id =>
            model.GetSymbolInfo(id).Symbol is IFieldSymbol f &&
            f.Type is INamedTypeSymbol ft && ft.IsGenericType && ft.Name.Contains("Repository"));

    private static string? InvokedName(InvocationExpressionSyntax inv) => inv.Expression switch
    {
        MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
        IdentifierNameSyntax id => id.Identifier.Text,
        _ => null
    };
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test --filter OperationKindTests`
Expected: PASS (3 tests).

- [ ] **Step 5: 인터페이스 추출기에 props 부착 — 실패 테스트 추가**

`src/CodeWiki.Tests/InterfaceImplementationExtractorTests.cs`에 추가:

```csharp
    [Fact]
    public void InterfaceMethodGetsDeterministicOperationProps()
    {
        var (c, _) = TestCompiler.Compile(@"namespace N {
            public interface IRepository<T> { void Update(T x); }
            public class Order {}
            public interface IOrderService { void Save(Order o); }
            public class OrderService : IOrderService {
                private IRepository<Order> _repo;
                public void Save(Order o){ _repo.Update(o); } } }");
        var g = new Graph();
        new InterfaceImplementationExtractor().Extract(new ExtractionContext(c, "/", "T"), g);
        var iface = g.Nodes.Single(n => n.FullName == "N.IOrderService.Save");
        Assert.Equal("true", iface.Props["mutatesState"]);
        Assert.Equal("command", iface.Props["operationType"]);
    }
```

- [ ] **Step 6: 테스트 실패 확인**

Run: `dotnet test --filter InterfaceImplementationExtractorTests`
Expected: FAIL — props 키 없음(KeyNotFound).

- [ ] **Step 7: `InterfaceImplementationExtractor` 수정**

`src/CodeWiki/Extraction/InterfaceImplementationExtractor.cs`의 루프 본문에서 `ifaceNode` 생성부를 교체:

```csharp
                    if (t.FindImplementationForInterfaceMember(member) is not IMethodSymbol impl) continue;
                    if (!SymbolEqualityComparer.Default.Equals(impl.ContainingType, t)) continue;
                    var implNode = SymbolNodes.ForMethod(impl);
                    var ifaceNode = SymbolNodes.ForMethod(member);

                    foreach (var sr in impl.DeclaringSyntaxReferences)
                    {
                        var syntax = sr.GetSyntax();
                        var model = ctx.Compilation.GetSemanticModel(syntax.SyntaxTree);
                        if (OperationKind.Classify(syntax, model) is { } k)
                        {
                            var props = new Dictionary<string, string>(ifaceNode.Props)
                            {
                                ["mutatesState"] = k.mutatesState,
                                ["operationType"] = k.operationType,
                            };
                            ifaceNode = ifaceNode with { Props = props };
                            break;
                        }
                    }

                    graph.AddNode(implNode);
                    graph.AddNode(ifaceNode);
                    graph.AddEdge(new Edge(Rel.ImplementsMethod, implNode.Pk, ifaceNode.Pk, Empty));
```

- [ ] **Step 8: 테스트 통과 + 회귀 확인**

Run: `dotnet test`
Expected: PASS (44 + 4 = 48). 기존 `InterfaceImplementationExtractor` 엣지 테스트도 그대로 통과.

- [ ] **Step 9: 커밋**

```bash
git add src/CodeWiki/Roslyn/OperationKind.cs src/CodeWiki/Extraction/InterfaceImplementationExtractor.cs src/CodeWiki.Tests/OperationKindTests.cs src/CodeWiki.Tests/InterfaceImplementationExtractorTests.cs
git commit -m "feat(codewiki): L0 결정론 mutatesState/operationType 인터페이스 메서드 부착"
```

---

## Phase B — 시맨틱 1차 자료(순수)

### Task 3: 사이드카 레코드 + `SummaryHash`

**Files:**
- Create: `src/CodeWiki/Semantic/SemanticRecord.cs`
- Test: `src/CodeWiki.Tests/SummaryHashTests.cs`

**Interfaces:**
- Produces: `record SemanticRecord(string Pk, string Summary, string? Effects, string? Caveats, string SummaryHash, string SummaryModel)`; `SummaryHash.Of(string) : string` (안정·결정론, 16 hex).

- [ ] **Step 1: 실패 테스트 작성**

`src/CodeWiki.Tests/SummaryHashTests.cs`:

```csharp
using CodeWiki.Semantic;
using Xunit;

namespace CodeWiki.Tests;

public class SummaryHashTests
{
    [Fact]
    public void SameInputSameHash()
        => Assert.Equal(SummaryHash.Of("abc"), SummaryHash.Of("abc"));

    [Fact]
    public void DifferentInputDifferentHash()
        => Assert.NotEqual(SummaryHash.Of("abc"), SummaryHash.Of("abd"));

    [Fact]
    public void HashIsSixteenHexChars()
        => Assert.Matches("^[0-9A-F]{16}$", SummaryHash.Of("anything"));
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test --filter SummaryHashTests`
Expected: FAIL — `CodeWiki.Semantic` 네임스페이스 없음.

- [ ] **Step 3: 구현**

`src/CodeWiki/Semantic/SemanticRecord.cs`:

```csharp
using System;
using System.Security.Cryptography;
using System.Text;

namespace CodeWiki.Semantic;

public sealed record SemanticRecord(
    string Pk, string Summary, string? Effects, string? Caveats,
    string SummaryHash, string SummaryModel);

public static class SummaryHash
{
    public static string Of(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))[..16];
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test --filter SummaryHashTests`
Expected: PASS (3 tests).

- [ ] **Step 5: 커밋**

```bash
git add src/CodeWiki/Semantic/SemanticRecord.cs src/CodeWiki.Tests/SummaryHashTests.cs
git commit -m "feat(codewiki): 시맨틱 사이드카 레코드 + SummaryHash"
```

---

### Task 4: 사이드카 직렬화 (`SemanticSerializer`)

**Files:**
- Create: `src/CodeWiki/Semantic/SemanticSerializer.cs`
- Test: `src/CodeWiki.Tests/SemanticSerializerTests.cs`

**Interfaces:**
- Consumes: `SemanticRecord`.
- Produces: `SemanticSerializer.Write(IEnumerable<SemanticRecord>, string path)`; `SemanticSerializer.Read(string path) : List<SemanticRecord>`. 라인당 JSON 1개(round-trip 보존).

- [ ] **Step 1: 실패 테스트 작성**

`src/CodeWiki.Tests/SemanticSerializerTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using CodeWiki.Semantic;
using Xunit;

namespace CodeWiki.Tests;

public class SemanticSerializerTests
{
    [Fact]
    public void RoundTripsRecords()
    {
        var path = Path.GetTempFileName();
        var recs = new List<SemanticRecord>
        {
            new("pk1", "검색한다", null, "페이징 필수", "ABCDEF0123456789", "claude-haiku-4-5-20251001"),
            new("pk2", "초기화", "없음", null, "0011223344556677", "claude-haiku-4-5-20251001"),
        };
        SemanticSerializer.Write(recs, path);
        var back = SemanticSerializer.Read(path);
        Assert.Equal(2, back.Count);
        Assert.Equal("검색한다", back[0].Summary);
        Assert.Null(back[0].Effects);
        Assert.Equal("없음", back[1].Effects);
        File.Delete(path);
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test --filter SemanticSerializerTests`
Expected: FAIL — `SemanticSerializer` 없음.

- [ ] **Step 3: 구현**

`src/CodeWiki/Semantic/SemanticSerializer.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CodeWiki.Semantic;

public static class SemanticSerializer
{
    public static void Write(IEnumerable<SemanticRecord> records, string path)
    {
        using var w = new StreamWriter(path, false);
        foreach (var r in records)
            w.WriteLine(JsonSerializer.Serialize(r));
    }

    public static List<SemanticRecord> Read(string path)
    {
        var list = new List<SemanticRecord>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            list.Add(JsonSerializer.Deserialize<SemanticRecord>(line)!);
        }
        return list;
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test --filter SemanticSerializerTests`
Expected: PASS.

- [ ] **Step 5: 커밋**

```bash
git add src/CodeWiki/Semantic/SemanticSerializer.cs src/CodeWiki.Tests/SemanticSerializerTests.cs
git commit -m "feat(codewiki): semantic.ndjson 직렬화"
```

---

## Phase C — LLM 경계

### Task 5: LLM 인터페이스 + 소스 슬라이서

**Files:**
- Create: `src/CodeWiki/Semantic/ILlmClient.cs`
- Create: `src/CodeWiki/Semantic/SourceSlicer.cs`
- Test: `src/CodeWiki.Tests/SourceSlicerTests.cs`

**Interfaces:**
- Produces:
  - `record LlmRequest(string System, string User)`
  - `record LlmField(string Key, string Summary, string? Effects, string? Caveats)`
  - `interface ILlmClient { Task<IReadOnlyList<LlmField>> EnrichAsync(LlmRequest req); }`
  - `SourceSlicer.WholeFile(string absPath) : string`
  - `SourceSlicer.Slice(string absPath, int startLine, int endLine) : string` (1-based, 양끝 포함)

- [ ] **Step 1: 실패 테스트 작성**

`src/CodeWiki.Tests/SourceSlicerTests.cs`:

```csharp
using System.IO;
using CodeWiki.Semantic;
using Xunit;

namespace CodeWiki.Tests;

public class SourceSlicerTests
{
    [Fact]
    public void SliceReturnsInclusiveLineRange()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "a\nb\nc\nd\n");
        Assert.Equal("b\nc", SourceSlicer.Slice(path, 2, 3));
        File.Delete(path);
    }

    [Fact]
    public void WholeFileReturnsAll()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "x\ny");
        Assert.Equal("x\ny", SourceSlicer.WholeFile(path));
        File.Delete(path);
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test --filter SourceSlicerTests`
Expected: FAIL — 형식 없음.

- [ ] **Step 3: 구현**

`src/CodeWiki/Semantic/ILlmClient.cs`:

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CodeWiki.Semantic;

public sealed record LlmRequest(string System, string User);
public sealed record LlmField(string Key, string Summary, string? Effects, string? Caveats);

public interface ILlmClient
{
    Task<IReadOnlyList<LlmField>> EnrichAsync(LlmRequest req);
}
```

`src/CodeWiki/Semantic/SourceSlicer.cs`:

```csharp
using System;
using System.IO;
using System.Linq;

namespace CodeWiki.Semantic;

public static class SourceSlicer
{
    public static string WholeFile(string absPath) => File.ReadAllText(absPath);

    public static string Slice(string absPath, int startLine, int endLine)
    {
        var lines = File.ReadAllLines(absPath);
        var from = Math.Max(1, startLine);
        var to = Math.Min(lines.Length, endLine);
        return string.Join("\n", lines.Skip(from - 1).Take(to - from + 1));
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test --filter SourceSlicerTests`
Expected: PASS.

- [ ] **Step 5: 커밋**

```bash
git add src/CodeWiki/Semantic/ILlmClient.cs src/CodeWiki/Semantic/SourceSlicer.cs src/CodeWiki.Tests/SourceSlicerTests.cs
git commit -m "feat(codewiki): LLM 클라이언트 경계 + 소스 슬라이서"
```

---

### Task 6: Anthropic 어댑터 (`AnthropicClient`) — 실 호출, 단위테스트 없음

**Files:**
- Create: `src/CodeWiki/Semantic/AnthropicClient.cs`

**Interfaces:**
- Consumes: `ILlmClient`, `LlmRequest`, `LlmField`; 환경변수 `ANTHROPIC_API_KEY`.
- Produces: `AnthropicClient(string apiKey, string model, HttpClient http) : ILlmClient`. Messages API에 tool-use 구조화 출력으로 `record_semantics` 도구를 강제, system 블록에 `cache_control` ephemeral.

> 이 태스크는 외부 HTTP라 단위테스트 대신 Task 13 실행으로 검증한다. 코드는 완성형으로 둔다.

- [ ] **Step 1: 구현**

`src/CodeWiki/Semantic/AnthropicClient.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace CodeWiki.Semantic;

public sealed class AnthropicClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly string _model;

    public AnthropicClient(string apiKey, string model, HttpClient http)
    {
        _http = http;
        _model = model;
        _http.BaseAddress ??= new Uri("https://api.anthropic.com/");
        _http.DefaultRequestHeaders.Remove("x-api-key");
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Remove("anthropic-version");
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    public async Task<IReadOnlyList<LlmField>> EnrichAsync(LlmRequest req)
    {
        var tool = new
        {
            name = "record_semantics",
            description = "각 코드 단위의 의미를 기록한다.",
            input_schema = new
            {
                type = "object",
                properties = new
                {
                    items = new
                    {
                        type = "array",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                key = new { type = "string" },
                                summary = new { type = "string" },
                                effects = new { type = "string" },
                                caveats = new { type = "string" }
                            },
                            required = new[] { "key", "summary" }
                        }
                    }
                },
                required = new[] { "items" }
            }
        };

        var body = new
        {
            model = _model,
            max_tokens = 2048,
            system = new[]
            {
                new { type = "text", text = req.System,
                      cache_control = new { type = "ephemeral" } }
            },
            tools = new[] { tool },
            tool_choice = new { type = "tool", name = "record_semantics" },
            messages = new[]
            {
                new { role = "user", content = req.User }
            }
        };

        using var resp = await _http.PostAsJsonAsync("v1/messages", body);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var list = new List<LlmField>();
        foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
        {
            if (block.GetProperty("type").GetString() != "tool_use") continue;
            var items = block.GetProperty("input").GetProperty("items");
            foreach (var it in items.EnumerateArray())
            {
                list.Add(new LlmField(
                    it.GetProperty("key").GetString() ?? "",
                    it.GetProperty("summary").GetString() ?? "",
                    it.TryGetProperty("effects", out var e) ? e.GetString() : null,
                    it.TryGetProperty("caveats", out var c) ? c.GetString() : null));
            }
        }
        return list;
    }
}
```

- [ ] **Step 2: 컴파일 확인**

Run: `dotnet build src/CodeWiki/CodeWiki.csproj -c Release`
Expected: 빌드 성공(0 error). 동작 검증은 Task 13.

- [ ] **Step 3: 커밋**

```bash
git add src/CodeWiki/Semantic/AnthropicClient.cs
git commit -m "feat(codewiki): Anthropic Messages API 어댑터(tool-use 구조화 출력 + 캐싱)"
```

---

## Phase D — enrich 오케스트레이션(가짜 LLM으로 TDD)

### Task 7: VM 프롬프트 빌더 (`VmPromptBuilder`, 순수)

**Files:**
- Create: `src/CodeWiki/Semantic/VmPromptBuilder.cs`
- Test: `src/CodeWiki.Tests/VmPromptBuilderTests.cs`

**Interfaces:**
- Consumes: VM.cs 내용(string), 핸들러 이름 목록.
- Produces: `VmPromptBuilder.Build(string vmCsContent, IReadOnlyList<string> handlerNames) : LlmRequest`. `System`은 정적 지시문(캐시 대상), `User`는 파일 내용 + "key=__viewmodel__ 와 각 핸들러 key를 채우라"는 지시. VM 요약 key 상수 `VmPromptBuilder.ViewModelKey = "__viewmodel__"`.

- [ ] **Step 1: 실패 테스트 작성**

`src/CodeWiki.Tests/VmPromptBuilderTests.cs`:

```csharp
using System.Collections.Generic;
using CodeWiki.Semantic;
using Xunit;

namespace CodeWiki.Tests;

public class VmPromptBuilderTests
{
    [Fact]
    public void UserPromptContainsFileAndHandlerKeys()
    {
        var req = VmPromptBuilder.Build("class VM { void SearchOrderAsync(){} }",
            new List<string> { "SearchOrderAsync", "ResetForm" });
        Assert.Contains("SearchOrderAsync", req.User);
        Assert.Contains("ResetForm", req.User);
        Assert.Contains(VmPromptBuilder.ViewModelKey, req.User);
        Assert.Contains("class VM", req.User);
    }

    [Fact]
    public void SystemPromptIsStaticAndMentionsThreeFields()
    {
        var a = VmPromptBuilder.Build("x", new List<string>());
        var b = VmPromptBuilder.Build("y", new List<string> { "H" });
        Assert.Equal(a.System, b.System);          // 캐시 가능하도록 입력 무관 정적
        Assert.Contains("summary", a.System);
        Assert.Contains("effects", a.System);
        Assert.Contains("caveats", a.System);
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test --filter VmPromptBuilderTests`
Expected: FAIL — `VmPromptBuilder` 없음.

- [ ] **Step 3: 구현**

`src/CodeWiki/Semantic/VmPromptBuilder.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace CodeWiki.Semantic;

public static class VmPromptBuilder
{
    public const string ViewModelKey = "__viewmodel__";

    private const string SystemPrompt =
        "당신은 WPF ViewModel 코드를 읽고 화면 동작의 의미를 요약한다. " +
        "record_semantics 도구로만 답한다. 각 item에 key/summary와, 해당되면 effects(부수효과)·caveats(주의점)를 채운다. " +
        "구조적 사실(어떤 엔티티를 만지는지 등)은 추정하지 말고 동작 의미만 한국어 한 줄로 요약한다. " +
        "필드는 summary·effects·caveats 셋뿐이다.";

    public static LlmRequest Build(string vmCsContent, IReadOnlyList<string> handlerNames)
    {
        var keys = new[] { ViewModelKey }.Concat(handlerNames);
        var user =
            "다음 ViewModel 파일을 요약하라.\n" +
            $"요약할 key 목록: {string.Join(", ", keys)}\n" +
            $"(key '{ViewModelKey}' = 이 화면 전체의 목적, 나머지 = 각 핸들러 메서드의 동작)\n\n" +
            "```csharp\n" + vmCsContent + "\n```";
        return new LlmRequest(SystemPrompt, user);
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test --filter VmPromptBuilderTests`
Expected: PASS.

- [ ] **Step 5: 커밋**

```bash
git add src/CodeWiki/Semantic/VmPromptBuilder.cs src/CodeWiki.Tests/VmPromptBuilderTests.cs
git commit -m "feat(codewiki): VM enrich 프롬프트 빌더"
```

---

### Task 8: VM enricher (`VmEnricher`) — 오케스트레이션 + 델타-스킵

**Files:**
- Create: `src/CodeWiki/Semantic/IGraphReader.cs` (입력 DTO 포함)
- Create: `src/CodeWiki/Semantic/VmEnricher.cs`
- Test: `src/CodeWiki.Tests/VmEnricherTests.cs`

**Interfaces:**
- Consumes: `ILlmClient`, `VmPromptBuilder`, `SourceSlicer.WholeFile`, `SummaryHash.Of`.
- Produces:
  - `record HandlerRef(string Pk, string Name)`
  - `record VmDossierInput(string VmPk, string VmCsPath, IReadOnlyList<HandlerRef> Handlers)`
  - `interface IGraphReader { VmDossierInput ReadVmDossier(string vmName); IfaceUnitInput ReadIfaceUnit(string ifaceMethodName); }` (IfaceUnitInput는 Task 9에서 정의 — 본 태스크에선 메서드 시그니처만 선언, 구현은 Task 10)
  - `VmEnricher(ILlmClient client, string model)` with `Task<List<SemanticRecord>> EnrichAsync(VmDossierInput input, string? currentVmHash, string? storedVmHash)`. `storedVmHash == currentVmHash`면 빈 리스트(스킵). 아니면 LLM 호출 후 key→pk 매핑(`ViewModelKey`→VmPk, 핸들러 이름→pk), 모든 레코드에 `currentVmHash`·`model` 부착.

> 주의: 본 태스크는 `IGraphReader`의 **선언만** 추가하고 `ReadIfaceUnit` 시그니처는 Task 9 이후 확정한다. 컴파일 순서를 위해 `IfaceUnitInput`를 Task 9에서 같은 파일에 추가한다. 본 태스크 테스트는 `IGraphReader`를 쓰지 않고 `VmEnricher`만 검증한다.

- [ ] **Step 1: 실패 테스트 작성**

`src/CodeWiki.Tests/VmEnricherTests.cs`:

```csharp
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
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test --filter VmEnricherTests`
Expected: FAIL — `VmEnricher`/`VmDossierInput`/`HandlerRef` 없음.

- [ ] **Step 3: `IGraphReader.cs` 작성(DTO + 인터페이스)**

`src/CodeWiki/Semantic/IGraphReader.cs`:

```csharp
using System.Collections.Generic;

namespace CodeWiki.Semantic;

public sealed record HandlerRef(string Pk, string Name);
public sealed record VmDossierInput(string VmPk, string VmCsPath, IReadOnlyList<HandlerRef> Handlers);

public interface IGraphReader
{
    VmDossierInput ReadVmDossier(string vmName);
    IfaceUnitInput ReadIfaceUnit(string ifaceMethodName);   // IfaceUnitInput: Task 9에서 정의
}
```

- [ ] **Step 4: `VmEnricher.cs` 구현**

`src/CodeWiki/Semantic/VmEnricher.cs`:

```csharp
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
```

> 테스트는 해시를 직접 넘기지만, 실 호출(Program)에서는 `currentVmHash = SummaryHash.Of(SourceSlicer.WholeFile(path))`로 계산해 넘긴다.

- [ ] **Step 5: 테스트 통과 확인**

Run: `dotnet test --filter VmEnricherTests`
Expected: PASS (2 tests). (`IfaceUnitInput` 미정의로 `IGraphReader.cs`가 컴파일 안 되면 Task 9를 먼저 합칠 것 — 본 계획은 Task 9가 바로 뒤따르므로, Step 3에서 `ReadIfaceUnit` 줄을 잠시 주석 처리하고 Task 9 Step에서 해제한다.)

- [ ] **Step 6: 커밋**

```bash
git add src/CodeWiki/Semantic/IGraphReader.cs src/CodeWiki/Semantic/VmEnricher.cs src/CodeWiki.Tests/VmEnricherTests.cs
git commit -m "feat(codewiki): VM enricher(키→pk 매핑 + 델타-스킵)"
```

---

### Task 9: 인터페이스 프롬프트 빌더 + enricher

**Files:**
- Create: `src/CodeWiki/Semantic/IfacePromptBuilder.cs`
- Create: `src/CodeWiki/Semantic/IfaceEnricher.cs`
- Modify: `src/CodeWiki/Semantic/IGraphReader.cs` (IfaceUnitInput 추가, `ReadIfaceUnit` 주석 해제)
- Test: `src/CodeWiki.Tests/IfaceEnricherTests.cs`

**Interfaces:**
- Produces:
  - `record SliceRef(string SourcePath, int StartLine, int EndLine)`
  - `record IfaceUnitInput(string IfacePk, string RootDir, IReadOnlyList<SliceRef> Slices)` — 서버 impl 슬라이스 + 1-hop 헬퍼 슬라이스(상대경로). 입력 텍스트 = 슬라이스 연결.
  - `IfacePromptBuilder.Build(string inputBundle, string methodName) : LlmRequest`
  - `IfaceEnricher(ILlmClient, string model)` with `Task<List<SemanticRecord>> EnrichAsync(IfaceUnitInput input, string methodName, string? storedHash)`. 입력 번들 해시로 델타-스킵, 단일 레코드(IfacePk) 산출.

- [ ] **Step 1: 실패 테스트 작성**

`src/CodeWiki.Tests/IfaceEnricherTests.cs`:

```csharp
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
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test --filter IfaceEnricherTests`
Expected: FAIL — 형식 없음.

- [ ] **Step 3: `IGraphReader.cs`에 `IfaceUnitInput` 추가 + `ReadIfaceUnit` 주석 해제**

`src/CodeWiki/Semantic/IGraphReader.cs`에 추가:

```csharp
public sealed record SliceRef(string SourcePath, int StartLine, int EndLine);
public sealed record IfaceUnitInput(string IfacePk, string RootDir, IReadOnlyList<SliceRef> Slices);
```

그리고 인터페이스의 `ReadIfaceUnit` 줄 주석을 해제한다.

- [ ] **Step 4: `IfacePromptBuilder.cs` 구현**

```csharp
namespace CodeWiki.Semantic;

public static class IfacePromptBuilder
{
    private const string SystemPrompt =
        "당신은 백엔드 서비스 구현 코드를 읽고 그 의미를 요약한다. " +
        "record_semantics 도구로만 답하며 item 하나만 만든다. key는 메서드 이름. " +
        "summary(동작 한 줄)·effects(부수효과)·caveats(주의점)만 채운다. " +
        "어떤 엔티티를 만지는지는 별도 결정론으로 알므로 추정하지 말라.";

    public static LlmRequest Build(string inputBundle, string methodName)
    {
        var user =
            $"다음 구현을 요약하라. key는 '{methodName}'.\n\n" +
            "```csharp\n" + inputBundle + "\n```";
        return new LlmRequest(SystemPrompt, user);
    }
}
```

- [ ] **Step 5: `IfaceEnricher.cs` 구현**

```csharp
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
```

- [ ] **Step 6: 테스트 통과 + 회귀**

Run: `dotnet test`
Expected: PASS (전체). VmEnricher 테스트도 `IfaceUnitInput` 정의 후 그대로 통과.

- [ ] **Step 7: 커밋**

```bash
git add src/CodeWiki/Semantic/IfacePromptBuilder.cs src/CodeWiki/Semantic/IfaceEnricher.cs src/CodeWiki/Semantic/IGraphReader.cs src/CodeWiki.Tests/IfaceEnricherTests.cs
git commit -m "feat(codewiki): 인터페이스 메서드 enricher(impl+1-hop 슬라이스 번들)"
```

---

## Phase E — 그래프 IO + CLI 배선

### Task 10: Neo4j 그래프 리더 (`Neo4jGraphReader`) — 통합

**Files:**
- Create: `src/CodeWiki/Semantic/Neo4jGraphReader.cs`

**Interfaces:**
- Consumes: `IGraphReader`, `VmDossierInput`/`IfaceUnitInput`, Neo4j.Driver.
- Produces: `Neo4jGraphReader(IDriver driver) : IGraphReader, IAsyncDisposable`. VM dossier: VM 노드 + `DEFINES_COMMAND→EXECUTES` 핸들러(pk,name)와 핸들러 `sourcePath`(VM.cs 경로 도출). iface unit: 인터페이스 메서드 pk + `Torba.Service` impl의 sourcePath/lines + impl의 `CALLS` 1-hop 메서드 슬라이스.

> 외부 DB 의존이라 단위테스트 없음. Task 13 실행으로 검증. 쿼리는 [docs/codewiki-v2-spec.md](../../codewiki-v2-spec.md) 및 cookbook 레시피 기반.

- [ ] **Step 1: 구현**

`src/CodeWiki/Semantic/Neo4jGraphReader.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Neo4j.Driver;

namespace CodeWiki.Semantic;

public sealed class Neo4jGraphReader : IGraphReader, IAsyncDisposable
{
    private readonly IDriver _driver;
    public Neo4jGraphReader(IDriver driver) => _driver = driver;

    public VmDossierInput ReadVmDossier(string vmName) => ReadVmDossierAsync(vmName).GetAwaiter().GetResult();
    public IfaceUnitInput ReadIfaceUnit(string ifaceMethodName) => ReadIfaceUnitAsync(ifaceMethodName).GetAwaiter().GetResult();

    private async Task<VmDossierInput> ReadVmDossierAsync(string vmName)
    {
        await using var s = _driver.AsyncSession();
        var cur = await s.RunAsync(@"
            MATCH (vm:ViewModel {name:$vm})
            MATCH (vm)-[:DEFINES_COMMAND]->(:Command)-[:EXECUTES]->(h:Method)
            WHERE h.sourcePath IS NOT NULL
            RETURN vm.pk AS vmPk,
                   collect(DISTINCT {pk:h.pk, name:h.name, sp:h.sourcePath}) AS handlers",
            new { vm = vmName });
        var rec = await cur.SingleAsync();
        var handlers = rec["handlers"].As<List<Dictionary<string, object>>>();
        var refs = handlers.Select(h => new HandlerRef(h["pk"].As<string>(), h["name"].As<string>())).ToList();
        var vmCsPath = handlers.Select(h => h["sp"].As<string>()).First();   // 핸들러는 VM.cs에 산다
        return new VmDossierInput(rec["vmPk"].As<string>(), vmCsPath, refs);
    }

    private async Task<IfaceUnitInput> ReadIfaceUnitAsync(string ifaceMethodName)
    {
        await using var s = _driver.AsyncSession();
        var cur = await s.RunAsync(@"
            MATCH (im:Method {name:$m})<-[:IMPLEMENTS_METHOD]-(impl:Method)
            WHERE impl.fullName STARTS WITH 'Torba.Service' AND impl.sourcePath IS NOT NULL
            OPTIONAL MATCH (impl)-[:CALLS]->(hlp:Method)
            WHERE hlp.sourcePath IS NOT NULL AND hlp.fullName STARTS WITH 'Torba.Service'
            RETURN im.pk AS ipk,
                   impl.sourcePath AS sp, impl.startLine AS sl, impl.endLine AS el,
                   collect(DISTINCT {sp:hlp.sourcePath, sl:hlp.startLine, el:hlp.endLine}) AS helpers",
            new { m = ifaceMethodName });
        var rec = await cur.SingleAsync();
        var slices = new List<SliceRef>
        {
            new(rec["sp"].As<string>(), rec["sl"].As<int>(), rec["el"].As<int>())
        };
        foreach (var h in rec["helpers"].As<List<Dictionary<string, object>>>())
            if (h.TryGetValue("sp", out var sp) && sp is not null)
                slices.Add(new SliceRef(sp.As<string>(), h["sl"].As<int>(), h["el"].As<int>()));
        return new IfaceUnitInput(rec["ipk"].As<string>(), "", slices);   // RootDir는 호출부에서 주입
    }

    public ValueTask DisposeAsync() => _driver.DisposeAsync();
}
```

> `startLine`/`endLine`는 ndjson에 문자열로 저장되므로, 적재 시 Neo4j에는 문자열 prop으로 들어간다. `.As<int>()`가 실패하면 `int.Parse(rec["sl"].As<string>())`로 바꾼다(Task 13에서 실측 후 확정). `RootDir`는 빈 문자열로 두고 Program에서 Vanuatu 루트로 채운다.

- [ ] **Step 2: 빌드 확인**

Run: `dotnet build src/CodeWiki/CodeWiki.csproj -c Release`
Expected: 빌드 성공.

- [ ] **Step 3: 커밋**

```bash
git add src/CodeWiki/Semantic/Neo4jGraphReader.cs
git commit -m "feat(codewiki): Neo4j 그래프 리더(VM dossier / iface unit 입력)"
```

---

### Task 11: 사이드카 적재 (`Neo4jLoader.ApplySemanticAsync`) + `load --semantic` 리플레이

**Files:**
- Modify: `src/CodeWiki/Storage/Neo4jLoader.cs`
- Test: `src/CodeWiki.Tests/SemanticApplyRowTests.cs` (행 빌드 순수 검증)

**Interfaces:**
- Consumes: `SemanticRecord`, Neo4j.Driver.
- Produces: `Neo4jLoader.SemanticRows(IEnumerable<SemanticRecord>) : List<Dictionary<string,object>>` (테스트 가능, null 필드 제외); `Task ApplySemanticAsync(IEnumerable<SemanticRecord>)` — `UNWIND $rows AS row MATCH (n:Node {pk:row.pk}) SET n += row.props`.

- [ ] **Step 1: 실패 테스트 작성**

`src/CodeWiki.Tests/SemanticApplyRowTests.cs`:

```csharp
using System.Collections.Generic;
using CodeWiki.Semantic;
using CodeWiki.Storage;
using Xunit;

namespace CodeWiki.Tests;

public class SemanticApplyRowTests
{
    [Fact]
    public void RowOmitsNullFieldsAndKeepsRequired()
    {
        var rows = Neo4jLoader.SemanticRows(new[]
        {
            new SemanticRecord("pk1", "검색", null, "주의", "HASH", "model"),
        });
        var row = Assert.Single(rows);
        Assert.Equal("pk1", row["pk"]);
        var props = (Dictionary<string, object>)row["props"];
        Assert.Equal("검색", props["summary"]);
        Assert.Equal("주의", props["caveats"]);
        Assert.False(props.ContainsKey("effects"));        // null 제외
        Assert.Equal("HASH", props["summaryHash"]);
        Assert.Equal("model", props["summaryModel"]);
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test --filter SemanticApplyRowTests`
Expected: FAIL — `SemanticRows` 없음.

- [ ] **Step 3: `Neo4jLoader` 수정**

`src/CodeWiki/Storage/Neo4jLoader.cs`에 `using CodeWiki.Semantic;`, `using System.Collections.Generic;`, `using System.Linq;` 추가 후 메서드 추가:

```csharp
    public static List<Dictionary<string, object>> SemanticRows(IEnumerable<SemanticRecord> records)
    {
        var rows = new List<Dictionary<string, object>>();
        foreach (var r in records)
        {
            var props = new Dictionary<string, object>
            {
                ["summary"] = r.Summary,
                ["summaryHash"] = r.SummaryHash,
                ["summaryModel"] = r.SummaryModel,
            };
            if (!string.IsNullOrEmpty(r.Effects)) props["effects"] = r.Effects;
            if (!string.IsNullOrEmpty(r.Caveats)) props["caveats"] = r.Caveats;
            rows.Add(new Dictionary<string, object> { ["pk"] = r.Pk, ["props"] = props });
        }
        return rows;
    }

    public async Task ApplySemanticAsync(IEnumerable<SemanticRecord> records)
    {
        await using var session = _driver.AsyncSession();
        var cursor = await session.RunAsync(
            "UNWIND $rows AS row MATCH (n:Node {pk: row.pk}) SET n += row.props",
            new Dictionary<string, object> { ["rows"] = SemanticRows(records) });
        await cursor.ConsumeAsync();
    }
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test --filter SemanticApplyRowTests`
Expected: PASS.

- [ ] **Step 5: 커밋**

```bash
git add src/CodeWiki/Storage/Neo4jLoader.cs src/CodeWiki.Tests/SemanticApplyRowTests.cs
git commit -m "feat(codewiki): 사이드카 시맨틱 props 적재(ApplySemanticAsync)"
```

---

### Task 12: CLI `enrich` verb + `load --semantic` 배선

**Files:**
- Modify: `src/CodeWiki/Cli/CliOptions.cs`
- Modify: `src/CodeWiki/Program.cs`
- Test: `src/CodeWiki.Tests/CliOptionsTests.cs` (추가)

**Interfaces:**
- Consumes: 모든 위 컴포넌트.
- Produces: `CliOptions`에 `Vm`/`Iface`/`Semantic`/`Model` 추가. verbs: `enrich --vm <name> -c <db:user:pass> --semantic <out> [--model <id>]`, `enrich --iface <methodName> ...`, `load ... [--semantic <path>]`.

- [ ] **Step 1: 실패 테스트 작성**

`src/CodeWiki.Tests/CliOptionsTests.cs`에 추가:

```csharp
    [Fact]
    public void ParsesEnrichVmOptions()
    {
        var o = CliOptions.Parse(new[]
        {
            "enrich", "--vm", "SearchOrderViewModel",
            "-c", "neo4j:neo4j:pw", "--semantic", "out/semantic.ndjson", "--model", "claude-haiku-4-5-20251001"
        });
        Assert.Equal("enrich", o.Verb);
        Assert.Equal("SearchOrderViewModel", o.Vm);
        Assert.Equal("out/semantic.ndjson", o.Semantic);
        Assert.Equal("claude-haiku-4-5-20251001", o.Model);
    }

    [Fact]
    public void ParsesLoadSemantic()
    {
        var o = CliOptions.Parse(new[] { "load", "-c", "a:b:c", "--ndjson", "g.ndjson", "--semantic", "s.ndjson" });
        Assert.Equal("s.ndjson", o.Semantic);
    }
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test --filter CliOptionsTests`
Expected: FAIL — `Vm`/`Semantic`/`Model` 멤버 없음.

- [ ] **Step 3: `CliOptions` 확장**

`src/CodeWiki/Cli/CliOptions.cs` 전체 교체:

```csharp
namespace CodeWiki.Cli;

public sealed record CliOptions(string Verb, string? Solution, string? Output,
    string? Credentials, string? Ndjson, bool Wipe,
    string? Vm, string? Iface, string? Semantic, string? Model)
{
    public static CliOptions Parse(string[] args)
    {
        string verb = args.Length > 0 ? args[0] : "";
        string? sln = null, o = null, c = null, ndjson = null;
        string? vm = null, iface = null, semantic = null, model = null;
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
                case "--vm": if (++i < args.Length) vm = args[i]; break;
                case "--iface": if (++i < args.Length) iface = args[i]; break;
                case "--semantic": if (++i < args.Length) semantic = args[i]; break;
                case "--model": if (++i < args.Length) model = args[i]; break;
            }
        }
        return new CliOptions(verb, sln, o, c, ndjson, wipe, vm, iface, semantic, model);
    }
}
```

- [ ] **Step 4: 기존 CliOptions 생성 호출 점검**

기존 테스트(`CliOptionsTests`)에서 `new CliOptions(...)`를 직접 호출하는 곳이 있으면 새 시그니처에 맞춰 인자 추가. 컴파일 에러로 드러난다.

Run: `dotnet build src/CodeWiki/CodeWiki.csproj -c Release`
Expected: 성공(또는 호출부 수정 후 성공).

- [ ] **Step 5: `Program.cs`에 `enrich` verb + `load --semantic` 배선**

`src/CodeWiki/Program.cs`의 `using` 블록에 추가:

```csharp
using System.Net.Http;
using CodeWiki.Semantic;
using Neo4j.Driver;
```

`case "load":` 블록의 `Console.WriteLine(...)` 직전에 리플레이 추가:

```csharp
        await loader.LoadAsync(graph, o.Wipe);
        if (o.Semantic != null && System.IO.File.Exists(o.Semantic))
        {
            var recs = SemanticSerializer.Read(o.Semantic);
            await loader.ApplySemanticAsync(recs);
            Console.WriteLine($"  + semantic replayed: {recs.Count} records");
        }
        Console.WriteLine($"loaded: {graph.Nodes.Count} nodes, {graph.Edges.Count} edges (wipe={o.Wipe})");
        break;
```

`default:` 앞에 새 case 추가:

```csharp
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
            var hash = SummaryHash.Of(SourceSlicer.WholeFile(System.IO.Path.Combine(vanuatuRoot, input.VmCsPath)));
            var input2 = input with { VmCsPath = System.IO.Path.Combine(vanuatuRoot, input.VmCsPath) };
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
```

- [ ] **Step 6: 테스트 통과 + 회귀**

Run: `dotnet test`
Expected: PASS (전체).

- [ ] **Step 7: 커밋**

```bash
git add src/CodeWiki/Cli/CliOptions.cs src/CodeWiki/Program.cs src/CodeWiki.Tests/CliOptionsTests.cs
git commit -m "feat(codewiki): enrich verb + load --semantic 리플레이 배선"
```

---

## Phase F — M1 게이트: SearchOrder 수직 슬라이스 검증

### Task 13: 엔드투엔드 실행 + 합격 판정 (수동·문서)

**Files:**
- Create: `docs/graphDoc/search-order-semantic-validation.md` (산출·판정 기록)

**Interfaces:**
- Consumes: 위 전체. 전제: Neo4j 기동, `ANTHROPIC_API_KEY` 설정, Vanuatu 솔루션 빌드 가능 환경.

> 외부 의존(실 LLM·실 DB·풀빌드)이라 단위테스트가 아니라 실행 검증이다. PRD §9 합격 기준 4개가 게이트.

- [ ] **Step 1: L0 포함 재추출 (M0 산출)**

Run:
```bash
dotnet run --project src/CodeWiki -c Release -- extract \
  -s "C:/develop/baw/phase2/baw-phase2-platform/Vanuatu/Vanuatu.sln" -o out/graph.ndjson
```
Expected: `extracted: ~21,300 nodes, ~72,500 edges → out/graph.ndjson` (수치는 Phase 1과 동일 수준).

- [ ] **Step 2: L0 props 적재 확인**

Run:
```bash
dotnet run --project src/CodeWiki -c Release -- load -c "neo4j:neo4j:strazhpass" --ndjson out/graph.ndjson --wipe
```
그 다음 Neo4j에서 확인(Browser 또는 cypher-shell):
```cypher
MATCH (m:Method {name:'SearchOrderAsync'}) RETURN m.sourcePath, m.startLine, m.endLine;
MATCH (im:Method {name:'SearchOrdersAsync'}) RETURN im.mutatesState, im.operationType;
```
Expected: `sourcePath`가 `.../SearchOrderViewModel.cs`, 라인 채워짐; `mutatesState='false'`, `operationType='query'`(검색은 읽기 전용).

- [ ] **Step 3: VM enrich 실행**

Run:
```bash
export ANTHROPIC_API_KEY=...   # 절대 커밋 금지
dotnet run --project src/CodeWiki -c Release -- enrich \
  --vm SearchOrderViewModel -c "neo4j:neo4j:strazhpass" --semantic out/semantic.ndjson
```
Expected: `enriched: 7 records → out/semantic.ndjson` (VM 1 + 핸들러 6). `out/semantic.ndjson`에 한국어 summary가 보인다.

- [ ] **Step 4: 인터페이스 메서드 enrich 실행**

Run:
```bash
dotnet run --project src/CodeWiki -c Release -- enrich \
  --iface SearchOrdersAsync -c "neo4j:neo4j:strazhpass" --semantic out/semantic.ndjson
```
Expected: `enriched: 1 records → out/semantic.ndjson` 추가. (기존 7 + 1 = 8 레코드 병합 보존.)

- [ ] **Step 5: 사이드카 리플레이 생존 확인(--wipe 안전성)**

Run:
```bash
dotnet run --project src/CodeWiki -c Release -- load \
  -c "neo4j:neo4j:strazhpass" --ndjson out/graph.ndjson --semantic out/semantic.ndjson --wipe
```
그 다음:
```cypher
MATCH (m:Method {name:'SearchOrderAsync'}) RETURN m.summary;
MATCH (im:Method {name:'SearchOrdersAsync'}) RETURN im.summary, im.mutatesState;
```
Expected: `--wipe` 후에도 summary 복원. 구조(L0)와 시맨틱(사이드카) 모두 존재.

- [ ] **Step 6: 델타-스킵 확인**

Step 3을 한 번 더 실행.
Expected: `enriched: 0 records (skipped if 0)` — VM.cs 미변경이라 해시 동일 → LLM 미호출.

- [ ] **Step 7: 합격 4기준 판정 + 기록**

`docs/graphDoc/search-order-semantic-validation.md`에 표로 기록(PRD §9):
- `summary`가 코드와 사실 일치 (SearchOrderAsync ↔ "필터로 주문 검색")
- 결정론 필드와 모순 0 (`mutatesState=false`인데 summary가 "수정"이라 하지 않는지)
- `caveats` 환각 0
- `effects` 근거 있음

각 항목 통과/실패 + 근거. **4개 모두 통과 시** M2(대량 `--l1` + 전 화면) 후속 계획 착수 승인. Haiku가 특정 필드에서 실패하면 그 필드만 Sonnet 승급 메모.

- [ ] **Step 8: 커밋**

```bash
git add docs/graphDoc/search-order-semantic-validation.md
git commit -m "docs(codewiki): v2 MVP 게이트 — SearchOrder 시맨틱 검증 기록"
```

---

## Self-Review (작성자 점검)

**Spec coverage (PRD 대비):**
- §5.1 입자(핸들러/인터페이스) → Task 8/9 + Task 13 실행으로 핸들러 6 + 인터페이스 1 확인. (헬퍼 skip = enrich가 핸들러/인터페이스만 대상 → 충족.)
- §5.2 필드 계약: LLM 3필드 → Task 7/9 프롬프트가 summary/effects/caveats만. 결정론 keyEntities=USES → 손대지 않음(그대로 그래프). mutatesState/operationType → Task 2. raw SQL "unknown" → `OperationKind` RawSqlMarkers. ✅
- §5.3 저장 위치: 핸들러 summary→Method(Task 8 pk 매핑), uiLabel→Command(기존 CommandExtractor가 이미 추출하는지 확인 필요 — **갭 가능성**, 아래 참고), 서버 의미→인터페이스 노드(Task 9 IfacePk). 
- §6 파이프라인: extract(L0)→enrich(사이드카+upsert)→load(리플레이) → Task 1/2 + 12 + 11. ✅
- §6.2 델타-스킵 hash=hash(입력): VM=hash(VM.cs) Task 8, iface=hash(번들) Task 9. ✅
- §6.2 사이드카 분리 → Task 4 + 11 + 13 Step 5. ✅
- §9 MVP 게이트 → Task 13. ✅

**식별된 갭(실행자 주의):**
1. **`uiLabel`/`eventKind`(XAML 결정론)** 는 본 MVP 계획에 별도 태스크가 없다. 기존 `CommandExtractor`가 이미 무엇을 추출하는지 확인하고, 없으면 M2에서 별도 태스크로 추가한다(MVP 합격 기준엔 불필요 — summary/effects/caveats만 판정).
2. **`ViewModel.viewPath`** 도 MVP 미사용(VM.cs 경로는 핸들러 sourcePath로 도출). M2의 dossier 렌더 단계에서 필요해지면 추가.
3. **startLine/endLine 타입** — ndjson은 문자열 저장. `Neo4jGraphReader`의 `.As<int>()`가 실패하면 `int.Parse(...As<string>())`로 교체(Task 10 주석, Task 13 Step 2에서 실측 확정).

**Placeholder scan:** 모든 코드 스텝에 실제 코드 포함. "적절히 처리" 류 없음. Task 6/10/13은 외부 의존이라 테스트-우선 대신 빌드/실행 검증으로 명시.

**Type consistency:** `SemanticRecord`(Pk,Summary,Effects?,Caveats?,SummaryHash,SummaryModel) — Task 3 정의, Task 4/8/9/11에서 동일 사용. `LlmField`(Key,Summary,Effects?,Caveats?) — Task 5 정의, 6/8/9 사용. `VmDossierInput`/`HandlerRef`/`IfaceUnitInput`/`SliceRef` — Task 8/9 정의, 10/12 사용. `VmPromptBuilder.ViewModelKey` — Task 7 정의, 8 사용. `Neo4jLoader.SemanticRows`/`ApplySemanticAsync` — Task 11, 12 사용. 일관 확인.

---

## Execution Handoff

후속(이 계획 밖): **M2** = `enrich --l1` 일괄(서버 인터페이스 ~505) + 동시성·rate-limit·부분 실패 격리. **M3** = 전 화면(~499) enrich + 통합. 둘 다 본 계획의 컴포넌트 재사용 + 오케스트레이션 루프 추가라 짧다. M2/M3는 Task 13 게이트 통과 후 별도 계획으로.
