# CodeWiki v2 — Source 시맨틱 주입 설계 (정본)

> **역할:** Phase 1(코어 ETL, Roslyn→Neo4j) 위에 **노드별 source 시맨틱**을 얹는 v2의 설계 정본.
> 2026-06-20 grill 세션으로 7개 결정을 명시화해 확정. 스키마 변경 0 — 기존 노드 props·적재 경로에
> 얹는다([codewiki-spec.md](codewiki-spec.md) §13 확장 지점). 이전 stub
> [_future/semantic-injection.md](_future/semantic-injection.md)을 대체한다(그 문서는 strazh 시절 가정 섞인 요약본).

## 목적

그래프의 각 노드에 **그 노드 소스의 의미**(무엇을 하는지·부수효과·함정)를 LLM으로 뽑아 저장한다.
구조적 연결(`BINDS_TO`/`DEFINES_COMMAND`/`EXECUTES`/`CALLS`/`IMPLEMENTS_METHOD`/`USES`)은 이미 Phase 1에
있으므로, **LLM은 의미만 보탠다.** 산출된 시맨틱은 화면 dossier·LLM 질의 컨텍스트의 재료가 된다(소비
레이어는 별도, 본 문서 범위 밖).

## 불변 원칙

1. **구조=결정론, 의미=LLM.** 그래프·Roslyn이 아는 것은 LLM에게 묻지 않는다(`keyEntities`=USES 엣지 등).
2. **시맨틱은 advisory.** ground truth는 코드. 결정론 필드와 LLM `summary`가 모순나면 자동 플래그.
3. **거칠고-정확 > 정밀하고-버그.** 무효화는 과(過)하더라도 단순·정확하게(VM 파일 통째 해시).
4. **돈 주고 만든 것은 영속.** 구조는 Roslyn으로 언제든 재생성, 시맨틱은 사이드카 파일로 영구 보존.

---

## 결정 기록 (grill 7문항)

### D1 — 입자 (어떤 노드에 저장하나)
비용이 아니라 **읽히는 입자**가 기준. 타입별 차등:
- **인터페이스 메서드 ~505** — 풀 시맨틱(경계 허브, E2E 의미 분기점).
- **VM 커맨드/이벤트 핸들러 전부** — 서버 도달 여부 무관(순수 UI 핸들러도 dossier엔 필요).
- **헬퍼·비핸들러 메서드** — 독립 저장 안 함. *호출하는 핸들러 요약의 context*로만 흡수.
- **프로퍼티 setter·보일러플레이트·생성자** — skip.

> 실측(`SearchOrderViewModel`): DECLARES 13 메서드 중 핸들러 6, 비핸들러 7. 커맨드 7개 중 6개가
> 서버 도달, `ResetCommand`(`ResetForm`)만 순수 UI → "서버 미경유, 로컬 초기화"로 명시 저장.

### D2 — 신뢰·갱신 (틀리거나 낡으면)
- **태깅:** props에 `summaryModel`·`summaryHash` 저장, 소비 시 advisory 명시.
- **모순 플래그:** 결정론 `mutatesState=false`인데 LLM `summary`가 "수정한다"면 자동 표시.
- **델타-스킵:** `summaryHash`로 변경분만 재-enrich(D6). 13k 규모에선 선택이 아니라 필수.

### D3 — 클라이언트 저장 단위
- **LLM 입력은 `*.xaml`이 아니라 `viewmodel.cs`.** XAML은 결정론 `uiLabel`/`eventKind`만 뽑는다(LLM 아님).
- **저장 위치 분리:** `uiLabel`/`eventKind`(결정론) → **Command 노드**, `summary`(코드 의미) → **Method(핸들러) 노드**.
  공유 핸들러(`EditOrder`가 `DoubleClickCommand`+`EditCommand` 둘에 연결)는 Method 저장 시 요약 1개로 자동 재사용, 중복·드리프트 0.
- **enrich 단위:** **VM당 LLM 1회** — `VM.cs` 통째를 캐시 프리픽스에 얹고 "VM 요약 + 핸들러 N개 요약"을 한 구조화 응답으로(화면 단위 자기완결).

### D4 — 필드 계약 (LLM 몫 vs 결정론)
| 필드 | 출처 | 방법 |
|---|---|---|
| `keyEntities` | **결정론** | 서버 impl `-[:USES]->(:Entity)` 그대로. LLM 금지 |
| `mutatesState` | **결정론** | impl 본문의 `repo.Insert/Update/Delete/SaveChanges` 호출 탐지. raw SQL(`CallRawSQL`)이면 `"unknown"` |
| `operationType` | **결정론** | `mutatesState` + 반환타입 상관 (query/command) |
| `summary` | **LLM** | 산문 요약 |
| `effects` | **LLM** | 엔티티 너머 부수효과(이메일·외부호출 등) |
| `caveats` | **LLM** | 미묘한 함정 |

> LLM 입력 슬라이스는 인터페이스의 **`Torba.Service` 서버 구현만**(클라 REST 프록시는 HTTP 송신뿐, 의미 없음).

### D5 — 영속·쓰기 경로
- **시맨틱은 `semantic.ndjson` 사이드카**(pk 키)에 산다. 구조 `graph.ndjson`과 분리 →
  `load --wipe`가 시맨틱을 날리지 않음, 구조는 무료 재생성.
- **`enrich`는 Neo4j live에서 `sourcePath` 조회** → 디스크 슬라이스 읽기 → Haiku 호출 → 사이드카 기록 **+** Neo4j upsert.
- **쓰기는 기존 적재 경로 재사용:** `MERGE (n{pk}) SET n += props`([CypherBuilder.cs:16](../src/CodeWiki/Storage/CypherBuilder.cs#L16)) — 신규 쓰기 코드 0.
- **`load`는 `graph.ndjson` 적재 후 `semantic.ndjson` 리플레이**로 props 덧입힘.

### D6 — `summaryHash` 구성
**`summaryHash = hash(LLM에게 보낸 입력 전체)`.**
- **VM 단위:** `hash(VM.cs 통째)` 하나 → 그 VM의 모든 요약(VM+핸들러)에 부착. 헬퍼가 같은 파일에 있으니 헬퍼 변경 자동 포착.
- **인터페이스 메서드:** `hash(서버 impl 슬라이스 + impl의 1-hop 헬퍼 번들)`.
- 재실행: 현재 입력 해시 ↔ 사이드카 저장 해시 비교, 같으면 그 단위 enrich 전부 skip.

### D7 — 첫 슬라이스(MVP)와 합격 기준
1. **`extract` 1회 재실행(~9분)** — `sourcePath`/`startLine`/`endLine`/`operationType`/`mutatesState`를 `graph.ndjson`에 적재(L0는 추출기 props 추가).
2. **`SearchOrder` 수직 슬라이스만** L0→L1→L2 끝까지 구현·실행 → `semantic.ndjson` 산출.
   대상: `SearchOrderViewModel`(핸들러 6) + `IOrderService.SearchOrdersAsync`(서버 impl 슬라이스).
3. **Haiku vs Sonnet** 3필드 나란히 비교.
4. **합격 기준 4개** — 통과 시에만 505+499 bulk 확장(Haiku, 합격 못 한 필드만 Sonnet 승급):
   - `summary`가 코드와 사실 일치
   - 결정론 필드와 **모순 0**
   - `caveats` 환각 0
   - `effects`에 실제 호출 근거 있음

---

## 노드 prop 스키마 (v2에서 채움)

| 노드 | 추가 props |
|---|---|
| `Method`(전체) | `sourcePath`, `startLine`, `endLine` (L0 결정론) |
| `Method`(핸들러) | `summary` (LLM), `summaryHash`, `summaryModel` |
| `Method`(인터페이스) | `mutatesState`·`operationType` (결정론) + `summary`·`effects`·`caveats` (LLM) + `summaryHash`·`summaryModel`. `keyEntities`는 별도 저장 없이 USES 엣지로 조회 |
| `Command` | `uiLabel`, `eventKind` (XAML 결정론) |
| `ViewModel` | `summary` (LLM), `viewPath` |

## 데이터 흐름

```
extract  ──▶ graph.ndjson      구조 + L0 결정론 props (sourcePath·lines·operationType·mutatesState)
enrich   ──▶ semantic.ndjson   pk → {summary, effects, caveats, summaryHash, summaryModel}
   │           Neo4j에서 sourcePath 조회 → 디스크 슬라이스(+1-hop 헬퍼) → Haiku(캐시 프리픽스, 구조화 출력)
   └─ 모드: --l1 (인터페이스 메서드 bulk) / --vm <path> (VM on-demand)
load     ──▶ graph.ndjson 적재 후 semantic.ndjson 리플레이 (둘 다 MERGE(n{pk}) SET n+=props)
```

## 비용

- 모델 **Haiku 4.5**(저가 티어), 공유 컨텍스트는 **prompt caching** 프리픽스로 고정 → 반복 입력 ~10%.
  비용을 죽이는 건 batch가 아니라 *caching + 싼 모델*(batch는 본문 토큰을 줄이지 않음, 오버헤드만 분산).
- 델타-스킵으로 재실행은 **변경된 파일 단위만** LLM 호출.

## 미결(전술·구현 단계에서 확정)

- bulk enrich 동시성·rate-limit 정책, 부분 실패 격리(프로젝트 단위 try/catch 유사).
- 구조화 출력 스키마(tool-use/JSON schema) 정확한 형태.
- `mutatesState="unknown"`(raw SQL) 노드의 LLM 보강 트리거 규칙.
- v1.5: `keyEntities`를 Entity 노드 엣지로 명시화. v2.x: 다중 파일 `BusinessRules`(코드 밖 사람 의도 — 별도 입력 필요).
