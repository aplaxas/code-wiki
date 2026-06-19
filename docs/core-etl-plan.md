# CodeWiki 코어 ETL Phase 1 — 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Vanuatu.sln을 Roslyn으로 분석해 화면→DB E2E가 끊김 없이 연결된 Neo4j 코드 지식 그래프를 NDJSON 경유 단일 경로로 적재한다.

**Architecture:** `추출 → Graph(중립 IR: Node/Edge record 2개) → 적재`. 추출은 실행 스코프별 작은 독립 추출기로 분리, 적재는 `Neo4jLoader` 한 곳에서만 Cypher 생성. Buildalyzer 풀빌드 + AdhocWorkspace로 컴파일 확보.

**Tech Stack:** .NET 10, C#, Roslyn(Microsoft.CodeAnalysis.CSharp.Workspaces), Buildalyzer + Buildalyzer.Workspaces, Neo4j.Driver, xUnit.

상위 설계: [codewiki-spec.md](codewiki-spec.md) · 태스크 개요: [core-etl-design.md](core-etl-design.md) · 질의: [cookbook.md](cookbook.md).

## Global Constraints

- **타깃 net10.0** (ETL 자체 + 테스트). 분석 대상은 Buildalyzer가 풀빌드.
- **노드 pk = FNV-1a 64bit**(`Pk.Of`), 프로세스 불변. `GetHashCode` 금지. 메서드 pk = `fullName|arguments|returnType`.
- **매직스트링 금지** — 라벨/엣지 타입은 `Labels`/`Rel` 상수만 사용.
- **엣지 어휘 정본**: `DECLARED_IN INCLUDED_IN CONTAINS DEPENDS_ON INHERITS IMPLEMENTS DECLARES CALLS INSTANTIATES USES_TYPE IMPLEMENTS_METHOD DEFINES_COMMAND EXECUTES BINDS_TO USES`.
- **역할 라벨**: `Entity ViewModel Controller Service Repository DTO View` (다중 라벨, 애매하면 생략).
- **단일 적재 경로**: 메모리/NDJSON 모두 `Neo4jLoader`의 같은 Cypher를 탄다. 직접 적재 분기 없음.
- **불변식**: 풀빌드(`DesignTime=false`), `addProjectReferences:false`, 미해석 부모 노드 skip, 프로젝트 단위 try/catch.
- **비목표**: `REGISTERS`(DI)·물리 `tableName`·DbContext 파싱·라우트 문자열·표현식 데이터플로우.

---

## 파일 구조 & 계약

```
src/CodeWiki/
  CodeWiki.csproj
  Program.cs                         # CLI: extract / load
  Model/
    Pk.cs            Pk.Of(params string[]) -> string (FNV-1a)
    Node.cs          record Node(string Label,string Pk,string Name,string FullName,
                                 IReadOnlyDictionary<string,string> Props,IReadOnlyList<string> Roles)
    Edge.cs          record Edge(string Type,string FromPk,string ToPk,IReadOnlyDictionary<string,string> Props)
    Graph.cs         AddNode(Node) / AddEdge(Edge) / Nodes / Edges  (dedup + props merge)
    Labels.cs        static const 라벨
    Rel.cs           static const 엣지 타입
  Roslyn/
    Names.cs         Names.Full(ISymbol) -> string (global:: 제거)
    SymbolNodes.cs   ForType(INamedTypeSymbol,RoleClassifier)->Node? / ForMethod(IMethodSymbol)->Node
    FileNodes.cs     ForPath(string abs,string root)->Node
    RoleClassifier.cs  Classify(INamedTypeSymbol)->IReadOnlyList<string>
  Extraction/
    ExtractionContext.cs  Compilation / SolutionRoot / SolutionName / SourceTypes()
    IExtractor.cs         void Extract(ExtractionContext,Graph)
    TypeExtractor.cs      노드+DECLARED_IN+INHERITS+IMPLEMENTS+DECLARES+CALLS+INSTANTIATES+역할
    InterfaceImplementationExtractor.cs   IMPLEMENTS_METHOD
    CommandExtractor.cs   DEFINES_COMMAND+EXECUTES
    TypeUsageExtractor.cs USES_TYPE
    RepositoryUsageExtractor.cs  USES
    ViewModelLinker.cs    Link(Graph)  (BINDS_TO 후처리)
  Pipeline/
    IWorkspaceBuilder.cs  IEnumerable<Compilation> Build(string slnPath)
    WorkspaceBuilder.cs   Buildalyzer 구현(불변식 캡슐화)
    AnalysisPipeline.cs   Graph Run(string slnPath)
  Storage/
    GraphSerializer.cs    Write(Graph,path) / Read(path)->Graph
    CypherBuilder.cs      NodeStatements(Graph)/EdgeStatements(Graph)  (순수, 테스트 대상)
    Neo4jLoader.cs        Task LoadAsync(Graph,bool wipe)
src/CodeWiki.Tests/
  TestCompiler.cs         Compile(string source)->(Compilation,SemanticModel)
  *Tests.cs
```

공용 상수 `Empty`(빈 props): 각 추출기는 `static readonly IReadOnlyDictionary<string,string> Empty = new Dictionary<string,string>();` 와 `static readonly IReadOnlyList<string> NoRoles = Array.Empty<string>();` 사용.

---

## 기반 (T1~T7)

### Task 1: 프로젝트 스캐폴드

**Files:**
- Create: `src/CodeWiki/CodeWiki.csproj`, `src/CodeWiki.Tests/CodeWiki.Tests.csproj`, `CodeWiki.sln`

- [ ] **Step 1: 솔루션·프로젝트 생성**
```bash
dotnet new sln -n CodeWiki
dotnet new console -n CodeWiki -o src/CodeWiki -f net10.0
dotnet new xunit -n CodeWiki.Tests -o src/CodeWiki.Tests -f net10.0
dotnet sln add src/CodeWiki/CodeWiki.csproj src/CodeWiki.Tests/CodeWiki.Tests.csproj
dotnet add src/CodeWiki.Tests/CodeWiki.Tests.csproj reference src/CodeWiki/CodeWiki.csproj
```

- [ ] **Step 2: 패키지 추가**
```bash
dotnet add src/CodeWiki/CodeWiki.csproj package Buildalyzer
dotnet add src/CodeWiki/CodeWiki.csproj package Buildalyzer.Workspaces
dotnet add src/CodeWiki/CodeWiki.csproj package Microsoft.CodeAnalysis.CSharp.Workspaces
dotnet add src/CodeWiki/CodeWiki.csproj package Neo4j.Driver
dotnet add src/CodeWiki.Tests/CodeWiki.Tests.csproj package Microsoft.CodeAnalysis.CSharp
```

- [ ] **Step 3: 빌드·테스트 동작 확인**
Run: `dotnet build && dotnet test`
Expected: 빌드 성공, 테스트 0개 통과(또는 템플릿 1개 통과).

- [ ] **Step 4: Commit**
```bash
git add CodeWiki.sln src/
git commit -m "chore(codewiki): net10 솔루션·프로젝트 스캐폴드 + 패키지"
```

---

### Task 2: `Pk` (FNV-1a 64bit)

**Files:** Create `src/CodeWiki/Model/Pk.cs`, Test `src/CodeWiki.Tests/PkTests.cs`

**Interfaces:** Produces `static string Pk.Of(params string[] parts)`.

- [ ] **Step 1: 실패 테스트**
```csharp
using CodeWiki.Model; using Xunit;
public class PkTests {
    [Fact] public void Deterministic() => Assert.Equal(Pk.Of("a","b"), Pk.Of("a","b"));
    [Fact] public void SeparatorAvoidsCollision() => Assert.NotEqual(Pk.Of("a","b"), Pk.Of("ab"));
    [Fact] public void DistinctInputsDiffer() => Assert.NotEqual(Pk.Of("x"), Pk.Of("y"));
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter PkTests` → FAIL(컴파일 에러: Pk 없음)

- [ ] **Step 3: 구현**
```csharp
using System.Globalization; using System.Text;
namespace CodeWiki.Model;
public static class Pk {
    public static string Of(params string[] parts) {
        const ulong Offset = 14695981039346656037UL, Prime = 1099511628211UL;
        ulong hash = Offset;
        foreach (var b in Encoding.UTF8.GetBytes(string.Join("|", parts))) { hash ^= b; hash *= Prime; }
        return hash.ToString(CultureInfo.InvariantCulture);
    }
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter PkTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "feat(codewiki): FNV-1a 안정 pk"`

---

### Task 3: `Node`/`Edge` record + `Graph`

**Files:** Create `Model/Node.cs`, `Model/Edge.cs`, `Model/Graph.cs`; Test `GraphTests.cs`

**Interfaces:** Produces `Node`, `Edge` records; `Graph` with `AddNode`/`AddEdge`/`Nodes`/`Edges`.

- [ ] **Step 1: 실패 테스트**
```csharp
using System.Collections.Generic; using CodeWiki.Model; using System.Linq; using Xunit;
public class GraphTests {
    static Node N(string pk, IReadOnlyDictionary<string,string> p) =>
        new("Class", pk, "n", "full", p, new[]{"ViewModel"});
    [Fact] public void DedupNodeByPk() {
        var g = new Graph();
        g.AddNode(N("1", new Dictionary<string,string>())); g.AddNode(N("1", new Dictionary<string,string>()));
        Assert.Single(g.Nodes);
    }
    [Fact] public void NonEmptyPropWinsOverEmpty() {
        var g = new Graph();
        g.AddNode(N("1", new Dictionary<string,string>{["k"]=""}));
        g.AddNode(N("1", new Dictionary<string,string>{["k"]="v"}));
        Assert.Equal("v", g.Nodes.Single().Props["k"]);
    }
    [Fact] public void DedupEdgeByFromToType() {
        var g = new Graph();
        var e = new Edge("CALLS","1","2", new Dictionary<string,string>());
        g.AddEdge(e); g.AddEdge(e);
        Assert.Single(g.Edges);
    }
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter GraphTests` → FAIL

- [ ] **Step 3: 구현**
```csharp
// Node.cs
namespace CodeWiki.Model;
public sealed record Node(string Label, string Pk, string Name, string FullName,
    System.Collections.Generic.IReadOnlyDictionary<string,string> Props,
    System.Collections.Generic.IReadOnlyList<string> Roles);
```
```csharp
// Edge.cs
namespace CodeWiki.Model;
public sealed record Edge(string Type, string FromPk, string ToPk,
    System.Collections.Generic.IReadOnlyDictionary<string,string> Props);
```
```csharp
// Graph.cs
using System.Collections.Generic; using System.Linq;
namespace CodeWiki.Model;
public sealed class Graph {
    private readonly Dictionary<string,Node> _nodes = new();
    private readonly Dictionary<string,Edge> _edges = new();
    public IReadOnlyCollection<Node> Nodes => _nodes.Values;
    public IReadOnlyCollection<Edge> Edges => _edges.Values;
    public void AddNode(Node n) => _nodes[n.Pk] = _nodes.TryGetValue(n.Pk, out var e) ? Merge(e, n) : n;
    public void AddEdge(Edge e) { var k = $"{e.FromPk}|{e.ToPk}|{e.Type}"; if (!_edges.ContainsKey(k)) _edges[k] = e; }
    private static Node Merge(Node a, Node b) {
        var props = new Dictionary<string,string>(a.Props);
        foreach (var (k,v) in b.Props) if (!string.IsNullOrEmpty(v)) props[k] = v;
        var roles = a.Roles.Concat(b.Roles).Distinct().ToList();
        return a with {
            Name = string.IsNullOrEmpty(a.Name) ? b.Name : a.Name,
            FullName = string.IsNullOrEmpty(a.FullName) ? b.FullName : a.FullName,
            Props = props, Roles = roles };
    }
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter GraphTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "feat(codewiki): Node/Edge record + Graph 빌더(dedup·props 병합)"`

---

### Task 4: `Labels` / `Rel` 상수

**Files:** Create `Model/Labels.cs`, `Model/Rel.cs`; Test `ConstantsTests.cs`

**Interfaces:** Produces `Labels.*`, `Rel.*` const 문자열.

- [ ] **Step 1: 실패 테스트**
```csharp
using CodeWiki.Model; using Xunit;
public class ConstantsTests {
    [Fact] public void LabelsExist() { Assert.Equal("Class", Labels.Class); Assert.Equal("ViewModel", Labels.ViewModel); Assert.Equal("Method", Labels.Method); }
    [Fact] public void RelsExist() { Assert.Equal("CALLS", Rel.Calls); Assert.Equal("IMPLEMENTS_METHOD", Rel.ImplementsMethod); Assert.Equal("DECLARES", Rel.Declares); }
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter ConstantsTests` → FAIL

- [ ] **Step 3: 구현**
```csharp
// Labels.cs
namespace CodeWiki.Model;
public static class Labels {
    public const string Class="Class", Interface="Interface", Method="Method", Command="Command",
        File="File", Folder="Folder", Solution="Solution", Project="Project", Package="Package",
        Entity="Entity", ViewModel="ViewModel", Controller="Controller", Service="Service",
        Repository="Repository", Dto="DTO", View="View";
}
```
```csharp
// Rel.cs
namespace CodeWiki.Model;
public static class Rel {
    public const string DeclaredIn="DECLARED_IN", IncludedIn="INCLUDED_IN", Contains="CONTAINS", DependsOn="DEPENDS_ON",
        Inherits="INHERITS", Implements="IMPLEMENTS", Declares="DECLARES", Calls="CALLS", Instantiates="INSTANTIATES",
        UsesType="USES_TYPE", ImplementsMethod="IMPLEMENTS_METHOD", DefinesCommand="DEFINES_COMMAND",
        Executes="EXECUTES", BindsTo="BINDS_TO", Uses="USES";
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter ConstantsTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "feat(codewiki): Labels/Rel 상수"`

---

### Task 5: `Names` + `SymbolNodes` + `FileNodes`

**Files:** Create `Roslyn/Names.cs`, `Roslyn/SymbolNodes.cs`, `Roslyn/FileNodes.cs`; Test `SymbolNodesTests.cs` (TestCompiler는 T6에서 만들지만 본 테스트는 인라인 미니 컴파일로 진행)

**Interfaces:** Produces `Names.Full(ISymbol)`, `SymbolNodes.ForType(INamedTypeSymbol, RoleClassifier)` (미해석/error 타입이면 `null`), `SymbolNodes.ForMethod(IMethodSymbol)`, `FileNodes.ForPath(string abs, string root)`. Consumes `RoleClassifier`(T7) — 본 태스크는 빈 분류기로 충분하게 설계(roles 인자 nullable 허용).

- [ ] **Step 1: 실패 테스트**
```csharp
using System.Linq; using CodeWiki.Model; using CodeWiki.Roslyn;
using Microsoft.CodeAnalysis; using Microsoft.CodeAnalysis.CSharp; using Xunit;
public class SymbolNodesTests {
    static Compilation Compile(string src) => CSharpCompilation.Create("t",
        new[]{CSharpSyntaxTree.ParseText(src)},
        new[]{MetadataReference.CreateFromFile(typeof(object).Assembly.Location)},
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    [Fact] public void TypeNodeHasFullNameAndLabel() {
        var c = Compile("namespace N { public class Foo {} }");
        var foo = (INamedTypeSymbol)c.GetSymbolsWithName("Foo").Single();
        var n = SymbolNodes.ForType(foo, null);
        Assert.Equal(Labels.Class, n!.Label);
        Assert.Equal("N.Foo", n.FullName);
    }
    [Fact] public void MethodPkIncludesSignature() {
        var c = Compile("namespace N { public class Foo { public int Bar(string s)=>1; public int Bar()=>2; } }");
        var foo = (INamedTypeSymbol)c.GetSymbolsWithName("Foo").Single();
        var ms = foo.GetMembers("Bar").OfType<IMethodSymbol>().Select(SymbolNodes.ForMethod).ToList();
        Assert.NotEqual(ms[0].Pk, ms[1].Pk);   // 오버로드 구분
    }
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter SymbolNodesTests` → FAIL

- [ ] **Step 3: 구현**
```csharp
// Names.cs
using Microsoft.CodeAnalysis;
namespace CodeWiki.Roslyn;
public static class Names {
    private static readonly SymbolDisplayFormat Fmt = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);
    public static string Full(ISymbol s) => s.ToDisplayString(Fmt);
}
```
```csharp
// SymbolNodes.cs
using System; using System.Collections.Generic; using System.Linq;
using CodeWiki.Model; using Microsoft.CodeAnalysis;
namespace CodeWiki.Roslyn;
public static class SymbolNodes {
    private static readonly IReadOnlyDictionary<string,string> Empty = new Dictionary<string,string>();
    public static Node? ForType(INamedTypeSymbol t, RoleClassifier? roles) {
        if (t.TypeKind != TypeKind.Class && t.TypeKind != TypeKind.Interface) return null;
        var label = t.TypeKind == TypeKind.Interface ? Labels.Interface : Labels.Class;
        var full = Names.Full(t);
        var roleList = roles?.Classify(t) ?? Array.Empty<string>();
        var props = t.DeclaredAccessibility != Accessibility.NotApplicable
            ? new Dictionary<string,string>{["modifiers"]=t.DeclaredAccessibility.ToString().ToLowerInvariant()} : Empty;
        return new Node(label, Pk.Of(full), t.Name, full, props, roleList);
    }
    public static Node ForMethod(IMethodSymbol m) {
        var full = Names.Full(m);
        var args = string.Join(", ", m.Parameters.Select(p => Names.Full(p.Type)));
        var ret = Names.Full(m.ReturnType);
        return new Node(Labels.Method, Pk.Of(full, args, ret), m.Name, full,
            new Dictionary<string,string>{["arguments"]=args, ["returnType"]=ret}, Array.Empty<string>());
    }
}
```
```csharp
// FileNodes.cs
using System; using System.Collections.Generic; using CodeWiki.Model;
namespace CodeWiki.Roslyn;
public static class FileNodes {
    public static Node ForPath(string abs, string root) {
        var rel = System.IO.Path.GetRelativePath(root, abs).Replace('\\','/');
        return new Node(Labels.File, Pk.Of(rel), System.IO.Path.GetFileName(rel), rel,
            new Dictionary<string,string>(), Array.Empty<string>());
    }
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter SymbolNodesTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "feat(codewiki): Names/SymbolNodes/FileNodes 심볼→노드 팩토리"`

---

### Task 6: `TestCompiler` 이식

**Files:** Create `src/CodeWiki.Tests/TestCompiler.cs`; Test `TestCompilerTests.cs`

**Interfaces:** Produces `(Compilation, SemanticModel) TestCompiler.Compile(string source)` — 참조 어셈블리 자동 포함(이후 모든 추출기 테스트가 사용).

- [ ] **Step 1: 실패 테스트**
```csharp
using System.Linq; using Microsoft.CodeAnalysis; using Xunit;
public class TestCompilerTests {
    [Fact] public void CompilesAndResolvesSymbol() {
        var (c, _) = TestCompiler.Compile("namespace N { public class Foo {} }");
        Assert.NotEmpty(c.GetSymbolsWithName("Foo"));
    }
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter TestCompilerTests` → FAIL

- [ ] **Step 3: 구현**
```csharp
using System; using System.Linq; using Microsoft.CodeAnalysis; using Microsoft.CodeAnalysis.CSharp;
public static class TestCompiler {
    public static (Compilation, SemanticModel) Compile(string source) {
        var tree = CSharpSyntaxTree.ParseText(source);
        var refs = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location));
        var c = CSharpCompilation.Create("Test", new[]{tree}, refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return (c, c.GetSemanticModel(tree));
    }
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter TestCompilerTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "test(codewiki): TestCompiler 소스문자열→Compilation 헬퍼"`

---

### Task 7: `RoleClassifier`

**Files:** Create `Roslyn/RoleClassifier.cs`; Test `RoleClassifierTests.cs`

**Interfaces:** Produces `IReadOnlyList<string> RoleClassifier.Classify(INamedTypeSymbol)`.

- [ ] **Step 1: 실패 테스트**
```csharp
using System.Linq; using CodeWiki.Model; using CodeWiki.Roslyn; using Microsoft.CodeAnalysis; using Xunit;
public class RoleClassifierTests {
    static INamedTypeSymbol T(string src, string name) {
        var (c,_) = TestCompiler.Compile(src);
        return (INamedTypeSymbol)c.GetSymbolsWithName(name).Single();
    }
    [Fact] public void ViewModelByName() {
        var t = T("public class FooViewModel {}", "FooViewModel");
        Assert.Contains(Labels.ViewModel, new RoleClassifier().Classify(t));
    }
    [Fact] public void ViewByName_NotViewModel() {
        var t = T("public class FooView {}", "FooView");
        var roles = new RoleClassifier().Classify(t);
        Assert.Contains(Labels.View, roles); Assert.DoesNotContain(Labels.ViewModel, roles);
    }
    [Fact] public void PlainClassNoRole() =>
        Assert.Empty(new RoleClassifier().Classify(T("public class Foo {}", "Foo")));
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter RoleClassifierTests` → FAIL

- [ ] **Step 3: 구현**
```csharp
using System.Collections.Generic; using System.Linq; using CodeWiki.Model; using Microsoft.CodeAnalysis;
namespace CodeWiki.Roslyn;
public sealed class RoleClassifier {
    public IReadOnlyList<string> Classify(INamedTypeSymbol t) {
        var roles = new List<string>();
        var name = t.Name;
        var ns = t.ContainingNamespace?.ToDisplayString() ?? "";
        bool Inherits(string baseName) { for (var b=t.BaseType; b!=null; b=b.BaseType) if (b.Name==baseName) return true; return false; }
        bool ImplementsName(string ifaceName) => t.AllInterfaces.Any(i => i.Name == ifaceName);

        if (ImplementsName("IBaseEntity")) roles.Add(Labels.Entity);
        if (name.EndsWith("ViewModel") || Inherits("BindableBase")) roles.Add(Labels.ViewModel);
        if (name.EndsWith("Controller") || Inherits("ControllerBase")) roles.Add(Labels.Controller);
        if (t.AllInterfaces.Any(i => i.Name.StartsWith("I") && i.Name.EndsWith("Service")
            && (i.ContainingNamespace?.ToDisplayString() ?? "").StartsWith("Vanuatu.Service"))) roles.Add(Labels.Service);
        if (name.Contains("Repository")) roles.Add(Labels.Repository);
        if (ns.Contains(".DTO") || name.EndsWith("DTO")) roles.Add(Labels.Dto);
        if (name.EndsWith("View") && !name.EndsWith("ViewModel")) roles.Add(Labels.View);
        return roles;
    }
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter RoleClassifierTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "feat(codewiki): RoleClassifier 역할 라벨 휴리스틱"`

---

## 추출기 (T8~T14)

공통: `ExtractionContext`를 먼저 만든다(아래 T8 Step 0). 모든 추출기 테스트는 `TestCompiler.Compile` → `new ExtractionContext(c, "/", "Test")` → `extractor.Extract(ctx, graph)` → graph 단언.

### Task 8: `ExtractionContext` + `IExtractor` + `TypeExtractor`(타입/상속/구현)

**Files:** Create `Extraction/ExtractionContext.cs`, `Extraction/IExtractor.cs`, `Extraction/TypeExtractor.cs`; Test `TypeExtractorTests.cs`

**Interfaces:**
- Produces `ExtractionContext(Compilation, string solutionRoot, string solutionName)` with `IEnumerable<INamedTypeSymbol> SourceTypes()`.
- Produces `interface IExtractor { void Extract(ExtractionContext ctx, Graph graph); }`.
- Produces `TypeExtractor(RoleClassifier)` — 본 태스크 범위: 노드 + `DECLARED_IN` + `INHERITS` + `IMPLEMENTS` + 역할 라벨.

- [ ] **Step 1: 실패 테스트**
```csharp
using System.Linq; using CodeWiki.Extraction; using CodeWiki.Model; using CodeWiki.Roslyn; using Xunit;
public class TypeExtractorTests {
    static Graph Run(string src) {
        var (c,_) = TestCompiler.Compile(src);
        var g = new Graph();
        new TypeExtractor(new RoleClassifier()).Extract(new ExtractionContext(c,"/","T"), g);
        return g;
    }
    [Fact] public void EmitsClassAndInterfaceImplementsEdge() {
        var g = Run("namespace N { public interface IFoo {} public class Foo : IFoo {} }");
        var foo = g.Nodes.Single(n => n.Name=="Foo");
        var ifoo = g.Nodes.Single(n => n.Name=="IFoo");
        Assert.Equal(Labels.Class, foo.Label); Assert.Equal(Labels.Interface, ifoo.Label);
        Assert.Contains(g.Edges, e => e.Type==Rel.Implements && e.FromPk==foo.Pk && e.ToPk==ifoo.Pk);
    }
    [Fact] public void InheritsEdge() {
        var g = Run("namespace N { public class B {} public class D : B {} }");
        var d = g.Nodes.Single(n=>n.Name=="D"); var b = g.Nodes.Single(n=>n.Name=="B");
        Assert.Contains(g.Edges, e => e.Type==Rel.Inherits && e.FromPk==d.Pk && e.ToPk==b.Pk);
    }
    [Fact] public void UnresolvedBaseSkipped() {  // 불변식 #3
        var g = Run("namespace N { public class Vm : BindableBase {} }"); // BindableBase 미참조
        Assert.DoesNotContain(g.Edges, e => e.Type==Rel.Inherits);
    }
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter TypeExtractorTests` → FAIL

- [ ] **Step 3: 구현**
```csharp
// ExtractionContext.cs
using System.Collections.Generic; using Microsoft.CodeAnalysis;
namespace CodeWiki.Extraction;
public sealed class ExtractionContext {
    public Compilation Compilation { get; }
    public string SolutionRoot { get; }
    public string SolutionName { get; }
    public ExtractionContext(Compilation c, string solutionRoot, string solutionName)
        { Compilation = c; SolutionRoot = solutionRoot; SolutionName = solutionName; }
    public IEnumerable<INamedTypeSymbol> SourceTypes() {
        var stack = new Stack<INamespaceOrTypeSymbol>();
        stack.Push(Compilation.Assembly.GlobalNamespace);
        while (stack.Count > 0) {
            foreach (var m in stack.Pop().GetMembers()) {
                if (m is INamespaceSymbol ns) stack.Push(ns);
                else if (m is INamedTypeSymbol t) { yield return t; foreach (var nt in t.GetTypeMembers()) stack.Push(nt); }
            }
        }
    }
}
```
```csharp
// IExtractor.cs
using CodeWiki.Model;
namespace CodeWiki.Extraction;
public interface IExtractor { void Extract(ExtractionContext ctx, Graph graph); }
```
```csharp
// TypeExtractor.cs
using System.Collections.Generic; using System.Linq; using CodeWiki.Model; using CodeWiki.Roslyn; using Microsoft.CodeAnalysis;
namespace CodeWiki.Extraction;
public sealed class TypeExtractor : IExtractor {
    private static readonly IReadOnlyDictionary<string,string> Empty = new Dictionary<string,string>();
    private readonly RoleClassifier _roles;
    public TypeExtractor(RoleClassifier roles) => _roles = roles;
    public void Extract(ExtractionContext ctx, Graph graph) {
        foreach (var t in ctx.SourceTypes()) {
            var node = SymbolNodes.ForType(t, _roles);
            if (node == null) continue;
            graph.AddNode(node);
            foreach (var loc in t.Locations.Where(l => l.IsInSource)) {
                var path = loc.SourceTree?.FilePath;
                if (string.IsNullOrEmpty(path)) continue;
                var file = FileNodes.ForPath(path, ctx.SolutionRoot);
                graph.AddNode(file);
                graph.AddEdge(new Edge(Rel.DeclaredIn, node.Pk, file.Pk, Empty));
            }
            if (t.BaseType is { TypeKind: TypeKind.Class, SpecialType: SpecialType.None } bt) {
                var bn = SymbolNodes.ForType(bt, _roles);
                if (bn != null) { graph.AddNode(bn); graph.AddEdge(new Edge(Rel.Inherits, node.Pk, bn.Pk, Empty)); }
            }
            foreach (var iface in t.Interfaces) {
                var inode = SymbolNodes.ForType(iface, _roles);
                if (inode == null) continue;
                graph.AddNode(inode);
                graph.AddEdge(new Edge(Rel.Implements, node.Pk, inode.Pk, Empty));
            }
        }
    }
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter TypeExtractorTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "feat(codewiki): TypeExtractor 노드+DECLARED_IN+INHERITS+IMPLEMENTS"`

---

### Task 9: `TypeExtractor` 확장 — `DECLARES`/`CALLS`/`INSTANTIATES`

**Files:** Modify `Extraction/TypeExtractor.cs`; Test add to `TypeExtractorTests.cs`

**Interfaces:** 같은 `TypeExtractor`가 메서드 노드 + `DECLARES`(타입→메서드) + `CALLS`(메서드→메서드, 인터페이스 직행 포함) + `INSTANTIATES`(메서드→생성타입)도 산출.

- [ ] **Step 1: 실패 테스트(추가)**
```csharp
[Fact] public void DeclaresAndCallsInterfaceDirect() {
    var g = Run(@"namespace N {
        public interface ISvc { void Do(); }
        public class Vm { private ISvc _s; public void Handler() { _s.Do(); } }
    }");
    var handler = g.Nodes.Single(n => n.Name=="Handler");
    var vm = g.Nodes.Single(n => n.Name=="Vm");
    var doMethod = g.Nodes.Single(n => n.Name=="Do");
    Assert.Contains(g.Edges, e => e.Type==Rel.Declares && e.FromPk==vm.Pk && e.ToPk==handler.Pk);
    Assert.Contains(g.Edges, e => e.Type==Rel.Calls && e.FromPk==handler.Pk && e.ToPk==doMethod.Pk); // 인터페이스 메서드로 직행
}
[Fact] public void Instantiates() {
    var g = Run("namespace N { public class A {} public class B { public void M(){ var a = new A(); } } }");
    var m = g.Nodes.Single(n=>n.Name=="M"); var a = g.Nodes.Single(n=>n.Name=="A");
    Assert.Contains(g.Edges, e => e.Type==Rel.Instantiates && e.FromPk==m.Pk && e.ToPk==a.Pk);
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter TypeExtractorTests` → FAIL(새 2건)

- [ ] **Step 3: 구현(Extract 루프 내, 노드 추가 직후 삽입)**
```csharp
foreach (var m in t.GetMembers().OfType<IMethodSymbol>()) {
    if (m.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet
        or MethodKind.EventAdd or MethodKind.EventRemove) continue;
    var mNode = SymbolNodes.ForMethod(m);
    graph.AddNode(mNode);
    graph.AddEdge(new Edge(Rel.Declares, node.Pk, mNode.Pk, Empty));
    foreach (var sr in m.DeclaringSyntaxReferences) {
        var syntax = sr.GetSyntax();
        var model = ctx.Compilation.GetSemanticModel(syntax.SyntaxTree);
        foreach (var inv in syntax.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>())
            if (model.GetSymbolInfo(inv).Symbol is IMethodSymbol callee) {
                var cn = SymbolNodes.ForMethod(callee.OriginalDefinition);
                graph.AddNode(cn);
                graph.AddEdge(new Edge(Rel.Calls, mNode.Pk, cn.Pk, Empty));
            }
        foreach (var oc in syntax.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ObjectCreationExpressionSyntax>())
            if (model.GetSymbolInfo(oc).Symbol is IMethodSymbol ctor && ctor.ContainingType is INamedTypeSymbol created) {
                var crn = SymbolNodes.ForType(created, _roles);
                if (crn != null) { graph.AddNode(crn); graph.AddEdge(new Edge(Rel.Instantiates, mNode.Pk, crn.Pk, Empty)); }
            }
    }
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter TypeExtractorTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "feat(codewiki): TypeExtractor DECLARES/CALLS/INSTANTIATES"`

---

### Task 10: `InterfaceImplementationExtractor` (`IMPLEMENTS_METHOD`)

**Files:** Create `Extraction/InterfaceImplementationExtractor.cs`; Test `InterfaceImplementationExtractorTests.cs`

**Interfaces:** Produces `IMPLEMENTS_METHOD`(구현 메서드 → 인터페이스 멤버). 경계 봉합 허브.

- [ ] **Step 1: 실패 테스트**
```csharp
using System.Linq; using CodeWiki.Extraction; using CodeWiki.Model; using CodeWiki.Roslyn; using Xunit;
public class InterfaceImplementationExtractorTests {
    [Fact] public void ImplMethodPointsToInterfaceMember() {
        var (c,_) = TestCompiler.Compile(
            "namespace N { public interface ISvc { void Do(); } public class Svc : ISvc { public void Do(){} } }");
        var g = new Graph();
        new InterfaceImplementationExtractor().Extract(new ExtractionContext(c,"/","T"), g);
        var impl = g.Nodes.Single(n => n.FullName=="N.Svc.Do");
        var iface = g.Nodes.Single(n => n.FullName=="N.ISvc.Do");
        Assert.Contains(g.Edges, e => e.Type==Rel.ImplementsMethod && e.FromPk==impl.Pk && e.ToPk==iface.Pk);
    }
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter InterfaceImplementationExtractorTests` → FAIL

- [ ] **Step 3: 구현**
```csharp
using System.Linq; using CodeWiki.Model; using CodeWiki.Roslyn; using Microsoft.CodeAnalysis;
namespace CodeWiki.Extraction;
public sealed class InterfaceImplementationExtractor : IExtractor {
    private static readonly System.Collections.Generic.IReadOnlyDictionary<string,string> Empty = new System.Collections.Generic.Dictionary<string,string>();
    public void Extract(ExtractionContext ctx, Graph graph) {
        foreach (var t in ctx.SourceTypes()) {
            if (t.TypeKind != TypeKind.Class) continue;
            foreach (var iface in t.AllInterfaces)
            foreach (var member in iface.GetMembers().OfType<IMethodSymbol>()) {
                if (t.FindImplementationForInterfaceMember(member) is not IMethodSymbol impl) continue;
                if (!SymbolEqualityComparer.Default.Equals(impl.ContainingType, t)) continue;
                var implNode = SymbolNodes.ForMethod(impl);
                var ifaceNode = SymbolNodes.ForMethod(member);
                graph.AddNode(implNode); graph.AddNode(ifaceNode);
                graph.AddEdge(new Edge(Rel.ImplementsMethod, implNode.Pk, ifaceNode.Pk, Empty));
            }
        }
    }
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter InterfaceImplementationExtractorTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "feat(codewiki): InterfaceImplementationExtractor(IMPLEMENTS_METHOD 허브)"`

---

### Task 11: `CommandExtractor` (`DEFINES_COMMAND`/`EXECUTES`)

**Files:** Create `Extraction/CommandExtractor.cs`; Test `CommandExtractorTests.cs`

**Interfaces:** Produces `Command` 노드 + `DEFINES_COMMAND`(VM→Command) + `EXECUTES`(Command→핸들러). Command pk = `Pk.Of(ownerFullName, commandName)`. Prism `new DelegateCommand(Handler)` / `DelegateCommand<T>(Handler)` 인식, `.ObservesCanExecute(...)` 체인 무시.

- [ ] **Step 1: 실패 테스트**
```csharp
using System.Linq; using CodeWiki.Extraction; using CodeWiki.Model; using CodeWiki.Roslyn; using Xunit;
public class CommandExtractorTests {
    const string Src = @"namespace N {
        public class DelegateCommand { public DelegateCommand(System.Action e){} public DelegateCommand ObservesCanExecute(System.Func<bool> f)=>this; }
        public class Vm { public DelegateCommand SearchCommand { get; }
            public Vm(){ SearchCommand = new DelegateCommand(Search).ObservesCanExecute(()=>true); }
            public void Search(){} } }";
    static Graph Run() { var (c,_) = TestCompiler.Compile(Src); var g = new Graph();
        new CommandExtractor(new RoleClassifier()).Extract(new ExtractionContext(c,"/","T"), g); return g; }
    [Fact] public void DefinesAndExecutes() {
        var g = Run();
        var vm = g.Nodes.Single(n => n.Name=="Vm");
        var cmd = g.Nodes.Single(n => n.Label==Labels.Command && n.Name=="SearchCommand");
        var handler = g.Nodes.Single(n => n.Name=="Search");
        Assert.Contains(g.Edges, e => e.Type==Rel.DefinesCommand && e.FromPk==vm.Pk && e.ToPk==cmd.Pk);
        Assert.Contains(g.Edges, e => e.Type==Rel.Executes && e.FromPk==cmd.Pk && e.ToPk==handler.Pk);
    }
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter CommandExtractorTests` → FAIL

- [ ] **Step 3: 구현**
```csharp
using System.Collections.Generic; using System.Linq; using CodeWiki.Model; using CodeWiki.Roslyn;
using Microsoft.CodeAnalysis; using Microsoft.CodeAnalysis.CSharp.Syntax;
namespace CodeWiki.Extraction;
public sealed class CommandExtractor : IExtractor {
    private static readonly IReadOnlyDictionary<string,string> Empty = new Dictionary<string,string>();
    private readonly RoleClassifier _roles;
    public CommandExtractor(RoleClassifier roles) => _roles = roles;
    public void Extract(ExtractionContext ctx, Graph graph) {
        foreach (var t in ctx.SourceTypes()) {
            var owner = SymbolNodes.ForType(t, _roles);
            if (owner == null) continue;
            foreach (var sr in t.DeclaringSyntaxReferences) {
                var syntax = sr.GetSyntax();
                var model = ctx.Compilation.GetSemanticModel(syntax.SyntaxTree);
                foreach (var oc in syntax.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()) {
                    var typeName = (oc.Type as GenericNameSyntax)?.Identifier.Text
                                ?? (oc.Type as IdentifierNameSyntax)?.Identifier.Text;
                    if (typeName is null || !typeName.StartsWith("DelegateCommand")) continue;
                    var cmdName = AssignedName(oc);
                    if (cmdName is null) continue;
                    var ownerFull = owner.FullName;
                    var cmd = new Node(Labels.Command, Pk.Of(ownerFull, cmdName), cmdName,
                        ownerFull + "." + cmdName, Empty, System.Array.Empty<string>());
                    graph.AddNode(owner); graph.AddNode(cmd);
                    graph.AddEdge(new Edge(Rel.DefinesCommand, owner.Pk, cmd.Pk, Empty));
                    var arg = oc.ArgumentList?.Arguments.FirstOrDefault();
                    if (arg != null && model.GetSymbolInfo(arg.Expression).Symbol is IMethodSymbol handler) {
                        var hn = SymbolNodes.ForMethod(handler);
                        graph.AddNode(hn);
                        graph.AddEdge(new Edge(Rel.Executes, cmd.Pk, hn.Pk, Empty));
                    }
                }
            }
        }
    }
    private static string? AssignedName(ObjectCreationExpressionSyntax oc) {
        // 체인(.ObservesCanExecute) 위로 올라가며 대입/초기화 LHS 찾기
        SyntaxNode? node = oc;
        while (node is not null && node is not AssignmentExpressionSyntax && node is not VariableDeclaratorSyntax
               && node is not PropertyDeclarationSyntax) node = node.Parent;
        return node switch {
            AssignmentExpressionSyntax a => (a.Left as IdentifierNameSyntax)?.Identifier.Text
                                          ?? (a.Left as MemberAccessExpressionSyntax)?.Name.Identifier.Text,
            VariableDeclaratorSyntax v => v.Identifier.Text,
            PropertyDeclarationSyntax p => p.Identifier.Text,
            _ => null };
    }
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter CommandExtractorTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "feat(codewiki): CommandExtractor(DEFINES_COMMAND/EXECUTES)"`

---

### Task 12: `TypeUsageExtractor` (`USES_TYPE`)

**Files:** Create `Extraction/TypeUsageExtractor.cs`; Test `TypeUsageExtractorTests.cs`

**Interfaces:** Produces `USES_TYPE`(메서드→파라미터/반환 도메인 타입). 프레임워크 타입(`System.*`/`Microsoft.*`)·특수 타입 제외(상위집합 잡음 억제).

- [ ] **Step 1: 실패 테스트**
```csharp
using System.Linq; using CodeWiki.Extraction; using CodeWiki.Model; using CodeWiki.Roslyn; using Xunit;
public class TypeUsageExtractorTests {
    [Fact] public void MethodUsesParameterAndReturnType() {
        var (c,_) = TestCompiler.Compile(@"namespace N {
            public class Filter {} public class Result {}
            public class Svc { public Result Search(Filter f)=>new Result(); } }");
        var g = new Graph();
        new TypeUsageExtractor(new RoleClassifier()).Extract(new ExtractionContext(c,"/","T"), g);
        var m = g.Nodes.Single(n => n.Name=="Search");
        var filter = g.Nodes.Single(n => n.Name=="Filter"); var result = g.Nodes.Single(n => n.Name=="Result");
        Assert.Contains(g.Edges, e => e.Type==Rel.UsesType && e.FromPk==m.Pk && e.ToPk==filter.Pk);
        Assert.Contains(g.Edges, e => e.Type==Rel.UsesType && e.FromPk==m.Pk && e.ToPk==result.Pk);
    }
    [Fact] public void SkipsFrameworkTypes() {
        var (c,_) = TestCompiler.Compile("namespace N { public class Svc { public string M(int x)=>\"\"; } }");
        var g = new Graph();
        new TypeUsageExtractor(new RoleClassifier()).Extract(new ExtractionContext(c,"/","T"), g);
        Assert.DoesNotContain(g.Edges, e => e.Type==Rel.UsesType);
    }
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter TypeUsageExtractorTests` → FAIL

- [ ] **Step 3: 구현**
```csharp
using System.Collections.Generic; using System.Linq; using CodeWiki.Model; using CodeWiki.Roslyn; using Microsoft.CodeAnalysis;
namespace CodeWiki.Extraction;
public sealed class TypeUsageExtractor : IExtractor {
    private static readonly IReadOnlyDictionary<string,string> Empty = new Dictionary<string,string>();
    private readonly RoleClassifier _roles;
    public TypeUsageExtractor(RoleClassifier roles) => _roles = roles;
    private static bool IsDomain(ITypeSymbol? s) =>
        s is INamedTypeSymbol n && n.SpecialType == SpecialType.None && n.TypeKind != TypeKind.Error
        && !(n.ContainingNamespace?.ToDisplayString() ?? "").StartsWith("System")
        && !(n.ContainingNamespace?.ToDisplayString() ?? "").StartsWith("Microsoft");
    public void Extract(ExtractionContext ctx, Graph graph) {
        foreach (var t in ctx.SourceTypes())
        foreach (var m in t.GetMembers().OfType<IMethodSymbol>()) {
            if (m.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet) continue;
            var mNode = SymbolNodes.ForMethod(m);
            foreach (var used in m.Parameters.Select(p => p.Type).Append(m.ReturnType).Distinct(SymbolEqualityComparer.Default)) {
                if (used is not INamedTypeSymbol u || !IsDomain(u)) continue;
                var un = SymbolNodes.ForType(u, _roles);
                if (un == null) continue;
                graph.AddNode(mNode); graph.AddNode(un);
                graph.AddEdge(new Edge(Rel.UsesType, mNode.Pk, un.Pk, Empty));
            }
        }
    }
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter TypeUsageExtractorTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "feat(codewiki): TypeUsageExtractor(USES_TYPE, 도메인 타입만)"`

---

### Task 13: `RepositoryUsageExtractor` (`USES`)

**Files:** Create `Extraction/RepositoryUsageExtractor.cs`; Test `RepositoryUsageExtractorTests.cs`

**Interfaces:** Produces `USES`(메서드→Entity). 메서드 본문에서 참조하는 `IRepository<T>`/`Repository<T>` 필드의 제네릭 인자 `T`를 Entity로. (물리 테이블명/DbContext는 비목표.)

- [ ] **Step 1: 실패 테스트**
```csharp
using System.Linq; using CodeWiki.Extraction; using CodeWiki.Model; using CodeWiki.Roslyn; using Xunit;
public class RepositoryUsageExtractorTests {
    [Fact] public void MethodUsesEntityViaRepositoryField() {
        var (c,_) = TestCompiler.Compile(@"namespace N {
            public interface IRepository<T> {}
            public class Order {}
            public class Svc { private IRepository<Order> _repo;
                public void Do(){ var x = _repo; } } }");
        var g = new Graph();
        new RepositoryUsageExtractor(new RoleClassifier()).Extract(new ExtractionContext(c,"/","T"), g);
        var m = g.Nodes.Single(n => n.Name=="Do"); var order = g.Nodes.Single(n => n.Name=="Order");
        Assert.Contains(g.Edges, e => e.Type==Rel.Uses && e.FromPk==m.Pk && e.ToPk==order.Pk);
    }
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter RepositoryUsageExtractorTests` → FAIL

- [ ] **Step 3: 구현**
```csharp
using System.Collections.Generic; using System.Linq; using CodeWiki.Model; using CodeWiki.Roslyn;
using Microsoft.CodeAnalysis; using Microsoft.CodeAnalysis.CSharp.Syntax;
namespace CodeWiki.Extraction;
public sealed class RepositoryUsageExtractor : IExtractor {
    private static readonly IReadOnlyDictionary<string,string> Empty = new Dictionary<string,string>();
    private readonly RoleClassifier _roles;
    public RepositoryUsageExtractor(RoleClassifier roles) => _roles = roles;
    public void Extract(ExtractionContext ctx, Graph graph) {
        foreach (var t in ctx.SourceTypes())
        foreach (var m in t.GetMembers().OfType<IMethodSymbol>()) {
            var mNode = SymbolNodes.ForMethod(m);
            foreach (var sr in m.DeclaringSyntaxReferences) {
                var syntax = sr.GetSyntax();
                var model = ctx.Compilation.GetSemanticModel(syntax.SyntaxTree);
                foreach (var id in syntax.DescendantNodes().OfType<IdentifierNameSyntax>()) {
                    if (model.GetSymbolInfo(id).Symbol is not IFieldSymbol f) continue;
                    if (f.Type is not INamedTypeSymbol ft || !ft.IsGenericType || !ft.Name.Contains("Repository")) continue;
                    if (ft.TypeArguments.FirstOrDefault() is not INamedTypeSymbol entity) continue;
                    var en = SymbolNodes.ForType(entity, _roles);
                    if (en == null) continue;
                    graph.AddNode(mNode); graph.AddNode(en);
                    graph.AddEdge(new Edge(Rel.Uses, mNode.Pk, en.Pk, Empty));
                }
            }
        }
    }
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter RepositoryUsageExtractorTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "feat(codewiki): RepositoryUsageExtractor(USES→Entity)"`

---

### Task 14: `ViewModelLinker` (`BINDS_TO` 후처리)

**Files:** Create `Extraction/ViewModelLinker.cs`; Test `ViewModelLinkerTests.cs`

**Interfaces:** Produces `void Link(Graph graph)` — 그래프 내 `:View` 노드(`XView`)를 `:ViewModel` 노드(`XViewModel`)에 `BINDS_TO`로 연결(네이밍: View 이름 + "Model").

- [ ] **Step 1: 실패 테스트**
```csharp
using System; using System.Collections.Generic; using System.Linq; using CodeWiki.Extraction; using CodeWiki.Model; using Xunit;
public class ViewModelLinkerTests {
    static Node N(string name, string role) => new(Labels.Class, name, name, "N."+name,
        new Dictionary<string,string>(), new[]{role});
    [Fact] public void LinksViewToViewModelByName() {
        var g = new Graph(); g.AddNode(N("SearchOrderView", Labels.View)); g.AddNode(N("SearchOrderViewModel", Labels.ViewModel));
        new ViewModelLinker().Link(g);
        var v = g.Nodes.Single(n=>n.Name=="SearchOrderView"); var vm = g.Nodes.Single(n=>n.Name=="SearchOrderViewModel");
        Assert.Contains(g.Edges, e => e.Type==Rel.BindsTo && e.FromPk==v.Pk && e.ToPk==vm.Pk);
    }
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter ViewModelLinkerTests` → FAIL

- [ ] **Step 3: 구현**
```csharp
using System.Collections.Generic; using System.Linq; using CodeWiki.Model;
namespace CodeWiki.Extraction;
public sealed class ViewModelLinker {
    private static readonly IReadOnlyDictionary<string,string> Empty = new Dictionary<string,string>();
    public void Link(Graph graph) {
        var vms = graph.Nodes.Where(n => n.Roles.Contains(Labels.ViewModel))
            .GroupBy(n => n.Name).ToDictionary(grp => grp.Key, grp => grp.First());
        foreach (var v in graph.Nodes.Where(n => n.Roles.Contains(Labels.View)).ToList())
            if (vms.TryGetValue(v.Name + "Model", out var vm))
                graph.AddEdge(new Edge(Rel.BindsTo, v.Pk, vm.Pk, Empty));
    }
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter ViewModelLinkerTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "feat(codewiki): ViewModelLinker(BINDS_TO 네이밍 후처리)"`

---

### Task 15: `StructureExtractor` (Solution/Project/File + 구조 엣지)

**Files:** Create `Extraction/StructureExtractor.cs`; Test `StructureExtractorTests.cs`

**Interfaces:** Produces Solution/Project/File 노드 + `CONTAINS`(Solution→Project) + `INCLUDED_IN`(File→Project) + `DEPENDS_ON`(Project→Package). Package 노드는 `compilation.ReferencedAssemblyNames`에서.

- [ ] **Step 1: 실패 테스트**
```csharp
using System.Linq; using CodeWiki.Extraction; using CodeWiki.Model; using Xunit;
public class StructureExtractorTests {
    [Fact] public void EmitsSolutionProjectContains() {
        var (c,_) = TestCompiler.Compile("namespace N { public class Foo {} }"); // AssemblyName="Test"
        var g = new Graph();
        new StructureExtractor().Extract(new ExtractionContext(c,"/","MySln"), g);
        var sln = g.Nodes.Single(n => n.Label==Labels.Solution);
        var proj = g.Nodes.Single(n => n.Label==Labels.Project);
        Assert.Equal("MySln", sln.Name);
        Assert.Contains(g.Edges, e => e.Type==Rel.Contains && e.FromPk==sln.Pk && e.ToPk==proj.Pk);
    }
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter StructureExtractorTests` → FAIL

- [ ] **Step 3: 구현**
```csharp
using System; using System.Collections.Generic; using System.Linq; using CodeWiki.Model; using CodeWiki.Roslyn;
namespace CodeWiki.Extraction;
public sealed class StructureExtractor : IExtractor {
    private static readonly IReadOnlyDictionary<string,string> Empty = new Dictionary<string,string>();
    private static readonly IReadOnlyList<string> NoRoles = Array.Empty<string>();
    public void Extract(ExtractionContext ctx, Graph graph) {
        var sln = new Node(Labels.Solution, Pk.Of("sln:"+ctx.SolutionName), ctx.SolutionName, ctx.SolutionName, Empty, NoRoles);
        graph.AddNode(sln);
        var asm = ctx.Compilation.AssemblyName ?? "unknown";
        var proj = new Node(Labels.Project, Pk.Of("proj:"+asm), asm, asm, Empty, NoRoles);
        graph.AddNode(proj);
        graph.AddEdge(new Edge(Rel.Contains, sln.Pk, proj.Pk, Empty));
        foreach (var tree in ctx.Compilation.SyntaxTrees.Where(t => !string.IsNullOrEmpty(t.FilePath))) {
            var file = FileNodes.ForPath(tree.FilePath, ctx.SolutionRoot);
            graph.AddNode(file);
            graph.AddEdge(new Edge(Rel.IncludedIn, file.Pk, proj.Pk, Empty));
        }
        foreach (var r in ctx.Compilation.ReferencedAssemblyNames) {
            var pkg = new Node(Labels.Package, Pk.Of("pkg:"+r.Name), r.Name, r.Name, Empty, NoRoles);
            graph.AddNode(pkg);
            graph.AddEdge(new Edge(Rel.DependsOn, proj.Pk, pkg.Pk, Empty));
        }
    }
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter StructureExtractorTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "feat(codewiki): StructureExtractor(Solution/Project/File/Package)"`

---

## 적재·오케스트레이션 (T16~T20)

### Task 16: `GraphSerializer` (Graph ↔ NDJSON)

**Files:** Create `Storage/GraphSerializer.cs`; Test `GraphSerializerTests.cs`

**Interfaces:** Produces `void GraphSerializer.Write(Graph, string path)` / `Graph GraphSerializer.Read(string path)`. 한 줄 = 한 JSON 객체(`kind`:"node"|"edge").

- [ ] **Step 1: 실패 테스트**
```csharp
using System.Collections.Generic; using System.IO; using System.Linq; using CodeWiki.Model; using CodeWiki.Storage; using Xunit;
public class GraphSerializerTests {
    [Fact] public void RoundTrip() {
        var g = new Graph();
        g.AddNode(new Node(Labels.Class,"1","Foo","N.Foo", new Dictionary<string,string>{["k"]="v"}, new[]{Labels.ViewModel}));
        g.AddNode(new Node(Labels.Method,"2","Bar","N.Foo.Bar", new Dictionary<string,string>(), new string[0]));
        g.AddEdge(new Edge(Rel.Declares,"1","2", new Dictionary<string,string>()));
        var path = Path.GetTempFileName();
        GraphSerializer.Write(g, path);
        var g2 = GraphSerializer.Read(path);
        Assert.Equal(2, g2.Nodes.Count); Assert.Single(g2.Edges);
        Assert.Equal("v", g2.Nodes.Single(n=>n.Pk=="1").Props["k"]);
        Assert.Contains(Labels.ViewModel, g2.Nodes.Single(n=>n.Pk=="1").Roles);
    }
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter GraphSerializerTests` → FAIL

- [ ] **Step 3: 구현**
```csharp
using System.Collections.Generic; using System.IO; using System.Linq; using System.Text.Json; using CodeWiki.Model;
namespace CodeWiki.Storage;
public static class GraphSerializer {
    private sealed record NodeLine(string kind, string label, string pk, string name, string fullName,
        Dictionary<string,string> props, List<string> roles);
    private sealed record EdgeLine(string kind, string type, string from, string to, Dictionary<string,string> props);
    public static void Write(Graph g, string path) {
        using var w = new StreamWriter(path, false);
        foreach (var n in g.Nodes)
            w.WriteLine(JsonSerializer.Serialize(new NodeLine("node", n.Label, n.Pk, n.Name, n.FullName,
                new(n.Props), n.Roles.ToList())));
        foreach (var e in g.Edges)
            w.WriteLine(JsonSerializer.Serialize(new EdgeLine("edge", e.Type, e.FromPk, e.ToPk, new(e.Props))));
    }
    public static Graph Read(string path) {
        var g = new Graph();
        foreach (var line in File.ReadLines(path)) {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            var kind = doc.RootElement.GetProperty("kind").GetString();
            if (kind == "node") { var n = JsonSerializer.Deserialize<NodeLine>(line)!;
                g.AddNode(new Node(n.label, n.pk, n.name, n.fullName, n.props ?? new(), n.roles ?? new())); }
            else { var e = JsonSerializer.Deserialize<EdgeLine>(line)!;
                g.AddEdge(new Edge(e.type, e.from, e.to, e.props ?? new())); }
        }
        return g;
    }
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter GraphSerializerTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "feat(codewiki): GraphSerializer NDJSON 라운드트립"`

---

### Task 17: `CypherBuilder` + `Neo4jLoader`

**Files:** Create `Storage/CypherBuilder.cs`, `Storage/Neo4jLoader.cs`; Test `CypherBuilderTests.cs`

**Interfaces:**
- Produces `IEnumerable<(string cypher, Dictionary<string,object> param)> CypherBuilder.NodeStatements(Graph)` / `EdgeStatements(Graph)` — **순수, 유일 Cypher 생성 지점**. 라벨 조합별 그룹(주 라벨+역할), 엣지 타입별 그룹.
- Produces `Neo4jLoader(string uri, string user, string pass)` with `Task LoadAsync(Graph, bool wipe)` (드라이버 실행; 통합 검증은 T21).

- [ ] **Step 1: 실패 테스트(순수 CypherBuilder만)**
```csharp
using System.Collections.Generic; using System.Linq; using CodeWiki.Model; using CodeWiki.Storage; using Xunit;
public class CypherBuilderTests {
    [Fact] public void NodeCypherHasMultiLabelAndRows() {
        var g = new Graph();
        g.AddNode(new Node(Labels.Class,"1","Vm","N.Vm", new Dictionary<string,string>(), new[]{Labels.ViewModel}));
        var (cypher, param) = CypherBuilder.NodeStatements(g).Single();
        Assert.Contains("MERGE (n:Class:ViewModel {pk: row.pk})", cypher);
        Assert.Single((List<Dictionary<string,object>>)param["rows"]);
    }
    [Fact] public void EdgeCypherByType() {
        var g = new Graph();
        g.AddEdge(new Edge(Rel.Calls,"1","2", new Dictionary<string,string>()));
        var (cypher, _) = CypherBuilder.EdgeStatements(g).Single();
        Assert.Contains("MERGE (a)-[r:CALLS]->(b)", cypher);
    }
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter CypherBuilderTests` → FAIL

- [ ] **Step 3: 구현**
```csharp
// CypherBuilder.cs
using System.Collections.Generic; using System.Linq; using CodeWiki.Model;
namespace CodeWiki.Storage;
public static class CypherBuilder {
    public static IEnumerable<(string cypher, Dictionary<string,object> param)> NodeStatements(Graph g) {
        foreach (var grp in g.Nodes.GroupBy(n => n.Label + ":" + string.Join(":", n.Roles))) {
            var first = grp.First();
            var labels = ":" + first.Label + (first.Roles.Count > 0 ? ":" + string.Join(":", first.Roles) : "");
            var cypher = $"UNWIND $rows AS row MERGE (n{labels} {{pk: row.pk}}) " +
                         "SET n += row.props, n.name = row.name, n.fullName = row.fullName";
            var rows = grp.Select(n => new Dictionary<string,object> {
                ["pk"]=n.Pk, ["name"]=n.Name, ["fullName"]=n.FullName,
                ["props"]=n.Props.ToDictionary(p=>p.Key, p=>(object)p.Value) }).ToList();
            yield return (cypher, new Dictionary<string,object>{["rows"]=rows});
        }
    }
    public static IEnumerable<(string cypher, Dictionary<string,object> param)> EdgeStatements(Graph g) {
        foreach (var grp in g.Edges.GroupBy(e => e.Type)) {
            var cypher = $"UNWIND $rows AS row MATCH (a {{pk: row.from}}) MATCH (b {{pk: row.to}}) " +
                         $"MERGE (a)-[r:{grp.Key}]->(b) SET r += row.props";
            var rows = grp.Select(e => new Dictionary<string,object> {
                ["from"]=e.FromPk, ["to"]=e.ToPk,
                ["props"]=e.Props.ToDictionary(p=>p.Key, p=>(object)p.Value) }).ToList();
            yield return (cypher, new Dictionary<string,object>{["rows"]=rows});
        }
    }
}
```
```csharp
// Neo4jLoader.cs
using System.Threading.Tasks; using CodeWiki.Model; using Neo4j.Driver;
namespace CodeWiki.Storage;
public sealed class Neo4jLoader : System.IAsyncDisposable {
    private readonly IDriver _driver;
    public Neo4jLoader(string uri, string user, string pass) => _driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, pass));
    public async Task LoadAsync(Graph g, bool wipe) {
        await using var session = _driver.AsyncSession();
        if (wipe) await session.RunAsync("MATCH (n) DETACH DELETE n");
        foreach (var (cypher, param) in CypherBuilder.NodeStatements(g)) await session.RunAsync(cypher, param);
        foreach (var (cypher, param) in CypherBuilder.EdgeStatements(g)) await session.RunAsync(cypher, param);
    }
    public async ValueTask DisposeAsync() => await _driver.DisposeAsync();
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter CypherBuilderTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "feat(codewiki): CypherBuilder(유일 생성지점)+Neo4jLoader 단일 적재"`

---

### Task 18: `WorkspaceBuilder` (Buildalyzer, 불변식 캡슐화)

**Files:** Create `Pipeline/IWorkspaceBuilder.cs`, `Pipeline/WorkspaceBuilder.cs`; Test `WorkspaceBuilderTests.cs`

**Interfaces:** Produces `interface IWorkspaceBuilder { IEnumerable<Compilation> Build(string slnPath); }` + `WorkspaceBuilder` 구현. 불변식: `DesignTime=false`, `addProjectReferences:false`, 프로젝트 단위 try/catch.

> ⚠️ 풀 검증은 실제 MSBuild이 필요해 T21(통합)에서 수행. 본 태스크 단위 테스트는 인터페이스 존재·잘못된 경로 graceful 처리만.

- [ ] **Step 1: 실패 테스트**
```csharp
using System.Linq; using CodeWiki.Pipeline; using Xunit;
public class WorkspaceBuilderTests {
    [Fact] public void MissingSolutionDoesNotThrow() {
        var wb = new WorkspaceBuilder();
        var result = wb.Build("Z:/does/not/exist.sln").ToList();  // 빈 결과, 예외 없음
        Assert.Empty(result);
    }
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter WorkspaceBuilderTests` → FAIL

- [ ] **Step 3: 구현**
```csharp
// IWorkspaceBuilder.cs
using System.Collections.Generic; using Microsoft.CodeAnalysis;
namespace CodeWiki.Pipeline;
public interface IWorkspaceBuilder { IEnumerable<Compilation> Build(string slnPath); }
```
```csharp
// WorkspaceBuilder.cs
using System; using System.Collections.Generic; using System.Linq;
using Buildalyzer; using Buildalyzer.Workspaces; using Microsoft.CodeAnalysis;
namespace CodeWiki.Pipeline;
public sealed class WorkspaceBuilder : IWorkspaceBuilder {
    public IEnumerable<Compilation> Build(string slnPath) {
        if (!System.IO.File.Exists(slnPath)) { Console.Error.WriteLine($"WARN: solution not found: {slnPath}"); yield break; }
        var manager = new AnalyzerManager(slnPath);
        var ws = new AdhocWorkspace();
        foreach (var p in manager.Projects.Values) {
            Compilation? comp = null;
            try {
                var env = new EnvironmentOptions { DesignTime = false };   // 불변식 #1 풀빌드
                var result = p.Build(env).FirstOrDefault();
                if (result is null) { Console.Error.WriteLine($"WARN: build empty: {p.ProjectFile.Path}"); continue; }
                var roslyn = result.AddToWorkspace(ws, addProjectReferences: false);  // 불변식 #2 빈 스텁 방지
                comp = roslyn.GetCompilationAsync().GetAwaiter().GetResult();
            } catch (Exception ex) { Console.Error.WriteLine($"WARN: project failed {p.ProjectFile.Path}: {ex.Message}"); } // 불변식 #4
            if (comp != null) yield return comp;
        }
    }
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter WorkspaceBuilderTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "feat(codewiki): WorkspaceBuilder(Buildalyzer 풀빌드·불변식 캡슐화)"`

---

### Task 19: `AnalysisPipeline` (오케스트레이션)

**Files:** Create `Pipeline/AnalysisPipeline.cs`; Test `AnalysisPipelineTests.cs`

**Interfaces:** Produces `AnalysisPipeline(IWorkspaceBuilder)` with `Graph Run(string slnPath)`. 스코프별 추출기 실행 + `ViewModelLinker` 후처리. 테스트는 `IWorkspaceBuilder` 스텁으로 in-memory compilation 주입.

- [ ] **Step 1: 실패 테스트**
```csharp
using System.Collections.Generic; using System.Linq; using CodeWiki.Model; using CodeWiki.Pipeline; using Microsoft.CodeAnalysis; using Xunit;
public class AnalysisPipelineTests {
    sealed class Stub : IWorkspaceBuilder {
        private readonly Compilation _c;
        public Stub(Compilation c) => _c = c;
        public IEnumerable<Compilation> Build(string slnPath) { yield return _c; }
    }
    [Fact] public void RunsExtractorsAndLinker() {
        var (c,_) = TestCompiler.Compile(@"namespace N {
            public class SearchOrderView {} public class SearchOrderViewModel {} }");
        var g = new AnalysisPipeline(new Stub(c)).Run("x.sln");
        var v = g.Nodes.Single(n=>n.Name=="SearchOrderView"); var vm = g.Nodes.Single(n=>n.Name=="SearchOrderViewModel");
        Assert.Contains(g.Edges, e => e.Type==Rel.BindsTo && e.FromPk==v.Pk && e.ToPk==vm.Pk);
    }
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter AnalysisPipelineTests` → FAIL

- [ ] **Step 3: 구현**
```csharp
using System; using System.IO; using CodeWiki.Extraction; using CodeWiki.Model; using CodeWiki.Roslyn;
namespace CodeWiki.Pipeline;
public sealed class AnalysisPipeline {
    private readonly IWorkspaceBuilder _workspace;
    public AnalysisPipeline(IWorkspaceBuilder workspace) => _workspace = workspace;
    public Graph Run(string slnPath) {
        var graph = new Graph();
        var roles = new RoleClassifier();
        var extractors = new IExtractor[] {
            new TypeExtractor(roles), new InterfaceImplementationExtractor(),
            new CommandExtractor(roles), new TypeUsageExtractor(roles),
            new RepositoryUsageExtractor(roles), new StructureExtractor() };
        var root = Path.GetDirectoryName(Path.GetFullPath(slnPath)) ?? ".";
        var slnName = Path.GetFileNameWithoutExtension(slnPath);
        foreach (var comp in _workspace.Build(slnPath)) {
            var ctx = new ExtractionContext(comp, root, slnName);
            foreach (var ex in extractors)
                try { ex.Extract(ctx, graph); }
                catch (Exception e) { Console.Error.WriteLine($"WARN: {ex.GetType().Name} on {comp.AssemblyName}: {e.Message}"); }
        }
        new ViewModelLinker().Link(graph);
        return graph;
    }
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter AnalysisPipelineTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "feat(codewiki): AnalysisPipeline 오케스트레이션+후처리"`

---

### Task 20: `Program` CLI (extract / load)

**Files:** Modify `src/CodeWiki/Program.cs`; Create `Cli/CliOptions.cs`; Test `CliOptionsTests.cs`

**Interfaces:** Produces `CliOptions.Parse(string[] args)` → `record CliOptions(string Verb, string? Solution, string? Output, string? Credentials, string? Ndjson, bool Wipe)`. `Program.Main`이 verb로 분기.

- [ ] **Step 1: 실패 테스트(순수 파서)**
```csharp
using CodeWiki.Cli; using Xunit;
public class CliOptionsTests {
    [Fact] public void ParsesExtract() {
        var o = CliOptions.Parse(new[]{"extract","-s","a.sln","-o","out.ndjson"});
        Assert.Equal("extract", o.Verb); Assert.Equal("a.sln", o.Solution); Assert.Equal("out.ndjson", o.Output);
    }
    [Fact] public void ParsesLoadWithWipe() {
        var o = CliOptions.Parse(new[]{"load","-c","neo4j:neo4j:pw","--ndjson","out.ndjson","--wipe"});
        Assert.Equal("load", o.Verb); Assert.Equal("neo4j:neo4j:pw", o.Credentials); Assert.True(o.Wipe);
    }
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter CliOptionsTests` → FAIL

- [ ] **Step 3: 구현**
```csharp
// Cli/CliOptions.cs
namespace CodeWiki.Cli;
public sealed record CliOptions(string Verb, string? Solution, string? Output,
    string? Credentials, string? Ndjson, bool Wipe) {
    public static CliOptions Parse(string[] args) {
        string verb = args.Length > 0 ? args[0] : "";
        string? sln=null, o=null, c=null, ndjson=null; bool wipe=false;
        for (int i=1;i<args.Length;i++) switch (args[i]) {
            case "-s": case "--solution": sln = args[++i]; break;
            case "-o": case "--output": o = args[++i]; break;
            case "-c": case "--credentials": c = args[++i]; break;
            case "--ndjson": ndjson = args[++i]; break;
            case "--wipe": wipe = true; break;
        }
        return new CliOptions(verb, sln, o, c, ndjson, wipe);
    }
}
```
```csharp
// Program.cs
using System; using System.Threading.Tasks; using CodeWiki.Cli; using CodeWiki.Pipeline; using CodeWiki.Storage;
var o = CliOptions.Parse(args);
switch (o.Verb) {
    case "extract": {
        var graph = new AnalysisPipeline(new WorkspaceBuilder()).Run(o.Solution!);
        GraphSerializer.Write(graph, o.Output!);
        Console.WriteLine($"extracted: {graph.Nodes.Count} nodes, {graph.Edges.Count} edges → {o.Output}");
        break;
    }
    case "load": {
        var parts = o.Credentials!.Split(':');  // db:user:pass (db 세그먼트는 현재 미사용)
        var graph = GraphSerializer.Read(o.Ndjson!);
        await using var loader = new Neo4jLoader("bolt://localhost:7687", parts[^2], parts[^1]);
        await loader.LoadAsync(graph, o.Wipe);
        Console.WriteLine($"loaded: {graph.Nodes.Count} nodes, {graph.Edges.Count} edges (wipe={o.Wipe})");
        break;
    }
    default: Console.Error.WriteLine("usage: codewiki extract -s <sln> -o <ndjson> | load -c <db:user:pass> --ndjson <f> [--wipe]"); break;
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter CliOptionsTests` → PASS, 그리고 `dotnet build` 성공

- [ ] **Step 5: Commit** — `git commit -am "feat(codewiki): CLI extract/load"`

---

## 완료 검증 (T21)

### Task 21: Vanuatu 통합 실행 & 완료 기준 확인

> 단위 테스트가 아니라 실제 Vanuatu.sln 대상 통합 검증. 풀빌드 환경(모든 NuGet/Telerik 복원)에서 수행.

- [ ] **Step 1: 전체 단위 테스트 통과** — Run: `dotnet test` → 모든 테스트 PASS

- [ ] **Step 2: 추출 실행**
```bash
dotnet run --project src/CodeWiki -c Release -- extract \
  -s "C:\develop\baw\phase2\baw-phase2-platform\Vanuatu\Vanuatu.sln" -o out/graph.ndjson
```
Expected: `extracted: N nodes, M edges → out/graph.ndjson`, `WARN: project ... failed` 0건(또는 사유 확인).

- [ ] **Step 3: 적재 실행** (Neo4j 기동 상태, README §1)
```bash
dotnet run --project src/CodeWiki -c Release -- load -c "neo4j:neo4j:strazhpass" --ndjson out/graph.ndjson --wipe
```
Expected: `loaded: N nodes, M edges (wipe=True)`.

- [ ] **Step 4: 완료 기준 — 무단절 연결 (cookbook §4-④)**
Neo4j Browser에서:
```cypher
MATCH (vm:ViewModel)
OPTIONAL MATCH (vm)-[:DEFINES_COMMAND]->(:Command)-[:EXECUTES]->(:Method)
              -[:CALLS*1..4]->(:Method)<-[:IMPLEMENTS_METHOD]-(:Method)-[:USES]->(e:Entity)
WITH vm, count(e) AS reached
RETURN reached>0 AS connected, count(vm) AS vmCount ORDER BY connected;
```
Expected: `connected=true` 행의 vmCount가 다수(연결 성립). 대표 화면 SearchOrder E2E를 cookbook §6 쿼리로 수동 확인.

- [ ] **Step 5: 완료 기준 — 커버리지**
```cypher
MATCH (p:Project) RETURN count(p);                          // ≈ 44
MATCH (n:ViewModel) RETURN count(DISTINCT n);               // 비정상적으로 적으면(빈 스텁) 불변식 #2 재발 의심
MATCH ()-[r]->() RETURN type(r), count(*) ORDER BY count(*) DESC;
```
Expected: Project ≈ 44, ViewModel 풍부, 엣지 타입 분포 정상(`CALLS`/`IMPLEMENTS_METHOD`/`DECLARES` 다수).

- [ ] **Step 6: 결과 기록 & Commit**
완료 기준 충족 시 `out/graph.ndjson`을 LFS로 공유(README §0)하고, **core-etl-design.md·core-etl-plan.md 정리(삭제)** 후:
```bash
git commit -am "chore(codewiki): Phase 1 완료 — Vanuatu 그래프 검증 통과"
```

---

## Self-Review (작성자 체크)

- **스펙 커버리지:** spec §6.2 엣지 15종 → T8~T16에 모두 매핑(REGISTERS 제외=비목표 일치). 3대 목적 #2 연결성 → T21 Step4. ✅
- **타입 일관성:** `IExtractor.Extract(ExtractionContext, Graph)` 전 추출기 동일. `ViewModelLinker.Link(Graph)`만 후처리 별도(파이프라인이 직접 호출). `SymbolNodes.ForType`은 `null` 반환 가능 — 호출부 전부 null 가드. `CypherBuilder` 그룹 키와 라벨 문자열 일치. ✅
- **플레이스홀더:** 없음(모든 스텝 실제 코드/명령). ✅
- **비목표 일치:** REGISTERS·tableName·DbContext 없음. ✅
