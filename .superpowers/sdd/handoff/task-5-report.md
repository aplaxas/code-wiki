# Task 5 Report: Names + SymbolNodes + FileNodes

## STATUS
✅ COMPLETE - All tests pass, implementation follows brief exactly.

## COMMIT HASH
`eb6aba3` — feat(codewiki): Names/SymbolNodes/FileNodes 심볼→노드 팩토리

## TEST RESULT
```
Passed!  - Failed: 0, Passed: 11, Skipped: 0, Total: 11, Duration: 518 ms
```
All 11 tests pass (2 new SymbolNodesTests + 9 existing tests).

## IMPLEMENTATION

### Files Created
1. **`src/CodeWiki/Roslyn/Names.cs`** — `Full(ISymbol)` with SymbolDisplayFormat
2. **`src/CodeWiki/Roslyn/SymbolNodes.cs`** — `ForType(INamedTypeSymbol, RoleClassifier?)`, `ForMethod(IMethodSymbol)`
3. **`src/CodeWiki/Roslyn/FileNodes.cs`** — `ForPath(string abs, string root)`
4. **`src/CodeWiki.Tests/SymbolNodesTests.cs`** — 2 fact tests (TypeNodeHasFullNameAndLabel, MethodPkIncludesSignature)

### RoleClassifier Stub
Created minimal stub at `src/CodeWiki/Roslyn/RoleClassifier.cs`:
```csharp
public class RoleClassifier
{
    public IReadOnlyList<string> Classify(INamedTypeSymbol t) => System.Array.Empty<string>();
}
```
**Reason:** Type reference required for compilation; null-coalescing in SymbolNodes line 19 delegates to T7 completion.

## TEST COVERAGE

### TypeNodeHasFullNameAndLabel
- Compiles namespace + class → extracts `INamedTypeSymbol` → calls `SymbolNodes.ForType(foo, null)`
- Asserts: Label = "Class", FullName = "N.Foo"
- **Passed:** Full name resolution via Names.Full + Roslyn SymbolDisplayFormat works correctly

### MethodPkIncludesSignature
- Compiles class with 2 overloaded methods `Bar(string s)` and `Bar()`
- Extracts both via `IMethodSymbol` → calls `SymbolNodes.ForMethod` on each
- Asserts: `ms[0].Pk != ms[1].Pk` (overload discrimination)
- **Passed:** Pk.Of(full, args, ret) includes argument/return type in hash → distinct overloads

## CONCERNS
None. Implementation:
- Follows brief code exactly
- Passes all tests without modification
- RoleClassifier nullable design (roles?.Classify) allows T7 to inject behavior later
- Node records created with correct Label/Pk/Name/FullName/Props/Roles structure
- No domain logic contamination (only model + Roslyn symbol manipulation)

## NEXT TASK
T6 (TestCompiler) will provide compilation utilities to reduce boilerplate; subsequent tasks will populate RoleClassifier and edge logic.
