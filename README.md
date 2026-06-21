# code-wiki — Vanuatu 코드 지식 그래프 ETL

거대 C# 솔루션(WPF + ASP.NET Core)인 **Vanuatu**를 Roslyn으로 분석해 **Neo4j 코드 지식 그래프**로 적재하고, MCP·Browser로 화면→DB End-to-End 흐름을 추적하는 파이프라인. **Vanuatu 전용·가독성 제1**로 새로 쓰는 프로젝트가 **CodeWiki**다.

```
Vanuatu.sln ──(Roslyn 추출)──▶ graph.ndjson ──(UNWIND 배치 MERGE)──▶ Neo4j ──(mcp-neo4j-cypher / Browser)──▶ LLM·사람
                                   └─ v2: enrich(Haiku) ──▶ semantic.ndjson ──(load --semantic 리플레이)──▶ Neo4j 노드 props
```

> **현재 상태:** CodeWiki 코어 ETL **Phase 1 완료(2026-06-20).** `src/CodeWiki/`(net10.0)가 그래프 생성의 **정본 경로**다. Vanuatu.sln 실측 **21,349 노드 / 72,522 엣지 / 42 프로젝트 0 실패**, 산출물은 `out/graph.ndjson`. strazh는 *처음 Neo4j를 접한 MIT 참조 프로젝트*일 뿐 종속 0(클린룸) — `strazh/`는 정리 대상.
>
> **v2(Source 시맨틱 주입) MVP 완료·게이트 PASS(2026-06-20):** `enrich`로 노드에 **소스 위치(L0 결정론) + 의미(`summary`/`effects`/`caveats`, Haiku)** 를 주입하고 사이드카 `out/semantic.ndjson`에 영속한다. 적재부터 enrich까지는 **§8** 참고.

## 문서

| 문서 | 역할 |
|---|---|
| [docs/codewiki-spec.md](docs/codewiki-spec.md) | 설계 정본 — 왜·무엇·어떻게(문제·3대목적·스키마·추출기·완료기준) |
| [docs/cookbook.md](docs/cookbook.md) | Neo4j 이해(SQL 대조) + 검증 Cypher + Browser 내비게이션 |
| [docs/core-etl-design.md](docs/core-etl-design.md) | Phase 1 코어 ETL 태스크·스코프 설계(한시) |
| [docs/core-etl-plan.md](docs/core-etl-plan.md) | Phase 1 바이트사이즈 TDD 실행 계획(한시) |
| [docs/codewiki-v2-spec.md](docs/codewiki-v2-spec.md) | v2 설계 정본(PRD) — Source 시맨틱 주입 |
| [CLAUDE.md](CLAUDE.md) | 운영 가이드·불변식 |

---

## 0. 사전 준비

| 항목 | 비고 | 팀원(적재만) | 관리자(재추출) |
|---|---|:---:|:---:|
| Docker | 로컬 Neo4j 실행용 | ✅ | ✅ |
| .NET SDK 10 | CodeWiki 빌드용. 분석 대상(net10-windows WPF 등)은 Buildalyzer가 빌드 | ✅ | ✅ |
| 공유 `out/graph.ndjson` | 관리자가 추출해 공유한 산출물(약 12.7MB) | ✅ | — |
| Vanuatu 소스 | `C:\develop\baw\phase2\baw-phase2-platform\Vanuatu\Vanuatu.sln` | — | ✅ |

> **팀원은 추출(~9분)을 돌릴 필요가 없습니다.** 관리자가 만든 `out/graph.ndjson`만 있으면 **빌드(§1) → Neo4j 기동(§2) → 적재(§3)**로 동일 그래프가 만들어집니다. 코드가 바뀌어 재추출이 필요할 때만 관리자 경로(§4)를 씁니다.
>
> **⚠️ 빌드 전제 — 모든 프로젝트가 빌드되어야 함(관리자 추출 시):** 추출기는 각 프로젝트를 **풀 빌드**(Buildalyzer `DesignTime=false`)해 소스를 캡처합니다(design-time 빌드는 WPF `.xaml.cs`/ViewModel을 누락). 따라서 **모든 NuGet(Telerik 포함)이 복원·빌드되는 환경**에서만 전체 커버리지(42/42 프로젝트)가 나옵니다. (상세 [CLAUDE.md](CLAUDE.md) 불변식)

---

## 1. CodeWiki 빌드

```bash
dotnet build src/CodeWiki/CodeWiki.csproj -c Release
dotnet test                                                # 42/42 통과 확인(선택)
```

---

## 2. Neo4j 실행 (Docker)

동일 버전 + APOC 플러그인 고정. APOC가 있어야 MCP 스키마 조회가 풀 스키마를 가져옵니다.

```powershell
docker rm -f neo4j 2>$null
docker run -d --name neo4j -p 7474:7474 -p 7687:7687 `
  -e NEO4J_AUTH=neo4j/strazhpass -e NEO4J_PLUGINS='["apoc"]' `
  -v neo4j:/data neo4j:2026.05.0
```
- 브라우저 http://localhost:7474 (`neo4j` / `strazhpass`) · Bolt `bolt://localhost:7687`
- 완전 초기화: `docker rm -f neo4j; docker volume rm neo4j`
- APOC 확인: `docker exec neo4j cypher-shell -u neo4j -p strazhpass "RETURN apoc.version();"`

---

## 3. 그래프 적재 (팀원 — 공유 NDJSON) — 약 14초

자격증명은 `db:user:pass` 형식(`db`는 현재 미사용). `--wipe`는 기존 그래프를 비우고 전체 재적재.
```powershell
dotnet run --project src/CodeWiki -c Release -- `
  load -c "neo4j:neo4j:strazhpass" --ndjson out/graph.ndjson --wipe
```
→ `loaded: 21300 nodes, 72522 edges (wipe=True)`. 적재는 공유 `:Node` 라벨 + pk 인덱스로 ~14초.

---

## 4. 그래프 추출 (관리자 — 코드 변경 시) — 약 9분

먼저 NDJSON을 만든 뒤(§4) 적재(§3)합니다. 추출은 Neo4j가 필요 없습니다(파일만 생성).
```powershell
dotnet run --project src/CodeWiki -c Release -- `
  extract -s "C:\develop\baw\phase2\baw-phase2-platform\Vanuatu\Vanuatu.sln" `
  -o out/graph.ndjson
```
→ `extracted: 21300 nodes, 72522 edges → out/graph.ndjson`. `WARN: project ... failed`가 보이면 그 프로젝트가 빌드되지 않은 것 → NuGet 복원·빌드 환경을 확인하세요(§0).

> **추출·적재 분리가 핵심:** 컴파일이 느리니(9분) NDJSON을 한 번 떠두고, Cypher·스키마 튜닝은 `load`만 14초로 반복합니다. 단일 적재 경로(Graph→Neo4jLoader, Cypher 생성 한 곳)라 역할 라벨 누락 같은 함정이 구조적으로 없습니다. ([spec §8](docs/codewiki-spec.md))

---

## 5. 적재 검증

Neo4j Browser(http://localhost:7474):
```cypher
MATCH (n) RETURN count(n);                                     // 21300
MATCH ()-[r]->() RETURN type(r), count(*) ORDER BY count(*) DESC;
```
화면→DB E2E 체인(검증된 CodeWiki 스키마):
```cypher
MATCH (v:View)-[:BINDS_TO]->(vm:ViewModel)-[:DEFINES_COMMAND]->(c:Command)-[:EXECUTES]->(h:Method)
MATCH (h)-[:CALLS*1..4]->(im:Method)<-[:IMPLEMENTS_METHOD]-(impl:Method)-[:USES]->(e:Entity)
RETURN v.name, vm.name, c.name, e.name LIMIT 10;
```
> 결과가 0건이면 코드에 없는 게 아니라 **빌드 커버리지** 문제일 수 있습니다(§0). 더 많은 패턴은 [docs/cookbook.md](docs/cookbook.md).
>
> **표 vs 그래프:** 위처럼 `RETURN v.name`(스칼라)은 **Table**로만 나옵니다. Browser 캔버스에 원·화살표로 그리려면 **노드·경로 자체**를 반환하세요 — `RETURN v, vm, c, e` 또는 경로를 잡아 `MATCH p=(...) RETURN p`.

---

## 6. Cypher 기본 문법 (빠른 입문)

SQL을 안다면 5분이면 충분합니다. SQL 대조·전체 레시피는 [docs/cookbook.md](docs/cookbook.md) §1·§4.

**문장 골격** — `MATCH`(=FROM/JOIN) → `WHERE`(=WHERE) → `RETURN`(=SELECT).
```cypher
MATCH (vm:ViewModel)              // (변수:라벨)  — 노드는 소괄호
WHERE vm.name STARTS WITH 'Order'
RETURN vm.name                    // 스칼라 → Table
ORDER BY vm.name LIMIT 10;
```

**관계(엣지)는 화살표** — `-[:타입]->`. 방향이 의미를 가짐.
```cypher
MATCH (c:Class {name:'OrderService'})-[:DECLARES]->(m:Method)   // {prop:값} = 인라인 WHERE
RETURN c.name, m.name;
```

| 개념 | 문법 | 비고 |
|---|---|---|
| 노드 | `(n:Label)` · `(n:Label {name:'X'})` | 라벨 = 테이블, `{}` = 등치 필터 |
| 관계 | `(a)-[:REL]->(b)` | 방향 필수. 양방향은 `-[:REL]-` |
| 가변 길이(재귀) | `-[:CALLS*1..4]->` | 1~4홉. 재귀 CTE 대체 |
| 여러 관계 타입 | `-[:CALLS\|EXECUTES]->` | OR |
| 경로 변수 | `p=(a)-[...]->(b)` | `RETURN p` → **그래프 시각화** |
| 외부조인 | `OPTIONAL MATCH` | 없으면 NULL |
| 집계 | `count(x)`, `collect(x)` | 비집계 키가 암시적 GROUP BY |
| 중복 제거 | `RETURN DISTINCT ...` | |
| 상위 N | `LIMIT 10` (`SKIP n LIMIT m`) | |

**Table로 보고 싶나, 그래프로 보고 싶나** — RETURN이 결정합니다.
```cypher
RETURN vm.name, c.name          // 스칼라 → Table (LLM·분석용)
RETURN vm, c, p                 // 노드·경로 → 그래프 캔버스 (눈으로 추적)
```

**메타 확인** (스키마 모를 때):
```cypher
CALL db.labels();                  // 라벨 목록
CALL db.relationshipTypes();       // 관계 타입 목록
CALL db.schema.visualization();    // ER 다이어그램(메타 그래프)
```

> 라벨·엣지·프로퍼티 전체 스키마(실측 수치 포함)는 [docs/cookbook.md](docs/cookbook.md) §2.

---

## 7. LLM 연동 (MCP)

읽기전용 계정 + 쿡북 주입으로 LLM이 정확한 Cypher를 작성하게 합니다.
- 설정: [docs/mcp/README.md](docs/mcp/README.md)
- 스키마·검증 쿼리(LLM 컨텍스트 주입): [docs/cookbook.md](docs/cookbook.md)

**예제 질문(실제 Vanuatu 식별자 기준):**
- *"`SearchInvoiceFilter`의 프로퍼티 타입을 바꾸면 영향받는 ViewModel·서비스·메서드를 전부 리스트업해줘."* (영향도 — `USES_TYPE`)
- *"`SearchOrderView`의 검색 버튼을 누르면 핸들러→`IOrderService`→어떤 엔티티까지 흐르는지 E2E를 Mermaid로 그려줘."* (E2E)
- *"`SearchOrderViewModel`이 정의한 모든 커맨드와 각 커맨드가 도달하는 서버 서비스 구현을 보여줘."* (화면→백엔드 내비)

---

## 8. v2 — Source 시맨틱 주입 (`enrich`)

> **상태:** v2 MVP(M0+M1) 구현·게이트 PASS(2026-06-20). 설계 정본 [docs/codewiki-v2-spec.md](docs/codewiki-v2-spec.md), 검증 기록 [docs/graphDoc/search-order-semantic-validation.md](docs/graphDoc/search-order-semantic-validation.md).

v2는 Phase 1 그래프의 **노드에 "소스 위치 + 그 코드가 무슨 일을 하는지"를 얹는다.** 구조는 그대로, 의미만 더한다.

- **L0 결정론(추출기, Roslyn):** 모든 Method에 `sourcePath`/`startLine`/`endLine`. 서버 인터페이스 메서드에 `mutatesState`(true/false/unknown)·`operationType`(command/query). 건드리는 엔티티는 `USES` 엣지가 곧 답이라 LLM에 묻지 않는다.
- **L1/L2 LLM(`enrich`, Haiku):** `summary`·`effects`·`caveats` **3필드만**. 인터페이스 메서드는 서버 구현 슬라이스(+1-hop 헬퍼), 화면은 `ViewModel.cs`를 통째로 읽어 화면 요약 + 핸들러별 요약을 한 번에 생성.
- **사이드카 분리:** 시맨틱은 구조 `graph.ndjson`과 **별개인 `out/semantic.ndjson`** 에 산다 → `load --wipe`로 그래프를 비워도 리플레이로 복원. 비싼 LLM 산출물을 영속.
- **델타-스킵:** LLM 입력(VM=`ViewModel.cs` 통째, iface=구현+헬퍼 번들)의 해시가 같으면 재실행 시 LLM을 호출하지 않는다.
- **advisory:** 시맨틱은 보조 레이어, **코드가 ground truth.** `summary`가 결정론 `mutatesState`와 모순되면 신뢰하지 말고 `sourcePath`로 소스를 확인.

### 8.0 사전 준비 (관리자 — enrich 실행 시)

| 항목 | 비고 |
|---|---|
| `ANTHROPIC_API_KEY` | 환경변수. **커밋·로그 금지.** enrich가 Anthropic Haiku 호출에 사용 |
| `VANUATU_ROOT` | Vanuatu 소스 루트(기본 `C:\develop\baw\phase2\baw-phase2-platform\Vanuatu`). enrich가 `sourcePath`를 이 루트에 붙여 슬라이스를 읽음 |
| Neo4j 적재 완료(§3) | enrich가 노드의 `sourcePath`를 그래프에서 조회하므로 적재가 선행 |

### 8.1 L0 포함 재추출 → 적재

v2 코드로 추출하면 `sourcePath`/`mutatesState`/`operationType`가 `graph.ndjson`에 함께 담긴다. **§4 추출 → §3 적재**를 그대로 한 번 다시 돌리면 된다(L0는 결정론이라 LLM·API 키 불필요).

### 8.2 시맨틱 생성 (`enrich`)

화면(ViewModel) 단위와 서버 인터페이스 메서드 단위로 돌린다. 사이드카 `out/semantic.ndjson`에 누적 기록되고, 동시에 Neo4j 노드에 props로 upsert된다.

```powershell
$env:ANTHROPIC_API_KEY = "sk-ant-..."     # 커밋 금지
$env:VANUATU_ROOT = "C:\develop\baw\phase2\baw-phase2-platform\Vanuatu"

# 화면 1개: VM 요약 + 그 화면의 핸들러 전부 (한 번의 LLM 호출)
dotnet run --project src/CodeWiki -c Release -- `
  enrich --vm SearchOrderViewModel -c "neo4j:neo4j:strazhpass" --semantic out/semantic.ndjson

# 서버 인터페이스 메서드 1개: 구현 슬라이스(+1-hop 헬퍼) 요약
dotnet run --project src/CodeWiki -c Release -- `
  enrich --iface SearchOrdersAsync -c "neo4j:neo4j:strazhpass" --semantic out/semantic.ndjson
```
→ `enriched: N records → out/semantic.ndjson`. 같은 명령을 다시 돌리면 변경 없는 입자는 `0 records`(델타-스킵).

### 8.3 시맨틱 복원 (`load --semantic`)

`--wipe` 재적재 후에도 사이드카를 리플레이해 시맨틱을 되살린다. 구조는 언제든 재생성, 의미는 보존.

```powershell
dotnet run --project src/CodeWiki -c Release -- `
  load -c "neo4j:neo4j:strazhpass" --ndjson out/graph.ndjson --semantic out/semantic.ndjson --wipe
```
→ `+ semantic replayed: N records` 후 `loaded: ...`. 이제 노드에 `summary`/`effects`/`caveats`/`mutatesState`/`operationType`/`sourcePath`가 붙는다.

> 시맨틱을 조회하는 Cypher는 [docs/cookbook.md](docs/cookbook.md). M2(대량 `--l1` 전체 인터페이스·전 화면)·`--iface` fullName 한정 등 후속은 [docs/codewiki-v2-spec.md](docs/codewiki-v2-spec.md).

---

## 알려진 한계
1. 빌드 커버리지(§0). 2. 생성자 주입 DI는 `USES_TYPE` 미반영(경계 관통은 공유 인터페이스 `IMPLEMENTS_METHOD`로만, 라우트 문자열 경계는 비목표). 3. 끝단은 `Repository<Entity>`의 Entity까지(`USES`) — DbContext·테이블명은 비목표. 4. `CallRawSQL`·`DTOGenerator` 사각지대. 5. 시맨틱 주입(소스 위치·`summary`/`effects`/`caveats`)은 **v2 MVP 구현(§8)**. 대량 일괄(`--l1` 전체·전 화면)·`--iface` fullName 한정은 M2/M3 후속 — [docs/codewiki-v2-spec.md](docs/codewiki-v2-spec.md).
