# Task 8 Completion Report

## STATUS
✅ **COMPLETE**

## Commit
```
49d0282 feat(codewiki): TypeExtractor 노드+DECLARED_IN+INHERITS+IMPLEMENTS
```

## Test Results
```
Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3, Duration: 435 ms - CodeWiki.Tests.dll
```

All three tests in TypeExtractorTests pass:
- `EmitsClassAndInterfaceImplementsEdge()` ✓
- `InheritsEdge()` ✓
- `UnresolvedBaseSkipped()` ✓ (불변식 #3: 미해석 베이스 타입 skip)

## Implementation Summary

### Files Created
1. **ExtractionContext.cs** - Compilation 래핑, SourceTypes() 반복자(깊이 우선 탐색)
2. **IExtractor.cs** - 추출기 인터페이스(Extract 메서드)
3. **TypeExtractor.cs** - 노드/엣지 추출 로직
4. **TypeExtractorTests.cs** - 3개 테스트 케이스

### Extraction Logic
- **노드**: SymbolNodes.ForType()로 클래스/인터페이스 노드 생성, RoleClassifier로 역할 라벨 자동 추가
- **DECLARED_IN**: 소스 파일 노드 생성, 타입과 파일 연결
- **INHERITS**: BaseType(클래스만, null/특수 타입 제외)이 있으면 엣지 추가
- **IMPLEMENTS**: AllInterfaces 순회, 각 인터페이스 노드와 엣지 추가
- **안전성**: 미해석 베이스(ForType 반환 null)는 스킵하여 null 엣지 방지

## Design Decisions
- T9(메서드/CALLS/INSTANTIATES)는 본 범위에 미포함 - 다음 태스크로 연기
- Graph.AddNode()가 자동 merge하므로 중복 처리 안 함
- Empty dict 재사용으로 GC 압박 최소화
