# code-wiki — Vanuatu 코드 지식 그래프 ETL

거대 C# 솔루션(WPF + ASP.NET Core)인 **Vanuatu**를 Roslyn으로 분석해 **Neo4j 코드 지식 그래프**로 적재하고, MCP·Browser로 화면→DB End-to-End 흐름을 추적하는 파이프라인. **Vanuatu 전용·가독성 제1**로 새로 쓰는 프로젝트가 **CodeWiki**다.

```
Vanuatu.sln ──(Roslyn 추출)──▶ graph.ndjson ──(UNWIND 배치 MERGE)──▶ Neo4j ──(mcp-neo4j-cypher / Browser)──▶ LLM·사람
```

> **현재 상태:** CodeWiki는 미착수(`src/CodeWiki/` 없음). 설계·계획이 먼저 확정된 단계다. 현 그래프는 *처음 Neo4j를 접한 MIT 참조 프로젝트* **strazh**가 생성한 것(`out/vanuatu.ndjson`, 레거시 스키마). CodeWiki는 strazh에 **종속되지 않고 클린룸으로 새로 쓰며**, 완성·검증 후 `strazh/`를 제거한다.

## 문서

| 문서 | 역할 |
|---|---|
| [docs/codewiki-spec.md](docs/codewiki-spec.md) | 설계 정본 — 왜·무엇·어떻게(문제·3대목적·스키마·추출기·완료기준) |
| [docs/cookbook.md](docs/cookbook.md) | Neo4j 이해(SQL 대조) + 검증 Cypher + Browser 내비게이션 |
| [docs/core-etl-design.md](docs/core-etl-design.md) | Phase 1 코어 ETL 태스크·스코프 설계(한시) |
| [docs/core-etl-plan.md](docs/core-etl-plan.md) | Phase 1 바이트사이즈 TDD 실행 계획(한시) |
| [docs/_future/semantic-injection.md](docs/_future/semantic-injection.md) | Phase 2(시맨틱 주입) 요약 |
| [CLAUDE.md](CLAUDE.md) | 운영 가이드·불변식 |

---

## 0. 사전 준비

| 항목 | 비고 | 팀원(적재만) | 관리자(재추출) |
|---|---|:---:|:---:|
| Docker | 로컬 Neo4j 실행용 | ✅ | ✅ |
| **Git LFS** | 공유 NDJSON(`out/vanuatu.ndjson`)을 받기 위해 필수 | ✅ | ✅ |
| .NET SDK 10 | ETL 도구 빌드용. 분석 대상(net10-windows WPF 등)은 Buildalyzer가 빌드 | — | ✅ |
| Vanuatu 소스 | `C:\develop\baw\phase2\baw-phase2-platform\Vanuatu\Vanuatu.sln` | — | ✅ |

> **팀원은 추출을 돌릴 필요가 없습니다.** 풀 커버리지 NDJSON이 Git LFS로 포함돼 있으니 **Neo4j 기동(§1) → 적재(§2)** 두 단계만 하면 동일 그래프가 만들어집니다.
> ```bash
> git lfs install && git lfs pull     # NDJSON 실제 파일 받기
> ```
>
> **⚠️ 빌드 전제 — 모든 프로젝트가 빌드되어야 함:** 추출기는 각 프로젝트를 **풀 빌드**(Buildalyzer `DesignTime=false`)해 소스를 캡처합니다(design-time 빌드는 WPF `.xaml.cs`/ViewModel을 누락). 따라서 **모든 NuGet(Telerik 포함)이 복원·빌드되는 환경**에서만 전체 커버리지(44/44 프로젝트)가 나옵니다. (상세 [CLAUDE.md](CLAUDE.md) 불변식)

---

## 1. Neo4j 실행 (Docker)

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

## 2. 그래프 적재 (현재 — strazh)

> CodeWiki 완성 전까지의 임시 경로. CodeWiki가 서면 `codewiki load -c <db:user:pass> --ndjson out/graph.ndjson --wipe`로 대체된다.

### 팀원 — 공유 NDJSON 적재 (wipe & reload)
```powershell
dotnet run --project strazh/Strazh/Strazh.csproj -c Release -- `
  -c "neo4j:neo4j:strazhpass" --load-ndjson out/vanuatu.ndjson -d true
```
> ⚠️ NDJSON이 LFS 포인터(텍스트 수백 바이트)로만 받아지면 적재가 비어버립니다. 파일 크기를 먼저 확인하고 아니면 `git lfs pull`.

### 관리자 — 재추출 (코드 변경 시)
```powershell
dotnet run --project strazh/Strazh/Strazh.csproj -c Release -- `
  -c "neo4j:neo4j:strazhpass" `
  -s "C:\develop\baw\phase2\baw-phase2-platform\Vanuatu\Vanuatu.sln" `
  -t code -o ndjson --ndjson-path out/vanuatu.ndjson
```
> **반드시 추출 → 적재 2단계**(NDJSON 경유)를 쓰세요. strazh의 1단계 직접 적재(`-o neo4j`)는 역할 라벨·`REGISTERS.lifetime`을 누락합니다. (CodeWiki는 단일 적재 경로로 이 함정을 구조적으로 제거 — [spec §8](docs/codewiki-spec.md))

---

## 3. 적재 검증

Neo4j Browser(http://localhost:7474):
```cypher
MATCH (n) RETURN count(n);
MATCH ()-[r]->() RETURN type(r), count(*) ORDER BY count(*) DESC;
```
화면→DB E2E 체인 — **현 그래프(strazh)는 레거시 엣지명**(`INVOKE`/`HAVE`/`OF_TYPE`). CodeWiki 목표 스키마(`CALLS` 등)와 쿼리 패턴은 [docs/cookbook.md](docs/cookbook.md) 참조.
```cypher
MATCH (v:View)-[:BINDS_TO]->(vm:ViewModel)-[:DEFINES_COMMAND]->(c:Command)-[:EXECUTES]->(h:Method)
MATCH (h)-[:INVOKE*1..4]->(im:Method)<-[:IMPLEMENTS_METHOD]-(impl:Method)-[:USES]->(e:Entity)
RETURN v.name, vm.name, c.name, e.name LIMIT 10;
```
> 결과가 0건이면 코드에 없는 게 아니라 **빌드 커버리지** 문제일 수 있습니다(§0).

---

## 4. LLM 연동 (MCP)

읽기전용 계정 + 쿡북 주입으로 LLM이 정확한 Cypher를 작성하게 합니다.
- 설정: [docs/mcp/README.md](docs/mcp/README.md)
- 스키마·검증 쿼리(LLM 컨텍스트 주입): [docs/cookbook.md](docs/cookbook.md)

**예제 질문(실제 Vanuatu 식별자 기준):**
- *"`SearchInvoiceFilter`의 프로퍼티 타입을 바꾸면 영향받는 ViewModel·서비스·메서드를 전부 리스트업해줘."* (영향도 — `USES_TYPE`)
- *"`SearchOrderView`의 검색 버튼을 누르면 핸들러→`IOrderService`→어떤 엔티티까지 흐르는지 E2E를 Mermaid로 그려줘."* (E2E)
- *"`IPaymentService`·`IOrderService`의 DI 등록 생명주기(`REGISTERS.lifetime`)와 프로젝트 순환 의존을 보여줘."* (DI)

---

## 알려진 한계
1. 빌드 커버리지(§0). 2. 생성자 주입 DI는 `USES_TYPE` 미반영(`REGISTERS` 생명주기까지만). 3. 라우트 문자열 경계는 비목표 — 경계 관통은 공유 인터페이스(`IMPLEMENTS_METHOD`)로만. 4. `CallRawSQL`·`DTOGenerator` 사각지대.
