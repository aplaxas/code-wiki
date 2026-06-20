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
