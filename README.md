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

> **⚠️ 커버리지:** 서버·서비스·DTO·인터페이스 측(`IMPLEMENTS_METHOD`/`USES`/`USES_TYPE`/`REGISTERS` 등)은 전체 커버됩니다. 다만 **WPF 모듈(`Shefa.Module.*`)의 화면 측(`View`/`ViewModel`/`Command` → `BINDS_TO`/`EXECUTES`)은 환경에 따라 부분적**입니다: net10-windows WPF 프로젝트의 design-time 빌드에 **Telerik NuGet 피드 인증**이 필요한데, 피드가 막힌 환경에서는 Buildalyzer가 모듈 소스를 일부만 캡처해 ViewModel/View가 거의 안 잡힙니다. **화면 측까지 보려면 Telerik 피드가 인증된(솔루션 전체가 정상 빌드되는) 환경에서 실행**하세요. 추출/적재 로그의 `WARN: project X failed` 및 끝의 요약으로 누락 프로젝트를 확인할 수 있습니다. 자세히: [docs/cookbook/schema-cookbook.md](docs/cookbook/schema-cookbook.md) §5.

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
dotnet run --project strazh/Strazh/Strazh.csproj -c Release -- \
  -c "neo4j:neo4j:strazhpass" \
  --load-ndjson out/vanuatu.ndjson \
  -d true
```
→ 기존 그래프를 비우고(`-d true`) `UNWIND` 배치로 고속 적재 + **역할 라벨(`:ViewModel` 등)과 `REGISTERS.lifetime`을 SET**합니다.

> **반드시 이 2단계 경로를 쓰세요.** 아래 1단계 직접 적재(`-o neo4j`)는 역할 라벨과 관계 프로퍼티(lifetime)를 누락합니다.

### (대안) 1단계 직접 적재 — 역할 라벨/lifetime 없음
```bash
dotnet run --project strazh/Strazh/Strazh.csproj -c Release -- \
  -c "neo4j:neo4j:strazhpass" \
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

활용 예: *"FilterDTO 프로퍼티 타입을 바꾸면 깨질 ViewModel·컨트롤러·서비스를 전부 리스트업"*, *"특정 서치 버튼부터 EF Core까지 E2E 흐름을 Mermaid로"*, *"Singleton 잘못 등록된 순환참조 탐지"*.

---

## 5. 더 알아보기
- 설계 배경·결정: [docs/vanuatu-wiki-prd.md](docs/vanuatu-wiki-prd.md)
- 구현 계획(14 태스크): [docs/superpowers/plans/2026-06-05-code-wiki-etl.md](docs/superpowers/plans/2026-06-05-code-wiki-etl.md)
- 추출기/도메인 코드: [strazh/Strazh/Analysis/Extractor.cs](strazh/Strazh/Analysis/Extractor.cs), [strazh/Strazh/Domain/](strazh/Strazh/Domain/)

### 알려진 한계
1. 빌드 커버리지(§0). 2. 생성자 주입 DI는 `USES_TYPE` 미반영(REGISTERS 생명주기까지만). 3. 라우트 문자열 경계(컨트롤러 HTTP 경로 매칭)는 미구현 — 경계 관통은 공유 인터페이스(`IMPLEMENTS_METHOD`)로만. 4. `BINDS_TO`는 프로젝트 단위 매칭.
