# SDD 진행 레저 — CodeWiki 코어 ETL

- 플랜: docs/core-etl-plan.md (21 태스크)
- 브랜치: feat/codewiki-core-etl

## 완료
(아직 없음)

## 완료
- Task 1: complete (commit a4c2ec9..5644ca0, review clean — spec ✅ / quality approved)
  - 수용: `dotnet new sln`이 신형 `.slnx` 생성(브리프의 .sln 대비) — deliverable 충족·CLI 동작 확인, 실질 갭 아님.
  - 추적(Minor, 최종 리뷰): NU1903 — 전이 의존 System.Security.Cryptography.Xml 9.0.0 취약점. 어느 패키지가 끌어오는지 후속 태스크에서 특정 후 pin 검토.
- Task 2: complete (commit 5644ca0..27257ce, review clean — spec ✅ / quality ✅). Minor: NU1608 CodeAnalysis 버전 경고(pre-existing, 추적).
- Task 3: complete (commit 27257ce..d51fa55, review clean — spec ✅ / quality ✅).
- Task 4: complete (commit d51fa55..a72ba8c, review clean — spec ✅ / quality ✅).
- Task 5: complete (commit a72ba8c..eb6aba3, review clean — spec ✅ / quality ✅ w/ adjudicated minors).
  - I-1(메서드 full에 파라미터 포함 의심) → 해소: Names.Fmt memberOptions=None이라 full=파라미터 없음, 3인자 pk가 오버로드 구분(정확).
  - 추적(Minor): modifiers 값이 ProtectedOrInternal→"protectedorinternal"(C# 키워드와 불일치, 쿼리 비경로). FileNodes 단위테스트 없음(T8/T15가 간접 커버).
  - 메모: RoleClassifier.cs를 최소 스텁으로 생성함 — T7이 실제 로직으로 교체해야 함.
- Task 6: complete (commit eb6aba3..fc684f0, review clean — spec ✅ / quality ✅).
- Task 7: complete (commit fc684f0..77e7668, review clean — spec ✅ / quality ✅ w/ adjudicated minors).
  - 추적(Minor): 휴리스틱 5종(Entity/Controller/Service/Repository/DTO) 단위테스트 부재(T8+/T21 간접 커버). Inherits()가 짧은이름 비교(동명 외부 BaseType 오탐 가능하나 Vanuatu=Prism 단일이라 저위험, 참조구현과 일치).
- Task 8: complete (commit 77e7668..49d0282, review clean — spec ✅ / quality ✅, 블로커 없음).
  - 코드 정확(t.Interfaces 직접구현만=올바름, 불변식#3 동작). 보고서가 'AllInterfaces'로 오기재(코드는 Interfaces, 무해). 추적(Minor): Empty dict가 TypeExtractor/SymbolNodes 중복 정의(DRY).
- Task 9: complete (commit 49d0282..fed417f, review ✅ + fix 적용).
  - fix: INSTANTIATES가 target-typed new() 포착(BaseObjectCreationExpressionSyntax). TypeExtractorTests 6/6, 전체 21/21.
  - 수용: 생성자 DECLARES 포함(메서드로 타당). 추적(Minor, 최종리뷰): CALLS가 프레임워크 메서드도 노드화(무제한, 플랜 의도=superset) — 그래프 노이즈 시 도메인 필터 검토.
- Task 10: complete (commit fed417f..933432c, review clean — spec ✅ / quality ✅).
  - 중요(긍정): Names.cs에 IncludeContainingType 추가 — 메서드 fullName이 N.Svc.Do로 정규화(쿡북 정합). 타입 fullName 무영향, 오버로드 구분 유지(검증됨). 전체 22 테스트 통과.
  - 추적(Minor): 인터페이스 프로퍼티/이벤트는 IMPLEMENTS_METHOD 미생성(스펙=메서드만, 향후 확장점). Empty dict 중복(반복 누적).
