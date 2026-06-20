# [Phase 2 요약] 시맨틱 컨텍스트 주입

> **⚠️ 이 문서는 대체되었다 → [docs/codewiki-v2-spec.md](../codewiki-v2-spec.md)가 v2 설계 정본이다.**
> 아래는 strazh 시절 가정이 섞인 초기 요약 stub(보존용). 2026-06-20 grill 세션에서 입자·필드 계약·
> 영속 경로·갱신·MVP 게이트를 명시화해 v2-spec으로 확정했으니, 진행은 그 문서를 따른다.
>
> 전제: Phase 1 그래프가 완성돼 있어야 한다. 시맨틱은 **그 그래프에 props를 더 얹는 일**이다 — 스키마 변경 0([codewiki-spec.md](../codewiki-spec.md) §13 확장 지점).

## 목적

화면(ViewModel) 하나를 입력하면 **그 화면의 이벤트(버튼/loaded) → 백엔드 인터페이스 메서드 → 의미·소스위치**를 한 장의 HTML로 보여준다. 구조적 연결은 이미 그래프에 있으므로(Phase 1의 `BINDS_TO`/`DEFINES_COMMAND`/`EXECUTES`/`CALLS`/`IMPLEMENTS_METHOD`/`USES`), **LLM은 *의미*만 보탠다.**

## 핵심 원칙

1. **구조 = 결정론, 의미 = LLM.** 소스경로·`domainArea`·`uiLabel`·`dependsOnServices`는 전부 결정론 추출. "코드맵은 신뢰성이 생명."
2. **연결은 이미 그래프에 있다.** 실측상 VM Command의 ~90%가 백엔드 인터페이스 메서드에 닿음 → LLM은 사슬을 다시 찾지 않는다.
3. **보조(advisory) 레이어.** 시맨틱은 탐색 가속기·검증 후보일 뿐 권위가 아니다. 코드가 ground truth.
4. **dossier는 저장이 아니라 조립.** 원자 사실(요약·소스경로)을 쿡북 Cypher로 쿼리 시점 조립 → HTML.

## 3층 구조 (기본 절차)

| 층 | 타깃 | 산출 | 방식 |
|---|---|---|---|
| **L0 결정론** | 모든 Method/Command/VM | 소스경로·라인범위, `domainArea`, `dependsOnServices`, XAML `uiLabel`/`uiSection`/`eventKind` | Roslyn/XAML 추출기(props 키 추가) |
| **L1 인터페이스 메서드** | ~505 백엔드 인터페이스 메서드 | `summary`/`operationType`/`mutatesState`/`effects`/`keyEntities`/`caveats` | LLM bulk (`--enrich-semantic`) |
| **L2 화면 dossier** | VM 1개 + 그 이벤트들 | VM 요약 + 이벤트별 요약 | LLM on-demand (`--enrich-mv -p <VM.cs>`) |

절차 골자:
1. **L0** — Phase 1 추출기에 props 키만 추가(소스경로/도메인/XAML 파싱). 스키마 불변.
2. **L1** — 적재된 그래프에서 타깃 인터페이스 메서드 + 서버 impl `sourcePath` 조회 → 슬라이스 읽어 LLM(구조화 출력) → 노드 props upsert(같은 단일 적재 경로 재사용). 모델 Claude Sonnet 4.6 단일.
3. **L2** — VM 하나에 대해 자기완결: `VM.cs`+`View.xaml` 통째 + 이벤트(핸들러+1-hop 헬퍼) 요약 + 닿는 인터페이스 메서드 즉석 L1. 델타-스킵(`summaryHash`).
4. **HTML dossier** — 별도 skill이 쿡북 파라미터 Cypher로 중첩 조회 → 자기완결 단일 `.html`. CLI=쓰기(enrich), skill=읽기+렌더.

## 추가 노드 속성 (Phase 2에서 채움)

| 노드 | 추가 props |
|---|---|
| `Method`(전체) | `sourcePath`, `startLine`, `endLine` |
| `Method`(인터페이스) | L1 6필드 + `summaryHash`, `summaryModel` |
| `Command` | `uiLabel`, `uiSection`, `eventKind`, `summary` |
| `ViewModel` | `summary`, `dependsOnServices`, `viewPath` |

## 비용·향후

- 전체 일회성 ~$50–75. 화면 단위 enrich는 건당 수 센트.
- v1.5: `keyEntities` → Entity 노드 엣지 연결. v2: 다중 파일 `BusinessRules`. Risk: 그래프 토폴로지 결정론 계산. 검색 GUI / 커스텀 MCP `get_screen_dossier`.

> ⚠️ 위 CLI 플래그·모델·수치는 Phase 1 완료 시점에 재검증한다(원래 strazh 기준 설계였으므로 CodeWiki CLI 동사 체계로 재매핑 필요).
