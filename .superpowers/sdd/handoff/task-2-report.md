# Task 2 Report: Pk (FNV-1a 64bit Stable Hash)

## Status: DONE

**Commit:** `27257ce` — `feat(codewiki): FNV-1a 안정 pk`

## Summary

Task 2 is complete. Implemented FNV-1a 64-bit stable hash function in `Pk.Of()` with full test coverage (3/3 passing).

## Files Created

1. **`src/CodeWiki/Model/Pk.cs`**
   - Static class with `Pk.Of(params string[] parts)` method
   - FNV-1a hash algorithm with UTF-8 byte encoding
   - Separator `"|"` prevents collision (e.g., `["a","b"]` != `["ab"]`)
   - Returns hash as invariant culture string for process-independent determinism

2. **`src/CodeWiki.Tests/PkTests.cs`**
   - Three xUnit tests covering required behaviors:
     - `Deterministic()`: Same inputs produce same hash
     - `SeparatorAvoidsCollision()`: `["a","b"]` ≠ `["ab"]`
     - `DistinctInputsDiffer()`: `["x"]` ≠ `["y"]`

## Test Results

```
Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3, Duration: 29 ms
```

All tests passing after implementation.

## Implementation Details

- **FNV-1a constants:**
  - Offset basis: `14695981039346656037UL`
  - Prime: `1099511628211UL`
- **Key invariants:**
  - Separator `"|"` ensures `parts` are distinct even if concatenated
  - `CultureInfo.InvariantCulture` ensures hash string is stable across locales/processes
  - No nullable/unsafe operations; straightforward bit manipulation

## Self-Check

- [x] TDD flow: Tests fail → Implementation → Tests pass → Commit
- [x] File locations match spec: `src/CodeWiki/Model/Pk.cs`, `src/CodeWiki.Tests/PkTests.cs`
- [x] Namespace correct: `CodeWiki.Model`
- [x] Commit message: `feat(codewiki): FNV-1a 안정 pk`
- [x] No extraneous files created (only .cs files)
- [x] Code matches brief exactly (same constants, Join logic, encoding)
- [x] No --no-verify used; pre-commit hooks satisfied

## Notes

- CLAUDE.md context mentions FNV-1a `StableHash` for node PKs to avoid process-dependent `.GetHashCode()`
- This Pk class is the foundational hash function for Task 3 (Node record definition)
- Determinism verified: repeated calls with same inputs always produce identical strings
