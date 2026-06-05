# 🌐 거대 C# 프로젝트 엔터프라이즈 아키텍처 분석 파이프라인 (PRD v2)

> WPF(프론트엔드)와 C# Web API(백엔드)가 공통 인터페이스 및 DTO 레이어로 결합된 거대 C# 솔루션(Vanuatu)의 유지보수 효율을 극대화하기 위한 **'Roslyn + Neo4j + MCP 클라우드 LLM'** 기반 정적 분석·아키텍처 시각화 ETL 파이프라인.
>
> **본 v2는 실제 코드베이스를 검증하며 진행한 설계 그릴(grill) 세션의 결론을 반영한 개정본입니다.** v1의 잘못된 연결 모델(컨트롤러가 인터페이스를 구현한다는 가정)을 바로잡고, 12개 아키텍처 결정과 신규 추출 항목을 명문화했습니다.

---

## 1. 아키텍처 개요 및 해결하려는 핵심 난제

### 💻 대상 시스템 구조 (검증됨: 46개 프로젝트, ~2,351개 .cs, net10 / net10-windows7.0)

- **Frontend**: WPF (Prism MVVM, `ViewModelLocator.AutoWireViewModel`, Telerik)
- **Backend**: ASP.NET Core REST API (Repository 패턴 + EF Core)
- **Core/공유 레이어**: 공통 Interface 추상화(`Vanuatu.Service`) 및 DTO(`Vanuatu.DTO`)
- **상호작용 (실제 구조)**: 프론트엔드의 **REST 클라이언트 프록시**가 공통 인터페이스를 구현해 HTTP 경로로 백엔드를 호출하고, 백엔드 **컨트롤러**가 라우트로 요청을 받아 **서버측 서비스**(같은 인터페이스 구현)에 위임하는 구조.

- **Root**: `C:\develop\baw\phase2\baw-phase2-platform\Vanuatu`
```
Vanuatu/
├── Server/                        # 백엔드 (ASP.NET Core REST API)
│   ├── Torba.Workbench/           # API 진입점 (Controllers, Startup.cs DI 등록)
│   ├── Torba.Service/             # 서버측 비즈니스 로직 (I*Service 구현)
│   └── Torba.DAL/                 # 데이터 접근 (EF Core, Repository<T>, VanuatuContext)
├── Client/                        # 프론트엔드 (WPF)
│   ├── App/Shefa.App.BAWPos/      # 메인 POS 앱 (Prism 부트스트랩·DI 등록)
│   ├── Shefa.Service/             # REST API 클라이언트 (AbstractRestAPI + RestAPI/*Service)
│   └── Module/Shefa.Module.*/     # 기능 모듈 (Views/ + ViewModels/)
├── Domain/                        # 공유 계층
│   ├── Vanuatu.DTO/               # 공유 DTO
│   └── Vanuatu.Service/           # 서비스 계약 인터페이스 (I*Service, *Filter)
├── Vanuatu.Core/                  # 공유 베이스 (IBaseEntity)
└── ...
```

### 🛑 기존 유지보수의 한계 (The Problem)

일반 IDE나 텍스트 기반 LLM은 두 경계선에서 호출 흐름(Call Chain)이 물리적으로 단절되어 **화면→DB End-to-End 추적이 불가능**합니다.

1. **네트워크 경계**: WPF ViewModel → 백엔드 Controller 호출이 HTTP 경계에서 단절
2. **추상화 경계**: 공통 Interface/DTO 참조 시 실제 구현체·EF Core 레이어로의 추적 단절

---

## 2. 🔑 핵심 통찰 — 경계는 어떻게 실제로 연결되는가 (코드 검증 결과)

> v1은 "컨트롤러가 인터페이스를 구현(`IMPLEMENTS_METHOD`)하므로 그것이 연결고리"라고 가정했으나, **이는 사실이 아니다.** 실제 검증 결과:

- **클라이언트 프록시** `Client/Shefa.Service/RestAPI/OrderService : AbstractRestAPI, IOrderService`
  → 메서드마다 `path = "/api/pos/ordercomment/search"` 같은 **하드코딩 HTTP 경로**로 호출.
- **컨트롤러** `Server/Torba.Workbench/Controllers/OrderController : ControllerBase`
  → **인터페이스를 구현하지 않음.** `[Route("api/pos")]` + 액션별 `[Route(...)]` 속성을 갖고, `IOrderService`를 주입받아 위임(`return await _orderService.X()`). HTTP 경계는 **라우트 문자열로만** 연결.
- **서버 서비스** `Server/Torba.Service/Order/OrderService : IOrderService`
  → 실제 서버측 구현.

**검증된 컨벤션 (견고):** 클라이언트 `OrderService` **71개 메서드 = 서버 `OrderService` 71개 메서드 = `IOrderService`** 가 정확히 일치.

### ⇒ 채택한 경계 연결 전략

**경로 A — 공유 인터페이스 메서드 조인 (1차 진실 소스):**
```
클라 OrderService.SearchOrdersAsync ─[IMPLEMENTS_METHOD]─► IOrderService.SearchOrdersAsync ◄─[IMPLEMENTS_METHOD]─ 서버 OrderService.SearchOrdersAsync
```
- 클라 프록시와 서버 서비스가 **동일 인터페이스 멤버**를 구현 → 공유 인터페이스 MethodNode가 그래프에서 두 컴파일을 봉합. Roslyn `FindImplementationForInterfaceMember` / `ExplicitOrImplicitInterfaceImplementations` 기반, **문자열 파싱 0**.
- 컨벤션이 71/71로 완벽해 깨질 일이 거의 없음.

**경로 B — 라우트 문자열 조인 (후순위, YAGNI):**
클라 `path` 리터럴 ↔ 컨트롤러 `[Route]` 속성 매칭. 진짜 HTTP URL과 컨트롤러를 잡지만, 문자열 정규화·`{param}` 템플릿 매칭이 필요해 정확한 URL이 필요해질 때만 추가.

---

## 3. 🛠️ 3단계 파이프라인 (Tech Stack)

```text
[Vanuatu.sln — 46개 프로젝트, 단일 메가 컴파일]
       ⬇️ (1단계: Roslyn 정적 분석 — Strazh 포크·확장)
[triples.ndjson — 트리플 한 줄씩(중간 산출물)]
       ⬇️ (2단계: 배치 로더가 UNWIND $batch ... MERGE 로 Neo4j 적재, 매 실행 wipe & reload)
[Neo4j 아키텍처 그래프 — 다중 라벨 속성 그래프]
       ⬇️ (3단계: 공식 mcp-neo4j-cypher(읽기전용) + 스키마 쿡북)
[Claude Opus 4.8 등 클라우드 LLM의 자연어 흐름 추적·분석]
```

### ① Roslyn 추출 (Strazh 포크·확장)
- 구문 트리(SyntaxTree) + 의미 분석 모델(SemanticModel) 기반의 컴파일러 수준 정확도.
- **빌드 전제는 검증 완료**: Strazh가 이 코드베이스에서 ETL을 이미 성공시켜 메가 컴파일 골격이 입증됨. 자체 ETL 재작성 동기는 빌드 문제가 아니라 **Strazh의 추출 정보 부족**(아래 §5).

### ② Neo4j 적재
- **중간 NDJSON → 배치 적재**: 추출(느린 컴파일)과 적재를 분리해, Cypher/스키마 튜닝 시 **재컴파일 없이 재적재**. Strazh의 "트리플당 1쿼리 왕복" 대신 `UNWIND`로 수천 개씩 묶어 고속 적재.
- **Wipe & Reload**: 매 실행 `MATCH (n) DETACH DELETE n` 후 전체 재적재. 유지보수 단계라 변경 빈도가 낮아 증분 불필요. 단 **노드 키는 안정 해시**(FullName 기반)로 — Strazh의 `string.GetHashCode()`는 프로세스마다 랜덤화되어 재실행 간 노드가 안 합쳐짐.
- `(Label, pk)` 유니크 제약으로 MERGE 정합성 보장.

### ③ MCP 기반 클라우드 LLM 연동
- 오픈소스라 보안 이슈가 없어 최강 추론 모델을 활용. LLM은 소스코드를 직접 읽지 않고 **로컬 Neo4j에 Cypher 쿼리**만 날려 가벼운 구조 데이터만 가져감.
- **공식 `mcp-neo4j-cypher`를 읽기전용 Neo4j 사용자로 등록** + 노드 라벨/엣지 타입/경계 조인 패턴/활용사례별 예제 Cypher를 담은 **스키마 쿡북**을 LLM 컨텍스트로 주입. LLM이 반복적으로 틀리는 쿼리만 커스텀 MCP 도구로 승격(쿡북이 곧 도구 명세).

---

## 4. 🧩 추출 스키마 (노드 / 엣지 / 역할 라벨)

### 노드 — 다중 라벨
모든 타입은 `:Class` 또는 `:Interface` + 휴리스틱 매칭 시 **2차 역할 라벨**:

| 역할 라벨 | 판별 휴리스틱 |
|---|---|
| `:Entity` | `IBaseEntity` 구현 (Vanuatu.Core) |
| `:ViewModel` | `BindableBase` 상속 / `*ViewModel` |
| `:Controller` | `ControllerBase` 상속 / `*Controller` |
| `:Service` | `Vanuatu.Service`의 `I*Service` 구현 |
| `:Repository` | `IRepository<T>` / `Repository<T>` |
| `:DTO` | `Vanuatu.DTO` 네임스페이스 / `*DTO` |
| `:View` | `x:Class`를 가진 `.xaml` |

그 외 구조 노드: `:Method`, `:Command`(ViewModel 속성), `:Project`, `:File`/`:Folder` 등. 애매하면 역할 라벨 생략(거짓 구조 방지).

### 엣지 (관계 타입)
- 체인: `BINDS_TO`(View→VM), `HAS`(VM→Command), `EXECUTES`(Command→핸들러), `CALLS`(메서드→메서드), `IMPLEMENTS_METHOD`(구현→인터페이스 멤버), `USES`(메서드→`IRepository<T>` 필드/Entity)
- 영향도: `USES_TYPE`(메서드/클래스 → 타입) — 파라미터/반환/필드 타입 + 객체생성 지점 **(타입 레벨)**
- DI: `REGISTERS`(인터페이스 → 구현, 속성 `lifetime`)
- 구조(Strazh 계승): `OF_TYPE`, `DECLARED_AT`, `INCLUDED_IN`, `DEPENDS_ON`, `CONTAINS`

---

## 5. 🆕 Strazh가 안 하는 신규 추출 4종 (= "내 ETL"의 존재 이유)

| # | 신규 추출 | Roslyn 근거 |
|---|---|---|
| 1 | **메서드 레벨 `IMPLEMENTS_METHOD`** (Strazh는 타입 레벨 `OF_TYPE`만) | `FindImplementationForInterfaceMember` / `ExplicitOrImplicitInterfaceImplementations` |
| 2 | **`Command → 핸들러`** | `new DelegateCommand(ExecuteX)` 인자 메서드 참조 |
| 3 | **`View → ViewModel`** | Prism `AutoWireViewModel` 네이밍 컨벤션 매칭 (`XView`→`XViewModel`) |
| 4 | **메서드 본문의 제네릭 `IRepository<T>` 필드 사용 추적** | 메서드 본문 식별자 → 필드 → 제네릭 타입 인자(Entity) |

### 국소 수술 3종 (Strazh 기존 코드 수정)
1. 트리플당 1쿼리 → **UNWIND 배치 적재**
2. `FullName.GetHashCode()` Pk → **안정 해시**
3. 노드 모델 확장 → **역할 라벨 + NDJSON 출력**

---

## 6. 📐 아키텍처 결정 요약 (그릴 세션 확정)

| # | 결정 | 후순위(YAGNI 보류) |
|---|---|---|
| 1 | 자체 ETL, 완전성+YAGNI | — |
| 2 | 경계 = 공유 인터페이스 조인(A) | 라우트 문자열 매칭(B) |
| 3 | 위쪽 끝 = VM·Command·핸들러 (View→VM은 Prism 네이밍) | Button→Command XAML 파싱 |
| 4 | 아래쪽 끝 = 메서드→`IRepository<T>`→Entity | 물리 테이블명 해석 |
| 5 | 단일 메가 컴파일 (빌드 검증 완료) | — |
| 6 | Wipe & Reload + 안정 해시 키 | 증분 MERGE / stale 스윕 |
| 7 | 중간 NDJSON + UNWIND 배치 적재 | — |
| 8 | 영향도 = 타입 레벨 `USES_TYPE` | 프로퍼티 레벨 멤버접근 |
| 9 | DI 등록 사실만; 탐지는 Cypher/LLM | 추출기 내 그래프 알고리즘 |
| 10 | 공식 MCP(읽기전용) + 스키마 쿡북 | 커스텀 MCP 도구 |
| 11 | 다중 라벨 (`:Class:ViewModel`) | role 프로퍼티 방식 |
| 12 | Strazh 포크·확장 (MIT) | 그린필드 |

---

## 7. 🗺️ 시각화된 End-to-End 데이터 흐름 (수정된 예시)

WPF 버튼 클릭부터 EF Core Entity(DB 종착점)까지, **공유 인터페이스 노드를 경유해** 한 줄로 관통됩니다.

```text
[View: SearchOrderView.xaml]
      │ [:BINDS_TO]  (Prism AutoWireViewModel 네이밍)
      ▼
[ViewModel: SearchOrderViewModel] ──[:HAS]──► [Command: SearchCommand]
                                                  │ [:EXECUTES]  (DelegateCommand(ExecuteSearch))
                                                  ▼
                                            [Method: ExecuteSearch]
                                                  │ [:CALLS]
                                                  ▼
   ┌───────────────── 네트워크 경계 (공유 인터페이스 조인) ─────────────────┐
   │  [클라 프록시: OrderService.SearchOrdersAsync]                          │
   │                    │ [:IMPLEMENTS_METHOD]                              │
   │                    ▼                                                   │
   │            [Interface: IOrderService.SearchOrdersAsync(SearchOrderFilter)]
   │                    ▲                                                   │
   │                    │ [:IMPLEMENTS_METHOD]                              │
   │  [서버 서비스: OrderService.SearchOrdersAsync]                          │
   └────────────────────────────────────────────────────────────────────┘
                                                  │ [:USES]  (메서드 본문 _orderRepository 참조)
                                                  ▼
                            [Repository: IRepository<Order>]
                                                  │
                                                  ▼
                            [Entity: Order]  ── DB 종착점 (VanuatuContext.Set<Order>)

   ※ 컨트롤러(OrderController, [Route("api/pos")])는 라우트 문자열로만 연결되는 별도 경로(B, 후순위).
```

---

## 8. 🚀 활용 사례 (각 사례를 지탱하는 추출)

- **영향도(사이드 이펙트) 분석** — *지탱: `USES_TYPE`(타입 레벨, §4)*
  - 🗣️ *"FilterDTO에 검색 필드를 추가하거나 프로퍼티 타입을 바꾸면 깨질 수 있는 ViewModel·컨트롤러·서비스를 누락 없이 전부 리스트업 해줘."*
  - → 타입 레벨은 상위집합이라 거짓음성(놓침) 없음.
- **E2E 흐름 추적 및 브리핑** — *지탱: 신규 추출 4종 + 공유 인터페이스 조인(§2, §5)*
  - 🗣️ *"특정 서치 버튼부터 EF Core Repository까지 Search 기능의 전체 데이터 흐름을 단계별로 브리핑하고 Mermaid 차트로 뽑아줘."*
- **아키텍처 결함(안티 패턴) 탐색** — *지탱: `REGISTERS`(lifetime) + 생성자 의존 엣지. 탐지는 Cypher/LLM(§4, 결정 9)*
  - 🗣️ *"DI 그래프에서 Singleton으로 잘못 등록돼 순환 참조나 메모리 누수를 유발하는 결합 구조를 찾아줘."*
  - → 순환참조 탐지는 추출기가 아니라 `MATCH path=(a)-[:DEPENDS_ON*]->(a)` 같은 Cypher로 소비 시점에 수행.

---

## 9. 구현 출발점 & 범위

- **출발점**: `strazh/`의 **Strazh(MIT)를 포크**해 §5의 신규 추출 4종 + 국소 수술 3종을 얹는다. "참조용"의 의미 = *검증된 골격 위에 내 추출을 얹는다*.
- **명시적 범위 밖(무시)**: `CallRawSQL` 경유 직접 SQL, `Vanuatu.DTOGenerator` 소스 제너레이터 산출물.

---

> **Take9**: Think for 9 Seconds Before you Click, Download, Share, or Submit.
