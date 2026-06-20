# Task 9 Report: TypeExtractor target-typed new() 지원

## STATUS
✅ COMPLETE

## Summary
INSTANTIATES 추출이 implicit object creation `new()`를 누락하던 문제 수정. BaseObjectCreationExpressionSyntax 사용으로 explicit `new A()`와 target-typed `new()` 모두 포착.

## Changes
- **File Modified**: `src/CodeWiki/Extraction/TypeExtractor.cs`
  - 라인 44: `ObjectCreationExpressionSyntax` → `BaseObjectCreationExpressionSyntax` 변경
  - 효과: 공통 베이스 클래스 사용으로 두 구문 유형 모두 처리

- **File Modified**: `src/CodeWiki.Tests/TypeExtractorTests.cs`
  - 테스트 `InstantiatesImplicitNew()` 추가
  - 소스: `namespace N { public class A {} public class B { public void M(){ A a = new(); } } }`
  - 검증: M → A 간 INSTANTIATES 엣지 존재

## Test Results
```
TypeExtractorTests:
Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6, Duration: 1 s

Full regression:
Passed! - Failed: 0, Passed: 21, Skipped: 0, Total: 21, Duration: 837 ms
```

## Commit Hash
```
fed417f fix(codewiki): INSTANTIATES가 target-typed new() 포착
```

## Key Implementation Notes
1. BaseObjectCreationExpressionSyntax는 ObjectCreationExpressionSyntax와 ImplicitObjectCreationExpressionSyntax의 공통 베이스
2. DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>() 한 번의 호출로 두 경우 모두 잡음
3. 기존 null 가드 로직 유지: `SymbolNodes.ForType()` 불가능 시 엣지 미생성
4. 다른 엣지 로직(CALLS, DECLARES) 미수정

## Concerns
None. 기존 테스트 완전 호환성 유지.
