# Task 3 Report: Node/Edge Record + Graph 빌더

## Status
✅ COMPLETE

## Commit
`d51fa55 feat(codewiki): Node/Edge record + Graph 빌더(dedup·props 병합)`

## Test Results
```
Passed!  - Failed: 0, Passed: 3, Skipped: 0, Duration: 115 ms
```

## Implementation Summary

### Files Created
1. **src/CodeWiki/Model/Node.cs** (6 lines)
   - Sealed record: `Node(string Label, string Pk, string Name, string FullName, IReadOnlyDictionary<string,string> Props, IReadOnlyList<string> Roles)`
   - Immutable value type for graph nodes

2. **src/CodeWiki/Model/Edge.cs** (4 lines)
   - Sealed record: `Edge(string Type, string FromPk, string ToPk, IReadOnlyDictionary<string,string> Props)`
   - Immutable value type for graph edges

3. **src/CodeWiki/Model/Graph.cs** (30 lines)
   - `AddNode(Node n)`: Deduplicates by Pk; merges duplicate nodes with:
     - Props: Non-empty values override empty ones
     - Roles: Distinct union of both node role lists
     - Name/FullName: Keep non-empty value, prefer first node
   - `AddEdge(Edge e)`: Deduplicates by composite key `{FromPk}|{ToPk}|{Type}`
   - Returns `IReadOnlyCollection<Node>` and `IReadOnlyCollection<Edge>`

4. **src/CodeWiki.Tests/GraphTests.cs** (31 lines)
   - `DedupNodeByPk`: Verifies same Pk stored once
   - `NonEmptyPropWinsOverEmpty`: Verifies props merge strategy (non-empty wins)
   - `DedupEdgeByFromToType`: Verifies edge dedup by composite key

### Design Decisions
- **Dedup Strategy**: By primary key (Node.Pk), edge identity key (Type|FromPk|ToPk)
- **Props Merge**: Non-empty values override empty; empty values dropped in winner
- **Roles Merge**: Distinct union preserves all unique roles across duplicates
- **Name/FullName Merge**: Prefer non-empty from either node, first wins if both non-empty
- **Immutable Collections**: IReadOnlyDictionary/IReadOnlyList to prevent external mutation

### Test Coverage
All 3 tests pass; covers:
- Deduplication logic
- Property merging (empty vs non-empty)
- Edge uniqueness by composite key

### No Concerns
- All tests passing
- Implementation matches spec exactly
- Ready for Task 4 (NdjsonWriter integration)
