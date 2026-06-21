# CodeWiki v2 MVP 게이트 — SearchOrder 시맨틱 검증 (PASS)

> 생성: CodeWiki v2 enrich 실행(2026-06-20) · 모델 Haiku 4.5(`claude-haiku-4-5-20251001`) · 대상 `SearchOrderViewModel` + `IOrderService.SearchOrdersAsync`
> 플랜: [docs/superpowers/plans/2026-06-20-codewiki-v2-semantic-injection.md](../superpowers/plans/2026-06-20-codewiki-v2-semantic-injection.md) Task 13 · PRD §9

## 결과 요약

v2 MVP(M0+M1) 엔드투엔드를 `SearchOrder` 수직 슬라이스로 라이브 검증했다. L0 결정론 props 추출·적재,
LLM enrich(VM 8 + iface 1 = 9 레코드), 사이드카 `--wipe` 생존, 델타-스킵 모두 통과. **합격 4기준 전부 PASS.**
Haiku 출력이 사실과 일치하고 결정론 필드와 모순 없으며 caveats/effects가 실제 소스에 근거함(스폿체크 확인).
→ **M2(대량 `--l1` + 전 화면) 착수 승인.**

## E2E 단계별 결과

| 단계 | 결과 |
|---|---|
| **M0 재추출** | 21,349 노드 / 72,522 엣지 (L0 props 포함), 0 실패 |
| **M0 적재** | `--wipe` 재적재. `SearchOrdersAsync` → `mutatesState=false`/`operationType=query`(읽기전용 정확), `SearchOrderAsync` sourcePath=`…/SearchOrderViewModel.cs:325` |
| **M1 enrich --vm** | 8 레코드 (VM 요약 1 + 핸들러 7개 pk; `EditOrder` 오버로드 2개 양쪽에 동일 요약 부착) |
| **M1 enrich --iface** | 1 레코드 (`SearchOrdersAsync`) |
| **Step5 사이드카 리플레이** | `load --wipe --semantic` 후 9 레코드 복원. L0 결정론 + L1 LLM 시맨틱 **모두 wipe 생존** ✅ |
| **Step6 델타-스킵** | 재실행 시 VM·iface 둘 다 `0 records`(해시 불변 → LLM 미호출) ✅ |

## 합격 4기준 판정

### 1. `summary` 코드와 사실 일치 — ✅
- iface `SearchOrdersAsync`: "주어진 필터 조건에 따라 주문을 비동기적으로 검색하여 결과를 반환한다"
- 핸들러 `SearchOrderAsync`: "현재 설정된 필터 조건으로 주문을 검색한다"
- VM: "주문을 검색·필터링하고 상세 조회, 편집, 엑셀 내보내기 기능을 제공하는 주문 검색 화면"
- `ResetForm`: "모든 검색 필터 조건을 초기화한다"(순수 UI, 서버 미경유 — 정확히 분류)

### 2. 결정론 필드와 모순 0 — ✅
`SearchOrdersAsync` 결정론 `mutatesState=false`/`operationType=query`. LLM summary·effects는 "검색하여 반환"·"데이터베이스 조회"로 **읽기 의미만** — "수정/저장/삭제" 없음. 모순 없음.
(최종 fix로 `OperationKind`를 리포지토리 수신자에 한정한 덕에, 결과 list.Add 등으로 인한 거짓 `command` 플래그가 발생하지 않음.)

### 3. `caveats` 환각 0 — ✅ (소스 스폿체크)
- `ExportExcelAsync` caveat "TotalPages > 10이면 경고 메시지 표시" → 소스 `SearchOrderViewModel.cs:399` `if (this.TotalPages > 10)` + `:401` `ShowMessage("Exporting more than ... Do you want to export all data?")` **정확히 일치**.
- `ChangePageAsync` caveat "pageIndex가 음수이면 동작하지 않음" → 합리적 추론(코드 본문 기반).

### 4. `effects`에 실제 호출 근거 — ✅ (소스 스폿체크)
- `SearchOrderAsync` effects "페이지 인덱스를 0으로 초기화하여 첫 페이지부터 검색" → 소스 `:338` `filter.PageIndex = 0;` **정확히 일치**.
- `EditOrder` effects "NavigationParameter로 주문 ID 전달, ContentRegion에 Order.EditOrder 뷰 로드" → Prism 내비게이션 코드 기반.
- iface effects "데이터베이스 조회 수행, 검색 결과를 SearchOrderDTO로 변환" → 서버 구현 슬라이스 기반(소스 직접읽기 비교에서 나온 `SearchOrderDTO` 프로젝션과 일치).

## 모델·비용 메모
- Haiku 4.5 단독으로 4기준 전부 통과 → Sonnet 승급 불필요(MVP 한정 결론).
- prompt caching: 1차 호출이라 `cache_read=0` 정상. 반복/대량(M2)에서 system 프리픽스 캐시 효과 확인 예정.

## 산출물
- `out/semantic.ndjson` — 9 레코드(사이드카). 구조 `out/graph.ndjson`과 분리, `--wipe` 안전.
- Neo4j: 노드에 `summary`/`effects`/`caveats`/`summaryHash`/`summaryModel` props 적재됨.

## 다음 (M2/M3, 후속 계획)
- 대량 `--l1`(서버 인터페이스 ~505) + 전 화면(~499) enrich, 동시성·rate-limit·부분 실패 격리.
- 최종 리뷰 이월 항목: `--iface` name→fullName 한정(동명 인터페이스 메서드 대비), partial-class VM.cs 경로 가정 보강, `OperationKind` RawSql 스코프.
