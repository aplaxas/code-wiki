# code-wiki — Vanuatu 코드 그래프 ETL

거대 C# 솔루션(WPF + ASP.NET Core)인 **Vanuatu**를 Roslyn으로 분석해 **Neo4j 코드 지식 그래프**로 적재하고, MCP를 통해 클라우드 LLM이 자연어로 질의하게 하는 파이프라인입니다. MIT 라이선스 [Strazh](strazh/)를 포크해 확장했습니다.

```
Vanuatu.sln ──(Roslyn 추출)──▶ triples.ndjson ──(배치 적재)──▶ Neo4j ──(mcp-neo4j-cypher)──▶ LLM
```

---

## 0. 사전 준비

| 항목 | 비고 |
|---|---|
| .NET SDK 9+ | ETL 도구 빌드용. 분석 대상(net10-windows WPF 등)은 Buildalyzer가 빌드 |
| Docker | 로컬 Neo4j 실행용 |
| Vanuatu 소스 | `C:\develop\baw\phase2\baw-phase2-platform\Vanuatu\Vanuatu.sln` |

> **커버리지:** 서버·서비스·DTO·인터페이스(`IMPLEMENTS_METHOD`/`USES`/`USES_TYPE`/`REGISTERS`)와 **WPF 화면 측(`View`/`ViewModel`/`Command` → `BINDS_TO`/`EXECUTES`)까지 전체 커버**됩니다. Vanuatu.sln 실측 44/44 프로젝트, 약 30k 트리플.
>
> **⚠️ 전제 — 모든 프로젝트가 빌드되어야 함:** 추출기는 각 프로젝트를 **풀 빌드**(Buildalyzer `DesignTime=false`)해 소스를 캡처합니다(design-time 빌드는 net10-windows WPF의 `.xaml.cs`/ViewModel을 누락하기 때문). 따라서 **모든 NuGet 패키지(Telerik 포함)가 복원되고 솔루션 전체가 빌드되는 환경**에서 실행해야 합니다. 트레이드오프: design-time보다 느리고 대상 프로젝트의 bin/obj에 빌드 산출물을 씁니다. 빌드 실패 프로젝트는 로그의 `WARN: skipped …` / `WARN: project X failed` 및 끝의 요약으로 확인됩니다.

---

## 1. Neo4j 실행 (Docker)

```bash
docker run -d --name neo4j-vanuatu `
  -p 7474:7474 -p 7687:7687 `
  -e NEO4J_AUTH=neo4j/strazhpass `
  neo4j:5
```
- 브라우저: http://localhost:7474 (id `neo4j` / pw `strazhpass`)
- Bolt: `bolt://localhost:7687`

---

## 2. ETL 실행 (2단계 — 권장)

도구 실행 형식: `dotnet run --project strazh/Strazh/Strazh.csproj -c Release -- <옵션>`
자격증명(`-c`)은 `DB이름:사용자:비밀번호` 형식입니다.

### ① 추출: Vanuatu.sln → NDJSON
```bash
dotnet run --project strazh/Strazh/Strazh.csproj -c Release -- `
  -c "neo4j:neo4j:strazhpass" `
  -s "C:\develop\baw\phase2\baw-phase2-platform\Vanuatu\Vanuatu.sln" `
  -t code `
  -o ndjson `
  --ndjson-path out/vanuatu.ndjson
```
→ `out/vanuatu.ndjson` 생성 (트리플 한 줄씩). 컴파일이 제일 느린 단계라, 한 번 떠두면 적재를 여러 번 재시도해도 재컴파일이 필요 없습니다.

### ② 적재: NDJSON → Neo4j (wipe & reload)
```bash
dotnet run --project strazh/Strazh/Strazh.csproj -c Release -- `
  -c "neo4j:neo4j:strazhpass" `
  --load-ndjson out/vanuatu.ndjson `
  -d true
```
→ 기존 그래프를 비우고(`-d true`) `UNWIND` 배치로 고속 적재 + **역할 라벨(`:ViewModel` 등)과 `REGISTERS.lifetime`을 SET**합니다.

> **반드시 이 2단계 경로를 쓰세요.** 아래 1단계 직접 적재(`-o neo4j`)는 역할 라벨과 관계 프로퍼티(lifetime)를 누락합니다.

### (대안) 1단계 직접 적재 — 역할 라벨/lifetime 없음
```bash
dotnet run --project strazh/Strazh/Strazh.csproj -c Release -- `
  -c "neo4j:neo4j:strazhpass" `
  -s "C:\develop\baw\phase2\baw-phase2-platform\Vanuatu\Vanuatu.sln" -t code
```

### 주요 CLI 옵션
| 옵션 | 의미 |
|---|---|
| `-c, --credentials` | (필수) `db:user:password` |
| `-s, --solution` | 분석할 `.sln` 절대경로 |
| `-p, --projects` | 또는 `.csproj` 목록 (`-s`와 동시 사용 불가) |
| `-t, --tier` | `project` \| `code` \| `all` (기본 `all`) |
| `-o, --output` | `neo4j`(기본) \| `ndjson` |
| `--ndjson-path` | ndjson 출력 경로 (기본 `triples.ndjson`) |
| `--load-ndjson <path>` | 분석 건너뛰고 기존 NDJSON을 Neo4j에 적재 |
| `-d, --delete` | 적재 전 그래프 삭제 `true`(기본)/`false` |

---

## 3. 적재 검증

Neo4j Browser(http://localhost:7474)에서:
```cypher
// 노드/관계 총량
MATCH (n) RETURN count(n);
MATCH ()-[r]->() RETURN type(r), count(*) ORDER BY count(*) DESC;

// 화면→DB E2E 체인 (커버리지가 충분할 때)
MATCH (v:View)-[:BINDS_TO]->(vm:ViewModel)-[:DEFINES_COMMAND]->(c:Command)-[:EXECUTES]->(h:Method)
MATCH (h)-[:INVOKE*1..4]->(im:Method)<-[:IMPLEMENTS_METHOD]-(impl:Method)-[:USES]->(e:Entity)
RETURN v.name, vm.name, c.name, e.name LIMIT 10;
```
> `BINDS_TO`/`USES`가 0건이면 코드에 없는 게 아니라 **빌드 커버리지** 문제일 수 있습니다(§0 경고 참고).

---

## 4. LLM 연동 (MCP)

읽기전용 계정 등록 + 스키마 쿡북 주입으로 LLM이 정확한 Cypher를 작성하게 합니다.
- 설정 방법: [docs/mcp/README.md](docs/mcp/README.md)
- 스키마/검증된 쿼리 패턴(LLM 컨텍스트로 주입): [docs/cookbook/schema-cookbook.md](docs/cookbook/schema-cookbook.md)

### MCP 예제 질문 (실제 Vanuatu.sln 기준)

아래는 실제 솔루션에 존재하는 타입·서비스·엔티티 이름을 그대로 쓴, LLM에게 바로 던질 수 있는 질문들입니다. (식별자: `Shefa.Module.*` WPF 모듈, `Vanuatu.Service.*`/`Torba.Service.*` 서비스, `Torba.DAL.Model` 엔티티, `Vanuatu.DTO` DTO)

**① 변경 영향도 — "이 타입 건드리면 어디가 깨지나"**
- "`SearchInvoiceFilter`의 프로퍼티 타입을 바꾸면 영향받는 ViewModel·서비스·메서드를 전부 리스트업해줘." *(검색 필터 DTO의 파급 범위 — `USES_TYPE` 역추적)*
- "`InvoiceDTO`를 파라미터나 반환 타입으로 쓰는 메서드를 레이어별로(ViewModel / Service / Repository) 묶어서 보여줘."
- "`Customer` 엔티티를 직접 다루는 서버 메서드(`USES`)를 모두 찾아줘. 어떤 서비스들이 고객 테이블에 의존하는지 알고 싶어."

**② 화면 → DB E2E 추적 — "이 버튼 누르면 무슨 일이 일어나나"**
- "`SearchOrderView`의 `SearchCommand`(검색 버튼)를 누르면 어떤 핸들러 → `IOrderService` → 어떤 엔티티까지 흐르는지 E2E 체인을 추적해서 Mermaid `graph TD`로 그려줘."
- "`SearchInvoiceViewModel`이 정의한 Command 목록과, 각 Command가 실행하는 핸들러 메서드를 보여줘."
- "`SearchCustomerView`(고객 검색 화면)에서 시작해 `Customer`·`Order`·`Invoice` 중 어떤 엔티티에 닿는지 끝까지 따라가줘."

**③ 모듈 구조 파악 — "이 모듈 안에 뭐가 있나" (사용자 진입점)**
- "`Shefa.Module.Order` 모듈에 속한 View·ViewModel·Command를 전부 나열하고, View ↔ ViewModel `BINDS_TO` 매칭을 표로 보여줘."
- "`Shefa.Module.Accounting`의 ViewModel들이 의존하는 서버 서비스 인터페이스(`I*Service`)를 모듈→서비스 의존 관계로 정리해줘."
- "ViewModel인데 대응되는 View가 없는(`BINDS_TO` 누락) 케이스를 모듈별로 찾아줘." *(고아 ViewModel 탐지)*

**④ 경계 관통 — "클라이언트 호출이 서버 어디로 가나"**
- "`IOrderService.SearchOrderAsync`를 구현한 서버 측 메서드(`IMPLEMENTS_METHOD`)를 찾고, 그 메서드가 만지는 엔티티까지 보여줘."
- "WPF 클라이언트에서 호출하는 서비스 인터페이스 메서드 중, 서버 구현이 없는(끊어진 경계) 것이 있으면 알려줘."

**⑤ DI·아키텍처 점검**
- "`IPaymentService`·`IOrderService`·`ICustomerService`의 DI 등록 생명주기(`REGISTERS.lifetime`)를 보여줘. Scoped/Singleton/Transient 중 무엇으로 등록됐나?"
- "프로젝트 간 순환 의존(`DEPENDS_ON` 사이클)이 있으면 경로를 보여줘."

> 위 질문들은 [docs/cookbook/schema-cookbook.md](docs/cookbook/schema-cookbook.md)의 검증된 Cypher 패턴으로 답해집니다. 결과가 비면 코드에 없는 게 아니라 §3 빌드 커버리지 문제일 수 있습니다.

---

## 5. 더 알아보기
- 설계 배경·결정: [docs/vanuatu-wiki-prd.md](docs/vanuatu-wiki-prd.md)
- 구현 계획(14 태스크): [docs/superpowers/plans/2026-06-05-code-wiki-etl.md](docs/superpowers/plans/2026-06-05-code-wiki-etl.md)
- 추출기/도메인 코드: [strazh/Strazh/Analysis/Extractor.cs](strazh/Strazh/Analysis/Extractor.cs), [strazh/Strazh/Domain/](strazh/Strazh/Domain/)

### 알려진 한계
1. 빌드 커버리지(§0). 2. 생성자 주입 DI는 `USES_TYPE` 미반영(REGISTERS 생명주기까지만). 3. 라우트 문자열 경계(컨트롤러 HTTP 경로 매칭)는 미구현 — 경계 관통은 공유 인터페이스(`IMPLEMENTS_METHOD`)로만. 4. `BINDS_TO`는 프로젝트 단위 매칭.
