# CodeWiki — Vanuatu 코드 지식 그래프 ETL (설계 정본)

> 거대 C# 솔루션 **Vanuatu**(WPF + ASP.NET Core)를 Roslyn으로 정적 분석해 **Neo4j 코드 지식 그래프**로 적재하는 ETL. 화면→DB End-to-End 호출 흐름을 끊김 없이 잇는 것이 목표다.
>
> 이 문서는 *왜·무엇·어떻게*를 담는 **단일 설계 정본**이다. 질의·활용은 [cookbook.md](cookbook.md), 코어 ETL 컴포넌트 설계는 [core-etl-design.md](core-etl-design.md)·실행 단계는 [core-etl-plan.md](core-etl-plan.md), 운영·실행은 [../CLAUDE.md](../CLAUDE.md).

```
Vanuatu.sln ──(Roslyn 추출)──▶ graph.ndjson ──(UNWIND 배치 MERGE)──▶ Neo4j ──(mcp-neo4j-cypher / Browser)──▶ LLM·사람
```

---

## 0. 정체성 — 무엇을 만들고, 무엇이 아닌가

- **Vanuatu.sln 전용 도구.** 범용 코드 분석기가 **아니다.** Vanuatu의 컨벤션(Prism MVVM, `Vanuatu.Service` 공유 인터페이스, `IRepository<T>`, 71/71 메서드 일치 등)에 **최적화**한다. Vanuatu에 가장 잘 맞는 방법이면 무엇이든 자유롭게 채택한다.
- **제1가치 = 가독성.** "읽으면 바로 이해되는" 코드·스키마·문서. 타입 2개(`Node`/`Edge`)·단일 적재 경로·작은 추출기로 전체를 머릿속에 담을 수 있게 한다.
- **클린룸 재작성.** Strazh(MIT, Vlad Batushkov)는 *처음 Neo4j를 접한 참조 프로젝트*일 뿐, 그 이상 의미 없다. **종속 0** — 산출물·스키마·완료 기준을 strazh에 맞추지 않는다. 기존 `out/vanuatu.ndjson`(strazh 출력)도 따를 기준이 아니다. 우리는 새 그래프를 *목표*로 만든다.

---

## 1. 왜 — 해결하는 문제

일반 IDE·텍스트 LLM은 **두 경계에서 호출 흐름이 물리적으로 단절**돼 화면→DB 추적이 불가능하다.

1. **네트워크 경계** — WPF ViewModel이 HTTP로 백엔드를 호출하는 지점에서 단절.
2. **추상화 경계** — 공유 인터페이스/DTO를 참조할 때 실제 구현체·EF Core 레이어로의 추적 단절.

CodeWiki는 이 두 경계를 **그래프 엣지로 봉합**해, 한 화면에서 시작해 DB Entity까지 한 줄로 관통하는 맵을 만든다.

---

## 2. 최종 목적 3가지

| # | 목적 | 소비 방식 |
|---|---|---|
| 1 | **LLM 질의** | LLM이 `mcp-neo4j-cypher`(읽기전용)로 Cypher를 날려 구조 데이터만 가져감 |
| 2 | **Browser 직접 내비게이션** | Neo4j Browser에서 ViewModel 노드 하나를 골라 클릭-확장하면 Command→인터페이스→서버서비스→Entity까지 **끊김 없이** 펼쳐짐 |
| 3 | **구조화 Markdown 생성** | LLM이 Cypher 결과로 시각화(Mermaid) + 소스 설명이 담긴 Markdown을 뽑음 |

> **목적 #2가 1급 비기능 요구를 만든다: 그래프는 끊김 없이 연결돼야 한다.** 임의 ViewModel에서 Entity까지 도달하는 경로가 Cypher로 나오는 것이 곧 완료 기준(§11).

---

## 3. 대상 시스템 — Vanuatu

검증된 규모: **44개 프로젝트**(.sln 등록 42 + 참조 2), 소스 약 2,351 `.cs`, 타깃 net10 계열(`net10.0` / `net10.0-windows7.0` 주축).

- **Frontend**: WPF — Prism MVVM, `ViewModelLocator.AutoWireViewModel`, Telerik.
- **Backend**: ASP.NET Core REST API — Repository 패턴 + EF Core.
- **공유 계층**: 인터페이스 계약(`Vanuatu.Service`) + DTO(`Vanuatu.DTO`) + 베이스(`Vanuatu.Core`의 `IBaseEntity`).

```
Vanuatu/
├── Server/
│   ├── Torba.Workbench/    # API 진입점 (Controllers, Startup DI 등록)
│   ├── Torba.Service/      # 서버측 비즈니스 로직 (I*Service 구현)
│   └── Torba.DAL/          # EF Core, Repository<T>, VanuatuContext
├── Client/
│   ├── App/Shefa.App.BAWPos/   # 메인 POS 앱 (Prism 부트스트랩·DI 등록)
│   ├── Shefa.Service/          # REST 클라이언트 (AbstractRestAPI + RestAPI/*Service)
│   └── Module/Shefa.Module.*/  # 기능 모듈 (Views/ + ViewModels/)
├── Domain/
│   ├── Vanuatu.DTO/        # 공유 DTO
│   └── Vanuatu.Service/    # 서비스 계약 인터페이스 (I*Service, *Filter)
└── Vanuatu.Core/           # 공유 베이스 (IBaseEntity)
```

---

## 4. 핵심 통찰 — 경계는 어떻게 연결되는가 (코드 검증)

- **클라이언트 프록시** `Client/Shefa.Service/RestAPI/OrderService : AbstractRestAPI, IOrderService`
  → 메서드마다 `path = "/api/pos/..."` 하드코딩 HTTP 경로로 호출.
- **컨트롤러** `Server/Torba.Workbench/Controllers/OrderController : ControllerBase`
  → **인터페이스를 구현하지 않음.** `[Route("api/pos")]` + 액션별 `[Route]`, `IOrderService`를 주입받아 위임. HTTP 경계는 **라우트 문자열로만** 연결.
- **서버 서비스** `Server/Torba.Service/Order/OrderService : IOrderService`
  → 실제 서버측 구현.

**검증된 컨벤션(견고):** 클라 `OrderService` 71개 메서드 = 서버 `OrderService` 71개 = `IOrderService` 71개가 정확히 일치.

### 채택 전략 — 공유 인터페이스 메서드 = 단일 허브

```
WPF 핸들러 ─CALLS→ IOrderService.SearchOrdersAsync ←IMPLEMENTS_METHOD─ 서버 OrderService.SearchOrdersAsync
                          ▲ IMPLEMENTS_METHOD
                   클라 프록시 OrderService.SearchOrdersAsync
```

- **WPF는 `IOrderService` 타입 필드로 호출**하므로 Roslyn(`SemanticModel`)이 호출 대상을 **인터페이스 멤버로 직행 해석**한다 — `CALLS`가 곧장 인터페이스 메서드 노드를 가리킨다(실측 확인). 클라 프록시 구상 메서드를 거치지 않는다.
- 그 인터페이스 메서드를 **클라·서버가 모두 `IMPLEMENTS_METHOD`로 구현** → 인터페이스 메서드 노드가 **두 컴파일을 봉합하는 단일 허브.** Roslyn `FindImplementationForInterfaceMember` / `ExplicitOrImplicitInterfaceImplementations` 기반, 문자열 파싱 0.
- 라우트 문자열 매칭(경로 B)은 **후순위(YAGNI)** — 정확한 HTTP URL이 필요해질 때만.

---

## 5. 데이터 모델 — 타입 2개

```csharp
record Node(string Label, string Pk, string Name, string FullName,
            IReadOnlyDictionary<string,string> Props, IReadOnlyList<string> Roles);
record Edge(string Type, string FromPk, string ToPk,
            IReadOnlyDictionary<string,string> Props);
```

- **`Pk`** = `Pk.Of(params string[])` → **FNV-1a 64bit**(프로세스 불변, `GetHashCode` 금지). 다중 필드 키는 `|`로 결합(충돌 방지). 메서드 pk = `fullName|arguments|returnType`.
- **`Graph`** = `AddNode`/`AddEdge` + pk·엣지키 dedup. 같은 pk 재등장 시 props 병합(빈 값이 채워진 값을 덮지 않음).
- **라벨·엣지 타입은 매직스트링 금지** — 정적 상수 클래스 `Labels`·`Rel`에 모은다(오타 방지 + 전체 목록 한눈에).
- **가독성 이득:** 새 엣지 추가 = 추출 함수에서 `graph.AddEdge(new Edge(Rel.Calls, a.Pk, b.Pk, props))` 한 줄.

---

## 6. 스키마 정본

### 6.1 노드 — 다중 라벨

모든 타입은 `:Class` 또는 `:Interface` + 휴리스틱 매칭 시 **2차 역할 라벨** N개(`(:Class:ViewModel)`). 애매하면 역할 라벨 생략(거짓 구조 방지).

| 역할 라벨 | 판별 휴리스틱 |
|---|---|
| `:Entity` | `IBaseEntity` 구현 (Vanuatu.Core) |
| `:ViewModel` | `BindableBase` 상속 / `*ViewModel` |
| `:Controller` | `ControllerBase` 상속 / `*Controller` |
| `:Service` | `Vanuatu.Service`의 `I*Service` 구현 |
| `:Repository` | `IRepository<T>` / `Repository<T>` |
| `:DTO` | `Vanuatu.DTO` 네임스페이스 / `*DTO` |
| `:View` | `x:Class`를 가진 `.xaml` |

그 외 구조 노드: `:Method`, `:Command`, `:Project`, `:File`, `:Folder`, `:Solution`, `:Package`.

### 6.2 엣지 — 우리 소유, 가독성 우선

이름은 **우리가 설계해 소유**한다. 가독성을 위해 다듬었다(개명 비용 0 — 새 그래프).

| 분류 | 엣지 | From → To |
|---|---|---|
| 구조 | `DECLARED_IN` | 타입 → 파일 |
| 구조 | `INCLUDED_IN` · `CONTAINS` · `DEPENDS_ON` | 파일/폴더/프로젝트/패키지 계층 |
| 타입 | `INHERITS` | 클래스 → 베이스 클래스 |
| 타입 | `IMPLEMENTS` | 타입 → 인터페이스 (**타입 레벨**) |
| 타입 | `DECLARES` | 클래스 → 메서드 |
| 호출 | `CALLS` | 메서드 → 메서드 |
| 호출 | `INSTANTIATES` | 메서드 → 생성 타입 |
| 영향도 | `USES_TYPE` | 메서드/클래스 → 타입 (파라미터/반환/필드, **타입 레벨**) |
| 경계 | `IMPLEMENTS_METHOD` | 구현 메서드 → 인터페이스 멤버 (**단일 허브**) |
| 체인 | `DEFINES_COMMAND` | ViewModel → Command |
| 체인 | `EXECUTES` | Command → 핸들러 메서드 |
| 체인 | `BINDS_TO` | View → ViewModel |
| 체인 | `USES` | 메서드 → `IRepository<T>`/Entity |

> **개명 이력(참조 strazh 선례 → CodeWiki):** `HAVE→DECLARES`, `OF_TYPE→`{`INHERITS`(베이스 클래스) + `IMPLEMENTS`(인터페이스, 분리)`}`, `CONSTRUCT→INSTANTIATES`, `INVOKE→CALLS`, `DECLARED_AT→DECLARED_IN`.
> `INHERITS`(클래스 상속)와 `IMPLEMENTS`(인터페이스 구현, 타입 레벨)는 **분리**한다 — strazh가 둘을 `OF_TYPE` 하나로 뭉갠 것과 다름. 메서드 레벨 봉합은 `IMPLEMENTS_METHOD`(별개).

### 6.3 종착점 — Entity

체인의 끝은 **DAL Entity 노드**(예: `Torba.DAL.Model.Order`)다. 이 Entity 이름이 곧 DB 종착점으로 충분하다 — 별도 `:Table` 노드도, 물리 테이블명(`tableName`) 추출도 하지 않는다. (Vanuatu는 EF Fluent `modelBuilder.Entity<X>().ToTable("...")` 매핑이지만 `DbContext`까지 파싱할 가치가 없다 — YAGNI.)

---

## 7. 추출 설계 — 실행 스코프별 격리

각 추출기는 `(ExtractionContext ctx, Graph graph)`를 받아 append하는 **독립 단위**(파일 하나·테스트 하나). "규칙이 어디 다 있나?" = 이 표가 답.

| 스코프 | 추출기 | 산출 |
|---|---|---|
| Solution 1회 | `StructureExtractor` | Solution/Project/Folder/File/Package, `DEPENDS_ON`/`INCLUDED_IN`/`CONTAINS` |
| Type 단위 | `TypeExtractor` | Class/Interface 노드, `DECLARED_IN`, `INHERITS`, `IMPLEMENTS`, `DECLARES`, `CALLS`, `INSTANTIATES`, 역할 라벨 |
| Type 단위 | `InterfaceImplementationExtractor` | `IMPLEMENTS_METHOD` (경계 봉합 허브) |
| Type 단위 | `CommandExtractor` | `DEFINES_COMMAND`, `EXECUTES` |
| Type 단위 | `RepositoryUsageExtractor` | `USES`(Repo→Entity), `USES_TYPE` |
| Solution 후처리 | `ViewModelLinker` | `BINDS_TO` (View↔VM 네이밍 컨벤션) |

### Vanuatu에 최적화된 핵심 추출 (이 도구의 가치)

1. **메서드 레벨 `IMPLEMENTS_METHOD`** — 경계 봉합 허브 (`FindImplementationForInterfaceMember`).
2. **`Command → 핸들러`** — `new DelegateCommand(ExecuteX)` 인자 메서드 참조.
3. **`View → ViewModel`** — Prism `AutoWireViewModel` 네이밍 매칭 (`XView`→`XViewModel`).
4. **메서드 본문의 제네릭 `IRepository<T>` 필드 추적** — 식별자 → 필드 → 제네릭 타입 인자(Entity).

---

## 8. 적재 — 단일 경로

흐름은 **항상** `추출 → Graph(중립 IR) → 적재`. 직접 적재 분기는 없다(가독성·안정성의 핵심).

- `GraphSerializer`: `Graph` ↔ NDJSON(디버그·재시도용 중간 산출).
- `Neo4jLoader`: `Graph`(메모리든 NDJSON 로드든) → **UNWIND 배치 MERGE. Cypher 생성은 여기 한 곳뿐.**
  - 노드: `MERGE (n {pk}) SET n += props, n.name=…, n.fullName=…` + 역할 라벨 `SET n:Role`.
  - 엣지: `(from,to,type)` 그룹별 `UNWIND … MERGE (a)-[r:TYPE]->(b) SET r += props`.
  - 메모리 경로·NDJSON 경로가 **같은 코드**를 타므로 역할 라벨 누락이 구조적으로 불가능.
- **Wipe & Reload**: 매 실행 `MATCH (n) DETACH DELETE n` 후 전체 재적재(유지보수 단계라 증분 불필요). `(Label, pk)` 유니크 제약으로 MERGE 정합성 보장.

### CLI

```
codewiki extract -s <Vanuatu.sln> -o <out/graph.ndjson>     # Neo4j 불필요, 파일만 생성
codewiki load -c <db:user:pass> --ndjson <out/graph.ndjson> [--wipe]
```

`extract`는 느린 컴파일, `load`는 빠른 재적재 — 분리해 Cypher/스키마 튜닝 시 재컴파일 없이 재적재.

---

## 9. Vanuatu 분석 불변식 (깨지기 쉬운 전제)

이것들은 strazh 트리비아가 아니라, **Vanuatu를 Roslyn+Buildalyzer로 분석하는 모든 도구가 지켜야 하는 불변식**이다. 운영 상세는 [../CLAUDE.md](../CLAUDE.md).

1. **풀빌드 전제** — `EnvironmentOptions { DesignTime = false }`. design-time 빌드면 WPF `.xaml.cs`/ViewModel 소스가 통째로 빈다. 모든 NuGet(Telerik 포함) 복원·빌드되는 환경에서만 전체 커버리지.
2. **빈 스텁 방지** — `AddToWorkspace(addProjectReferences:false)`. 각 프로젝트를 자기 전체 문서로 한 번만 추가. `true`면 앱 프로젝트가 모듈을 문서 0개 스텁으로 선점 → 모듈 통째 누락. **되돌리지 말 것.**
3. **null 부모 노드 skip** — 미해석 베이스 타입(Prism `BindableBase` 등)으로 엣지를 만들지 않는다(추출기 내부 방어).
4. **프로젝트 단위 try/catch** — 한 프로젝트 실패가 전체를 죽이지 않게 격리.

---

## 10. E2E 흐름 (검증된 예시)

```text
[View: SearchOrderView.xaml]
      │ BINDS_TO  (Prism AutoWireViewModel 네이밍)
      ▼
[ViewModel: SearchOrderViewModel] ──DEFINES_COMMAND──► [Command: SearchCommand]
                                                          │ EXECUTES  (DelegateCommand(ExecuteSearch))
                                                          ▼
                                                    [Method: ExecuteSearch]
                                                          │ CALLS  (IOrderService 타입 필드로 호출 → 인터페이스 직행)
                                                          ▼
                              ┌──── 네트워크+추상화 경계 (단일 허브) ────┐
                              │  [Interface Method: IOrderService.SearchOrdersAsync]
                              │     ▲ IMPLEMENTS_METHOD      ▲ IMPLEMENTS_METHOD
                              │  클라 프록시 OrderService    서버 OrderService
                              └────────────────────────────────────────┘
                                                          │ USES  (서버서비스 본문 _orderRepository 참조)
                                                          ▼
                                          [Repository: IRepository<Order>]
                                                          ▼
                                  [Entity: Order]  ── DB 종착점 (DAL Entity 이름)

   ※ 컨트롤러(OrderController, [Route("api/pos")])는 라우트 문자열로만 연결되는 별도 경로(B, 후순위).
```

---

## 11. 완료 기준

strazh와의 동치 diff·베이스라인 카운트는 **완료 기준이 아니다(폐기).** 완료 = **3대 목적 성립 + 자기 정합성:**

1. **무단절 연결** — 임의 ViewModel에서 Entity까지 끊김 없는 경로가 Cypher로 나온다(목적 #2). 검증 쿼리는 [cookbook.md](cookbook.md).
2. **대표 화면 일치** — SearchOrder 등 대표 E2E 체인이 수동 검증과 일치.
3. **커버리지** — 44개 프로젝트 0 실패, **빈 스텁 없음**(불변식 #2 재발 감시).

---

## 12. 비목표 (YAGNI)

- 표현식 수준 데이터플로우 — 타입 레벨로 충분.
- 라우트 문자열(HTTP 경로) 매칭(경로 B) — 인터페이스 조인으로 충분.
- 프로퍼티 레벨 멤버접근(`dto.X`) — 타입 레벨로 충분.
- 증분 적재 — wipe & reload로 충분.
- **DI 등록·생명주기·안티패턴 (`REGISTERS`) — 전면 비목표.** 사용자가 DI를 직접 관리하며, 경계 봉합은 `IMPLEMENTS_METHOD` 허브가 하므로 DI 그래프는 핵심 목적(VM→Entity 내비게이션)에 불필요. 필요해지면 추출기 하나로 후속 복원(§13).
- `CallRawSQL`(raw SQL) 경유, `Vanuatu.DTOGenerator` 산출물 — 사각지대로 기록.

---

## 13. 확장 지점 — Phase 2 (시맨틱 주입) 수용

`Props`가 dict이므로 다음 단계가 스키마 변경 0으로 얹힌다(`SET n += props`).

- **L0 결정론**: 메서드 `sourcePath`/`startLine`/`endLine`, 인터페이스 `domainArea`, VM `dependsOnServices`, XAML 추출(Command `uiLabel` 등) — 해당 추출기에서 props 키 추가.
- **L1/L2 enrich**: 별도 명령이 적재된 그래프를 읽어 LLM 호출 후 같은 적재 경로로 props upsert. "시맨틱 = props 더 얹기."

설계 개요: [_future/semantic-injection.md](_future/semantic-injection.md) (Phase 1 완료 후 진행).

---

## 14. 폴더 구조 (구현)

```
src/CodeWiki/
  Program.cs                  # CLI (extract / load)
  Pipeline/   AnalysisPipeline.cs  WorkspaceBuilder.cs(불변식 캡슐화)
  Model/      Node.cs Edge.cs Graph.cs  Pk.cs  Labels.cs  Rel.cs
  Extraction/ ExtractionContext.cs  StructureExtractor.cs  TypeExtractor.cs
              InterfaceImplementationExtractor.cs  CommandExtractor.cs
              RepositoryUsageExtractor.cs  ViewModelLinker.cs  RoleClassifier.cs
  Storage/    GraphSerializer.cs  Neo4jLoader.cs  Neo4jHealthcheck.cs
src/CodeWiki.Tests/   TestCompiler.cs  *ExtractorTests.cs
```

타깃 **net10.0**(ETL 자체 — 분석 대상 Vanuatu와 동일 .NET 10으로 통일). 분석 대상은 Buildalyzer가 풀빌드.

---

> 참조: Strazh (MIT, Vlad Batushkov) — 처음 Neo4j를 접한 외부 구현. `strazh/` 디렉터리에 동작 비교용으로만 보존하며 CodeWiki 완성 후 제거.
