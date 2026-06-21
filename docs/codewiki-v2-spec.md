# CodeWiki v2 — Source 시맨틱 주입 PRD

> **상태:** 설계 확정(2026-06-20 grill 세션). Phase 1(코어 ETL) 완료 후 진행.
> **한 줄 요약:** Phase 1이 만든 코드 그래프의 각 노드에, **"이 코드가 무슨 일을 하는지"** 를
> 값싼 LLM으로 한 번 뽑아 붙여두는 작업. 구조는 그대로 두고 *의미*만 얹는다. 스키마 변경 0.

---

## 1. 왜 필요한가 (배경)

Phase 1 그래프는 Vanuatu 코드의 **뼈대**다 — "어느 화면이 어느 ViewModel을 쓰고, 그 버튼이 어느
서버 메서드를 거쳐 어느 테이블 엔티티에 닿는지"를 미리 이어둔 ER 다이어그램. 이 뼈대 덕분에
**"어디→어디"** 질문은 아주 싸게 답한다.

하지만 그래프엔 **소스 본문이 없다.** 그래서 **"그래서 그게 무슨 일을 하는데?"** 는 답 못 한다.
실측 비교가 이걸 분명히 보여줬다(같은 질문 "SearchOrder 검색 버튼 → 종착 엔티티"):

| 방식 | 토큰 | 시간 | 답의 품질 |
|---|---|---|---|
| **그래프로 구조 추적** | ~10k | ~8초 | "→ `Order`" (어디까지 가는지) ✅ |
| **소스를 직접 읽어 의미 파악** | ~86k | ~78초 | "읽기 전용 필터 검색, `Order` 조회" (무슨 일인지) ✅ |

→ 구조는 그래프가 **8배 싸고 10배 빠르다.** 의미는 소스를 읽어야 나오는데 **매번 4,853줄짜리
서비스 파일을 다시 읽는 건 비싸다.** v2의 아이디어는 단순하다:

> **그 비싼 "소스 읽고 의미 뽑기"를 한 번만 해서 그래프 노드에 박아두자.**
> 그러면 다음부터는 그래프 한 번 순회로 *구조 + 의미*를 동시에, 싸게 얻는다.

## 2. 목표 / 비목표

**목표**
- 그래프의 의미 있는 노드(서버 인터페이스 메서드, 화면 핸들러)에 **요약·부수효과·주의점**을 저장한다.
- 한 번 만든 의미는 **파일로 영구 보존**하고, 코드가 바뀐 부분만 다시 만든다(델타-스킵).
- **싸게** 한다(값싼 모델 + 캐싱). 전체 일회성 비용을 가능한 한 낮춘다.

**비목표 (이번엔 안 한다)**
- 의미를 보여주는 **화면/문서 렌더링**(HTML dossier 등) — 이 PRD는 *저장*까지. 소비는 별도.
- **테이블명·DB 스키마** — 엔티티명(`Order`)까지면 충분(`USES`가 `Repository<T>`의 `T`까지).
- **코드 밖 사람의 의도**(왜 이렇게 짰나·비즈니스 규칙) — LLM은 코드만 보고 추론하므로 못 만든다.
  진짜 사람 의도가 필요하면 별도 입력(주석·설계노트)을 먹여야 함 → v2.x `BusinessRules`로 후순위.

## 3. 누가 쓰나 (사용 시나리오)

1. **개발자가 화면 하나를 맡았을 때** — "이 화면의 버튼들이 각각 뭘 하고 서버에서 무슨 일이 나나"를
   그래프 질의 한 번으로 본다(요약까지 같이 나옴).
2. **LLM 에이전트(나 같은)가 코드 질문에 답할 때** — 소스를 새로 읽지 않고, 그래프에 박힌 의미로
   바로 답한다. §1 비교의 86k 토큰을 거의 0으로.

## 4. 핵심 원칙 (설계 전체를 지배하는 4가지)

1. **구조는 결정론, 의미만 LLM.** 그래프·Roslyn이 *이미 아는 사실*은 LLM에게 묻지 않는다.
   (예: "이 메서드가 어느 엔티티를 만지나"는 그래프의 `USES` 엣지에 이미 있음 → LLM 금지.)
2. **의미는 보조(advisory), 코드가 진실.** LLM 요약은 틀릴 수 있으니 출처·모델을 태깅하고,
   결정론 사실과 모순나면 자동으로 플래그를 띄운다.
3. **거칠지만 정확하게 > 정밀하지만 버그.** 무효화 판정은 단순하게(파일 통째 해시) 가서
   "놓치는 변경"이 없게 한다. 조금 과하게 다시 만드는 건 값이 싸니 감수.
4. **돈 주고 만든 의미는 영속.** 구조는 Roslyn으로 언제든 공짜 재생성. 의미는 LLM 비용이 드니
   별도 파일에 저장해 `--wipe`에도 살아남게 한다.

## 5. 무엇을 저장하나 (요구사항)

### 5.1 어떤 노드에 (입자)
**"읽힐 노드"** 에만 저장한다. 비용이 싸도 아무도 안 읽을 노드에 박으면 그래프 노이즈일 뿐.

- ✅ **서버 인터페이스 메서드(~505개)** — 클라/서버 경계의 허브. E2E 의미의 핵심.
- ✅ **화면(ViewModel) 핸들러 전부** — 버튼/이벤트가 실행하는 메서드. **서버에 안 닿는 순수 UI
  핸들러도 포함**(예: Reset 버튼 = "로컬 폼 초기화, 서버 미경유" — 이것도 화면 설명에 필요).
- ❌ **헬퍼 메서드** — 독립 저장 안 함. 대신 *그걸 부르는 핸들러 요약을 만들 때 같이 읽어* 녹인다.
- ❌ **프로퍼티 setter·생성자·보일러플레이트** — 건너뜀.

> 실측(`SearchOrderViewModel`): 메서드 13개 중 핸들러는 6개(`SearchOrderAsync`, `EditOrder`,
> `ExportExcelAsync`, `ChangePageAsync`, `ShowOrderDetails`, `ResetForm`), 나머지 7개는 헬퍼·잡일.

### 5.2 무슨 값을 (필드)
필드마다 **출처가 코드(결정론)인지 LLM인지**를 엄격히 가른다:

| 필드 | 뜻 | 출처 |
|---|---|---|
| `summary` | 한 줄 요약(무슨 일을 하나) | **LLM** |
| `effects` | 엔티티 너머 부수효과(이메일 발송·외부 호출 등) | **LLM** |
| `caveats` | 미묘한 함정·주의점 | **LLM** |
| `keyEntities` | 건드리는 엔티티 | **결정론** — `USES` 엣지 그대로(LLM 금지) |
| `mutatesState` | 데이터를 바꾸나(읽기/쓰기) | **결정론** — 본문이 `repo.Insert/Update/Delete/SaveChanges` 부르는지 탐지 |
| `operationType` | query / command | **결정론** — `mutatesState` + 반환타입 |

> LLM이 실제로 답하는 건 **`summary`·`effects`·`caveats` 3개뿐.** 나머지는 코드가 보증하므로
> 틀릴 일이 없고, 비용도 그만큼 싸다. 그리고 "LLM이 `summary`에 '수정한다'고 썼는데 결정론
> `mutatesState=false`"면 자동 모순 플래그가 뜬다.
>
> raw SQL(`CallRawSQL`)은 결정론 탐지의 사각지대 → 그 노드는 `mutatesState="unknown"`으로 두고
> 따로 LLM 보강 후보로 태깅.

### 5.3 어디에 저장하나
- **화면 핸들러의 `summary` → Method 노드.** (Command 노드가 아니라.) 한 핸들러를 두 버튼이
  공유하면(`EditOrder`를 더블클릭+편집 둘이 씀) 요약 하나로 자동 재사용되어 중복이 없다.
- **버튼 메타(`uiLabel`="Search", `eventKind`) → Command 노드.** 이건 XAML에서 **결정론**으로 뽑는다
  (LLM 아님). 즉 LLM은 `*.xaml`이 아니라 `viewmodel.cs`를 읽고, XAML은 버튼 텍스트 같은 사실만 준다.
- **서버 의미 → 인터페이스 메서드 노드.** 화면 핸들러에 서버 의미를 복사하지 않는다. 기존
  `CALLS → IMPLEMENTS_METHOD` 엣지로 이어져 있으니 질의 때 조인하면 된다.

## 6. 어떻게 만드나 (파이프라인)

### 6.1 전체 흐름
```
extract  ──▶ graph.ndjson      (Phase 1 구조 + L0 결정론 props 추가)
                                 sourcePath·startLine·endLine·operationType·mutatesState
enrich   ──▶ semantic.ndjson    (신규 단계) pk → {summary, effects, caveats, summaryHash, summaryModel}
   │           Neo4j에서 노드의 sourcePath 조회 → 디스크에서 그 소스 슬라이스 읽기
   │           → 값싼 LLM 호출(캐시 프리픽스 + 구조화 출력) → 사이드카 기록 + Neo4j upsert
   └─ 두 모드:  --l1  (서버 인터페이스 메서드 일괄)
               --vm <path>  (화면 1개, 그 VM의 핸들러 일괄)
load     ──▶ graph.ndjson 적재 후 semantic.ndjson 리플레이
                                 둘 다 같은 한 줄: MERGE (n {pk}) SET n += props  → 신규 쓰기 코드 0
```

### 6.2 핵심 메커니즘 셋
- **사이드카 분리.** 의미는 구조(`graph.ndjson`)와 **다른 파일(`semantic.ndjson`)** 에 산다. 그래서
  `load --wipe`로 그래프를 날렸다 다시 올려도, 사이드카 리플레이로 의미가 복원된다. 구조는 공짜
  재생성, 의미는 산 채로 보존.
- **VM당 LLM 1회.** 한 화면을 enrich할 때 `viewmodel.cs` **파일 통째**를 캐시 프리픽스에 얹고,
  "화면 요약 + 핸들러 N개 요약"을 **한 번의 구조화 응답**으로 받는다. 헬퍼가 같은 파일에 있으니
  자연히 같이 읽힌다. 비용↓, 화면 일관성↑.
- **델타-스킵 (`summaryHash`).** `summaryHash = hash(LLM에게 보낸 입력 전체)`.
  - 화면: `hash(viewmodel.cs 통째)` → 그 화면의 모든 요약에 부착. 파일이 안 바뀌면 통째 skip,
    헬퍼만 바뀌어도 파일 해시가 달라지니 자동으로 다시 만든다.
  - 인터페이스 메서드: `hash(서버 구현 슬라이스 + 그 구현이 부르는 1-hop 헬퍼)`.

## 7. 노드 스키마 (v2에서 채우는 props)

| 노드 | 결정론 props | LLM props |
|---|---|---|
| `Method` (전체) | `sourcePath`, `startLine`, `endLine` | — |
| `Method` (화면 핸들러) | ↑ | `summary` (+ `summaryHash`, `summaryModel`) |
| `Method` (서버 인터페이스) | ↑ + `mutatesState`, `operationType` | `summary`, `effects`, `caveats` (+ `summaryHash`, `summaryModel`) |
| `Command` | `uiLabel`, `eventKind` (XAML) | — |
| `ViewModel` | `viewPath` | `summary` |

> `keyEntities`는 별도 prop으로 저장하지 않는다 — `USES` 엣지로 조회.

## 8. 비용

- 모델 **Haiku 4.5**(값싼 티어)로 시작. 작업이 "무슨 일을 하나 / 읽기냐 쓰기냐" 같은 경계가 분명한
  요약·분류라 값싼 모델로 충분하다는 가설(§9 MVP에서 검증).
- 비용 레버는 **batch가 아니라 caching + 싼 모델.** batch는 보낸 코드 토큰 합을 줄이지 못한다
  (선형). 공유 컨텍스트(스키마·지시문)를 **prompt cache 프리픽스**로 고정해야 반복 입력이 ~10%로 준다.
- 델타-스킵으로 재실행은 **바뀐 파일 단위만** LLM을 부른다.

## 9. MVP — 첫 수직 슬라이스와 합격 기준

505개 + 499개에 LLM을 풀기 **전에**, 우리가 속속들이 아는 화면 하나로 끝까지 한 번 돌려 검증한다.

1. **`extract` 1회 재실행(~9분)** — `sourcePath`/lines/`operationType`/`mutatesState`를 그래프에 적재
   (L0는 기존 추출기에 props 키만 추가).
2. **`SearchOrder` 수직 슬라이스만** enrich:
   `SearchOrderViewModel`(핸들러 6개) + `IOrderService.SearchOrdersAsync`(서버 구현 슬라이스).
3. **Haiku vs Sonnet** 3필드를 나란히 비교.
4. **합격 기준 4개** — 모두 통과해야 505+499 일괄 확장(Haiku, 합격 못 한 필드만 Sonnet 승급):
   - `summary`가 코드와 사실 일치
   - 결정론 필드(`mutatesState` 등)와 **모순 0**
   - `caveats`에 환각 0
   - `effects`에 실제 호출 근거 있음

> 이 첫 슬라이스 산출물이 그대로 회귀 테스트 픽스처가 된다(정답: `Order` 종착·읽기전용·필터검색).

## 10. 마일스톤

| 단계 | 내용 | 산출 |
|---|---|---|
| **M0** | L0 결정론 props를 추출기에 추가 + `extract` 재실행 | `sourcePath` 등 담긴 `graph.ndjson` |
| **M1** | `enrich --vm` 구현 → `SearchOrder` 슬라이스 1개 | `semantic.ndjson`(부분) + 합격 판정 |
| **M2** | 합격 시 `enrich --l1` 일괄(서버 인터페이스 ~505) | 서버 의미 채워진 그래프 |
| **M3** | `enrich --vm` 전 화면(~499) + `load` 리플레이 통합 | 의미 완비 그래프 |

## 11. 리스크 / 미결 (구현 단계에서 확정)

- **Haiku 품질** — MVP 게이트(§9)로 검증 후 결정. 실패 필드만 Sonnet 승급.
- **raw SQL 사각지대** — `mutatesState="unknown"` 노드의 LLM 보강 트리거 규칙 미정.
- **일괄 실행 운영** — 동시성·rate-limit, 부분 실패 격리(Phase 1의 프로젝트 단위 try/catch 유사).
- **구조화 출력 형식** — tool-use/JSON schema 정확한 스키마 미정.
- **후속** — v1.5: `keyEntities`를 Entity 노드 엣지로 명시화. v2.x: 다중 파일 `BusinessRules`
  (코드 밖 사람 의도 — 별도 입력 필요).

---

## 부록 — `SearchOrder` 화면으로 보는 전체 그림

검색 버튼을 예로 v2가 무엇을 더해주는지:

```
[현재 Phase 1 — 구조만]
SearchCommand → SearchOrderAsync → IOrderService.SearchOrdersAsync → (Torba)OrderService.SearchOrdersAsync → Order

[v2 후 — 구조 + 의미가 노드에 박힘]
SearchCommand {uiLabel:"Search", eventKind:"Click"}
  → SearchOrderAsync {summary:"입력한 필터로 주문을 검색해 그리드에 채운다"}
    → IOrderService.SearchOrdersAsync {
         summary:"필터 조건으로 주문 목록 조회",
         operationType:"query", mutatesState:false,    ← 결정론
         keyEntities:[Order],                            ← USES 엣지
         effects:[], caveats:"페이징 파라미터 필수"       ← LLM
      }
    → Order
```

즉 같은 한 번의 그래프 순회로, 이제 **"어디까지 가나(Order)" 와 "무슨 일을 하나(읽기전용 필터검색)"
를 동시에, 소스를 다시 안 읽고** 얻는다. §1 비교의 86k 토큰짜리 소스 읽기가 일회성으로 끝난다.
