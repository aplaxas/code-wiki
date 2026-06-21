# Final Fix Report — feat/codewiki-v2-semantic

Date: 2026-06-20

## Fix A — OperationKind: MutationVerb 리포지토리 수신자 한정

**파일:** `src/CodeWiki/Roslyn/OperationKind.cs`

**문제:** `MutationVerbs` 집계가 메서드 바디 내 모든 invocation을 대상으로 하여, `results.Add(...)` / `list.Add(...)` 같은 비-리포 수신자 호출도 `mutatesState="true"`로 분류. `SearchOrdersAsync` 같은 read-only 쿼리가 false positive `command`가 되는 결함.

**수정:** 리포지토리 필드 수신자(`IFieldSymbol` whose type `INamedTypeSymbol.Name.Contains("Repository")`) 경우에만 MutationVerb 집계. `RawSqlMarkers` 체크는 기존처럼 `InvokedName` 기반 광범위 매칭 유지.

**변경 핵심:**
```csharp
// Before
var bare = name.EndsWith("Async") ? name[..^5] : name;
if (MutationVerbs.Contains(bare)) mutates = true;

// After
if (inv.Expression is MemberAccessExpressionSyntax ma &&
    model.GetSymbolInfo(ma.Expression).Symbol is IFieldSymbol rf &&
    rf.Type is INamedTypeSymbol rft && rft.IsGenericType && rft.Name.Contains("Repository"))
{
    var bare = ma.Name.Identifier.Text;
    bare = bare.EndsWith("Async") ? bare[..^5] : bare;
    if (MutationVerbs.Contains(bare)) mutates = true;
}
```

---

## Fix B — Program.cs: enrich --vm 경로 가드

**파일:** `src/CodeWiki/Program.cs` (enrich case, VM branch)

**문제:** `input.VmCsPath`가 비어있거나(`""`), 해당 파일이 존재하지 않을 때 `Path.Combine(root, "")` = 디렉터리가 되어 `SourceSlicer.WholeFile` / `File.ReadAllText`가 불투명한 예외 발생.

**수정:** `combined` 계산 직후, `string.IsNullOrEmpty(input.VmCsPath)` OR `!File.Exists(combined)` 이면 명확한 오류 메시지 출력 후 `return`. throw 없음.

```csharp
if (string.IsNullOrEmpty(input.VmCsPath) || !System.IO.File.Exists(combined))
{
    Console.Error.WriteLine($"VM.cs not found for '{o.Vm}' (path '{combined}'). Re-run extract with L0 props or check VANUATU_ROOT.");
    return;
}
```

---

## 신규 테스트 — NonRepoAddIsNotCountedAsMutation (OperationKindTests.cs)

**RED→GREEN 시나리오:**
- Fix A 이전: `list.Add(...)` + `_repo.Table` 참조 → `("true","command")` (wrong)
- Fix A 이후: 동일 메서드 → `("false","query")` (correct)

테스트 메서드명: `NonRepoAddIsNotCountedAsMutation`

---

## 빌드 및 테스트 결과

```
dotnet build src/CodeWiki/CodeWiki.csproj -c Release
→ 0 Error(s), 11 Warning(s) (기존 NU1903/NU1608 경고만)

dotnet test
→ Passed! Failed: 0, Passed: 63, Skipped: 0, Total: 63
  (OperationKindTests: RepoMutationIsCommand, RepoReadOnlyIsQuery, NoRepoReturnsNull, NonRepoAddIsNotCountedAsMutation 포함)
```
