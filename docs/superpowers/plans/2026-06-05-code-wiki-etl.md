# code-wiki ETL Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Strazh(MIT)를 포크해 Vanuatu(WPF + ASP.NET Core) 솔루션의 화면→DB E2E 흐름·영향도·DI 그래프를 Neo4j에 적재하는 자체 Roslyn ETL을 만든다.

**Architecture:** Strazh의 검증된 Buildalyzer 메가 컴파일·도메인 모델을 재사용하고, ① Strazh가 못 하는 신규 추출 4종(메서드 레벨 IMPLEMENTS_METHOD, Command→핸들러 EXECUTES, View→ViewModel BINDS_TO, 제네릭 `IRepository<T>` 필드 사용 USES) + 영향도용 USES_TYPE + DI REGISTERS를 추가하고, ② 노드에 의미적 역할 라벨(다중 라벨)을 부여하고, ③ 출력을 중간 NDJSON으로 분리해 `UNWIND` 배치 로더로 적재(wipe & reload)하며, ④ 읽기전용 MCP + 스키마 쿡북으로 LLM이 질의한다.

**Tech Stack:** C# / .NET 9 (Strazh 호스트), Roslyn `Microsoft.CodeAnalysis.CSharp` 4.13, Buildalyzer 7.1, Neo4j.Driver 5.27, xUnit (신규 테스트 프로젝트), Neo4j(Docker), `mcp-neo4j-cypher`.

**관련 결정·근거:** `docs/vanuatu-wiki-prd.md` (PRD v2). 경계 조인은 공유 인터페이스(A) 1차, 라우트 매칭(B) 후순위. 범위 밖: `CallRawSQL`, `Vanuatu.DTOGenerator`.

---

## File Structure

리포지토리 루트: `c:\develop\Tools\code-wiki`. 포크 대상 코드: `strazh\Strazh\` (프로젝트 `Strazh.csproj`, 솔루션 `strazh\Strazh\Strazh.sln`).

**신규 파일**
- `strazh\Strazh.Tests\Strazh.Tests.csproj` — xUnit 테스트 프로젝트
- `strazh\Strazh.Tests\TestCompiler.cs` — 인메모리 Roslyn 컴파일 헬퍼(추출기 단위 테스트 기반)
- `strazh\Strazh.Tests\*Tests.cs` — 태스크별 테스트
- `strazh\Strazh\Analysis\RoleClassifier.cs` — 역할 라벨 휴리스틱
- `strazh\Strazh\Database\NdjsonWriter.cs` — 트리플 NDJSON 직렬화
- `strazh\Strazh\Database\BatchLoader.cs` — `UNWIND` 배치 적재
- `docs\cookbook\schema-cookbook.md` — LLM용 스키마 범례 + 예제 Cypher
- `docs\mcp\claude_desktop_config.example.json` — 읽기전용 MCP 등록 예시

**수정 파일**
- `strazh\Strazh\Domain\Nodes.cs` — 안정 해시 키, `CommandNode`, 다중 라벨(`Labels`)
- `strazh\Strazh\Domain\Relationships.cs` — `IMPLEMENTS_METHOD`/`EXECUTES`/`BINDS_TO`/`USES`/`USES_TYPE`/`REGISTERS` + 관계 프로퍼티
- `strazh\Strazh\Domain\Triples.cs` — 신규 Triple 타입 + 직렬화에 라벨/관계 프로퍼티 반영
- `strazh\Strazh\Analysis\Extractor.cs` — 신규 추출 메서드
- `strazh\Strazh\Analysis\Analyzer.cs` — 신규 추출 호출 + View↔VM 글로벌 후처리 + 출력 분기
- `strazh\Strazh\Program.cs` — CLI 옵션(`--output ndjson|neo4j`, `--ndjson-path`)

각 태스크는 독립적으로 빌드·테스트되는 변경을 만든다.

---

## Phase 0 — 포크 & 테스트 기반

### Task 1: 포크 가져오기 + xUnit 테스트 프로젝트 + 인메모리 컴파일 헬퍼

**Files:**
- Create: `strazh\Strazh.Tests\Strazh.Tests.csproj`
- Create: `strazh\Strazh.Tests\TestCompiler.cs`
- Create: `strazh\Strazh.Tests\TestCompilerTests.cs`
- Modify: `strazh\Strazh\Strazh.sln` (테스트 프로젝트 추가)

- [ ] **Step 1: 테스트 프로젝트 csproj 생성**

`strazh\Strazh.Tests\Strazh.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.13.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Strazh\Strazh.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: 인메모리 컴파일 헬퍼 작성**

`strazh\Strazh.Tests\TestCompiler.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Strazh.Tests;

public static class TestCompiler
{
    /// <summary>단일 소스를 컴파일해 (구문트리, 의미모델)을 반환.</summary>
    public static (SyntaxTree tree, SemanticModel model) Compile(string source)
    {
        var trees = new[] { CSharpSyntaxTree.ParseText(source, path: "Source.cs") };
        var compilation = CreateCompilation(trees);
        return (trees[0], compilation.GetSemanticModel(trees[0]));
    }

    /// <summary>여러 소스를 한 컴파일에 넣어 각 (구문트리, 의미모델)을 반환.</summary>
    public static IReadOnlyList<(SyntaxTree tree, SemanticModel model)> CompileMany(params string[] sources)
    {
        var trees = sources.Select((s, i) => CSharpSyntaxTree.ParseText(s, path: $"Source{i}.cs")).ToArray();
        var compilation = CreateCompilation(trees);
        return trees.Select(t => (t, compilation.GetSemanticModel(t))).ToList();
    }

    private static CSharpCompilation CreateCompilation(SyntaxTree[] trees)
    {
        var tpa = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        var refs = tpa.Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll"))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
        return CSharpCompilation.Create(
            "TestAssembly",
            trees,
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
```

- [ ] **Step 3: 헬퍼 자체 테스트 작성**

`strazh\Strazh.Tests\TestCompilerTests.cs`:
```csharp
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Strazh.Tests;

public class TestCompilerTests
{
    [Fact]
    public void Resolves_class_symbol_from_in_memory_compilation()
    {
        var (tree, model) = TestCompiler.Compile("namespace N { public class Foo { } }");
        var decl = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var symbol = model.GetDeclaredSymbol(decl);
        Assert.NotNull(symbol);
        Assert.Equal("N.Foo", symbol!.ToString());
    }
}
```

- [ ] **Step 4: 테스트 실행 — 통과 확인**

Run: `dotnet test strazh\Strazh.Tests\Strazh.Tests.csproj`
Expected: PASS (1 passed)

- [ ] **Step 5: 솔루션에 테스트 프로젝트 추가 후 커밋**

```bash
cd strazh/Strazh
dotnet sln Strazh.sln add ../Strazh.Tests/Strazh.Tests.csproj
cd ../..
git add strazh/Strazh.Tests strazh/Strazh/Strazh.sln
git commit -m "test: add xUnit project and in-memory Roslyn compile helper"
```

---

### Task 2: 안정 노드 키 (GetHashCode → FNV-1a)

`string.GetHashCode()`는 프로세스마다 랜덤화되어 재실행 간 노드가 합쳐지지 않는다. 결정적 FNV-1a 64비트 해시로 교체한다.

**Files:**
- Modify: `strazh\Strazh\Domain\Nodes.cs:25-28` (`SetPrimaryKey`)
- Modify: `strazh\Strazh\Domain\Nodes.cs:101-104` (`MethodNode.SetPrimaryKey`)
- Test: `strazh\Strazh.Tests\StableKeyTests.cs`

- [ ] **Step 1: 실패 테스트 작성**

`strazh\Strazh.Tests\StableKeyTests.cs`:
```csharp
using Strazh.Domain;
using Xunit;

namespace Strazh.Tests;

public class StableKeyTests
{
    [Fact]
    public void Pk_is_deterministic_for_same_fullName()
    {
        var a = new ClassNode("N.Foo", "Foo");
        var b = new ClassNode("N.Foo", "Foo");
        Assert.Equal(a.Pk, b.Pk);
        // 결정적이고 알려진 값(FNV-1a 64-bit of "N.Foo", char 단위)
        Assert.Equal("16177116733985609327", a.Pk);
    }
}
```

> 실행자 메모: 기대 상수가 다르면 구현의 FNV 결과로 교체하되, **하드코딩된 리터럴로 고정**해 회귀를 잡는다(랜덤 해시 재유입 방지).

- [ ] **Step 2: 실패 확인**

Run: `dotnet test strazh\Strazh.Tests\Strazh.Tests.csproj --filter StableKeyTests`
Expected: FAIL (Pk가 랜덤 GetHashCode 값)

- [ ] **Step 3: 안정 해시 구현**

`strazh\Strazh\Domain\Nodes.cs` — `Node` 클래스에 헬퍼 추가하고 `SetPrimaryKey` 교체:
```csharp
protected static string StableHash(string text)
{
    // FNV-1a 64-bit (deterministic across processes/runtimes)
    ulong hash = 14695981039346656037UL;
    foreach (char c in text)
    {
        hash ^= c;
        hash *= 1099511628211UL;
    }
    return hash.ToString();
}

protected virtual void SetPrimaryKey()
{
    Pk = StableHash(FullName);
}
```

`MethodNode.SetPrimaryKey` 교체:
```csharp
protected override void SetPrimaryKey()
{
    Pk = StableHash($"{FullName}{Arguments}{ReturnType}");
}
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet test strazh\Strazh.Tests\Strazh.Tests.csproj --filter StableKeyTests`
Expected: PASS

- [ ] **Step 5: 커밋**

```bash
git add strazh/Strazh/Domain/Nodes.cs strazh/Strazh.Tests/StableKeyTests.cs
git commit -m "fix: deterministic FNV-1a node keys (replace process-randomized GetHashCode)"
```

---

## Phase 1 — 신규 추출 (프로젝트의 존재 이유)

### Task 3: 메서드 레벨 IMPLEMENTS_METHOD

클라 프록시와 서버 서비스가 같은 인터페이스 멤버를 구현 → 공유 인터페이스 MethodNode로 경계 관통.

**Files:**
- Modify: `strazh\Strazh\Domain\Relationships.cs` (관계 추가)
- Modify: `strazh\Strazh\Domain\Triples.cs` (Triple 추가)
- Modify: `strazh\Strazh\Analysis\Extractor.cs` (추출 메서드 + 공개 MethodNode 팩토리)
- Test: `strazh\Strazh.Tests\ImplementsMethodTests.cs`

- [ ] **Step 1: 실패 테스트 작성**

`strazh\Strazh.Tests\ImplementsMethodTests.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Strazh.Analysis;
using Strazh.Domain;
using Xunit;

namespace Strazh.Tests;

public class ImplementsMethodTests
{
    [Fact]
    public void Links_implementing_method_to_interface_member()
    {
        var src = @"
namespace N {
  public interface IOrderService { int Search(string f); }
  public class OrderService : IOrderService { public int Search(string f) => 0; }
}";
        var (tree, model) = TestCompiler.Compile(src);
        var classDecl = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var triples = new List<Triple>();

        Extractor.GetInterfaceImplementations(triples, classDecl, model);

        Assert.Contains(triples, t =>
            t.Relationship is ImplementsMethodRelationship &&
            t.NodeA.FullName == "N.OrderService.Search" &&
            t.NodeB.FullName == "N.IOrderService.Search");
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test strazh\Strazh.Tests\Strazh.Tests.csproj --filter ImplementsMethodTests`
Expected: FAIL (`GetInterfaceImplementations` / `ImplementsMethodRelationship` 없음)

- [ ] **Step 3: 관계 + 트리플 추가**

`strazh\Strazh\Domain\Relationships.cs` — 클래스 추가:
```csharp
public class ImplementsMethodRelationship : Relationship
{
    public override string Type => "IMPLEMENTS_METHOD";
}
```

`strazh\Strazh\Domain\Triples.cs` — 클래스 추가:
```csharp
public class TripleImplementsMethod : Triple
{
    public TripleImplementsMethod(MethodNode implementation, MethodNode interfaceMember)
        : base(implementation, interfaceMember, new ImplementsMethodRelationship())
    { }
}
```

- [ ] **Step 4: 추출 메서드 + 공개 MethodNode 팩토리 구현**

`strazh\Strazh\Analysis\Extractor.cs` — 기존 `private static MethodNode CreateMethodNode(this IMethodSymbol ...)` 옆에 공개 래퍼와 추출기 추가:
```csharp
public static MethodNode ToMethodNode(this IMethodSymbol symbol)
    => symbol.CreateMethodNode();

/// <summary>이 타입이 구현하는 인터페이스 멤버를, 이 타입에서 구현한 메서드와 연결.</summary>
public static void GetInterfaceImplementations(IList<Triple> triples, TypeDeclarationSyntax declaration, SemanticModel sem)
{
    if (sem.GetDeclaredSymbol(declaration) is not INamedTypeSymbol typeSymbol)
        return;
    foreach (var iface in typeSymbol.AllInterfaces)
    {
        foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
        {
            if (typeSymbol.FindImplementationForInterfaceMember(member) is IMethodSymbol impl
                && SymbolEqualityComparer.Default.Equals(impl.ContainingType, typeSymbol))
            {
                triples.Add(new TripleImplementsMethod(impl.ToMethodNode(), member.ToMethodNode()));
            }
        }
    }
}
```

> `using System.Collections.Generic;` 가 Extractor.cs 상단에 이미 존재함(확인).

- [ ] **Step 5: 통과 확인**

Run: `dotnet test strazh\Strazh.Tests\Strazh.Tests.csproj --filter ImplementsMethodTests`
Expected: PASS

- [ ] **Step 6: 커밋**

```bash
git add strazh/Strazh/Domain/Relationships.cs strazh/Strazh/Domain/Triples.cs strazh/Strazh/Analysis/Extractor.cs strazh/Strazh.Tests/ImplementsMethodTests.cs
git commit -m "feat: extract method-level IMPLEMENTS_METHOD edges"
```

---

### Task 4: USES_TYPE (타입 레벨 영향도)

메서드 파라미터/반환 타입 + 클래스 필드/프로퍼티 타입의 타입 참조를 추출(System/Microsoft 네임스페이스 제외).

**Files:**
- Modify: `strazh\Strazh\Domain\Relationships.cs`
- Modify: `strazh\Strazh\Domain\Triples.cs`
- Modify: `strazh\Strazh\Analysis\Extractor.cs`
- Test: `strazh\Strazh.Tests\UsesTypeTests.cs`

- [ ] **Step 1: 실패 테스트 작성**

`strazh\Strazh.Tests\UsesTypeTests.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Strazh.Analysis;
using Strazh.Domain;
using Xunit;

namespace Strazh.Tests;

public class UsesTypeTests
{
    [Fact]
    public void Links_method_to_parameter_type()
    {
        var src = @"
namespace N {
  public class FilterDTO { }
  public class Svc { public void Do(FilterDTO f) { } }
}";
        var (tree, model) = TestCompiler.Compile(src);
        var svc = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(c => c.Identifier.Text == "Svc");
        var triples = new List<Triple>();

        Extractor.GetTypeUsages(triples, svc, model);

        Assert.Contains(triples, t =>
            t.Relationship is UsesTypeRelationship &&
            t.NodeA.FullName == "N.Svc.Do" &&
            t.NodeB.FullName == "N.FilterDTO");
    }

    [Fact]
    public void Skips_framework_types()
    {
        var src = @"namespace N { public class Svc { public void Do(string s) { } } }";
        var (tree, model) = TestCompiler.Compile(src);
        var svc = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var triples = new List<Triple>();

        Extractor.GetTypeUsages(triples, svc, model);

        Assert.DoesNotContain(triples, t => t.Relationship is UsesTypeRelationship);
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test strazh\Strazh.Tests\Strazh.Tests.csproj --filter UsesTypeTests`
Expected: FAIL

- [ ] **Step 3: 관계 + 트리플 추가**

`Relationships.cs`:
```csharp
public class UsesTypeRelationship : Relationship
{
    public override string Type => "USES_TYPE";
}
```
`Triples.cs`:
```csharp
public class TripleUsesType : Triple
{
    public TripleUsesType(CodeNode user, TypeNode usedType)
        : base(user, usedType, new UsesTypeRelationship())
    { }
}
```

- [ ] **Step 4: 추출 메서드 구현**

`Extractor.cs` 추가:
```csharp
private static bool IsDomainType(ITypeSymbol? type, out INamedTypeSymbol named)
{
    named = (type as INamedTypeSymbol)!;
    if (named == null) return false;
    if (named.TypeKind != TypeKind.Class && named.TypeKind != TypeKind.Interface) return false;
    var ns = named.ContainingNamespace?.ToString() ?? "";
    if (ns.StartsWith("System") || ns.StartsWith("Microsoft")) return false;
    return true;
}

private static TypeNode ToTypeNode(this INamedTypeSymbol named)
{
    var fullName = (named.ContainingNamespace?.ToString() ?? "") + "." + named.Name;
    return named.TypeKind == TypeKind.Interface
        ? new InterfaceNode(fullName, named.Name)
        : new ClassNode(fullName, named.Name);
}

/// <summary>메서드 파라미터/반환 타입 + 필드/프로퍼티 타입의 도메인 타입 참조를 추출.</summary>
public static void GetTypeUsages(IList<Triple> triples, TypeDeclarationSyntax declaration, SemanticModel sem)
{
    foreach (var method in declaration.DescendantNodes().OfType<MethodDeclarationSyntax>())
    {
        if (sem.GetDeclaredSymbol(method) is not IMethodSymbol m) continue;
        var methodNode = m.ToMethodNode();
        foreach (var p in m.Parameters)
            if (IsDomainType(p.Type, out var nt))
                triples.Add(new TripleUsesType(methodNode, nt.ToTypeNode()));
        if (IsDomainType(m.ReturnType, out var rt))
            triples.Add(new TripleUsesType(methodNode, rt.ToTypeNode()));
    }
}
```

> 필드/프로퍼티 타입 추출은 동일 패턴으로 후속 확장 가능(YAGNI상 메서드 시그니처 우선). 본 태스크는 파라미터/반환만으로 테스트 통과.

- [ ] **Step 5: 통과 확인**

Run: `dotnet test strazh\Strazh.Tests\Strazh.Tests.csproj --filter UsesTypeTests`
Expected: PASS (2 passed)

- [ ] **Step 6: 커밋**

```bash
git add strazh/Strazh/Domain/Relationships.cs strazh/Strazh/Domain/Triples.cs strazh/Strazh/Analysis/Extractor.cs strazh/Strazh.Tests/UsesTypeTests.cs
git commit -m "feat: extract type-level USES_TYPE edges for impact analysis"
```

---

### Task 5: Command → 핸들러 (EXECUTES) + CommandNode

`new DelegateCommand(ExecuteX)` 패턴에서 Command 속성과 핸들러 메서드를 연결.

**Files:**
- Modify: `strazh\Strazh\Domain\Nodes.cs` (`CommandNode`)
- Modify: `strazh\Strazh\Domain\Relationships.cs` (`EXECUTES`, `DEFINES_COMMAND`)
- Modify: `strazh\Strazh\Domain\Triples.cs`
- Modify: `strazh\Strazh\Analysis\Extractor.cs`
- Test: `strazh\Strazh.Tests\CommandTests.cs`

- [ ] **Step 1: 실패 테스트 작성**

`strazh\Strazh.Tests\CommandTests.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Strazh.Analysis;
using Strazh.Domain;
using Xunit;

namespace Strazh.Tests;

public class CommandTests
{
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
        var (tree, model) = TestCompiler.Compile(src);
        var vm = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(c => c.Identifier.Text == "VM");
        var triples = new List<Triple>();

        Extractor.GetCommands(triples, vm, model);

        Assert.Contains(triples, t =>
            t.Relationship is ExecutesRelationship &&
            t.NodeA.FullName == "N.VM.SearchCommand" &&
            t.NodeB.FullName == "N.VM.ExecuteSearch");
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test strazh\Strazh.Tests\Strazh.Tests.csproj --filter CommandTests`
Expected: FAIL

- [ ] **Step 3: CommandNode + 관계 + 트리플 추가**

`Nodes.cs` 추가:
```csharp
public class CommandNode : CodeNode
{
    public CommandNode(string fullName, string name) : base(fullName, name) { }
    public override string Label { get; } = "Command";
}
```
`Relationships.cs` 추가:
```csharp
public class ExecutesRelationship : Relationship
{
    public override string Type => "EXECUTES";
}
public class DefinesCommandRelationship : Relationship
{
    public override string Type => "DEFINES_COMMAND";
}
```
`Triples.cs` 추가:
```csharp
public class TripleDefinesCommand : Triple
{
    public TripleDefinesCommand(TypeNode owner, CommandNode command)
        : base(owner, command, new DefinesCommandRelationship())
    { }
}
public class TripleExecutes : Triple
{
    public TripleExecutes(CommandNode command, MethodNode handler)
        : base(command, handler, new ExecutesRelationship())
    { }
}
```

- [ ] **Step 4: 추출 메서드 구현**

`Extractor.cs` 추가:
```csharp
/// <summary>*Command 타입의 객체 생성에서 Command 멤버명과 핸들러 메서드를 연결.</summary>
public static void GetCommands(IList<Triple> triples, TypeDeclarationSyntax declaration, SemanticModel sem)
{
    if (sem.GetDeclaredSymbol(declaration) is not INamedTypeSymbol owner) return;
    var ownerFullName = (owner.ContainingNamespace?.ToString() ?? "") + "." + owner.Name;
    var ownerNode = new ClassNode(ownerFullName, owner.Name);

    foreach (var creation in declaration.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
    {
        var typeName = creation.Type.ToString();
        if (!typeName.Contains("Command")) continue;

        // 대상 Command 멤버명: 할당식 좌변 또는 프로퍼티/필드 이니셜라이저
        string? commandName = creation.Ancestors()
            .OfType<AssignmentExpressionSyntax>()
            .Select(a => (a.Left as IdentifierNameSyntax)?.Identifier.Text)
            .FirstOrDefault(n => n != null);
        commandName ??= creation.Ancestors().OfType<PropertyDeclarationSyntax>().FirstOrDefault()?.Identifier.Text
                     ?? creation.Ancestors().OfType<VariableDeclaratorSyntax>().FirstOrDefault()?.Identifier.Text;
        if (commandName == null) continue;

        var commandNode = new CommandNode($"{ownerFullName}.{commandName}", commandName);
        triples.Add(new TripleDefinesCommand(ownerNode, commandNode));

        var firstArg = creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
        if (firstArg == null) continue;
        var info = sem.GetSymbolInfo(firstArg);
        if ((info.Symbol ?? info.CandidateSymbols.FirstOrDefault()) is IMethodSymbol handler)
            triples.Add(new TripleExecutes(commandNode, handler.ToMethodNode()));
    }
}
```

- [ ] **Step 5: 통과 확인**

Run: `dotnet test strazh\Strazh.Tests\Strazh.Tests.csproj --filter CommandTests`
Expected: PASS

- [ ] **Step 6: 커밋**

```bash
git add strazh/Strazh/Domain/Nodes.cs strazh/Strazh/Domain/Relationships.cs strazh/Strazh/Domain/Triples.cs strazh/Strazh/Analysis/Extractor.cs strazh/Strazh.Tests/CommandTests.cs
git commit -m "feat: extract Command->handler EXECUTES edges and CommandNode"
```

---

### Task 6: View → ViewModel (BINDS_TO) 글로벌 후처리

이름 컨벤션(`XView`→`XViewModel`)으로 매칭. 단일 구문트리가 아니라 수집된 클래스 노드 전체에 대한 후처리.

**Files:**
- Modify: `strazh\Strazh\Domain\Relationships.cs`
- Modify: `strazh\Strazh\Domain\Triples.cs`
- Modify: `strazh\Strazh\Analysis\Extractor.cs`
- Test: `strazh\Strazh.Tests\BindsToTests.cs`

- [ ] **Step 1: 실패 테스트 작성**

`strazh\Strazh.Tests\BindsToTests.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using Strazh.Analysis;
using Strazh.Domain;
using Xunit;

namespace Strazh.Tests;

public class BindsToTests
{
    [Fact]
    public void Links_view_to_viewmodel_by_naming_convention()
    {
        var classes = new List<ClassNode>
        {
            new("App.Views.SearchOrderView", "SearchOrderView"),
            new("App.ViewModels.SearchOrderViewModel", "SearchOrderViewModel"),
            new("App.Other.Unrelated", "Unrelated"),
        };
        var triples = new List<Triple>();

        Extractor.LinkViewsToViewModels(triples, classes);

        Assert.Single(triples);
        var t = triples[0];
        Assert.True(t.Relationship is BindsToRelationship);
        Assert.Equal("App.Views.SearchOrderView", t.NodeA.FullName);
        Assert.Equal("App.ViewModels.SearchOrderViewModel", t.NodeB.FullName);
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test strazh\Strazh.Tests\Strazh.Tests.csproj --filter BindsToTests`
Expected: FAIL

- [ ] **Step 3: 관계 + 트리플 추가**

`Relationships.cs`:
```csharp
public class BindsToRelationship : Relationship
{
    public override string Type => "BINDS_TO";
}
```
`Triples.cs`:
```csharp
public class TripleBindsTo : Triple
{
    public TripleBindsTo(ClassNode view, ClassNode viewModel)
        : base(view, viewModel, new BindsToRelationship())
    { }
}
```

- [ ] **Step 4: 후처리 메서드 구현**

`Extractor.cs` 추가:
```csharp
/// <summary>이름 컨벤션 XView -> XViewModel 로 View와 ViewModel을 연결.</summary>
public static void LinkViewsToViewModels(IList<Triple> triples, IList<ClassNode> classes)
{
    var byName = classes
        .GroupBy(c => c.Name)
        .ToDictionary(g => g.Key, g => g.First());
    foreach (var view in classes)
    {
        if (!view.Name.EndsWith("View") || view.Name.EndsWith("ViewModel")) continue;
        var vmName = view.Name + "Model"; // SearchOrderView -> SearchOrderViewModel
        if (byName.TryGetValue(vmName, out var vm))
            triples.Add(new TripleBindsTo(view, vm));
    }
}
```

- [ ] **Step 5: 통과 확인**

Run: `dotnet test strazh\Strazh.Tests\Strazh.Tests.csproj --filter BindsToTests`
Expected: PASS

- [ ] **Step 6: 커밋**

```bash
git add strazh/Strazh/Domain/Relationships.cs strazh/Strazh/Domain/Triples.cs strazh/Strazh/Analysis/Extractor.cs strazh/Strazh.Tests/BindsToTests.cs
git commit -m "feat: link View->ViewModel via Prism naming convention (BINDS_TO)"
```

---

### Task 7: 제네릭 `IRepository<T>` 필드 사용 (USES → Entity)

서버 서비스 메서드 본문이 참조하는 `IRepository<T>` 필드의 타입 인자(Entity)를 DB 종착점으로 연결.

**Files:**
- Modify: `strazh\Strazh\Domain\Relationships.cs`
- Modify: `strazh\Strazh\Domain\Triples.cs`
- Modify: `strazh\Strazh\Analysis\Extractor.cs`
- Test: `strazh\Strazh.Tests\RepositoryUsesTests.cs`

- [ ] **Step 1: 실패 테스트 작성**

`strazh\Strazh.Tests\RepositoryUsesTests.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Strazh.Analysis;
using Strazh.Domain;
using Xunit;

namespace Strazh.Tests;

public class RepositoryUsesTests
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
        var svc = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(c => c.Identifier.Text == "OrderService");
        var triples = new List<Triple>();

        Extractor.GetRepositoryUsages(triples, svc, model);

        Assert.Contains(triples, t =>
            t.Relationship is UsesRelationship &&
            t.NodeA.FullName == "N.OrderService.Search" &&
            t.NodeB.FullName == "N.Order");
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test strazh\Strazh.Tests\Strazh.Tests.csproj --filter RepositoryUsesTests`
Expected: FAIL

- [ ] **Step 3: 관계 + 트리플 추가**

`Relationships.cs`:
```csharp
public class UsesRelationship : Relationship
{
    public override string Type => "USES";
}
```
`Triples.cs`:
```csharp
public class TripleUses : Triple
{
    public TripleUses(MethodNode method, TypeNode entity)
        : base(method, entity, new UsesRelationship())
    { }
}
```

- [ ] **Step 4: 추출 메서드 구현**

`Extractor.cs` 추가:
```csharp
/// <summary>메서드 본문이 참조하는 IRepository&lt;T&gt; 필드의 엔티티 T를 USES로 연결.</summary>
public static void GetRepositoryUsages(IList<Triple> triples, TypeDeclarationSyntax declaration, SemanticModel sem)
{
    foreach (var method in declaration.DescendantNodes().OfType<MethodDeclarationSyntax>())
    {
        if (sem.GetDeclaredSymbol(method) is not IMethodSymbol m) continue;
        var methodNode = m.ToMethodNode();
        var seen = new HashSet<string>();
        foreach (var id in method.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (sem.GetSymbolInfo(id).Symbol is not IFieldSymbol f) continue;
            if (f.Type is not INamedTypeSymbol nt) continue;
            if (!nt.Name.Contains("Repository") || nt.TypeArguments.Length != 1) continue;
            if (nt.TypeArguments[0] is not INamedTypeSymbol entity) continue;
            var entityNode = entity.ToTypeNode();
            if (seen.Add(entityNode.FullName))
                triples.Add(new TripleUses(methodNode, entityNode));
        }
    }
}
```

- [ ] **Step 5: 통과 확인**

Run: `dotnet test strazh\Strazh.Tests\Strazh.Tests.csproj --filter RepositoryUsesTests`
Expected: PASS

- [ ] **Step 6: 커밋**

```bash
git add strazh/Strazh/Domain/Relationships.cs strazh/Strazh/Domain/Triples.cs strazh/Strazh/Analysis/Extractor.cs strazh/Strazh.Tests/RepositoryUsesTests.cs
git commit -m "feat: link service method to entity via generic IRepository<T> field (USES)"
```

---

### Task 8: DI 등록 (REGISTERS + lifetime)

`AddScoped/AddSingleton/AddTransient<I,Impl>()` 및 Prism `RegisterSingleton<I,Impl>()`에서 인터페이스→구현 + 생명주기를 추출. 관계에 프로퍼티(`lifetime`)를 싣는다.

**Files:**
- Modify: `strazh\Strazh\Domain\Relationships.cs` (관계 프로퍼티 지원)
- Modify: `strazh\Strazh\Domain\Triples.cs`
- Modify: `strazh\Strazh\Analysis\Extractor.cs`
- Test: `strazh\Strazh.Tests\DiRegistrationTests.cs`

- [ ] **Step 1: 실패 테스트 작성**

`strazh\Strazh.Tests\DiRegistrationTests.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using Strazh.Analysis;
using Strazh.Domain;
using Xunit;

namespace Strazh.Tests;

public class DiRegistrationTests
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
        var triples = new List<Triple>();

        Extractor.GetDiRegistrations(triples, tree, model);

        var t = Assert.Single(triples.Where(x => x.Relationship is RegistersRelationship));
        Assert.Equal("N.IOrderService", t.NodeA.FullName);
        Assert.Equal("N.OrderService", t.NodeB.FullName);
        Assert.Equal("Scoped", ((RegistersRelationship)t.Relationship).Lifetime);
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test strazh\Strazh.Tests\Strazh.Tests.csproj --filter DiRegistrationTests`
Expected: FAIL

- [ ] **Step 3: 관계(프로퍼티 포함) + 트리플 추가**

`Relationships.cs` — 베이스에 프로퍼티 dict 추가:
```csharp
public abstract class Relationship : IInspectable
{
    public abstract string Type { get; }
    public virtual System.Collections.Generic.IReadOnlyDictionary<string, string> Properties
        => new System.Collections.Generic.Dictionary<string, string>();
    public string ToInspection() => $$"""{ "Type": {{Type.Inspect()}} }""";
}
```
> 기존 `Relationship` 선언을 위 형태로 교체(나머지 관계 클래스는 그대로).

`Relationships.cs` — 등록 관계 추가:
```csharp
public class RegistersRelationship : Relationship
{
    public RegistersRelationship(string lifetime) => Lifetime = lifetime;
    public string Lifetime { get; }
    public override string Type => "REGISTERS";
    public override System.Collections.Generic.IReadOnlyDictionary<string, string> Properties
        => new System.Collections.Generic.Dictionary<string, string> { ["lifetime"] = Lifetime };
}
```
`Triples.cs`:
```csharp
public class TripleRegisters : Triple
{
    public TripleRegisters(InterfaceNode iface, ClassNode impl, string lifetime)
        : base(iface, impl, new RegistersRelationship(lifetime))
    { }
}
```

- [ ] **Step 4: 추출 메서드 구현**

`Extractor.cs` 추가:
```csharp
private static readonly System.Collections.Generic.Dictionary<string, string> RegisterMethods = new()
{
    ["AddScoped"] = "Scoped", ["AddSingleton"] = "Singleton", ["AddTransient"] = "Transient",
    ["RegisterScoped"] = "Scoped", ["RegisterSingleton"] = "Singleton", ["Register"] = "Transient",
};

/// <summary>DI 등록 호출 X&lt;I,Impl&gt;() 에서 인터페이스→구현 + lifetime 추출.</summary>
public static void GetDiRegistrations(IList<Triple> triples, SyntaxTree tree, SemanticModel sem)
{
    foreach (var inv in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
    {
        GenericNameSyntax? generic = inv.Expression switch
        {
            MemberAccessExpressionSyntax ma => ma.Name as GenericNameSyntax,
            GenericNameSyntax g => g,
            _ => null,
        };
        if (generic == null) continue;
        if (!RegisterMethods.TryGetValue(generic.Identifier.Text, out var lifetime)) continue;
        var typeArgs = generic.TypeArgumentList.Arguments;
        if (typeArgs.Count != 2) continue;

        if (sem.GetSymbolInfo(typeArgs[0]).Symbol is not INamedTypeSymbol ifaceSym) continue;
        if (sem.GetSymbolInfo(typeArgs[1]).Symbol is not INamedTypeSymbol implSym) continue;
        if (ifaceSym.ToTypeNode() is not InterfaceNode ifaceNode) continue;
        triples.Add(new TripleRegisters(ifaceNode, new ClassNode(implSym.ToTypeNode().FullName, implSym.Name), lifetime));
    }
}
```

- [ ] **Step 5: 통과 확인**

Run: `dotnet test strazh\Strazh.Tests\Strazh.Tests.csproj --filter DiRegistrationTests`
Expected: PASS

- [ ] **Step 6: 커밋**

```bash
git add strazh/Strazh/Domain/Relationships.cs strazh/Strazh/Domain/Triples.cs strazh/Strazh/Analysis/Extractor.cs strazh/Strazh.Tests/DiRegistrationTests.cs
git commit -m "feat: extract DI REGISTERS edges with lifetime property"
```

---

## Phase 2 — 역할 라벨 (다중 라벨)

### Task 9: 역할 분류기 + 노드 다중 라벨

휴리스틱으로 `:Class`/`:Interface`에 `:ViewModel`/`:Controller`/`:Service`/`:Repository`/`:Entity`/`:DTO`/`:View` 2차 라벨 부여.

**Files:**
- Modify: `strazh\Strazh\Domain\Nodes.cs` (`Labels` 가상 멤버)
- Create: `strazh\Strazh\Analysis\RoleClassifier.cs`
- Test: `strazh\Strazh.Tests\RoleClassifierTests.cs`

- [ ] **Step 1: 실패 테스트 작성**

`strazh\Strazh.Tests\RoleClassifierTests.cs`:
```csharp
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Strazh.Analysis;
using Xunit;

namespace Strazh.Tests;

public class RoleClassifierTests
{
    [Fact]
    public void Classifies_entity_by_IBaseEntity()
    {
        var src = @"
namespace N {
  public interface IBaseEntity { }
  public class Order : IBaseEntity { }
}";
        var (tree, model) = TestCompiler.Compile(src);
        var decl = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var symbol = model.GetDeclaredSymbol(decl)!;

        var roles = RoleClassifier.Classify(symbol);

        Assert.Contains("Entity", roles);
    }

    [Fact]
    public void Classifies_controller_by_base_type_name()
    {
        var src = @"
namespace N {
  public class ControllerBase { }
  public class OrderController : ControllerBase { }
}";
        var (tree, model) = TestCompiler.Compile(src);
        var decl = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(c => c.Identifier.Text == "OrderController");
        var symbol = model.GetDeclaredSymbol(decl)!;

        Assert.Contains("Controller", RoleClassifier.Classify(symbol));
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test strazh\Strazh.Tests\Strazh.Tests.csproj --filter RoleClassifierTests`
Expected: FAIL

- [ ] **Step 3: 역할 분류기 구현**

`strazh\Strazh\Analysis\RoleClassifier.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Strazh.Analysis;

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

Run: `dotnet test strazh\Strazh.Tests\Strazh.Tests.csproj --filter RoleClassifierTests`
Expected: PASS (2 passed)

- [ ] **Step 5: 노드 다중 라벨 지원 추가**

`Nodes.cs` — `Node`에 추가(직렬화는 Task 10에서 사용):
```csharp
/// <summary>주 라벨 + 역할 라벨. 기본은 주 라벨 하나.</summary>
public virtual IReadOnlyList<string> Labels => new[] { Label };

private string[]? _extraLabels;
public void AddRoleLabels(IEnumerable<string> roles) => _extraLabels = roles.ToArray();
public IReadOnlyList<string> AllLabels =>
    _extraLabels == null ? new[] { Label } : new[] { Label }.Concat(_extraLabels).ToArray();
```
> `using System.Collections.Generic;` 와 `using System.Linq;` 가 Nodes.cs 상단에 있는지 확인하고 없으면 추가.

- [ ] **Step 6: 커밋**

```bash
git add strazh/Strazh/Analysis/RoleClassifier.cs strazh/Strazh/Domain/Nodes.cs strazh/Strazh.Tests/RoleClassifierTests.cs
git commit -m "feat: role classifier and multi-label node support"
```

---

## Phase 3 — 출력(NDJSON) & 배치 적재

### Task 10: 트리플 NDJSON 직렬화

각 트리플을 로더가 소비할 JSON 한 줄로 직렬화(노드 라벨 배열·프로퍼티·관계 타입·관계 프로퍼티 포함).

**Files:**
- Create: `strazh\Strazh\Database\NdjsonWriter.cs`
- Test: `strazh\Strazh.Tests\NdjsonWriterTests.cs`

- [ ] **Step 1: 실패 테스트 작성**

`strazh\Strazh.Tests\NdjsonWriterTests.cs`:
```csharp
using System.Text.Json;
using Strazh.Database;
using Strazh.Domain;
using Xunit;

namespace Strazh.Tests;

public class NdjsonWriterTests
{
    [Fact]
    public void Serializes_triple_with_labels_and_relationship()
    {
        var triple = new TripleImplementsMethod(
            new MethodNode("N.OrderService.Search", "Search", new (string, string)[0], "int"),
            new MethodNode("N.IOrderService.Search", "Search", new (string, string)[0], "int"));

        var line = NdjsonWriter.Serialize(triple);
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        Assert.Equal("IMPLEMENTS_METHOD", root.GetProperty("rel").GetProperty("type").GetString());
        Assert.Equal("N.OrderService.Search", root.GetProperty("a").GetProperty("pk_source").GetString());
        Assert.Equal("Method", root.GetProperty("a").GetProperty("labels")[0].GetString());
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test strazh\Strazh.Tests\Strazh.Tests.csproj --filter NdjsonWriterTests`
Expected: FAIL

- [ ] **Step 3: NdjsonWriter 구현**

`strazh\Strazh\Database\NdjsonWriter.cs`:
```csharp
using System.Collections.Generic;
using System.Text.Json;
using Strazh.Domain;

namespace Strazh.Database;

public static class NdjsonWriter
{
    public static string Serialize(Triple triple)
    {
        var obj = new Dictionary<string, object?>
        {
            ["a"] = NodeObj(triple.NodeA),
            ["b"] = NodeObj(triple.NodeB),
            ["rel"] = new Dictionary<string, object?>
            {
                ["type"] = triple.Relationship.Type,
                ["props"] = triple.Relationship.Properties,
            },
        };
        return JsonSerializer.Serialize(obj);
    }

    private static Dictionary<string, object?> NodeObj(Node node) => new()
    {
        ["pk"] = node.Pk,
        ["pk_source"] = node.FullName,
        ["name"] = node.Name,
        ["labels"] = node.AllLabels,
    };
}
```
> `pk_source`는 디버깅·테스트 가독성을 위한 원본 FullName. 로더는 `pk`로 MERGE한다.

- [ ] **Step 4: 통과 확인**

Run: `dotnet test strazh\Strazh.Tests\Strazh.Tests.csproj --filter NdjsonWriterTests`
Expected: PASS

- [ ] **Step 5: 커밋**

```bash
git add strazh/Strazh/Database/NdjsonWriter.cs strazh/Strazh.Tests/NdjsonWriterTests.cs
git commit -m "feat: NDJSON triple serializer with labels and relationship props"
```

---

### Task 11: 배치 로더 (UNWIND) — Cypher 생성 단위 테스트

NDJSON 트리플 묶음을 `UNWIND $batch`로 적재하는 Cypher를 생성. (Neo4j 왕복은 통합에서, 여기선 쿼리 문자열을 단위 검증.)

**Files:**
- Create: `strazh\Strazh\Database\BatchLoader.cs`
- Test: `strazh\Strazh.Tests\BatchLoaderTests.cs`

- [ ] **Step 1: 실패 테스트 작성**

`strazh\Strazh.Tests\BatchLoaderTests.cs`:
```csharp
using Strazh.Database;
using Xunit;

namespace Strazh.Tests;

public class BatchLoaderTests
{
    [Fact]
    public void Cypher_uses_unwind_and_merges_on_pk()
    {
        var cypher = BatchLoader.MergeCypher("Method", "Class", "USES");

        Assert.Contains("UNWIND $batch AS row", cypher);
        Assert.Contains("MERGE (a:Method { pk: row.a.pk })", cypher);
        Assert.Contains("MERGE (b:Class { pk: row.b.pk })", cypher);
        Assert.Contains("MERGE (a)-[r:USES]->(b)", cypher);
        Assert.Contains("SET r += row.rel.props", cypher);
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test strazh\Strazh.Tests\Strazh.Tests.csproj --filter BatchLoaderTests`
Expected: FAIL

- [ ] **Step 3: BatchLoader 구현 (Cypher 생성 + 적재 메서드)**

`strazh\Strazh\Database\BatchLoader.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Neo4j.Driver;

namespace Strazh.Database;

public static class BatchLoader
{
    /// <summary>(주 라벨 a, 주 라벨 b, 관계타입) 그룹별 UNWIND MERGE Cypher 생성.</summary>
    public static string MergeCypher(string labelA, string labelB, string relType) =>
        $"UNWIND $batch AS row " +
        $"MERGE (a:{labelA} {{ pk: row.a.pk }}) SET a += row.a.props, a.name = row.a.name " +
        $"MERGE (b:{labelB} {{ pk: row.b.pk }}) SET b += row.b.props, b.name = row.b.name " +
        $"MERGE (a)-[r:{relType}]->(b) SET r += row.rel.props";

    /// <summary>row 객체 목록을 (labelA,labelB,relType)로 그룹핑해 배치 적재. 보조 라벨은 별도 SET.</summary>
    public static async Task LoadAsync(
        IAsyncSession session,
        IReadOnlyList<IDictionary<string, object>> rows,
        bool wipe,
        int batchSize = 5000)
    {
        if (wipe)
            await session.RunAsync("MATCH (n) DETACH DELETE n;");

        var groups = rows.GroupBy(r =>
        {
            var a = (IDictionary<string, object>)r["a"];
            var b = (IDictionary<string, object>)r["b"];
            var rel = (IDictionary<string, object>)r["rel"];
            var la = ((IList<object>)a["labels"])[0].ToString()!;
            var lb = ((IList<object>)b["labels"])[0].ToString()!;
            return (la, lb, rel["type"].ToString()!);
        });

        foreach (var g in groups)
        {
            var cypher = MergeCypher(g.Key.la, g.Key.lb, g.Key.Item3);
            foreach (var chunk in g.Chunk(batchSize))
                await session.RunAsync(cypher, new { batch = chunk });
        }
    }
}
```
> 보조(역할) 라벨은 노드 생성 후 `MATCH ... SET n:Label` 패스로 적용한다(통합 단계에서 추가; 본 단위 태스크는 `MergeCypher`만 검증).

- [ ] **Step 4: 통과 확인**

Run: `dotnet test strazh\Strazh.Tests\Strazh.Tests.csproj --filter BatchLoaderTests`
Expected: PASS

- [ ] **Step 5: 커밋**

```bash
git add strazh/Strazh/Database/BatchLoader.cs strazh/Strazh.Tests/BatchLoaderTests.cs
git commit -m "feat: UNWIND batch loader cypher (replaces per-triple writes)"
```

---

## Phase 4 — 파이프라인 통합 & 소비

### Task 12: Analyzer/Program 배선 — 신규 추출 호출 + NDJSON 출력 분기

신규 추출기를 코드 티어 분석에 연결하고, View↔VM 글로벌 후처리를 수행하며, `--output ndjson` 시 NDJSON 파일로 떨군다.

**Files:**
- Modify: `strazh\Strazh\Analysis\Extractor.cs:114-135` (`AnalyzeTree` 에서 신규 추출 호출 + 클래스 수집)
- Modify: `strazh\Strazh\Analysis\Analyzer.cs:184-201` (코드 티어 후 신규 추출 + View↔VM 후처리)
- Modify: `strazh\Strazh\Analysis\Analyzer.cs:92-96` (출력 분기: NDJSON vs Neo4j)
- Modify: `strazh\Strazh\Program.cs:43-51` (CLI 옵션 `--output`, `--ndjson-path`)
- Modify: `strazh\Strazh\Analysis\AnalyzerConfig.cs` (Output/NdjsonPath 필드)

- [ ] **Step 1: `AnalyzeTree`에 신규 추출 + 역할 라벨 + 클래스 수집 연결**

`Extractor.cs` `AnalyzeTree<T>` 의 `if (node != null)` 블록을 다음으로 확장(기존 `GetInherits`/`GetMethodsAll` 호출 유지):
```csharp
if (node != null)
{
    if (sem.GetDeclaredSymbol(declaration) is INamedTypeSymbol named)
        node.AddRoleLabels(Strazh.Analysis.RoleClassifier.Classify(named));

    triples.Add(new TripleDeclaredAt(node, fileNode));
    GetInherits(triples, declaration, sem, node);
    GetMethodsAll(triples, declaration, sem, node);

    // 신규 추출
    GetInterfaceImplementations(triples, declaration, sem);
    GetTypeUsages(triples, declaration, sem);
    GetCommands(triples, declaration, sem);
    GetRepositoryUsages(triples, declaration, sem);
}
```
그리고 `AnalyzeTree`가 처리한 ClassNode들을 모으도록, 시그니처에 `IList<ClassNode> collectedClasses` 파라미터를 추가하고 `node is ClassNode cn`이면 `collectedClasses.Add(cn)`. DI 등록은 트리 단위이므로 별도 호출:
```csharp
// AnalyzeTree 말미 (declarations 루프 밖)
GetDiRegistrations(triples, st, sem);
```

> 실행자 메모: `AnalyzeTree` 호출부(Analyzer.cs:193-194)도 새 파라미터에 맞춰 수정.

- [ ] **Step 2: Analyzer에서 클래스 수집 → View↔VM 후처리**

`Analyzer.cs` 코드 티어 루프를 클래스 수집 가능하도록 변경하고, 프로젝트 분석 종료 후:
```csharp
var collectedClasses = new List<ClassNode>();
foreach (var st in syntaxTreeRoot)
{
    var sem = compilation.GetSemanticModel(st);
    Extractor.AnalyzeTree<InterfaceDeclarationSyntax>(triples, st, sem, rootNode, collectedClasses);
    Extractor.AnalyzeTree<ClassDeclarationSyntax>(triples, st, sem, rootNode, collectedClasses);
}
Extractor.LinkViewsToViewModels(triples, collectedClasses);
```
> View와 ViewModel이 서로 다른 프로젝트에 있을 수 있으므로, 정확도를 높이려면 수집을 솔루션 전역으로 올리는 후속 개선이 가능(YAGNI: 1차는 프로젝트 단위, 같은 모듈 내 View/VM이 대부분).

- [ ] **Step 3: 출력 분기 — NDJSON vs Neo4j**

`AnalyzerConfig.cs`에 필드 추가: `public string Output { get; }` (기본 `"neo4j"`), `public string NdjsonPath { get; }`. `Analyzer.cs`의 `DbManager.InsertData(...)` 호출부를 분기:
```csharp
if (config.Output == "ndjson")
{
    var path = string.IsNullOrEmpty(config.NdjsonPath) ? "triples.ndjson" : config.NdjsonPath;
    using var sw = new StreamWriter(path, append: index != 0);
    foreach (var triple in triples)
        sw.WriteLine(NdjsonWriter.Serialize(triple));
}
else
{
    await DbManager.InsertData(triples, config.Credentials, config.IsDelete && index == 0);
}
```
> `using Strazh.Database;`, `using System.IO;` 가 Analyzer.cs에 있는지 확인.

- [ ] **Step 4: CLI 옵션 추가**

`Program.cs`에 옵션 추가(기존 패턴 따라):
```csharp
var optionOutput = new Option<string>("--output", "optional `neo4j` (default) or `ndjson`");
optionOutput.AddAlias("-o");
rootCommand.Add(optionOutput);

var optionNdjsonPath = new Option<string>("--ndjson-path", "optional output path for ndjson (default triples.ndjson)");
rootCommand.Add(optionNdjsonPath);
```
`SetHandler`와 `BuildKnowledgeGraph` 시그니처, `AnalyzerConfig` 생성자에 두 인자를 전달하도록 확장. `--output ndjson`이면 `Healthcheck.IsNeo4jReady()` 검사를 건너뛴다.

- [ ] **Step 5: 빌드 + 전체 단위 테스트**

Run: `dotnet build strazh\Strazh\Strazh.csproj` 그리고 `dotnet test strazh\Strazh.Tests\Strazh.Tests.csproj`
Expected: 빌드 성공, 모든 단위 테스트 PASS

- [ ] **Step 6: Vanuatu에 대해 스모크 실행 (NDJSON)**

Run:
```bash
dotnet run --project strazh/Strazh/Strazh.csproj -- -s "C:\develop\baw\phase2\baw-phase2-platform\Vanuatu\Vanuatu.sln" -t code -o ndjson --ndjson-path out/vanuatu.ndjson
```
Expected: `out/vanuatu.ndjson` 생성. 다음으로 핵심 엣지 존재를 확인:
```bash
grep -m1 IMPLEMENTS_METHOD out/vanuatu.ndjson
grep -m1 '"BINDS_TO"' out/vanuatu.ndjson
grep -m1 '"USES"' out/vanuatu.ndjson
grep -m1 '"REGISTERS"' out/vanuatu.ndjson
```
Expected: 각 grep이 최소 1줄 출력(추출 성공 증거). 누락 시 해당 추출 태스크의 휴리스틱을 Vanuatu 실제 코드에 맞춰 조정.

- [ ] **Step 7: 커밋**

```bash
git add strazh/Strazh/Analysis/Extractor.cs strazh/Strazh/Analysis/Analyzer.cs strazh/Strazh/Analysis/AnalyzerConfig.cs strazh/Strazh/Program.cs
git commit -m "feat: wire new extractors, View-VM post-pass, and ndjson output"
```

---

### Task 13: NDJSON → Neo4j 배치 적재 경로 + 역할 라벨 적용

NDJSON 파일을 읽어 `BatchLoader`로 적재하고, 보조(역할) 라벨을 SET하는 로더 진입점.

**Files:**
- Modify: `strazh\Strazh\Database\BatchLoader.cs` (파일 적재 진입점 + 역할 라벨 패스)
- Modify: `strazh\Strazh\Program.cs` (`load` 서브커맨드 또는 `--load-ndjson` 옵션)
- Test: `strazh\Strazh.Tests\BatchLoaderRowTests.cs` (NDJSON 한 줄 → row 파싱)

- [ ] **Step 1: 실패 테스트 작성 (NDJSON 라인 파싱 → 라벨 SET Cypher)**

`strazh\Strazh.Tests\BatchLoaderRowTests.cs`:
```csharp
using Strazh.Database;
using Xunit;

namespace Strazh.Tests;

public class BatchLoaderRowTests
{
    [Fact]
    public void Builds_secondary_label_set_cypher()
    {
        var cypher = BatchLoader.RoleLabelCypher("ViewModel");
        Assert.Contains("MATCH (n { pk: $pk })", cypher);
        Assert.Contains("SET n:ViewModel", cypher);
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test strazh\Strazh.Tests\Strazh.Tests.csproj --filter BatchLoaderRowTests`
Expected: FAIL

- [ ] **Step 3: 역할 라벨 Cypher + 파일 적재 구현**

`BatchLoader.cs` 추가:
```csharp
public static string RoleLabelCypher(string role) =>
    $"MATCH (n {{ pk: $pk }}) SET n:{role}";

public static async Task LoadFileAsync(IAsyncSession session, string ndjsonPath, bool wipe)
{
    var rows = new List<IDictionary<string, object>>();
    foreach (var line in System.IO.File.ReadLines(ndjsonPath))
    {
        if (string.IsNullOrWhiteSpace(line)) continue;
        rows.Add(System.Text.Json.JsonSerializer
            .Deserialize<Dictionary<string, object>>(line)!);
    }
    // 주: 실제 적재 시 JsonElement → 중첩 Dictionary 변환 헬퍼 필요.
    await LoadAsync(session, NormalizeRows(rows), wipe);
    await ApplyRoleLabelsAsync(session, ndjsonPath);
}
```
> 실행자 메모: `System.Text.Json`은 중첩을 `JsonElement`로 역직렬화하므로, Neo4j 파라미터로 넘기기 전 `JsonElement`를 `Dictionary<string,object>`/`List<object>`/원시값으로 변환하는 `NormalizeRows`/`Convert(JsonElement)` 재귀 헬퍼를 추가한다. `ApplyRoleLabelsAsync`는 각 노드의 `labels[1..]`(보조 라벨)에 대해 `RoleLabelCypher(role)`를 `pk`별로 실행한다.

- [ ] **Step 4: 통과 확인**

Run: `dotnet test strazh\Strazh.Tests\Strazh.Tests.csproj --filter BatchLoaderRowTests`
Expected: PASS

- [ ] **Step 5: Program에 적재 경로 배선 + 통합 스모크**

`Program.cs`에 `--load-ndjson <path>` 옵션 추가. 지정 시 추출을 건너뛰고 `BatchLoader.LoadFileAsync`로 적재(wipe=delete 플래그). Neo4j(Docker) 기동 후:
```bash
dotnet run --project strazh/Strazh/Strazh.csproj -- -c "neo4j:neo4j:password" --load-ndjson out/vanuatu.ndjson -d true
```
Expected: 예외 없이 적재 완료. Neo4j 브라우저에서:
```cypher
MATCH (v:View)-[:BINDS_TO]->(vm:ViewModel) RETURN v.name, vm.name LIMIT 5;
```
Expected: View→ViewModel 행 반환.

- [ ] **Step 6: 커밋**

```bash
git add strazh/Strazh/Database/BatchLoader.cs strazh/Strazh/Program.cs strazh/Strazh.Tests/BatchLoaderRowTests.cs
git commit -m "feat: load ndjson into neo4j via batch loader and apply role labels"
```

---

### Task 14: 스키마 쿡북 + 읽기전용 MCP 설정 (문서)

LLM이 정확한 Cypher를 쓰도록 노드 라벨/엣지/경계 조인 패턴/활용사례별 예제 쿼리를 문서화하고, 읽기전용 MCP를 등록한다. (코드 없음 — 문서/설정.)

**Files:**
- Create: `docs\cookbook\schema-cookbook.md`
- Create: `docs\mcp\claude_desktop_config.example.json`

- [ ] **Step 1: 스키마 쿡북 작성**

`docs\cookbook\schema-cookbook.md` — 다음을 포함:
  - **노드 라벨**: `Method, Class, Interface, Command, File, Folder, Project, Solution, Package` + 역할 라벨 `ViewModel/Controller/Service/Repository/Entity/DTO/View`. 모든 노드는 `pk`(안정 해시)·`name`·`fullName` 보유.
  - **엣지**: `BINDS_TO, DEFINES_COMMAND, EXECUTES, CALLS(=INVOKE), IMPLEMENTS_METHOD, USES, USES_TYPE, REGISTERS{lifetime}, OF_TYPE, HAVE, CONSTRUCT, DECLARED_AT, INCLUDED_IN, DEPENDS_ON, CONTAINS`.
  - **경계 조인 패턴(핵심)**:
    ```cypher
    // 화면→DB E2E: View 이름으로 시작
    MATCH (v:View {name:$viewName})-[:BINDS_TO]->(vm:ViewModel)
    MATCH (vm)-[:DEFINES_COMMAND]->(cmd:Command)-[:EXECUTES]->(h:Method)
    MATCH (h)-[:CALLS|INVOKE*1..3]->(ifm:Method)<-[:IMPLEMENTS_METHOD]-(impl:Method)
    MATCH (impl)-[:USES]->(e:Entity)
    RETURN v.name, vm.name, cmd.name, h.name, ifm.name, impl.name, e.name;
    ```
  - **활용사례별 예제**: ① 영향도 `MATCH (m)-[:USES_TYPE]->(:DTO {name:$dto}) RETURN ...`, ② E2E(위), ③ 순환참조 `MATCH p=(a)-[:DEPENDS_ON*]->(a) RETURN p` / captive `MATCH (i)-[r:REGISTERS]->(impl) WHERE r.lifetime='Singleton' ...`.

- [ ] **Step 2: 읽기전용 MCP 설정 예시 작성**

`docs\mcp\claude_desktop_config.example.json`:
```json
{
  "mcpServers": {
    "neo4j-vanuatu": {
      "command": "uvx",
      "args": ["mcp-neo4j-cypher", "--transport", "stdio"],
      "env": {
        "NEO4J_URI": "neo4j://localhost:7687",
        "NEO4J_USERNAME": "reader",
        "NEO4J_PASSWORD": "***",
        "NEO4J_DATABASE": "neo4j"
      }
    }
  }
}
```
> Neo4j에 **읽기전용 사용자**(`reader`)를 생성: `CREATE USER reader SET PASSWORD '***' CHANGE NOT REQUIRED; GRANT ROLE reader TO reader;`(Neo4j의 내장 `reader` 롤). MCP는 이 계정으로 등록해 LLM의 쓰기/삭제를 차단.

- [ ] **Step 3: 커밋**

```bash
git add docs/cookbook/schema-cookbook.md docs/mcp/claude_desktop_config.example.json
git commit -m "docs: schema cookbook and read-only MCP config"
```

---

## Self-Review 메모 (계획 작성자 점검 결과)

- **Spec 커버리지**: PRD v2의 12개 결정 매핑 — 경계조인 A(Task 3), 위쪽끝(Task 5/6), 아래쪽끝(Task 7), 메가컴파일(기존 Strazh 유지), wipe&reload+안정키(Task 2/11), NDJSON+배치(Task 10/11/12/13), 타입레벨 USES_TYPE(Task 4), DI 등록(Task 8), 공식MCP+쿡북(Task 14), 다중라벨(Task 9), Strazh 포크(Task 1). 모두 태스크 존재.
- **타입 일관성**: `ToMethodNode`(Task 3 정의) → Task 4/5/7에서 재사용. `ToTypeNode`(Task 4 정의) → Task 7/8 재사용. `AllLabels`(Task 9) → Task 10 사용. `MergeCypher`(Task 11) → Task 13 `RoleLabelCypher`와 동일 클래스.
- **알려진 후속 개선(YAGNI 보류, 문서화됨)**: View↔VM 전역 수집(현 프로젝트 단위), USES_TYPE 필드/프로퍼티 타입 확장, DI `typeof(IRepository<>)` 오픈제네릭 형태, `JsonElement` 정규화 헬퍼 구현 세부.

---

## Execution Handoff

이 계획은 `docs/superpowers/plans/2026-06-05-code-wiki-etl.md`에 저장되었습니다. 실행 방식 두 가지:

1. **Subagent-Driven (권장)** — 태스크마다 새 서브에이전트를 띄우고 사이사이 리뷰. 빠른 반복.
2. **Inline Execution** — 현재 세션에서 체크포인트마다 묶어 실행.

어느 방식으로 진행할까요?
