# code-wiki — Vanuatu 코드 지식 그래프 ETL

거대 C# 솔루션(WPF + ASP.NET Core)인 **Vanuatu**를 Roslyn으로 분석해 **Neo4j 코드 지식 그래프**로 적재하고,
각 노드에 **소스 위치와 의미(요약·부수효과·주의점)** 까지 주입해, 화면→DB End-to-End 흐름과 코드 의미를
MCP·Browser로 추적하는 파이프라인. **Vanuatu 전용·가독성 제1**로 쓰인 프로젝트가 **CodeWiki**(`src/CodeWiki/`, net10.0)다.

```
Vanuatu.sln ──(추출)──▶ graph.ndjson ─┐
                                       ├─(적재)──▶ Neo4j(구조 + 소스위치 + 의미) ──(MCP / Browser)──▶ LLM·사람
        (enrich: Haiku)──▶ semantic.ndjson ─┘
```

> CodeWiki가 그래프 생성의 **정본 경로**다. Vanuatu.sln 실측 **21,349 노드 / 72,522 엣지 / 42 프로젝트 0 실패**.
> 그래프 노드는 **구조**(타입·메서드·화면·커맨드·엔티티와 그 관계) + **소스 위치**(`sourcePath`/라인) +
> **의미**(`summary`/`effects`/`caveats`, `mutatesState`/`operationType`)를 담는다.

## 문서

| 문서 | 역할 |
|---|---|
| [docs/codewiki-spec.md](docs/codewiki-spec.md) | 설계 정본 — 왜·무엇·어떻게(문제·목적·스키마·추출기·완료기준) |
| [docs/codewiki-v2-spec.md](docs/codewiki-v2-spec.md) | 시맨틱 주입 설계(PRD) — 소스 위치·의미 props |
| [docs/cookbook.md](docs/cookbook.md) | **질의·학습** — 라벨·엣지·프로퍼티 스키마 + 검증·탐색 Cypher + Browser 내비게이션 |
| [docs/mcp/README.md](docs/mcp/README.md) | LLM(MCP) 연동 설정 |
| [CLAUDE.md](CLAUDE.md) | 운영 가이드·불변식 |

전체 흐름은 **§0 사전 준비 → §1 빌드 → §2 Neo4j → §3 추출 → §4 적재 → §5 검증 → §6 시맨틱 생성 → §7 시맨틱 복원** 순서다.

---

## 0. 사전 준비

| 항목 | 비고 | 팀원(적재만) | 관리자(추출·enrich) |
|---|---|:---:|:---:|
| Docker | 로컬 Neo4j 실행용 | ✅ | ✅ |
| .NET SDK 10 | CodeWiki 빌드용. 분석 대상(net10-windows WPF 등)은 Buildalyzer가 빌드 | ✅ | ✅ |
| 공유 `out/graph.ndjson` | 관리자가 추출해 공유한 산출물(약 14MB) | ✅ | — |
| 공유 `out/semantic.ndjson` | 관리자가 enrich로 만든 시맨틱 사이드카(선택) | ✅ | — |
| Vanuatu 소스 | `C:\develop\baw\phase2\baw-phase2-platform\Vanuatu\Vanuatu.sln` | — | ✅ |
| Anthropic API 키 | enrich(시맨틱 생성)에만 필요. §6 참고 | — | ✅ |

> **팀원은 추출(~9분)·enrich를 돌릴 필요가 없습니다.** 관리자가 만든 `out/graph.ndjson`(+선택적 `out/semantic.ndjson`)만
> 있으면 **빌드(§1) → Neo4j 기동(§2) → 적재(§4·§7)**로 동일 그래프가 만들어집니다. 코드가 바뀌어 재추출/재생성이
> 필요할 때만 관리자 경로(§3·§6)를 씁니다.
>
> **⚠️ 빌드 전제 — 모든 프로젝트가 빌드되어야 함(관리자 추출 시):** 추출기는 각 프로젝트를 **풀 빌드**(Buildalyzer
> `DesignTime=false`)해 소스를 캡처합니다(design-time 빌드는 WPF `.xaml.cs`/ViewModel을 누락). 따라서 **모든
> NuGet(Telerik 포함)이 복원·빌드되는 환경**에서만 전체 커버리지(42/42 프로젝트)가 나옵니다. (상세 [CLAUDE.md](CLAUDE.md))

---

## 1. CodeWiki 빌드

```powershell
dotnet build src/CodeWiki/CodeWiki.csproj -c Release
dotnet test                                                # 단위테스트 통과 확인(선택)
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

## 3. 그래프 추출 (관리자 — 코드 변경 시) — 약 9분

Vanuatu.sln을 풀 빌드·분석해 `out/graph.ndjson`을 만든다. 구조뿐 아니라 **소스 위치**(`sourcePath`/`startLine`/`endLine`)와
서버 인터페이스 메서드의 **연산 종류**(`mutatesState`/`operationType`)까지 결정론으로 담긴다. 추출은 Neo4j가 필요 없다(파일만 생성).

```powershell
dotnet run --project src/CodeWiki -c Release -- `
  extract -s "C:\develop\baw\phase2\baw-phase2-platform\Vanuatu\Vanuatu.sln" `
  -o out/graph.ndjson
```
→ `extracted: 21349 nodes, 72522 edges → out/graph.ndjson`. `WARN: project ... failed`가 보이면 그 프로젝트가
빌드되지 않은 것 → NuGet 복원·빌드 환경을 확인하세요(§0).

> **추출·적재 분리가 핵심:** 컴파일이 느리니(9분) NDJSON을 한 번 떠두고, 튜닝은 `load`만 ~14초로 반복합니다.
> 단일 적재 경로(Graph→Neo4jLoader, Cypher 생성 한 곳)라 역할 라벨 누락 같은 함정이 구조적으로 없습니다.

---

## 4. 그래프 적재 (load) — 약 14초

자격증명은 `db:user:pass` 형식(`db`는 현재 미사용). `--wipe`는 기존 그래프를 비우고 전체 재적재.

```powershell
dotnet run --project src/CodeWiki -c Release -- `
  load -c "neo4j:neo4j:strazhpass" --ndjson out/graph.ndjson --wipe
```
→ `loaded: 21349 nodes, 72522 edges (wipe=True)`. 공유 `:Node` 라벨 + pk 인덱스로 ~14초.

> 시맨틱 사이드카(`out/semantic.ndjson`)가 이미 있다면 이 단계에서 함께 복원할 수 있습니다 → **§7**.

---

## 5. 적재 검증

Neo4j Browser(http://localhost:7474)에서 노드 수가 **21,349** 근처면 정상이다. 화면→DB End-to-End 체인은
`View → ViewModel → Command → 핸들러 → 인터페이스(경계 허브) → 서버 구현 → Entity`로 한 번의 순회로 이어진다.

> **검증·탐색 Cypher(영향도 / E2E 추적 / 시맨틱 조회 / 스키마 등)는 모두 [docs/cookbook.md](docs/cookbook.md)에 있습니다.**
> 결과가 0건이면 코드에 없는 게 아니라 **빌드 커버리지**(§0) 문제일 수 있습니다.

---

## 6. 시맨틱 생성 (enrich) — 관리자

그래프 노드에 **의미**(`summary`/`effects`/`caveats`)를 LLM으로 채운다. 구조는 그대로, 의미만 더한다.
인터페이스 메서드는 서버 구현 슬라이스(+1-hop 헬퍼)를, 화면은 `ViewModel.cs`를 통째로 읽어 **화면 요약 + 핸들러별 요약**을
한 번에 생성한다. 건드리는 엔티티는 `USES` 엣지가 곧 답이라 LLM에 묻지 않는다.

산출물은 **사이드카 `out/semantic.ndjson`**(구조 `graph.ndjson`과 분리). 동시에 Neo4j 노드에 props로 upsert된다.

### 6.0 사전 준비

| 항목 | 비고 |
|---|---|
| Anthropic API 키 | enrich가 Haiku 호출에 사용. 우선순위 **`ANTHROPIC_API_KEY` 환경변수 > `appsettings.json`**. **커밋·로그 금지** |
| Vanuatu 루트 | enrich가 `sourcePath`를 이 루트에 붙여 슬라이스를 읽음. `VANUATU_ROOT` 환경변수 > `appsettings.json` > 기본값 `C:\develop\baw\phase2\baw-phase2-platform\Vanuatu` |
| Neo4j 적재 완료(§4) | enrich가 노드의 `sourcePath`를 그래프에서 조회하므로 적재가 선행 |

**`appsettings.json`(권장, `.gitignore`됨):** `src/CodeWiki/appsettings.json`에 두면 매번 환경변수를 export할 필요가 없다. 빌드 시 출력 디렉터리로 복사된다.
```json
{
  "Anthropic": { "ApiKey": "sk-ant-..." },
  "Vanuatu":   { "Root": "C:\\develop\\baw\\phase2\\baw-phase2-platform\\Vanuatu" }
}
```
> ⚠️ 이 파일은 **절대 커밋 금지**(`.gitignore`에 등록됨). 환경변수가 있으면 환경변수가 우선한다.

### 6.1 실행

`enrich`는 대화형 TUI다. 실행하면 ① 화면 ViewModel / 서버 인터페이스를 고르고, ② 화면은 프로젝트→ViewModel 다중·전체 선택, ③ 인터페이스는 폴더별 인터페이스→메서드 다중 선택으로 대상을 정한다.

```powershell
# (appsettings.json을 안 쓸 경우에만) 환경변수로 주입
$env:ANTHROPIC_API_KEY = "sk-ant-..."     # 커밋 금지

dotnet run --project src/CodeWiki -c Release -- `
  enrich -c "neo4j:neo4j:strazhpass" --semantic out/semantic.ndjson
```
선택분이 각각 처리되고 `enriched N / skipped M / failed K`로 요약된다. 변경 없는 입자는 자동 건너뜀(델타-스킵).

> **의미는 보조(advisory) — 코드가 ground truth.** `summary`가 결정론 `mutatesState`와 모순되면 신뢰하지 말고
> `sourcePath`로 소스를 확인하세요.

---

## 7. 시맨틱 복원 (load --semantic)

시맨틱은 구조와 분리된 사이드카에 살기 때문에, `--wipe`로 그래프를 비우고 다시 적재해도 **사이드카를 리플레이해 의미를 되살린다.**
구조는 언제든 무료 재생성, 비싼 LLM 산출물은 보존.

```powershell
dotnet run --project src/CodeWiki -c Release -- `
  load -c "neo4j:neo4j:strazhpass" --ndjson out/graph.ndjson --semantic out/semantic.ndjson --wipe
```
→ `+ semantic replayed: N records` 후 `loaded: 21349 nodes, ...`. 이제 노드에
`summary`/`effects`/`caveats`/`mutatesState`/`operationType`/`sourcePath`가 모두 붙는다.

이 그래프를 MCP로 LLM에 연결하면([docs/mcp/README.md](docs/mcp/README.md)), 화면→DB 흐름과 코드 의미를 자연어로 질의할 수 있다.
질의 예시·Cypher 레시피는 [docs/cookbook.md](docs/cookbook.md).

---

## 알려진 한계

1. **빌드 커버리지**(§0) — 빌드 실패 모듈은 그래프에서 빠진다(빈 결과 ≠ 코드에 없음).
2. 생성자 주입 DI는 `USES_TYPE` 미반영(경계 관통은 공유 인터페이스 `IMPLEMENTS_METHOD`로만, 라우트 문자열 경계는 비목표).
3. 끝단은 `Repository<Entity>`의 Entity까지(`USES`) — DbContext·물리 테이블명은 비목표.
4. `CallRawSQL`(raw SQL)·`DTOGenerator` 산출물은 사각지대. raw SQL 변이는 `mutatesState="unknown"`으로 표기.
5. 의미(`summary`/`effects`/`caveats`)는 LLM 산출물이라 advisory — 코드가 ground truth.
