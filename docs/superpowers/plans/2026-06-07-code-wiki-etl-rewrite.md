# CodeWiki 코어 ETL 재작성 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** strazh의 추출+적재(Roslyn → Neo4j 코드 지식 그래프)를 기능 동일하게 유지하되, 데이터 중심·단일 적재 경로의 이해하기 쉬운 구조로 재작성한다.

**Architecture:** 도메인 타입 37개(Triple*/Relationship*/Node 계층)를 `Node`/`Edge` record 2개 + `Graph` 빌더로 대체한다. 추출 규칙은 실행 스코프(Type/Tree/Project/후처리)별 작은 추출기로 격리하고, 적재는 `Graph → Neo4jLoader` 단일 경로(Cypher 생성 한 곳)로 통일한다. props가 `Dictionary<string,string>`라 이후 L0~L2가 키만 추가하면 수용된다.

**Tech Stack:** C# net9.0, Microsoft.CodeAnalysis(Roslyn) 4.13, Buildalyzer 7.1, Neo4j.Driver 5.27, System.CommandLine, xUnit.

**설계 근거:** [docs/superpowers/specs/2026-06-07-code-wiki-etl-rewrite-design.md](../specs/2026-06-07-code-wiki-etl-rewrite-design.md)

## 불변식 (작업 중 깨면 안 되는 것)

- **풀빌드 전제:** `EnvironmentOptions { DesignTime = false }` (WPF `.xaml.cs`/ViewModel 소스 캡처).
- **빈 스텁 방지:** `AddToWorkspace(addProjectReferences:false)` (앱 프로젝트가 모듈을 문서 0개 스텁으로 선점하는 함정 회피).
- **null 부모 노드 skip:** 미해석 베이스 타입으로 엣지를 만들지 않는다.
- **안정 해시:** pk는 FNV-1a(`Pk.Of`), 프로세스 불변. 다중 필드는 `|` 결합.
- **단일 적재 경로:** Cypher 생성은 `Neo4jLoader` 한 곳에서만.

---

## File Structure

```
src/CodeWiki/
  CodeWiki.csproj
  Program.cs                                 # CLI: extract / load
  Model/
    Pk.cs                                    # FNV-1a 해시
    Node.cs  Edge.cs                         # record 2개
    Graph.cs                                 # dedup 빌더
    Labels.cs  Rel.cs                        # 라벨/엣지 타입 상수
  Extraction/
    SymbolNodes.cs                           # Roslyn 심볼 → Node 팩토리
    RoleClassifier.cs                        # 타입 → 역할 라벨
    ITypeExtractor.cs                        # 타입 스코프 추출기 인터페이스
    InheritanceExtractor.cs                  # OF_TYPE
    MethodExtractor.cs                       # HAVE / INVOKE / CONSTRUCT
    InterfaceImplementationExtractor.cs      # IMPLEMENTS_METHOD
    CommandExtractor.cs                      # DEFINES_COMMAND / EXECUTES
    TypeUsageExtractor.cs                    # USES_TYPE
    RepositoryUsageExtractor.cs             # USES
    DiRegistrationExtractor.cs               # REGISTERS (Tree 스코프)
    ViewModelLinker.cs                       # BINDS_TO (후처리)
    StructureExtractor.cs                    # Solution/Project/Folder/File/Package + DECLARED_AT
  Pipeline/
    WorkspaceBuilder.cs                      # Buildalyzer + AdhocWorkspace (불변식 캡슐화)
    AnalysisPipeline.cs                      # 오케스트레이션
  Storage/
    GraphSerializer.cs                       # Graph ↔ NDJSON
    Neo4jLoader.cs                           # UNWIND 배치 MERGE (Cypher 유일 지점)
    Neo4jHealthcheck.cs
src/CodeWiki.Tests/
  CodeWiki.Tests.csproj
  TestCompiler.cs                            # 소스 문자열 → Compilation
  PkTests.cs  GraphTests.cs  SerializerTests.cs  Neo4jLoaderTests.cs
  RoleClassifierTests.cs
  InheritanceExtractorTests.cs  MethodExtractorTests.cs
  InterfaceImplementationExtractorTests.cs  CommandExtractorTests.cs
  TypeUsageExtractorTests.cs  RepositoryUsageExtractorTests.cs
  DiRegistrationExtractorTests.cs  ViewModelLinkerTests.cs
```

---

## Task 1: 프로젝트 스캐폴드

**Files:**
- Create: `src/CodeWiki/CodeWiki.csproj`
- Create: `src/CodeWiki.Tests/CodeWiki.Tests.csproj`

- [ ] **Step 1: 실행 프로젝트 csproj 작성**

`src/CodeWiki/CodeWiki.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>CodeWiki</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Buildalyzer" Version="7.1.0" />
    <PackageReference Include="Buildalyzer.Workspaces" Version="7.1.0" />
    <PackageReference Include="Microsoft.CodeAnalysis" Version="4.13.0" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.13.0" />
    <PackageReference Include="Neo4j.Driver" Version="5.27.0" />
    <PackageReference Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: 테스트 프로젝트 csproj 작성**

`src/CodeWiki.Tests/CodeWiki.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.13.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\CodeWiki\CodeWiki.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: 빌드 확인**

Run: `dotnet build src/CodeWiki/CodeWiki.csproj -c Release`
Expected: 빌드 성공(아직 코드 없음, 0 경고/오류).

- [ ] **Step 4: 커밋**

```bash
git add src/CodeWiki/CodeWiki.csproj src/CodeWiki.Tests/CodeWiki.Tests.csproj
git commit -m "chore(codewiki): scaffold rewrite projects"
```

---

## Task 2: Pk (FNV-1a 안정 해시)

**Files:**
- Create: `src/CodeWiki/Model/Pk.cs`
- Test: `src/CodeWiki.Tests/PkTests.cs`

- [ ] **Step 1: 실패 테스트 작성**

`src/CodeWiki.Tests/PkTests.cs`:
```csharp
using CodeWiki.Model;
using Xunit;

namespace CodeWiki.Tests;

public class PkTests
{
    [Fact]
    public void Of_is_deterministic_for_same_input()
    {
        Assert.Equal(Pk.Of("N.Foo"), Pk.Of("N.Foo"));
    }

    [Fact]
    public void Of_matches_known_fnv1a_anchor()
    {
        // strazh StableHash("N.Foo") 회귀 앵커
        Assert.Equal("16177116733985609327", Pk.Of("N.Foo"));
    }

    [Fact]
    public void Of_joins_multiple_parts_with_pipe()
    {
        // strazh PackageNode pk = StableHash("Newtonsoft.Json|13.0.0")
        Assert.Equal("11543004957276216214", Pk.Of("Newtonsoft.Json", "13.0.0"));
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter PkTests`
Expected: FAIL — `Pk` 타입 없음(컴파일 오류).

- [ ] **Step 3: 구현**

`src/CodeWiki/Model/Pk.cs`:
```csharp
namespace CodeWiki.Model;

/// <summary>노드 기본키. FNV-1a 64bit (프로세스/런타임 불변). 다중 필드는 '|'로 결합.</summary>
public static class Pk
{
    public static string Of(params string[] parts) => Fnv1a(string.Join("|", parts));

    private static string Fnv1a(string text)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char c in text)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        return hash.ToString();
    }
}
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter PkTests`
Expected: PASS (3 tests).

- [ ] **Step 5: 커밋**

```bash
git add src/CodeWiki/Model/Pk.cs src/CodeWiki.Tests/PkTests.cs
git commit -m "feat(codewiki): add FNV-1a Pk with regression anchors"
```

---

## Task 3: Node / Edge record + Graph 빌더

**Files:**
- Create: `src/CodeWiki/Model/Node.cs`, `src/CodeWiki/Model/Edge.cs`, `src/CodeWiki/Model/Graph.cs`
- Test: `src/CodeWiki.Tests/GraphTests.cs`

- [ ] **Step 1: 실패 테스트 작성**

`src/CodeWiki.Tests/GraphTests.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using CodeWiki.Model;
using Xunit;

namespace CodeWiki.Tests;

public class GraphTests
{
    private static Node N(string pk, IDictionary<string, string>? props = null, string[]? roles = null)
        => new("Class", pk, "Name", "Full.Name", props ?? new Dictionary<string, string>(), roles ?? System.Array.Empty<string>());

    [Fact]
    public void AddNode_dedups_by_pk()
    {
        var g = new Graph();
        g.AddNode(N("p1"));
        g.AddNode(N("p1"));
        Assert.Single(g.Nodes);
    }

    [Fact]
    public void AddNode_merges_props_keeping_nonempty()
    {
        var g = new Graph();
        g.AddNode(N("p1", new Dictionary<string, string> { ["modifiers"] = "" }));
        g.AddNode(N("p1", new Dictionary<string, string> { ["modifiers"] = "public" }));
        Assert.Equal("public", g.Nodes.Single().Props["modifiers"]);
    }

    [Fact]
    public void AddNode_unions_role_labels()
    {
        var g = new Graph();
        g.AddNode(N("p1", roles: new[] { "Entity" }));
        g.AddNode(N("p1", roles: new[] { "DTO" }));
        Assert.Equal(new[] { "Entity", "DTO" }, g.Nodes.Single().Roles.OrderBy(x => x));
    }

    [Fact]
    public void AddEdge_dedups_by_type_from_to()
    {
        var g = new Graph();
        var e = new Edge("INVOKE", "a", "b", new Dictionary<string, string>());
        g.AddEdge(e);
        g.AddEdge(e);
        Assert.Single(g.Edges);
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter GraphTests`
Expected: FAIL — `Node`/`Edge`/`Graph` 없음.

- [ ] **Step 3: 구현**

`src/CodeWiki/Model/Node.cs`:
```csharp
namespace CodeWiki.Model;

/// <summary>그래프 노드. 주 라벨 + 역할 라벨 N개. Props는 Neo4j 노드에 SET += 되는 스칼라들.</summary>
public sealed record Node(
    string Label,
    string Pk,
    string Name,
    string FullName,
    IReadOnlyDictionary<string, string> Props,
    IReadOnlyList<string> Roles);
```

`src/CodeWiki/Model/Edge.cs`:
```csharp
namespace CodeWiki.Model;

/// <summary>그래프 엣지. 끝점은 pk로만 참조(라벨은 적재 시 노드맵에서 해석).</summary>
public sealed record Edge(
    string Type,
    string FromPk,
    string ToPk,
    IReadOnlyDictionary<string, string> Props);
```

`src/CodeWiki/Model/Graph.cs`:
```csharp
namespace CodeWiki.Model;

/// <summary>추출 결과 누적기. 노드는 pk로, 엣지는 (type,from,to)로 dedup.</summary>
public sealed class Graph
{
    private readonly Dictionary<string, Node> _nodes = new();
    private readonly Dictionary<string, Edge> _edges = new();

    public IReadOnlyCollection<Node> Nodes => _nodes.Values;
    public IReadOnlyCollection<Edge> Edges => _edges.Values;

    public void AddNode(Node node)
    {
        if (!_nodes.TryGetValue(node.Pk, out var existing))
        {
            _nodes[node.Pk] = node;
            return;
        }
        _nodes[node.Pk] = Merge(existing, node);
    }

    public void AddEdge(Edge edge)
    {
        var key = $"{edge.Type}|{edge.FromPk}|{edge.ToPk}";
        if (!_edges.ContainsKey(key))
            _edges[key] = edge;
    }

    /// <summary>같은 pk 재등장 시: 비어있지 않은 prop 값을 채우고 역할 라벨을 합집합.</summary>
    private static Node Merge(Node a, Node b)
    {
        var props = new Dictionary<string, string>(a.Props);
        foreach (var (k, v) in b.Props)
            if (!string.IsNullOrEmpty(v) && (!props.TryGetValue(k, out var cur) || string.IsNullOrEmpty(cur)))
                props[k] = v;

        var roles = a.Roles.Concat(b.Roles).Distinct().ToArray();
        return a with { Props = props, Roles = roles };
    }
}
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter GraphTests`
Expected: PASS (4 tests).

- [ ] **Step 5: 커밋**

```bash
git add src/CodeWiki/Model/Node.cs src/CodeWiki/Model/Edge.cs src/CodeWiki/Model/Graph.cs src/CodeWiki.Tests/GraphTests.cs
git commit -m "feat(codewiki): add Node/Edge records and dedup Graph builder"
```

---

## Task 4: Labels / Rel 상수

**Files:**
- Create: `src/CodeWiki/Model/Labels.cs`, `src/CodeWiki/Model/Rel.cs`

- [ ] **Step 1: 구현 (상수만 — 별도 테스트 불필요, 컴파일로 검증)**

`src/CodeWiki/Model/Labels.cs`:
```csharp
namespace CodeWiki.Model;

/// <summary>노드 주 라벨 상수. 매직스트링 방지 + 전체 목록 한눈에.</summary>
public static class Labels
{
    public const string Class = "Class";
    public const string Interface = "Interface";
    public const string Method = "Method";
    public const string Command = "Command";
    public const string File = "File";
    public const string Folder = "Folder";
    public const string Solution = "Solution";
    public const string Project = "Project";
    public const string Package = "Package";
}
```

`src/CodeWiki/Model/Rel.cs`:
```csharp
namespace CodeWiki.Model;

/// <summary>엣지(관계) 타입 상수.</summary>
public static class Rel
{
    public const string Have = "HAVE";
    public const string Invoke = "INVOKE";
    public const string Construct = "CONSTRUCT";
    public const string OfType = "OF_TYPE";
    public const string DeclaredAt = "DECLARED_AT";
    public const string IncludedIn = "INCLUDED_IN";
    public const string DependsOn = "DEPENDS_ON";
    public const string Contains = "CONTAINS";
    public const string ImplementsMethod = "IMPLEMENTS_METHOD";
    public const string UsesType = "USES_TYPE";
    public const string Uses = "USES";
    public const string Executes = "EXECUTES";
    public const string DefinesCommand = "DEFINES_COMMAND";
    public const string BindsTo = "BINDS_TO";
    public const string Registers = "REGISTERS";
}
```

- [ ] **Step 2: 빌드 확인**

Run: `dotnet build src/CodeWiki/CodeWiki.csproj -c Release`
Expected: 성공.

- [ ] **Step 3: 커밋**

```bash
git add src/CodeWiki/Model/Labels.cs src/CodeWiki/Model/Rel.cs
git commit -m "feat(codewiki): add Labels/Rel constants"
```

---

## Task 5: SymbolNodes (Roslyn 심볼 → Node 팩토리)

**Files:**
- Create: `src/CodeWiki/Extraction/SymbolNodes.cs`
- Test: `src/CodeWiki.Tests/TestCompiler.cs` (먼저 Task 6에서 만들지만, 이 Task 테스트가 필요로 하므로 여기서 같이 생성)

> 주의: 이 Task는 TestCompiler에 의존한다. Task 6 Step 1의 `TestCompiler.cs`를 먼저 만든 뒤 진행하거나, 두 Task를 함께 수행한다.

- [ ] **Step 1: 실패 테스트 작성**

`src/CodeWiki.Tests/SymbolNodesTests.cs`:
```csharp
using System.Linq;
using CodeWiki.Extraction;
using CodeWiki.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace CodeWiki.Tests;

public class SymbolNodesTests
{
    [Fact]
    public void Method_fullName_is_namespace_type_method()
    {
        var src = "namespace N { class C { public int Add(string a) => 0; } }";
        var (tree, model) = TestCompiler.Compile(src);
        var m = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var symbol = (IMethodSymbol)model.GetDeclaredSymbol(m)!;

        var node = SymbolNodes.Method(symbol);

        Assert.Equal(Labels.Method, node.Label);
        Assert.Equal("N.C.Add", node.FullName);
        Assert.Equal("System.String a", node.Props["arguments"]);
        Assert.Equal("int", node.Props["returnType"]);
    }

    [Fact]
    public void Type_fullName_is_namespace_name()
    {
        var src = "namespace N { class C { } }";
        var (tree, model) = TestCompiler.Compile(src);
        var c = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var symbol = (INamedTypeSymbol)model.GetDeclaredSymbol(c)!;

        var node = SymbolNodes.Class(symbol);

        Assert.Equal(Labels.Class, node.Label);
        Assert.Equal("N.C", node.FullName);
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter SymbolNodesTests`
Expected: FAIL — `SymbolNodes` 없음.

- [ ] **Step 3: 구현**

`src/CodeWiki/Extraction/SymbolNodes.cs`:
```csharp
using CodeWiki.Model;
using Microsoft.CodeAnalysis;

namespace CodeWiki.Extraction;

/// <summary>Roslyn 심볼을 그래프 Node로 변환하는 팩토리. fullName 계산 규칙을 한 곳에 모은다.</summary>
public static class SymbolNodes
{
    public static Node Class(INamedTypeSymbol s, IEnumerable<string>? roles = null, string[]? modifiers = null)
        => Type(s, Labels.Class, roles, modifiers);

    public static Node Interface(INamedTypeSymbol s, IEnumerable<string>? roles = null, string[]? modifiers = null)
        => Type(s, Labels.Interface, roles, modifiers);

    /// <summary>심볼 종류에 따라 Class 또는 Interface 노드. (도메인 타입만 호출되는 전제)</summary>
    public static Node OfKind(INamedTypeSymbol s)
        => s.TypeKind == TypeKind.Interface ? Interface(s) : Class(s);

    private static Node Type(INamedTypeSymbol s, string label, IEnumerable<string>? roles, string[]? modifiers)
    {
        var fullName = (s.ContainingNamespace?.ToString() ?? "") + "." + s.Name;
        var props = new Dictionary<string, string>();
        if (modifiers is { Length: > 0 }) props["modifiers"] = string.Join(", ", modifiers);
        return new Node(label, Pk.Of(fullName), s.Name, fullName, props,
            (roles ?? Enumerable.Empty<string>()).ToArray());
    }

    public static Node Method(IMethodSymbol s, string[]? modifiers = null)
    {
        var fullName = NamespacePrefix(s.ContainingNamespace, $"{s.ContainingType.Name}.{s.Name}");
        var arguments = string.Join(", ", s.Parameters.Select(p => $"{p.Type} {p.Name}"));
        var returnType = s.ReturnType.ToString()!;
        var props = new Dictionary<string, string> { ["returnType"] = returnType, ["arguments"] = arguments };
        if (modifiers is { Length: > 0 }) props["modifiers"] = string.Join(", ", modifiers);
        return new Node(Labels.Method, Pk.Of(fullName, arguments, returnType), s.Name, fullName, props,
            System.Array.Empty<string>());
    }

    public static Node Command(string ownerFullName, string name)
    {
        var fullName = $"{ownerFullName}.{name}";
        return new Node(Labels.Command, Pk.Of(fullName), name, fullName,
            new Dictionary<string, string>(), System.Array.Empty<string>());
    }

    private static string NamespacePrefix(INamespaceSymbol? ns, string name)
    {
        var segment = ns?.Name;
        if (string.IsNullOrEmpty(segment)) return name;
        return NamespacePrefix(ns!.ContainingNamespace, $"{segment}.{name}");
    }
}
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter SymbolNodesTests`
Expected: PASS (2 tests).

- [ ] **Step 5: 커밋**

```bash
git add src/CodeWiki/Extraction/SymbolNodes.cs src/CodeWiki.Tests/SymbolNodesTests.cs
git commit -m "feat(codewiki): add SymbolNodes factory (symbol -> Node)"
```

---

## Task 6: TestCompiler 이식

**Files:**
- Create: `src/CodeWiki.Tests/TestCompiler.cs`

- [ ] **Step 1: 구현 (strazh에서 이식, 네임스페이스만 변경)**

`src/CodeWiki.Tests/TestCompiler.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeWiki.Tests;

/// <summary>소스 문자열을 컴파일해 (구문트리, 의미모델)을 반환. 추출기 단위 테스트용.</summary>
public static class TestCompiler
{
    public static (SyntaxTree tree, SemanticModel model) Compile(string source)
    {
        var trees = new[] { CSharpSyntaxTree.ParseText(source, path: "Source.cs") };
        var compilation = CreateCompilation(trees);
        return (trees[0], compilation.GetSemanticModel(trees[0]));
    }

    public static IReadOnlyList<(SyntaxTree tree, SemanticModel model)> CompileMany(params string[] sources)
    {
        var trees = sources.Select((s, i) => CSharpSyntaxTree.ParseText(s, path: $"Source{i}.cs")).ToArray();
        var compilation = CreateCompilation(trees);
        return trees.Select(t => (t, compilation.GetSemanticModel(t))).ToList();
    }

    private static CSharpCompilation CreateCompilation(SyntaxTree[] trees)
    {
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException(
                "TRUSTED_PLATFORM_ASSEMBLIES is not set. Ensure the test host runs on .NET Core/5+.");
        var refs = tpa.Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll"))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
        return CSharpCompilation.Create("TestAssembly", trees, refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
```

- [ ] **Step 2: 빌드 확인 (Task 5 테스트가 이걸 사용)**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter SymbolNodesTests`
Expected: PASS (TestCompiler 해석됨).

- [ ] **Step 3: 커밋**

```bash
git add src/CodeWiki.Tests/TestCompiler.cs
git commit -m "test(codewiki): port TestCompiler helper"
```

---

## Task 7: RoleClassifier

**Files:**
- Create: `src/CodeWiki/Extraction/RoleClassifier.cs`
- Test: `src/CodeWiki.Tests/RoleClassifierTests.cs`

- [ ] **Step 1: 실패 테스트 작성**

`src/CodeWiki.Tests/RoleClassifierTests.cs`:
```csharp
using System.Linq;
using CodeWiki.Extraction;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace CodeWiki.Tests;

public class RoleClassifierTests
{
    private static INamedTypeSymbol TypeOf(string src, string name)
    {
        var (tree, model) = TestCompiler.Compile(src);
        var decl = tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>()
            .First(t => t.Identifier.Text == name);
        return (INamedTypeSymbol)model.GetDeclaredSymbol(decl)!;
    }

    [Fact]
    public void ViewModel_by_name_suffix()
    {
        var roles = RoleClassifier.Classify(TypeOf("namespace N { class OrderViewModel { } }", "OrderViewModel"));
        Assert.Contains("ViewModel", roles);
    }

    [Fact]
    public void Entity_by_IBaseEntity()
    {
        var src = "namespace N { interface IBaseEntity { } class Order : IBaseEntity { } }";
        Assert.Contains("Entity", RoleClassifier.Classify(TypeOf(src, "Order")));
    }

    [Fact]
    public void Service_by_interface_naming()
    {
        var src = "namespace N { interface IFooService { } class FooService : IFooService { } }";
        Assert.Contains("Service", RoleClassifier.Classify(TypeOf(src, "FooService")));
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter RoleClassifierTests`
Expected: FAIL — `RoleClassifier` 없음.

- [ ] **Step 3: 구현 (strazh 로직 이식)**

`src/CodeWiki/Extraction/RoleClassifier.cs`:
```csharp
using Microsoft.CodeAnalysis;

namespace CodeWiki.Extraction;

/// <summary>타입 → 역할 라벨 휴리스틱 (Entity/ViewModel/Controller/Service/Repository/DTO/View).</summary>
public static class RoleClassifier
{
    public static IReadOnlyList<string> Classify(INamedTypeSymbol type)
    {
        var roles = new List<string>();
        var name = type.Name;
        var ns = type.ContainingNamespace?.ToString() ?? "";
        var allIfaces = type.AllInterfaces.Select(i => i.Name).ToHashSet();
        var baseNames = BaseChain(type).Select(b => b.Name).ToHashSet();

        if (allIfaces.Contains("IBaseEntity")) roles.Add("Entity");
        if (baseNames.Contains("BindableBase") || name.EndsWith("ViewModel")) roles.Add("ViewModel");
        if (baseNames.Contains("ControllerBase") || name.EndsWith("Controller")) roles.Add("Controller");
        if (allIfaces.Any(i => i.StartsWith("I") && i.EndsWith("Service"))) roles.Add("Service");
        if (name.Contains("Repository")) roles.Add("Repository");
        if (ns.Contains(".DTO") || name.EndsWith("DTO")) roles.Add("DTO");
        if (name.EndsWith("View") && !name.EndsWith("ViewModel")) roles.Add("View");
        return roles;
    }

    private static IEnumerable<INamedTypeSymbol> BaseChain(INamedTypeSymbol type)
    {
        for (var b = type.BaseType; b != null; b = b.BaseType)
            yield return b;
    }
}
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter RoleClassifierTests`
Expected: PASS (3 tests).

- [ ] **Step 5: 커밋**

```bash
git add src/CodeWiki/Extraction/RoleClassifier.cs src/CodeWiki.Tests/RoleClassifierTests.cs
git commit -m "feat(codewiki): port RoleClassifier"
```

---

## Task 8: ITypeExtractor + InheritanceExtractor (OF_TYPE)

**Files:**
- Create: `src/CodeWiki/Extraction/ITypeExtractor.cs`, `src/CodeWiki/Extraction/InheritanceExtractor.cs`
- Test: `src/CodeWiki.Tests/InheritanceExtractorTests.cs`

- [ ] **Step 1: 실패 테스트 작성**

`src/CodeWiki.Tests/InheritanceExtractorTests.cs`:
```csharp
using System.Linq;
using CodeWiki.Extraction;
using CodeWiki.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace CodeWiki.Tests;

public class InheritanceExtractorTests
{
    [Fact]
    public void Emits_OF_TYPE_for_base_class()
    {
        var src = "namespace N { class Base { } class Derived : Base { } }";
        var (tree, model) = TestCompiler.Compile(src);
        var decl = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(c => c.Identifier.Text == "Derived");
        var symbol = (INamedTypeSymbol)model.GetDeclaredSymbol(decl)!;
        var graph = new Graph();

        new InheritanceExtractor().Extract(symbol, decl, model, graph);

        var derivedPk = Pk.Of("N.Derived");
        var basePk = Pk.Of("N.Base");
        Assert.Contains(graph.Edges, e => e.Type == Rel.OfType && e.FromPk == derivedPk && e.ToPk == basePk);
    }

    [Fact]
    public void Skips_unresolved_base_type()
    {
        // BindableBase는 해석 안 됨 → OF_TYPE 엣지 없어야 함(null 노드 skip 불변식)
        var src = "namespace N { class VM : BindableBase { } }";
        var (tree, model) = TestCompiler.Compile(src);
        var decl = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var symbol = (INamedTypeSymbol)model.GetDeclaredSymbol(decl)!;
        var graph = new Graph();

        new InheritanceExtractor().Extract(symbol, decl, model, graph);

        Assert.DoesNotContain(graph.Edges, e => e.Type == Rel.OfType);
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter InheritanceExtractorTests`
Expected: FAIL — 타입 없음.

- [ ] **Step 3: 구현**

`src/CodeWiki/Extraction/ITypeExtractor.cs`:
```csharp
using CodeWiki.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeWiki.Extraction;

/// <summary>한 타입 선언 단위로 동작하는 추출기. 파이프라인이 타입마다 모든 구현을 실행한다.</summary>
public interface ITypeExtractor
{
    void Extract(INamedTypeSymbol symbol, TypeDeclarationSyntax declaration, SemanticModel model, Graph graph);
}
```

`src/CodeWiki/Extraction/InheritanceExtractor.cs`:
```csharp
using CodeWiki.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeWiki.Extraction;

/// <summary>상속/구현 베이스 타입을 OF_TYPE으로. 미해석 베이스(예: Prism BindableBase)는 skip.</summary>
public sealed class InheritanceExtractor : ITypeExtractor
{
    public void Extract(INamedTypeSymbol symbol, TypeDeclarationSyntax declaration, SemanticModel model, Graph graph)
    {
        if (declaration.BaseList is null) return;
        var self = SymbolNodes.OfKind(symbol);

        foreach (var baseType in declaration.BaseList.Types)
        {
            if (model.GetTypeInfo(baseType.Type).Type is not INamedTypeSymbol parent) continue;
            if (parent.TypeKind != TypeKind.Class && parent.TypeKind != TypeKind.Interface) continue;

            var parentNode = SymbolNodes.OfKind(parent);
            graph.AddNode(self);
            graph.AddNode(parentNode);
            graph.AddEdge(new Edge(Rel.OfType, self.Pk, parentNode.Pk, new Dictionary<string, string>()));
        }
    }
}
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter InheritanceExtractorTests`
Expected: PASS (2 tests).

- [ ] **Step 5: 커밋**

```bash
git add src/CodeWiki/Extraction/ITypeExtractor.cs src/CodeWiki/Extraction/InheritanceExtractor.cs src/CodeWiki.Tests/InheritanceExtractorTests.cs
git commit -m "feat(codewiki): add ITypeExtractor + InheritanceExtractor (OF_TYPE)"
```

---

## Task 9: MethodExtractor (HAVE / INVOKE / CONSTRUCT)

**Files:**
- Create: `src/CodeWiki/Extraction/MethodExtractor.cs`
- Test: `src/CodeWiki.Tests/MethodExtractorTests.cs`

- [ ] **Step 1: 실패 테스트 작성**

`src/CodeWiki.Tests/MethodExtractorTests.cs`:
```csharp
using System.Linq;
using CodeWiki.Extraction;
using CodeWiki.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace CodeWiki.Tests;

public class MethodExtractorTests
{
    private static (INamedTypeSymbol, TypeDeclarationSyntax, SemanticModel) Owner(string src, string name)
    {
        var (tree, model) = TestCompiler.Compile(src);
        var decl = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(c => c.Identifier.Text == name);
        return ((INamedTypeSymbol)model.GetDeclaredSymbol(decl)!, decl, model);
    }

    [Fact]
    public void Emits_HAVE_for_declared_method()
    {
        var (sym, decl, model) = Owner("namespace N { class C { void Foo() { } } }", "C");
        var graph = new Graph();

        new MethodExtractor().Extract(sym, decl, model, graph);

        Assert.Contains(graph.Edges, e => e.Type == Rel.Have && e.FromPk == Pk.Of("N.C"));
    }

    [Fact]
    public void Emits_INVOKE_for_called_method()
    {
        var src = "namespace N { class C { void A() { B(); } void B() { } } }";
        var (sym, decl, model) = Owner(src, "C");
        var graph = new Graph();

        new MethodExtractor().Extract(sym, decl, model, graph);

        Assert.Contains(graph.Edges, e => e.Type == Rel.Invoke);
    }

    [Fact]
    public void Emits_CONSTRUCT_for_object_creation()
    {
        var src = "namespace N { class Dep { } class C { void A() { var d = new Dep(); } } }";
        var (sym, decl, model) = Owner(src, "C");
        var graph = new Graph();

        new MethodExtractor().Extract(sym, decl, model, graph);

        Assert.Contains(graph.Edges, e => e.Type == Rel.Construct && e.ToPk == Pk.Of("N.Dep"));
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter MethodExtractorTests`
Expected: FAIL — `MethodExtractor` 없음.

- [ ] **Step 3: 구현**

`src/CodeWiki/Extraction/MethodExtractor.cs`:
```csharp
using CodeWiki.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeWiki.Extraction;

/// <summary>타입이 가진 메서드(HAVE), 메서드 본문의 호출(INVOKE)·객체생성(CONSTRUCT)을 추출.</summary>
public sealed class MethodExtractor : ITypeExtractor
{
    public void Extract(INamedTypeSymbol symbol, TypeDeclarationSyntax declaration, SemanticModel model, Graph graph)
    {
        var owner = SymbolNodes.OfKind(symbol);

        foreach (var methodDecl in declaration.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (model.GetDeclaredSymbol(methodDecl) is not IMethodSymbol ms) continue;
            var modifiers = methodDecl.Modifiers.Select(t => t.ValueText).ToArray();
            var methodNode = SymbolNodes.Method(ms, modifiers);
            graph.AddNode(owner);
            graph.AddNode(methodNode);
            graph.AddEdge(new Edge(Rel.Have, owner.Pk, methodNode.Pk, new Dictionary<string, string>()));

            foreach (var expr in methodDecl.DescendantNodes().OfType<ExpressionSyntax>())
            {
                switch (expr)
                {
                    case ObjectCreationExpressionSyntax creation:
                        if (model.GetTypeInfo(creation).Type is INamedTypeSymbol created)
                        {
                            var createdNode = SymbolNodes.Class(created);
                            graph.AddNode(createdNode);
                            graph.AddEdge(new Edge(Rel.Construct, methodNode.Pk, createdNode.Pk, new Dictionary<string, string>()));
                        }
                        break;

                    case InvocationExpressionSyntax invocation:
                        if (model.GetSymbolInfo(invocation).Symbol is IMethodSymbol invoked)
                        {
                            var invokedNode = SymbolNodes.Method(invoked);
                            graph.AddNode(invokedNode);
                            graph.AddEdge(new Edge(Rel.Invoke, methodNode.Pk, invokedNode.Pk, new Dictionary<string, string>()));
                        }
                        break;
                }
            }
        }
    }
}
```

> 참고: strazh는 CONSTRUCT 대상 타입 노드를 `CreateClassNode`로 무조건 Class 라벨로 만든다(인터페이스 new는 불가하므로 안전). 동일 유지.

- [ ] **Step 4: 통과 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter MethodExtractorTests`
Expected: PASS (3 tests).

- [ ] **Step 5: 커밋**

```bash
git add src/CodeWiki/Extraction/MethodExtractor.cs src/CodeWiki.Tests/MethodExtractorTests.cs
git commit -m "feat(codewiki): add MethodExtractor (HAVE/INVOKE/CONSTRUCT)"
```

---

## Task 10: InterfaceImplementationExtractor (IMPLEMENTS_METHOD)

**Files:**
- Create: `src/CodeWiki/Extraction/InterfaceImplementationExtractor.cs`
- Test: `src/CodeWiki.Tests/InterfaceImplementationExtractorTests.cs`

- [ ] **Step 1: 실패 테스트 작성**

`src/CodeWiki.Tests/InterfaceImplementationExtractorTests.cs`:
```csharp
using System.Linq;
using CodeWiki.Extraction;
using CodeWiki.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace CodeWiki.Tests;

public class InterfaceImplementationExtractorTests
{
    [Fact]
    public void Links_impl_method_to_interface_member()
    {
        var src = @"
namespace N {
  public interface IOrderService { int Search(string f); }
  public class OrderService : IOrderService { public int Search(string f) => 0; }
}";
        var (tree, model) = TestCompiler.Compile(src);
        var decl = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var symbol = (INamedTypeSymbol)model.GetDeclaredSymbol(decl)!;
        var graph = new Graph();

        new InterfaceImplementationExtractor().Extract(symbol, decl, model, graph);

        var implPk = Pk.Of("N.OrderService.Search", "System.String f", "int");
        var ifacePk = Pk.Of("N.IOrderService.Search", "System.String f", "int");
        Assert.Contains(graph.Edges, e =>
            e.Type == Rel.ImplementsMethod && e.FromPk == implPk && e.ToPk == ifacePk);
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter InterfaceImplementationExtractorTests`
Expected: FAIL — 타입 없음.

- [ ] **Step 3: 구현**

`src/CodeWiki/Extraction/InterfaceImplementationExtractor.cs`:
```csharp
using CodeWiki.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeWiki.Extraction;

/// <summary>이 타입이 구현한 인터페이스 멤버를, 이 타입의 구현 메서드와 IMPLEMENTS_METHOD로 연결.
/// 클라 프록시와 서버 구현이 동일 인터페이스 멤버를 가리켜 경계 관통의 다리가 된다.</summary>
public sealed class InterfaceImplementationExtractor : ITypeExtractor
{
    public void Extract(INamedTypeSymbol symbol, TypeDeclarationSyntax declaration, SemanticModel model, Graph graph)
    {
        foreach (var iface in symbol.AllInterfaces)
        {
            foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
            {
                if (symbol.FindImplementationForInterfaceMember(member) is not IMethodSymbol impl) continue;
                if (!SymbolEqualityComparer.Default.Equals(impl.ContainingType, symbol)) continue;

                var implNode = SymbolNodes.Method(impl);
                var memberNode = SymbolNodes.Method(member);
                graph.AddNode(implNode);
                graph.AddNode(memberNode);
                graph.AddEdge(new Edge(Rel.ImplementsMethod, implNode.Pk, memberNode.Pk, new Dictionary<string, string>()));
            }
        }
    }
}
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter InterfaceImplementationExtractorTests`
Expected: PASS.

- [ ] **Step 5: 커밋**

```bash
git add src/CodeWiki/Extraction/InterfaceImplementationExtractor.cs src/CodeWiki.Tests/InterfaceImplementationExtractorTests.cs
git commit -m "feat(codewiki): add InterfaceImplementationExtractor (IMPLEMENTS_METHOD)"
```

---

## Task 11: CommandExtractor (DEFINES_COMMAND / EXECUTES)

**Files:**
- Create: `src/CodeWiki/Extraction/CommandExtractor.cs`
- Test: `src/CodeWiki.Tests/CommandExtractorTests.cs`

- [ ] **Step 1: 실패 테스트 작성**

`src/CodeWiki.Tests/CommandExtractorTests.cs`:
```csharp
using System.Linq;
using CodeWiki.Extraction;
using CodeWiki.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace CodeWiki.Tests;

public class CommandExtractorTests
{
    private static (INamedTypeSymbol, TypeDeclarationSyntax, SemanticModel) VmFrom(string src)
    {
        var (tree, model) = TestCompiler.Compile(src);
        var decl = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(c => c.Identifier.Text == "VM");
        return ((INamedTypeSymbol)model.GetDeclaredSymbol(decl)!, decl, model);
    }

    [Fact]
    public void Links_command_to_handler_method()
    {
        var src = @"
namespace N {
  public class DelegateCommand { public DelegateCommand(System.Action a) { } }
  public class VM {
    public DelegateCommand SearchCommand { get; }
    public VM() { SearchCommand = new DelegateCommand(ExecuteSearch); }
    void ExecuteSearch() { }
  }
}";
        var (sym, decl, model) = VmFrom(src);
        var graph = new Graph();

        new CommandExtractor().Extract(sym, decl, model, graph);

        var cmdPk = Pk.Of("N.VM.SearchCommand");
        Assert.Contains(graph.Edges, e => e.Type == Rel.DefinesCommand && e.ToPk == cmdPk);
        Assert.Contains(graph.Edges, e => e.Type == Rel.Executes && e.FromPk == cmdPk);
    }

    [Fact]
    public void Links_command_with_this_qualified_assignment()
    {
        var src = @"
namespace N {
  public class DelegateCommand { public DelegateCommand(System.Action a) { } }
  public class VM {
    public DelegateCommand SaveCommand { get; }
    public VM() { this.SaveCommand = new DelegateCommand(ExecuteSave); }
    void ExecuteSave() { }
  }
}";
        var (sym, decl, model) = VmFrom(src);
        var graph = new Graph();

        new CommandExtractor().Extract(sym, decl, model, graph);

        Assert.Contains(graph.Edges, e => e.Type == Rel.Executes && e.FromPk == Pk.Of("N.VM.SaveCommand"));
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter CommandExtractorTests`
Expected: FAIL — 타입 없음.

- [ ] **Step 3: 구현 (strazh GetCommands 이식)**

`src/CodeWiki/Extraction/CommandExtractor.cs`:
```csharp
using CodeWiki.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeWiki.Extraction;

/// <summary>*Command 타입 객체 생성에서 Command 멤버명과 핸들러 메서드를 연결
/// (DEFINES_COMMAND: 소유타입→Command, EXECUTES: Command→핸들러).</summary>
public sealed class CommandExtractor : ITypeExtractor
{
    public void Extract(INamedTypeSymbol symbol, TypeDeclarationSyntax declaration, SemanticModel model, Graph graph)
    {
        var ownerFullName = (symbol.ContainingNamespace?.ToString() ?? "") + "." + symbol.Name;
        var ownerNode = SymbolNodes.Class(symbol);

        foreach (var creation in declaration.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            if (!creation.Type.ToString().Contains("Command")) continue;

            string? commandName = creation.Ancestors()
                .OfType<AssignmentExpressionSyntax>()
                .Select(a => a.Left switch
                {
                    IdentifierNameSyntax id => id.Identifier.Text,
                    MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                    _ => (string?)null
                })
                .FirstOrDefault(n => n != null);
            commandName ??= creation.Ancestors().OfType<PropertyDeclarationSyntax>().FirstOrDefault()?.Identifier.Text
                         ?? creation.Ancestors().OfType<VariableDeclaratorSyntax>().FirstOrDefault()?.Identifier.Text;
            if (commandName is null) continue;

            var commandNode = SymbolNodes.Command(ownerFullName, commandName);
            graph.AddNode(ownerNode);
            graph.AddNode(commandNode);
            graph.AddEdge(new Edge(Rel.DefinesCommand, ownerNode.Pk, commandNode.Pk, new Dictionary<string, string>()));

            var firstArg = creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
            if (firstArg is null) continue;
            var info = model.GetSymbolInfo(firstArg);
            if ((info.Symbol ?? info.CandidateSymbols.FirstOrDefault()) is IMethodSymbol handler)
            {
                var handlerNode = SymbolNodes.Method(handler);
                graph.AddNode(handlerNode);
                graph.AddEdge(new Edge(Rel.Executes, commandNode.Pk, handlerNode.Pk, new Dictionary<string, string>()));
            }
        }
    }
}
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter CommandExtractorTests`
Expected: PASS (2 tests).

- [ ] **Step 5: 커밋**

```bash
git add src/CodeWiki/Extraction/CommandExtractor.cs src/CodeWiki.Tests/CommandExtractorTests.cs
git commit -m "feat(codewiki): add CommandExtractor (DEFINES_COMMAND/EXECUTES)"
```

---

## Task 12: TypeUsageExtractor (USES_TYPE)

**Files:**
- Create: `src/CodeWiki/Extraction/TypeUsageExtractor.cs`
- Test: `src/CodeWiki.Tests/TypeUsageExtractorTests.cs`

- [ ] **Step 1: 실패 테스트 작성**

`src/CodeWiki.Tests/TypeUsageExtractorTests.cs`:
```csharp
using System.Linq;
using CodeWiki.Extraction;
using CodeWiki.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace CodeWiki.Tests;

public class TypeUsageExtractorTests
{
    [Fact]
    public void Emits_USES_TYPE_for_param_and_return_domain_types()
    {
        var src = @"
namespace N {
  public class Order { }
  public class Receipt { }
  public class Svc { public Receipt Make(Order o) => null; }
}";
        var (tree, model) = TestCompiler.Compile(src);
        var decl = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(c => c.Identifier.Text == "Svc");
        var symbol = (INamedTypeSymbol)model.GetDeclaredSymbol(decl)!;
        var graph = new Graph();

        new TypeUsageExtractor().Extract(symbol, decl, model, graph);

        var methodPk = Pk.Of("N.Svc.Make", "N.Order o", "N.Receipt");
        Assert.Contains(graph.Edges, e => e.Type == Rel.UsesType && e.FromPk == methodPk && e.ToPk == Pk.Of("N.Order"));
        Assert.Contains(graph.Edges, e => e.Type == Rel.UsesType && e.FromPk == methodPk && e.ToPk == Pk.Of("N.Receipt"));
    }

    [Fact]
    public void Skips_framework_types()
    {
        var src = "namespace N { public class Svc { public string Hi(int x) => null; } }";
        var (tree, model) = TestCompiler.Compile(src);
        var decl = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var symbol = (INamedTypeSymbol)model.GetDeclaredSymbol(decl)!;
        var graph = new Graph();

        new TypeUsageExtractor().Extract(symbol, decl, model, graph);

        Assert.DoesNotContain(graph.Edges, e => e.Type == Rel.UsesType);
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter TypeUsageExtractorTests`
Expected: FAIL — 타입 없음.

- [ ] **Step 3: 구현 (strazh GetTypeUsages + IsDomainType 이식)**

`src/CodeWiki/Extraction/TypeUsageExtractor.cs`:
```csharp
using CodeWiki.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeWiki.Extraction;

/// <summary>메서드 파라미터/반환 타입 중 도메인 타입(System/Microsoft 제외)을 USES_TYPE으로.</summary>
public sealed class TypeUsageExtractor : ITypeExtractor
{
    public void Extract(INamedTypeSymbol symbol, TypeDeclarationSyntax declaration, SemanticModel model, Graph graph)
    {
        foreach (var methodDecl in declaration.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (model.GetDeclaredSymbol(methodDecl) is not IMethodSymbol ms) continue;
            var methodNode = SymbolNodes.Method(ms);

            foreach (var p in ms.Parameters)
                Emit(graph, methodNode, p.Type);
            Emit(graph, methodNode, ms.ReturnType);
        }
    }

    private static void Emit(Graph graph, Node methodNode, ITypeSymbol? type)
    {
        if (!IsDomainType(type, out var named)) return;
        var typeNode = SymbolNodes.OfKind(named);
        graph.AddNode(methodNode);
        graph.AddNode(typeNode);
        graph.AddEdge(new Edge(Rel.UsesType, methodNode.Pk, typeNode.Pk, new Dictionary<string, string>()));
    }

    /// <summary>Class/Interface이면서 System*/Microsoft* 네임스페이스가 아닌 것만 도메인 타입.</summary>
    private static bool IsDomainType(ITypeSymbol? type, out INamedTypeSymbol named)
    {
        named = (type as INamedTypeSymbol)!;
        if (named is null) return false;
        if (named.TypeKind != TypeKind.Class && named.TypeKind != TypeKind.Interface) return false;
        var ns = named.ContainingNamespace?.ToString() ?? "";
        return !ns.StartsWith("System") && !ns.StartsWith("Microsoft");
    }
}
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter TypeUsageExtractorTests`
Expected: PASS (2 tests).

- [ ] **Step 5: 커밋**

```bash
git add src/CodeWiki/Extraction/TypeUsageExtractor.cs src/CodeWiki.Tests/TypeUsageExtractorTests.cs
git commit -m "feat(codewiki): add TypeUsageExtractor (USES_TYPE)"
```

---

## Task 13: RepositoryUsageExtractor (USES)

**Files:**
- Create: `src/CodeWiki/Extraction/RepositoryUsageExtractor.cs`
- Test: `src/CodeWiki.Tests/RepositoryUsageExtractorTests.cs`

- [ ] **Step 1: 실패 테스트 작성**

`src/CodeWiki.Tests/RepositoryUsageExtractorTests.cs`:
```csharp
using System.Linq;
using CodeWiki.Extraction;
using CodeWiki.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace CodeWiki.Tests;

public class RepositoryUsageExtractorTests
{
    [Fact]
    public void Links_method_to_entity_via_repository_field()
    {
        var src = @"
namespace N {
  public interface IRepository<T> { }
  public class Order { }
  public class OrderService {
    private readonly IRepository<Order> _orders;
    public OrderService(IRepository<Order> orders) { _orders = orders; }
    public void Search() { var x = _orders; }
  }
}";
        var (tree, model) = TestCompiler.Compile(src);
        var decl = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(c => c.Identifier.Text == "OrderService");
        var symbol = (INamedTypeSymbol)model.GetDeclaredSymbol(decl)!;
        var graph = new Graph();

        new RepositoryUsageExtractor().Extract(symbol, decl, model, graph);

        var methodPk = Pk.Of("N.OrderService.Search", "", "void");
        Assert.Contains(graph.Edges, e => e.Type == Rel.Uses && e.FromPk == methodPk && e.ToPk == Pk.Of("N.Order"));
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter RepositoryUsageExtractorTests`
Expected: FAIL — 타입 없음.

- [ ] **Step 3: 구현 (strazh GetRepositoryUsages 이식)**

`src/CodeWiki/Extraction/RepositoryUsageExtractor.cs`:
```csharp
using CodeWiki.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeWiki.Extraction;

/// <summary>메서드 본문이 참조하는 IRepository&lt;T&gt; 필드의 엔티티 T를 USES로 연결.</summary>
public sealed class RepositoryUsageExtractor : ITypeExtractor
{
    public void Extract(INamedTypeSymbol symbol, TypeDeclarationSyntax declaration, SemanticModel model, Graph graph)
    {
        foreach (var methodDecl in declaration.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (model.GetDeclaredSymbol(methodDecl) is not IMethodSymbol ms) continue;
            var methodNode = SymbolNodes.Method(ms);
            var seen = new HashSet<string>();

            foreach (var id in methodDecl.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (model.GetSymbolInfo(id).Symbol is not IFieldSymbol f) continue;
                if (f.Type is not INamedTypeSymbol nt) continue;
                if (!nt.Name.Contains("Repository") || nt.TypeArguments.Length != 1) continue;
                if (nt.TypeArguments[0] is not INamedTypeSymbol entity) continue;

                var entityNode = SymbolNodes.OfKind(entity);
                if (!seen.Add(entityNode.FullName)) continue;
                graph.AddNode(methodNode);
                graph.AddNode(entityNode);
                graph.AddEdge(new Edge(Rel.Uses, methodNode.Pk, entityNode.Pk, new Dictionary<string, string>()));
            }
        }
    }
}
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter RepositoryUsageExtractorTests`
Expected: PASS.

- [ ] **Step 5: 커밋**

```bash
git add src/CodeWiki/Extraction/RepositoryUsageExtractor.cs src/CodeWiki.Tests/RepositoryUsageExtractorTests.cs
git commit -m "feat(codewiki): add RepositoryUsageExtractor (USES)"
```

---

## Task 14: DiRegistrationExtractor (REGISTERS, Tree 스코프)

**Files:**
- Create: `src/CodeWiki/Extraction/DiRegistrationExtractor.cs`
- Test: `src/CodeWiki.Tests/DiRegistrationExtractorTests.cs`

- [ ] **Step 1: 실패 테스트 작성**

`src/CodeWiki.Tests/DiRegistrationExtractorTests.cs`:
```csharp
using System.Linq;
using CodeWiki.Extraction;
using CodeWiki.Model;
using Xunit;

namespace CodeWiki.Tests;

public class DiRegistrationExtractorTests
{
    [Fact]
    public void Extracts_interface_impl_and_lifetime()
    {
        var src = @"
namespace N {
  public interface IOrderService { }
  public class OrderService : IOrderService { }
  public interface IServiceCollection { }
  public static class Reg {
    public static void AddScoped<TI, TImpl>(this IServiceCollection s) { }
    public static void Configure(IServiceCollection services) { services.AddScoped<IOrderService, OrderService>(); }
  }
}";
        var (tree, model) = TestCompiler.Compile(src);
        var graph = new Graph();

        new DiRegistrationExtractor().Extract(tree, model, graph);

        var edge = Assert.Single(graph.Edges.Where(e => e.Type == Rel.Registers));
        Assert.Equal(Pk.Of("N.IOrderService"), edge.FromPk);
        Assert.Equal(Pk.Of("N.OrderService"), edge.ToPk);
        Assert.Equal("Scoped", edge.Props["lifetime"]);
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter DiRegistrationExtractorTests`
Expected: FAIL — 타입 없음.

- [ ] **Step 3: 구현 (strazh GetDiRegistrations 이식)**

`src/CodeWiki/Extraction/DiRegistrationExtractor.cs`:
```csharp
using CodeWiki.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeWiki.Extraction;

/// <summary>DI 등록 호출 X&lt;I,Impl&gt;()에서 인터페이스→구현 + lifetime을 REGISTERS로. 구문트리 1회 실행.</summary>
public sealed class DiRegistrationExtractor
{
    private static readonly Dictionary<string, string> RegisterMethods = new()
    {
        ["AddScoped"] = "Scoped", ["AddSingleton"] = "Singleton", ["AddTransient"] = "Transient",
        ["RegisterScoped"] = "Scoped", ["RegisterSingleton"] = "Singleton", ["Register"] = "Transient",
    };

    public void Extract(SyntaxTree tree, SemanticModel model, Graph graph)
    {
        foreach (var inv in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            GenericNameSyntax? generic = inv.Expression switch
            {
                MemberAccessExpressionSyntax ma => ma.Name as GenericNameSyntax,
                GenericNameSyntax g => g,
                _ => null,
            };
            if (generic is null) continue;
            if (!RegisterMethods.TryGetValue(generic.Identifier.Text, out var lifetime)) continue;

            var typeArgs = generic.TypeArgumentList.Arguments;
            if (typeArgs.Count != 2) continue;
            if (model.GetSymbolInfo(typeArgs[0]).Symbol is not INamedTypeSymbol ifaceSym) continue;
            if (model.GetSymbolInfo(typeArgs[1]).Symbol is not INamedTypeSymbol implSym) continue;
            if (ifaceSym.TypeKind != TypeKind.Interface) continue;

            var ifaceNode = SymbolNodes.Interface(ifaceSym);
            var implNode = SymbolNodes.Class(implSym);
            graph.AddNode(ifaceNode);
            graph.AddNode(implNode);
            graph.AddEdge(new Edge(Rel.Registers, ifaceNode.Pk, implNode.Pk,
                new Dictionary<string, string> { ["lifetime"] = lifetime }));
        }
    }
}
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter DiRegistrationExtractorTests`
Expected: PASS.

- [ ] **Step 5: 커밋**

```bash
git add src/CodeWiki/Extraction/DiRegistrationExtractor.cs src/CodeWiki.Tests/DiRegistrationExtractorTests.cs
git commit -m "feat(codewiki): add DiRegistrationExtractor (REGISTERS)"
```

---

## Task 15: ViewModelLinker (BINDS_TO, 후처리)

**Files:**
- Create: `src/CodeWiki/Extraction/ViewModelLinker.cs`
- Test: `src/CodeWiki.Tests/ViewModelLinkerTests.cs`

- [ ] **Step 1: 실패 테스트 작성**

`src/CodeWiki.Tests/ViewModelLinkerTests.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using CodeWiki.Extraction;
using CodeWiki.Model;
using Xunit;

namespace CodeWiki.Tests;

public class ViewModelLinkerTests
{
    [Fact]
    public void Links_view_to_viewmodel_by_naming_convention()
    {
        var classes = new List<Node>
        {
            new(Labels.Class, Pk.Of("App.Views.SearchOrderView"), "SearchOrderView", "App.Views.SearchOrderView", new Dictionary<string, string>(), new[] { "View" }),
            new(Labels.Class, Pk.Of("App.ViewModels.SearchOrderViewModel"), "SearchOrderViewModel", "App.ViewModels.SearchOrderViewModel", new Dictionary<string, string>(), new[] { "ViewModel" }),
            new(Labels.Class, Pk.Of("App.Other.Unrelated"), "Unrelated", "App.Other.Unrelated", new Dictionary<string, string>(), System.Array.Empty<string>()),
        };
        var graph = new Graph();

        new ViewModelLinker().Link(classes, graph);

        var edge = Assert.Single(graph.Edges.Where(e => e.Type == Rel.BindsTo));
        Assert.Equal(Pk.Of("App.Views.SearchOrderView"), edge.FromPk);
        Assert.Equal(Pk.Of("App.ViewModels.SearchOrderViewModel"), edge.ToPk);
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter ViewModelLinkerTests`
Expected: FAIL — 타입 없음.

- [ ] **Step 3: 구현 (strazh LinkViewsToViewModels 이식 — Node 기반)**

`src/CodeWiki/Extraction/ViewModelLinker.cs`:
```csharp
using CodeWiki.Model;

namespace CodeWiki.Extraction;

/// <summary>이름 컨벤션 XView → XViewModel로 View와 ViewModel을 BINDS_TO로 연결. 솔루션 후처리 1회.</summary>
public sealed class ViewModelLinker
{
    public void Link(IReadOnlyCollection<Node> classNodes, Graph graph)
    {
        var byName = classNodes
            .GroupBy(c => c.Name)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var view in classNodes)
        {
            if (!view.Name.EndsWith("View") || view.Name.EndsWith("ViewModel")) continue;
            var vmName = view.Name + "Model"; // SearchOrderView -> SearchOrderViewModel
            if (byName.TryGetValue(vmName, out var vm))
                graph.AddEdge(new Edge(Rel.BindsTo, view.Pk, vm.Pk, new Dictionary<string, string>()));
        }
    }
}
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter ViewModelLinkerTests`
Expected: PASS.

- [ ] **Step 5: 커밋**

```bash
git add src/CodeWiki/Extraction/ViewModelLinker.cs src/CodeWiki.Tests/ViewModelLinkerTests.cs
git commit -m "feat(codewiki): add ViewModelLinker (BINDS_TO)"
```

---

## Task 16: StructureExtractor (Solution/Project/Folder/File/Package + DECLARED_AT)

**Files:**
- Create: `src/CodeWiki/Extraction/StructureExtractor.cs`
- Test: `src/CodeWiki.Tests/StructureExtractorTests.cs`

> 구조 노드는 심볼이 아닌 문자열(경로/이름)에서 만들어지므로, 작은 정적 메서드로 구성해 단위 테스트한다.

- [ ] **Step 1: 실패 테스트 작성**

`src/CodeWiki.Tests/StructureExtractorTests.cs`:
```csharp
using System.Linq;
using CodeWiki.Extraction;
using CodeWiki.Model;
using Xunit;

namespace CodeWiki.Tests;

public class StructureExtractorTests
{
    [Fact]
    public void FolderChain_links_file_up_to_root()
    {
        var graph = new Graph();
        // 루트 폴더명 "Proj" 기준, 상대경로 Proj/Sub/Foo.cs
        StructureExtractor.AddFileWithFolders(graph, "Proj", "Proj/Sub/Foo.cs");

        // File -> Sub, Sub -> Proj (INCLUDED_IN)
        Assert.Contains(graph.Edges, e => e.Type == Rel.IncludedIn
            && e.FromPk == Pk.Of("Proj/Sub/Foo.cs") && e.ToPk == Pk.Of("Proj/Sub"));
        Assert.Contains(graph.Edges, e => e.Type == Rel.IncludedIn
            && e.FromPk == Pk.Of("Proj/Sub") && e.ToPk == Pk.Of("Proj"));
    }

    [Fact]
    public void Project_dependencies_and_packages()
    {
        var graph = new Graph();
        StructureExtractor.AddProject(graph, "Order", root: "Order",
            projectRefs: new[] { "Common" },
            packages: new[] { ("Newtonsoft.Json", "13.0.0") });

        Assert.Contains(graph.Edges, e => e.Type == Rel.DependsOn
            && e.FromPk == Pk.Of("Order") && e.ToPk == Pk.Of("Common"));
        Assert.Contains(graph.Edges, e => e.Type == Rel.DependsOn
            && e.FromPk == Pk.Of("Order") && e.ToPk == Pk.Of("Newtonsoft.Json", "13.0.0"));
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter StructureExtractorTests`
Expected: FAIL — 타입 없음.

- [ ] **Step 3: 구현**

`src/CodeWiki/Extraction/StructureExtractor.cs`:
```csharp
using CodeWiki.Model;

namespace CodeWiki.Extraction;

/// <summary>구조 노드/엣지(Solution/Project/Folder/File/Package, INCLUDED_IN/DEPENDS_ON/CONTAINS/DECLARED_AT).
/// 경로/이름 문자열에서 결정론적으로 생성.</summary>
public static class StructureExtractor
{
    private static Node Folder(string fullName, string name) => new(Labels.Folder, Pk.Of(fullName), name, fullName, Empty(), NoRoles());
    private static Node File(string fullName, string name) => new(Labels.File, Pk.Of(fullName), name, fullName, Empty(), NoRoles());
    private static Node Project(string name) => new(Labels.Project, Pk.Of(name), name, name, Empty(), NoRoles());
    private static Node Package(string name, string version) => new(Labels.Package, Pk.Of(name, version), name, name, new Dictionary<string, string> { ["version"] = version }, NoRoles());
    private static Node Solution(string name) => new(Labels.Solution, Pk.Of(name), name, name, Empty(), NoRoles());

    private static Dictionary<string, string> Empty() => new();
    private static string[] NoRoles() => System.Array.Empty<string>();

    /// <summary>상대 파일경로(rootName으로 시작, '/' 구분)를 폴더 체인 + File 노드로 전개해 INCLUDED_IN 연결.</summary>
    public static void AddFileWithFolders(Graph graph, string rootName, string relativePath)
    {
        var fileName = relativePath.Split('/').Last();
        var fileNode = File(relativePath, fileName);
        graph.AddNode(fileNode);

        var segments = relativePath.Split('/');
        Node? prev = null;
        var path = "";
        foreach (var segment in segments)
        {
            if (path.Length == 0) { path = segment; prev = Folder(path, segment); graph.AddNode(prev); continue; }
            if (segment == fileName) { graph.AddEdge(new Edge(Rel.IncludedIn, fileNode.Pk, prev!.Pk, Empty())); return; }

            path = $"{path}/{segment}";
            var cur = Folder(path, segment);
            graph.AddNode(cur);
            graph.AddEdge(new Edge(Rel.IncludedIn, cur.Pk, prev!.Pk, Empty()));
            prev = cur;
        }
    }

    /// <summary>프로젝트 노드 + 루트폴더 INCLUDED_IN + 프로젝트/패키지 DEPENDS_ON.</summary>
    public static void AddProject(Graph graph, string projectName, string root,
        IEnumerable<string> projectRefs, IEnumerable<(string name, string version)> packages)
    {
        var project = Project(projectName);
        var rootFolder = Folder(root, root);
        graph.AddNode(project);
        graph.AddNode(rootFolder);
        graph.AddEdge(new Edge(Rel.IncludedIn, project.Pk, rootFolder.Pk, Empty()));

        foreach (var dep in projectRefs)
        {
            var depNode = Project(dep);
            graph.AddNode(depNode);
            graph.AddEdge(new Edge(Rel.DependsOn, project.Pk, depNode.Pk, Empty()));
        }
        foreach (var (name, version) in packages)
        {
            var pkg = Package(name, version);
            graph.AddNode(pkg);
            graph.AddEdge(new Edge(Rel.DependsOn, project.Pk, pkg.Pk, Empty()));
        }
    }

    /// <summary>솔루션 노드 + 루트폴더 INCLUDED_IN + 프로젝트 CONTAINS.</summary>
    public static void AddSolution(Graph graph, string solutionName, string root, IEnumerable<string> projectNames)
    {
        var solution = Solution(solutionName);
        var rootFolder = Folder(root, root);
        graph.AddNode(solution);
        graph.AddNode(rootFolder);
        graph.AddEdge(new Edge(Rel.IncludedIn, solution.Pk, rootFolder.Pk, Empty()));
        foreach (var p in projectNames)
        {
            var project = Project(p);
            graph.AddNode(project);
            graph.AddEdge(new Edge(Rel.Contains, solution.Pk, project.Pk, Empty()));
        }
    }

    /// <summary>타입 → 선언 파일 DECLARED_AT. (타입 노드는 호출측에서 추가됨)</summary>
    public static void AddDeclaredAt(Graph graph, Node typeNode, string relativeFilePath)
    {
        var fileName = relativeFilePath.Split('/').Last();
        var fileNode = File(relativeFilePath, fileName);
        graph.AddNode(fileNode);
        graph.AddEdge(new Edge(Rel.DeclaredAt, typeNode.Pk, fileNode.Pk, Empty()));
    }
}
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter StructureExtractorTests`
Expected: PASS (2 tests).

- [ ] **Step 5: 커밋**

```bash
git add src/CodeWiki/Extraction/StructureExtractor.cs src/CodeWiki.Tests/StructureExtractorTests.cs
git commit -m "feat(codewiki): add StructureExtractor (folders/project/package/declared-at)"
```

---

## Task 17: GraphSerializer (Graph ↔ NDJSON)

**Files:**
- Create: `src/CodeWiki/Storage/GraphSerializer.cs`
- Test: `src/CodeWiki.Tests/SerializerTests.cs`

> NDJSON 포맷: 한 줄당 노드 또는 엣지 하나. `kind` 판별자. 노드는 라벨[주+역할]·props 포함.

- [ ] **Step 1: 실패 테스트 작성**

`src/CodeWiki.Tests/SerializerTests.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using CodeWiki.Model;
using CodeWiki.Storage;
using Xunit;

namespace CodeWiki.Tests;

public class SerializerTests
{
    [Fact]
    public void Roundtrip_preserves_nodes_and_edges()
    {
        var g = new Graph();
        g.AddNode(new Node(Labels.Class, "p1", "Order", "N.Order",
            new Dictionary<string, string> { ["modifiers"] = "public" }, new[] { "Entity" }));
        g.AddNode(new Node(Labels.Method, "p2", "Save", "N.Order.Save", new Dictionary<string, string>(), System.Array.Empty<string>()));
        g.AddEdge(new Edge(Rel.Have, "p1", "p2", new Dictionary<string, string>()));

        var lines = GraphSerializer.Write(g).ToList();
        var loaded = GraphSerializer.Read(lines);

        Assert.Equal(2, loaded.Nodes.Count);
        Assert.Single(loaded.Edges);
        var order = loaded.Nodes.Single(n => n.Pk == "p1");
        Assert.Equal("public", order.Props["modifiers"]);
        Assert.Equal(new[] { "Entity" }, order.Roles);
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter SerializerTests`
Expected: FAIL — `GraphSerializer` 없음.

- [ ] **Step 3: 구현**

`src/CodeWiki/Storage/GraphSerializer.cs`:
```csharp
using System.Text.Json;
using CodeWiki.Model;

namespace CodeWiki.Storage;

/// <summary>Graph ↔ NDJSON. 한 줄당 노드/엣지 하나(kind 판별자). 적재·디버그·재시도용 중립 IR.</summary>
public static class GraphSerializer
{
    public static IEnumerable<string> Write(Graph graph)
    {
        foreach (var n in graph.Nodes)
            yield return JsonSerializer.Serialize(new NodeDto("node", n.Label, n.Pk, n.Name, n.FullName, n.Props, n.Roles));
        foreach (var e in graph.Edges)
            yield return JsonSerializer.Serialize(new EdgeDto("edge", e.Type, e.FromPk, e.ToPk, e.Props));
    }

    public static Graph Read(IEnumerable<string> lines)
    {
        var graph = new Graph();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            var kind = doc.RootElement.GetProperty("kind").GetString();
            if (kind == "node")
            {
                var dto = JsonSerializer.Deserialize<NodeDto>(line)!;
                graph.AddNode(new Node(dto.Label, dto.Pk, dto.Name, dto.FullName, dto.Props, dto.Roles));
            }
            else
            {
                var dto = JsonSerializer.Deserialize<EdgeDto>(line)!;
                graph.AddEdge(new Edge(dto.Type, dto.FromPk, dto.ToPk, dto.Props));
            }
        }
        return graph;
    }

    public static void WriteFile(Graph graph, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllLines(path, Write(graph));
    }

    public static Graph ReadFile(string path) => Read(File.ReadLines(path));

    private sealed record NodeDto(string kind, string Label, string Pk, string Name, string FullName,
        IReadOnlyDictionary<string, string> Props, IReadOnlyList<string> Roles);
    private sealed record EdgeDto(string kind, string Type, string FromPk, string ToPk,
        IReadOnlyDictionary<string, string> Props);
}
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter SerializerTests`
Expected: PASS.

- [ ] **Step 5: 커밋**

```bash
git add src/CodeWiki/Storage/GraphSerializer.cs src/CodeWiki.Tests/SerializerTests.cs
git commit -m "feat(codewiki): add GraphSerializer (NDJSON roundtrip)"
```

---

## Task 18: Neo4jLoader (Cypher 빌더 + 적재) + Healthcheck

**Files:**
- Create: `src/CodeWiki/Storage/Neo4jLoader.cs`, `src/CodeWiki/Storage/Neo4jHealthcheck.cs`
- Test: `src/CodeWiki.Tests/Neo4jLoaderTests.cs`

> 라이브 Neo4j 없이 **Cypher 문자열 빌더**를 단위 테스트(strazh BatchLoaderRow 대응). 실제 적재는 Task 22 동치 검증에서 라이브로 확인.

- [ ] **Step 1: 실패 테스트 작성**

`src/CodeWiki.Tests/Neo4jLoaderTests.cs`:
```csharp
using CodeWiki.Storage;
using Xunit;

namespace CodeWiki.Tests;

public class Neo4jLoaderTests
{
    [Fact]
    public void Node_merge_cypher_sets_props_and_identity()
    {
        var cypher = Neo4jLoader.NodeMergeCypher("Class");
        Assert.Contains("MERGE (n:Class { pk: row.pk })", cypher);
        Assert.Contains("SET n += row.props", cypher);
        Assert.Contains("n.name = row.name", cypher);
        Assert.Contains("n.fullName = row.fullName", cypher);
    }

    [Fact]
    public void Role_label_cypher_sets_secondary_label()
    {
        var cypher = Neo4jLoader.RoleLabelCypher("ViewModel");
        Assert.Contains("MATCH (n { pk: pk })", cypher);
        Assert.Contains("SET n:ViewModel", cypher);
    }

    [Fact]
    public void Edge_merge_cypher_joins_endpoints_and_sets_rel_props()
    {
        var cypher = Neo4jLoader.EdgeMergeCypher("Command", "Method", "EXECUTES");
        Assert.Contains("MERGE (a:Command { pk: row.from })", cypher);
        Assert.Contains("MERGE (b:Method { pk: row.to })", cypher);
        Assert.Contains("MERGE (a)-[r:EXECUTES]->(b)", cypher);
        Assert.Contains("SET r += row.props", cypher);
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter Neo4jLoaderTests`
Expected: FAIL — `Neo4jLoader` 없음.

- [ ] **Step 3: 구현**

`src/CodeWiki/Storage/Neo4jLoader.cs`:
```csharp
using CodeWiki.Model;
using Neo4j.Driver;

namespace CodeWiki.Storage;

/// <summary>Graph → Neo4j 단일 적재 경로. Cypher 생성은 오직 여기. 노드 MERGE → 역할 라벨 → 엣지 MERGE.</summary>
public sealed class Neo4jLoader
{
    private readonly int _batchSize;
    public Neo4jLoader(int batchSize = 5000) => _batchSize = batchSize;

    public static string NodeMergeCypher(string label) =>
        $"UNWIND $batch AS row " +
        $"MERGE (n:{label} {{ pk: row.pk }}) " +
        $"SET n += row.props, n.name = row.name, n.fullName = row.fullName";

    public static string RoleLabelCypher(string role) =>
        $"UNWIND $pks AS pk MATCH (n {{ pk: pk }}) SET n:{role}";

    public static string EdgeMergeCypher(string fromLabel, string toLabel, string relType) =>
        $"UNWIND $batch AS row " +
        $"MERGE (a:{fromLabel} {{ pk: row.from }}) " +
        $"MERGE (b:{toLabel} {{ pk: row.to }}) " +
        $"MERGE (a)-[r:{relType}]->(b) SET r += row.props";

    public async Task LoadAsync(IAsyncSession session, Graph graph, bool wipe)
    {
        if (wipe) await session.RunAsync("MATCH (n) DETACH DELETE n;");

        var labelByPk = graph.Nodes.ToDictionary(n => n.Pk, n => n.Label);

        // 1) 노드 — 주 라벨별 그룹 배치 MERGE
        foreach (var group in graph.Nodes.GroupBy(n => n.Label))
        {
            var cypher = NodeMergeCypher(group.Key);
            foreach (var chunk in group.Chunk(_batchSize))
            {
                var batch = chunk.Select(n => (object)new Dictionary<string, object>
                {
                    ["pk"] = n.Pk, ["name"] = n.Name, ["fullName"] = n.FullName,
                    ["props"] = n.Props.ToDictionary(p => p.Key, p => (object)p.Value),
                }).ToList();
                await session.RunAsync(cypher, new Dictionary<string, object> { ["batch"] = batch });
            }
        }

        // 2) 역할 라벨 — 역할별 pk 묶음 SET
        foreach (var group in graph.Nodes.Where(n => n.Roles.Count > 0)
                     .SelectMany(n => n.Roles.Select(r => (role: r, pk: n.Pk)))
                     .GroupBy(x => x.role))
        {
            var pks = group.Select(x => (object)x.pk).Distinct().ToList();
            await session.RunAsync(RoleLabelCypher(group.Key), new Dictionary<string, object> { ["pks"] = pks });
        }

        // 3) 엣지 — (fromLabel,toLabel,type)별 그룹 배치 MERGE
        foreach (var group in graph.Edges.GroupBy(e => (
                     from: labelByPk.GetValueOrDefault(e.FromPk),
                     to: labelByPk.GetValueOrDefault(e.ToPk),
                     e.Type)))
        {
            if (group.Key.from is null || group.Key.to is null) continue; // 끝점 노드 누락 방어
            var cypher = EdgeMergeCypher(group.Key.from, group.Key.to, group.Key.Type);
            foreach (var chunk in group.Chunk(_batchSize))
            {
                var batch = chunk.Select(e => (object)new Dictionary<string, object>
                {
                    ["from"] = e.FromPk, ["to"] = e.ToPk,
                    ["props"] = e.Props.ToDictionary(p => p.Key, p => (object)p.Value),
                }).ToList();
                await session.RunAsync(cypher, new Dictionary<string, object> { ["batch"] = batch });
            }
        }
    }
}
```

`src/CodeWiki/Storage/Neo4jHealthcheck.cs`:
```csharp
using Neo4j.Driver;

namespace CodeWiki.Storage;

/// <summary>적재 전 Neo4j 가용성 확인.</summary>
public static class Neo4jHealthcheck
{
    public static async Task<bool> IsReadyAsync(string uri, string user, string password, string database)
    {
        try
        {
            var driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));
            await using var session = driver.AsyncSession(o => o.WithDatabase(database));
            await session.RunAsync("RETURN 1");
            await driver.DisposeAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj --filter Neo4jLoaderTests`
Expected: PASS (3 tests).

- [ ] **Step 5: 커밋**

```bash
git add src/CodeWiki/Storage/Neo4jLoader.cs src/CodeWiki/Storage/Neo4jHealthcheck.cs src/CodeWiki.Tests/Neo4jLoaderTests.cs
git commit -m "feat(codewiki): add Neo4jLoader (single load path) + Healthcheck"
```

---

## Task 19: WorkspaceBuilder (Buildalyzer + AdhocWorkspace, 불변식 캡슐화)

**Files:**
- Create: `src/CodeWiki/Pipeline/WorkspaceBuilder.cs`

> Buildalyzer는 실제 솔루션이 필요해 단위 테스트하지 않는다(strazh도 안 함). 로직을 얇게 유지하고 불변식을 주석으로 못박는다. 검증은 Task 22.

- [ ] **Step 1: 구현**

`src/CodeWiki/Pipeline/WorkspaceBuilder.cs`:
```csharp
using Buildalyzer;
using Buildalyzer.Environment;
using Buildalyzer.Workspaces;
using Microsoft.Build.Construction;
using Microsoft.CodeAnalysis;

namespace CodeWiki.Pipeline;

/// <summary>솔루션의 모든 프로젝트를 풀빌드해 분석용 Roslyn 워크스페이스를 구성한다.
/// 두 불변식을 한 곳에 가둔다: (1) DesignTime=false 풀빌드, (2) addProjectReferences:false.</summary>
public sealed class WorkspaceBuilder
{
    public sealed record BuiltProject(Project Project, IAnalyzerResult Result);

    public sealed record BuiltSolution(AdhocWorkspace Workspace, IReadOnlyList<BuiltProject> Projects, string SolutionPath);

    public BuiltSolution Build(string solutionPath)
    {
        var manager = new AnalyzerManager(solutionPath);

        // (1) 풀빌드: design-time 빌드면 WPF의 .xaml.cs/ViewModel 소스가 통째로 빈다.
        var results = new List<IAnalyzerResult>();
        foreach (var project in manager.Projects.Values)
        {
            var result = project.Build(new EnvironmentOptions { DesignTime = false }).FirstOrDefault();
            if (result is not null) results.Add(result);
            else Console.WriteLine($"WARN: skipped {project.ProjectFile.Name} - build produced no result.");
        }

        var workspace = new AdhocWorkspace();
        var solutionInfo = SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Default, solutionPath);
        workspace.AddSolution(solutionInfo);

        // 솔루션 파일 순서대로 정렬(strazh와 동일한 추가 순서 보장).
        var order = manager.SolutionFile.ProjectsInOrder.ToList();
        results = results.OrderBy(p => order.FindIndex(g => g.AbsolutePath == p.ProjectFilePath)).ToList();

        // (2) addProjectReferences:false — 각 프로젝트를 자기 전체 문서로 한 번만 추가.
        // true면 앱 프로젝트가 참조 모듈을 문서 0개 스텁으로 선점 → 모듈 통째 누락. (되돌리지 말 것.)
        var built = new List<BuiltProject>();
        foreach (var result in results)
        {
            var project = result.AddToWorkspace(workspace, addProjectReferences: false);
            built.Add(new BuiltProject(project, result));
        }

        Console.WriteLine($"Workspace ready: {built.Count} project(s).");
        return new BuiltSolution(workspace, built, solutionPath);
    }
}
```

- [ ] **Step 2: 빌드 확인**

Run: `dotnet build src/CodeWiki/CodeWiki.csproj -c Release`
Expected: 성공.

- [ ] **Step 3: 커밋**

```bash
git add src/CodeWiki/Pipeline/WorkspaceBuilder.cs
git commit -m "feat(codewiki): add WorkspaceBuilder with build invariants"
```

---

## Task 20: AnalysisPipeline (오케스트레이션)

**Files:**
- Create: `src/CodeWiki/Pipeline/AnalysisPipeline.cs`

> 추출기들을 스코프별로 실행해 Graph를 채운다. 추출기 목록이 "규칙 전부"의 단일 지점.

- [ ] **Step 1: 구현**

`src/CodeWiki/Pipeline/AnalysisPipeline.cs`:
```csharp
using CodeWiki.Extraction;
using CodeWiki.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeWiki.Pipeline;

/// <summary>BuiltSolution → Graph. 스코프별 추출기 실행을 한눈에 보이게 모은다.</summary>
public sealed class AnalysisPipeline
{
    private readonly IReadOnlyList<ITypeExtractor> _typeExtractors = new ITypeExtractor[]
    {
        new InheritanceExtractor(),
        new MethodExtractor(),
        new InterfaceImplementationExtractor(),
        new CommandExtractor(),
        new TypeUsageExtractor(),
        new RepositoryUsageExtractor(),
    };
    private readonly DiRegistrationExtractor _diExtractor = new();
    private readonly ViewModelLinker _viewModelLinker = new();

    public async Task<Graph> RunAsync(WorkspaceBuilder.BuiltSolution solution)
    {
        var graph = new Graph();
        var solutionName = Path.GetFileNameWithoutExtension(solution.SolutionPath);
        var solutionRoot = DirectoryName(solution.SolutionPath);
        var classNodes = new List<Node>();

        StructureExtractor.AddSolution(graph, solutionName, solutionRoot,
            solution.Projects.Select(p => ProjectName(p.Project.Name)));

        foreach (var built in solution.Projects)
        {
            var projectName = ProjectName(built.Project.Name);
            var projectRoot = DirectoryName(built.Project.FilePath!);

            StructureExtractor.AddProject(graph, projectName, projectRoot,
                built.Result.ProjectReferences.Select(ProjectName),
                built.Result.PackageReferences.Select(p =>
                    (p.Key, p.Value.Values.FirstOrDefault(v => v.Contains('.')) ?? "none")));

            if (!built.Project.SupportsCompilation) continue;
            var compilation = await built.Project.GetCompilationAsync();
            if (compilation is null) continue;

            foreach (var tree in compilation.SyntaxTrees.Where(t => !t.FilePath.Contains("obj")))
            {
                var model = compilation.GetSemanticModel(tree);
                var root = tree.GetRoot();
                var relativePath = RelativePath(tree.FilePath, projectRoot);

                foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (typeDecl is not (ClassDeclarationSyntax or InterfaceDeclarationSyntax)) continue;
                    if (model.GetDeclaredSymbol(typeDecl) is not INamedTypeSymbol symbol) continue;

                    var roles = RoleClassifier.Classify(symbol);
                    var modifiers = typeDecl.Modifiers.Select(t => t.ValueText).ToArray();
                    var typeNode = symbol.TypeKind == TypeKind.Interface
                        ? SymbolNodes.Interface(symbol, roles, modifiers)
                        : SymbolNodes.Class(symbol, roles, modifiers);

                    graph.AddNode(typeNode);
                    StructureExtractor.AddDeclaredAt(graph, typeNode, relativePath);
                    if (typeNode.Label == Labels.Class) classNodes.Add(typeNode);

                    foreach (var extractor in _typeExtractors)
                        SafeExtract(() => extractor.Extract(symbol, typeDecl, model, graph), typeNode.FullName);
                }

                SafeExtract(() => _diExtractor.Extract(tree, model, graph), tree.FilePath);
            }
        }

        _viewModelLinker.Link(classNodes, graph);
        return graph;
    }

    private static void SafeExtract(Action action, string context)
    {
        try { action(); }
        catch (Exception ex) { Console.WriteLine($"WARN: extractor failed at {context}: {ex.GetType().Name}: {ex.Message}"); }
    }

    private static string ProjectName(string fullName) => Path.GetFileNameWithoutExtension(fullName);
    private static string DirectoryName(string path) => new DirectoryInfo(Path.GetDirectoryName(path)!).Name;

    /// <summary>파일 절대경로를 프로젝트 루트 폴더명부터 시작하는 상대경로('/' 정규화)로.</summary>
    private static string RelativePath(string filePath, string rootName)
    {
        var normalized = filePath.Replace('\\', '/');
        var idx = normalized.IndexOf("/" + rootName + "/", StringComparison.Ordinal);
        return idx < 0 ? normalized : normalized[(idx + 1)..];
    }
}
```

> 참고: `RelativePath`/`DirectoryName`은 strazh의 `GetRoot`(파일 경로에서 부모 폴더명 추출) 의도를 잇는다. Task 22에서 실제 경로 형태를 확인해 미세 조정한다.

- [ ] **Step 2: 빌드 확인**

Run: `dotnet build src/CodeWiki/CodeWiki.csproj -c Release`
Expected: 성공.

- [ ] **Step 3: 커밋**

```bash
git add src/CodeWiki/Pipeline/AnalysisPipeline.cs
git commit -m "feat(codewiki): add AnalysisPipeline orchestration"
```

---

## Task 21: Program CLI (extract / load)

**Files:**
- Create: `src/CodeWiki/Program.cs`

- [ ] **Step 1: 구현**

`src/CodeWiki/Program.cs`:
```csharp
using System.CommandLine;
using CodeWiki.Pipeline;
using CodeWiki.Storage;
using Neo4j.Driver;

var root = new RootCommand("CodeWiki — Roslyn → Neo4j 코드 지식 그래프 ETL");

// extract: 솔루션 분석 → NDJSON
var solutionOpt = new Option<string>("--solution", "분석할 .sln 절대경로") { IsRequired = true };
solutionOpt.AddAlias("-s");
var outOpt = new Option<string>("--out", () => "out/graph.ndjson", "NDJSON 출력 경로");
outOpt.AddAlias("-o");

var extract = new Command("extract", "솔루션을 분석해 NDJSON으로 덤프(Neo4j 불필요)") { solutionOpt, outOpt };
extract.SetHandler(async (solution, outPath) =>
{
    var builtSolution = new WorkspaceBuilder().Build(solution);
    var graph = await new AnalysisPipeline().RunAsync(builtSolution);
    GraphSerializer.WriteFile(graph, outPath);
    builtSolution.Workspace.Dispose();
    Console.WriteLine($"Wrote {graph.Nodes.Count} nodes, {graph.Edges.Count} edges → {outPath}");
}, solutionOpt, outOpt);

// load: NDJSON → Neo4j
var credOpt = new Option<string>("--credentials", "db:user:password") { IsRequired = true };
credOpt.AddAlias("-c");
var ndjsonOpt = new Option<string>("--ndjson", "적재할 NDJSON 경로") { IsRequired = true };
var wipeOpt = new Option<bool>("--wipe", () => true, "적재 전 그래프 전체 삭제");
var uriOpt = new Option<string>("--uri", () => "neo4j://localhost:7687", "Neo4j URI");

var load = new Command("load", "NDJSON을 Neo4j에 배치 적재") { credOpt, ndjsonOpt, wipeOpt, uriOpt };
load.SetHandler(async (creds, ndjson, wipe, uri) =>
{
    var parts = creds.Split(':');
    var (db, user, pass) = (parts[0], parts[1], parts[2]);

    if (!await Neo4jHealthcheck.IsReadyAsync(uri, user, pass, db))
    {
        Console.WriteLine("적재 실패: 사용 가능한 Neo4j 인스턴스가 없습니다.");
        return;
    }

    var graph = GraphSerializer.ReadFile(ndjson);
    var driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, pass));
    await using var session = driver.AsyncSession(o => o.WithDatabase(db));
    await new Neo4jLoader().LoadAsync(session, graph, wipe);
    await driver.DisposeAsync();
    Console.WriteLine($"Loaded {graph.Nodes.Count} nodes, {graph.Edges.Count} edges into \"{db}\".");
}, credOpt, ndjsonOpt, wipeOpt, uriOpt);

root.AddCommand(extract);
root.AddCommand(load);
return await root.InvokeAsync(args);
```

- [ ] **Step 2: 빌드 확인**

Run: `dotnet build src/CodeWiki/CodeWiki.csproj -c Release`
Expected: 성공.

- [ ] **Step 3: 전체 테스트 실행 (회귀 확인)**

Run: `dotnet test src/CodeWiki.Tests/CodeWiki.Tests.csproj`
Expected: PASS (전체 단위 테스트 통과).

- [ ] **Step 4: 커밋**

```bash
git add src/CodeWiki/Program.cs
git commit -m "feat(codewiki): add CLI (extract/load verbs)"
```

---

## Task 22: 동치 검증 (Vanuatu.sln 대상, 완료 기준)

**Files:**
- 수정 가능성: `src/CodeWiki/Pipeline/AnalysisPipeline.cs` (경로/카운트 미세 조정 시)

> 라이브 환경(모든 NuGet 복원·빌드되는 환경 + Neo4j)에서 수행. 기준선은 CLAUDE.md 실측(2026-06-05): 44/44 프로젝트, ~53k 트리플, ViewModel 492, View 351, Command 1199, EXECUTES 1197, BINDS_TO 351, IMPLEMENTS_METHOD 4359, INVOKE 23044, Entity 378.

- [ ] **Step 1: 새 ETL로 추출**

Run:
```powershell
dotnet run --project src/CodeWiki/CodeWiki.csproj -c Release -- `
  extract -s "C:\develop\baw\phase2\baw-phase2-platform\Vanuatu\Vanuatu.sln" -o out/codewiki.ndjson
```
Expected: "Wrote N nodes, M edges". 크래시 없이 완료.

- [ ] **Step 2: 핵심 카운트 확인 (NDJSON 직접 집계)**

Run (PowerShell — 고유 ViewModel 역할 노드 수):
```powershell
(Get-Content out/codewiki.ndjson | Where-Object { $_ -match '"kind":"node"' -and $_ -match '"Roles":\[[^\]]*"ViewModel"' }).Count
```
Expected: ≈ 492 (≈50이면 빈 스텁 함정 #2 재발 — WorkspaceBuilder 점검).

추가 확인(엣지 타입별 카운트 예시):
```powershell
(Get-Content out/codewiki.ndjson | Where-Object { $_ -match '"Type":"IMPLEMENTS_METHOD"' }).Count   # ≈ 4359
(Get-Content out/codewiki.ndjson | Where-Object { $_ -match '"Type":"EXECUTES"' }).Count              # ≈ 1197
(Get-Content out/codewiki.ndjson | Where-Object { $_ -match '"Type":"BINDS_TO"' }).Count              # ≈ 351
```

- [ ] **Step 3: 불일치 시 좁혀서 수정**

카운트가 크게 어긋나면(예: ViewModel ≈50, BINDS_TO ≈28):
- WorkspaceBuilder의 `addProjectReferences:false`·`DesignTime=false` 확인(불변식).
- 경로 정규화(`RelativePath`/`DirectoryName`)가 DECLARED_AT/INCLUDED_IN의 폴더 노드를 strazh와 다르게 만드는지 확인.
- 해당 추출기 단위 테스트로 좁혀 재현 후 수정 → 재실행.

- [ ] **Step 4: Neo4j 적재 + 라이브 검증**

Run:
```powershell
dotnet run --project src/CodeWiki/CodeWiki.csproj -c Release -- `
  load -c "neo4j:neo4j:strazhpass" --ndjson out/codewiki.ndjson --wipe
```
그 후 Neo4j에서 확인:
```cypher
MATCH (n:ViewModel) RETURN count(DISTINCT n);   // ≈ 492
MATCH ()-[r:REGISTERS]->() RETURN r.lifetime, count(*);  // lifetime 채워짐 (함정 #2 검증)
MATCH (n:Method) RETURN count(n);
```
Expected: 역할 라벨·REGISTERS.lifetime이 정상 적재(단일 경로라 누락 불가).

- [ ] **Step 5: strazh NDJSON과 정규화 diff (선택, 동등 증명)**

Run (양쪽을 정렬해 비교 — strazh는 triple 단위, CodeWiki는 node/edge 단위라 엣지 집합 기준 비교):
```powershell
# CodeWiki 엣지를 (type,fromPk,toPk)로 정규화 정렬
Get-Content out/codewiki.ndjson | Where-Object { $_ -match '"kind":"edge"' } | Sort-Object | Set-Content out/codewiki.edges.sorted
```
strazh `out/vanuatu.ndjson`의 엣지와 fullName 기준 교차 비교해 누락/초과 엣지 타입을 식별. 차이가 설명 가능한 수준(예: dedup 방식 차이)인지 확인.

- [ ] **Step 6: 기준선 메모 갱신 + 커밋**

CLAUDE.md 기준선과 일치 확인 후, 조정한 코드가 있으면 커밋:
```bash
git add -A
git commit -m "test(codewiki): verify Vanuatu equivalence (~53k triples, VM≈492)"
```

---

## 완료 후 정리 (별도 판단)

- 동치 검증 통과 후 `strazh/` 제거 여부는 사용자 확인 후 결정(설계 §3). 제거 시 `.mcp.json`/README/CLAUDE.md의 strazh 경로를 CodeWiki로 갱신하는 후속 태스크 필요.
- 다음 sub-project = semantic-injection.md Phase A(L0). 본 구조의 props 확장으로 진행.

---

## Self-Review

- **Spec 커버리지:** 설계 §5.1(모델)→T2~4, §5.2(폴더)→전체, §5.3(추출기 7종)→T8~16, §5.4(불변식)→T19, §5.5(단일 적재)→T17~18, §5.6(CLI)→T21, §7(L0 확장 지점)=props dict로 구조 내재(별도 구현 없음, 의도대로), §8(테스트)→각 Task, §9(동치)→T22. 누락 없음.
- **Placeholder:** 모든 코드 블록 실제 구현. "적절히 처리" 류 없음.
- **타입 일관성:** `Pk.Of`, `Node`/`Edge` 생성자 인자 순서(Label,Pk,Name,FullName,Props,Roles / Type,FromPk,ToPk,Props), `ITypeExtractor.Extract(symbol,declaration,model,graph)`, `SymbolNodes.Method/Class/Interface/OfKind/Command`, `Neo4jLoader.NodeMergeCypher/RoleLabelCypher/EdgeMergeCypher`, `GraphSerializer.Write/Read/WriteFile/ReadFile` — Task 간 시그니처 일치 확인 완료.
- **주의 사항:** `RelativePath`/구조 경로 정규화는 strazh와 미세 차이 가능 → T22 Step 3에서 실측 조정하도록 명시.
