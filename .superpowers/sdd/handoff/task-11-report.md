# Task 11: CommandExtractor Report

**STATUS:** COMPLETED

**Commit:** `54eb284` - feat(codewiki): CommandExtractor(DEFINES_COMMAND/EXECUTES)

**Test Result:** `Passed! 1/1 - Duration: 715 ms - CodeWiki.Tests.dll`

## Concerns

None. Implementation follows established patterns:
- Detects `ObjectCreationExpressionSyntax` with type name starting with "DelegateCommand"
- Extracts command name via `AssignedName()` helper that walks up syntax tree ignoring `.ObservesCanExecute(...)` chains
- Creates Command node with `Pk.Of(ownerFullName, commandName)`
- Produces two edges: `DEFINES_COMMAND` (owner→cmd) and `EXECUTES` (cmd→handler)
- Handles null nodes gracefully (skips if handler unresolved)

---

## Fix: CommandExtractor CandidateSymbols Fallback

**Commit:** `653c876` - fix(codewiki): CommandExtractor 핸들러 해석에 CandidateSymbols 폴백

**Problem:** Overloaded DelegateCommand constructors (e.g., `DelegateCommand(Action)` and `DelegateCommand(Action, Func<bool>)`) caused method group binding ambiguity. When Symbol==null, candidates were only in CandidateSymbols, resulting in silent EXECUTES edge loss.

**Fix:**
```csharp
var si = model.GetSymbolInfo(arg.Expression);
var handler = si.Symbol as IMethodSymbol
           ?? si.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
if (handler != null) { /* create EXECUTES edge */ }
```

**Test:** `ExecutesResolvesHandlerWithOverloadedCtor` - Regression test for overloaded constructor case.

**Test Command:** `dotnet test --filter CommandExtractorTests`

**Result:** `Passed! 24/24 - Duration: 782 ms - All regressions clean`
