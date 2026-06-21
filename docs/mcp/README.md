# MCP 연동 — LLM이 코드 그래프에 질의 (읽기전용)

클라우드 LLM이 CodeWiki Neo4j 코드 그래프(Vanuatu.sln)에 **Cypher로 질의**하도록 공식
`mcp-neo4j-cypher` 서버를 등록한다. **LLM이 그래프를 변형·삭제하지 못하도록 읽기전용으로** 쓴다.

```
LLM ──(자연어)──▶ mcp-neo4j-cypher ──(Cypher)──▶ Neo4j ──(JSON 행)──▶ LLM ──▶ 답변/Markdown
```

> **Browser vs MCP:** Neo4j Browser(`:7474`)는 사람이 그래프를 *눈으로* 보는 곳, MCP는 LLM이
> *프로그램으로* 질의해 **JSON 행**을 받는 곳이다. MCP 결과엔 시각화가 없다(표 형태). 시각화가
> 필요하면 LLM에게 결과를 Mermaid로 그려달라고 하거나, 그래프 기반 Markdown 문서를 자동
> 생성하는 **`codewiki-graph-doc` 스킬**(§6)을 쓴다.

---

## 1. 전제 — 그래프가 적재돼 있어야 함

MCP는 이미 적재된 Neo4j를 질의할 뿐이다. 먼저 그래프가 있는지 확인한다(README 루트 §1~§3):
```bash
docker exec neo4j cypher-shell -u neo4j -p strazhpass "MATCH (n) RETURN count(n);"
# 약 21,300 이면 정상. 0이면 적재 필요:
dotnet run --project src/CodeWiki -c Release -- load -c "neo4j:neo4j:strazhpass" --ndjson out/graph.ndjson --wipe
```

> **의미 계층(enrich) 유무도 한 번 확인.** `enrich`로 노드에 입힌 한국어 요약(`summary`)이 있으면
> LLM이 "무슨 일을 하나"를 자연어로 답하고 *행위로* 코드를 찾을 수 있다(§7 샘플 ⑨):
> `docker exec neo4j cypher-shell -u neo4j -p strazhpass "MATCH (n:Node) WHERE n.summary IS NOT NULL RETURN count(n);"`
> — 0이면 아직 enrich 안 한 것(구조만 질의). 채우려면 README 루트 §6(`enrich`).

---

## 2. (권장) 읽기전용 Neo4j 사용자

LLM에 쓰기 권한을 주지 않기 위해 전용 `reader` 계정을 만든다. Browser나 cypher-shell에서
`system` DB에 접속해:
```cypher
:use system
CREATE USER reader SET PASSWORD 'REPLACE_ME' CHANGE NOT REQUIRED;
GRANT ROLE reader TO reader;   // Neo4j 내장 reader 롤 = 읽기 전용
```
> Community Edition은 롤 기반 권한이 제한적이다. 그 경우 적재 후 컨테이너를 read-only로
> 운용하거나, 별도 읽기전용 인스턴스를 띄우는 방식을 고려. 로컬 개발에선 그냥 기본 `neo4j`
> 계정으로 써도 되지만, **공유·원격 노출 시엔 반드시 `reader`로** 연결한다.

---

## 3. MCP 서버 등록

`mcp-neo4j-cypher`는 `uvx`로 실행한다(없으면 `pip install uv` 또는 `pipx install uv`).

### Claude Code (이 저장소)
프로젝트 루트 `.mcp.json`에 등록한다. **이 파일은 평문 비밀번호를 담으므로 `.gitignore`에
있다 — 커밋 금지.** 예:
```json
{
  "mcpServers": {
    "neo4j": {
      "command": "uvx",
      "args": ["mcp-neo4j-cypher@latest"],
      "env": {
        "NEO4J_URI": "bolt://localhost:7687",
        "NEO4J_USERNAME": "reader",
        "NEO4J_PASSWORD": "REPLACE_ME",
        "NEO4J_DATABASE": "neo4j"
      }
    }
  }
}
```
- 등록되면 도구 `mcp__neo4j__read_neo4j_cypher`(읽기), `mcp__neo4j__get_neo4j_schema`(스키마)가
  노출된다. 로컬 개발이면 `NEO4J_USERNAME/PASSWORD`를 `neo4j`/`strazhpass`로 둬도 동작한다.

### Claude Desktop / Cursor
[claude_desktop_config.example.json](claude_desktop_config.example.json)을 각 앱의 MCP 설정에
병합하고 `NEO4J_PASSWORD`를 채운다. 서버 이름(`neo4j-vanuatu` 등)은 자유.

---

## 4. LLM에 스키마 지식 공급 — 스킬 우선

LLM은 스키마·경계 패턴을 모르면 부정확한 Cypher를 쓴다. 지식을 공급하는 두 경로가 있는데,
**cookbook을 상시 컨텍스트에 통째로 주입하지 말고 — 토큰 부담·수동 복붙 드리프트 소지 —
스킬 경로를 우선**한다.

### 권장: `codewiki-graph-doc` 스킬에 맡기기 (지식이 스킬 안에 있음)
스킬이 트리거되면 자신의 `references/cypher-recipes.md`(스키마 + 경계 패턴 + 검증 레시피 +
Mermaid 변환)를 **그때 로드**한다(progressive disclosure). 그래서:
- 평소 컨텍스트에 아무 문서도 얹어둘 필요가 없다 — 토큰 상시 점유·구버전 혼입이 없다.
- 지식의 단일 출처가 스킬 안에 고정돼, cookbook 본문을 사람이 복붙하다 누락·갱신 누락이 생기는
  문제가 구조적으로 사라진다.
즉 "문서/리포트/추적 만들어줘" 류 요청은 스킬에 맡기면 스키마 주입이 자동이다(§6).

### 수동 MCP 질의 (스킬 없이 단발 질문)
스킬을 안 쓰고 MCP로 바로 물을 땐 **전체 cookbook 대신 아래 최소 힌트만** 주면 충분하다:
- **라벨** `Method/Class/Interface/Command/View(+역할 ViewModel/Service/Entity/...)`
- **흐름** `BINDS_TO → DEFINES_COMMAND → EXECUTES → CALLS → IMPLEMENTS_METHOD → USES`
- **경계** 클라(`Shefa.*`)와 서버(`Torba.Service.*`)는 인터페이스 메서드(`IMPLEMENTS_METHOD`)로 봉합.
  서버 끝단은 `WHERE x.fullName STARTS WITH 'Torba.Service'`.
- **의미(enrich, 선택)** enrich된 노드는 `summary`(한국어 요약)·`summaryModel`도 가진다. "무슨 일을 하나"는
  `n.summary`로 바로 답하고, *행위로* 코드를 찾을 땐 `WHERE n.summary CONTAINS '키워드'`. 보조(advisory)·
  부분 커버리지(enrich한 것만, 없으면 빈 칸이지 "코드에 없음" 아님).

> 더 깊은 레시피가 필요하면 [../cookbook.md](../cookbook.md) §2~§4를 **그때** 참고(상시 주입 X).
> cookbook은 사람용 학습 문서, 스킬의 `references/cypher-recipes.md`는 기계용 자급 참조다 — 같은
> 스키마를 두 청중에게 맞춘 것.

---

## 5. 사용 방법 — 자연어로 묻기

최소 힌트(§4)나 스킬이 있으면 **자연어로 물으면** LLM이 알아서 Cypher를 작성·실행하고 답한다.
LLM이 헤매면 "경계는 `IMPLEMENTS_METHOD` 허브로 조인해라" 식으로 패턴을 지목하면 정확해진다.
아래 §7에 자연어 질문 → 기대 동작 → 실제로 매핑되는 Cypher를 여러 개 실었다.

---

## 6. 그래프 → Markdown 문서 자동 생성 (`codewiki-graph-doc` 스킬)

단발 질의를 넘어 **구조화된 문서**(Mermaid + 표 + 근거 Cypher)를 원하면 프로젝트 스킬
[`.claude/skills/codewiki-graph-doc`](../../.claude/skills/codewiki-graph-doc/SKILL.md)를 쓴다.
"SearchOrderView E2E 문서 만들어줘", "OrderDTO 영향도 리포트", "SearchOrderViewModel 도시에"
같은 요청이면 자동 트리거되어 그래프를 질의하고 `docs/graphDoc/<주제>.md`로 저장한다.
MCP(질의)와 스킬(문서화)은 같은 그래프를 보는 짝이다.

---

## 7. 구체적 샘플 (자연어 질문 → 매핑 Cypher)

모두 실측 그래프로 검증된 패턴. 식별자는 실제 Vanuatu 이름. `$param`은 LLM이 질문에서 채운다.

### 샘플 ① 타입 변경 영향도
> 🗣️ *"`OrderDTO` 타입을 바꾸면 영향받는 메서드랑 ViewModel·서비스를 전부 리스트업해줘."*
```cypher
MATCH (m:Method)-[:USES_TYPE]->(t {name:'OrderDTO'})
OPTIONAL MATCH (owner)-[:DECLARES]->(m)
RETURN labels(owner) AS kind, owner.fullName AS owner, m.fullName AS method ORDER BY owner;
```
> 실측: `OrderDTO` 37개 메서드 직접 참조. 동명 타입이 둘 이상이면 `t.fullName`으로 구분해 되묻는다.

### 샘플 ② 화면 → DB End-to-End 추적
> 🗣️ *"`SearchOrderView`의 검색 버튼을 누르면 핸들러→서버→어떤 엔티티까지 가는지 보여줘."*
```cypher
MATCH (v:View {name:'SearchOrderView'})-[:BINDS_TO]->(vm:ViewModel)
MATCH (vm)-[:DEFINES_COMMAND]->(cmd:Command {name:'SearchCommand'})-[:EXECUTES]->(h:Method)
MATCH (h)-[:CALLS*1..4]->(im:Method)<-[:IMPLEMENTS_METHOD]-(impl:Method)-[:USES]->(e:Entity)
WHERE impl.fullName STARTS WITH 'Torba.Service'
RETURN h.name, im.name, impl.fullName, e.name;
```
> 실측 체인: `SearchOrderAsync → GetSearchOrder → SearchOrdersAsync → OrderService.SearchOrdersAsync → Order`.

### 샘플 ③ ViewModel 도시에 (한 화면의 전모)
> 🗣️ *"`SearchOrderViewModel`이 가진 커맨드 전부랑, 각 커맨드가 닿는 서버 구현·엔티티를 표로."*
```cypher
MATCH (vm:ViewModel {name:'SearchOrderViewModel'})-[:DEFINES_COMMAND]->(c:Command)
OPTIONAL MATCH (c)-[:EXECUTES]->(h:Method)-[:CALLS*1..4]->(im:Method)
              <-[:IMPLEMENTS_METHOD]-(impl:Method)
WHERE impl.fullName STARTS WITH 'Torba.Service'
RETURN c.name AS command, collect(DISTINCT im.name) AS ifaceMethods,
       collect(DISTINCT impl.fullName) AS serverImpls ORDER BY command;
```
> 빈 `serverImpls`는 서버 미경유(순수 UI) 커맨드 — 누락이 아니라 분류.
> **의미 결합(enrich돼 있으면):** 커맨드마다 자연어 설명까지 붙이려면 핸들러 `summary`를 함께 조회 —
> `MATCH (vm:ViewModel {name:'SearchOrderViewModel'})-[:DEFINES_COMMAND]->(c:Command)-[:EXECUTES]->(h:Method)`
> `RETURN c.name, h.name, h.summary ORDER BY c.name;` → 구조 표가 바로 화면 기능 명세가 된다.

### 샘플 ④ 엔티티 역추적 (DB → 화면)
> 🗣️ *"`Order` 테이블을 만지는 화면(ViewModel·커맨드)이 어디어디야?"*
```cypher
MATCH (e:Entity {name:'Order'})<-[:USES]-(impl:Method)
WHERE impl.fullName STARTS WITH 'Torba.Service'
MATCH (impl)-[:IMPLEMENTS_METHOD]->(im:Method)<-[:CALLS*1..4]-(h:Method)
MATCH (vm:ViewModel)-[:DEFINES_COMMAND]->(c:Command)-[:EXECUTES]->(h)
RETURN DISTINCT vm.name AS viewModel, c.name AS command ORDER BY viewModel, command;
```

### 샘플 ⑤ 변경 위험이 큰 엔티티 핫스팟
> 🗣️ *"서버에서 제일 많이 건드리는 엔티티 top 10 보여줘. 회귀 우선순위 잡게."*
```cypher
MATCH (impl:Method)-[:USES]->(e:Entity) WHERE impl.fullName STARTS WITH 'Torba.Service'
RETURN e.name AS entity, count(DISTINCT impl) AS serverMethods ORDER BY serverMethods DESC LIMIT 10;
```
> 실측: `Order`(104) `OrderPV`(63) `PV`(62) `Customer`(30) `Invoice`(20).

### 샘플 ⑥ 서비스 인벤토리 (클라 프록시 ↔ 서버 구현)
> 🗣️ *"클라이언트 REST 프록시랑 서버 구현이 짝지어진 서비스 메서드 목록 줘."*
```cypher
MATCH (im:Method)<-[:IMPLEMENTS_METHOD]-(impl:Method)
WITH im,
     [x IN collect(DISTINCT impl.fullName) WHERE x STARTS WITH 'Torba.Service']         AS server,
     [x IN collect(DISTINCT impl.fullName) WHERE x STARTS WITH 'Shefa.Service.RestAPI'] AS client
WHERE size(server) > 0 AND size(client) > 0
RETURN im.name AS ifaceMethod, client[0] AS clientProxy, server[0] AS serverImpl ORDER BY ifaceMethod LIMIT 30;
```

### 샘플 ⑦ 호출 트리 (영향 전파)
> 🗣️ *"`SearchOrdersAsync`를 부르는 상위 호출자를 다 찾아줘."*
```cypher
MATCH (caller:Method)-[:CALLS]->(m:Method {name:'SearchOrdersAsync'})
RETURN DISTINCT caller.fullName ORDER BY caller.fullName;
```

### 샘플 ⑧ 구조 개요 / 통계 / 순환 의존
> 🗣️ *"그래프 전체 통계랑 프로젝트 순환 의존이 있으면 보여줘."*
```cypher
MATCH (n) UNWIND labels(n) AS l RETURN l, count(*) AS c ORDER BY c DESC;          // 라벨별 노드 수
MATCH ()-[r]->() RETURN type(r) AS rel, count(*) AS c ORDER BY c DESC;            // 엣지별 수
MATCH path=(p:Project)-[:DEPENDS_ON*2..]->(p) RETURN [n IN nodes(path)|n.name] AS cycle LIMIT 20;
```

### 샘플 ⑨ 의미로 코드 찾기 — 이름이 아니라 '하는 일'로 (enrich 필요)
> 🗣️ *"결제(Authorize.Net) 관련 동작을 하는 핸들러가 뭐가 있어?"*
```cypher
MATCH (n:Node) WHERE n.summary CONTAINS $keyword        // 예: '결제', 'Authorize.Net', '배송지', 'PDF'
RETURN n.name AS name, n.summary AS summary ORDER BY name;
```
> `enrich`로 입힌 `summary`를 검색 — 메서드명이 암호 같아도 **행위**로 잡힌다. 빈 결과면 해당 의미가 아직
> enrich 안 된 것(부분 커버리지)이지 "코드에 없음"이 아니다. `summary`는 보조 — 모순 시 소스를 따른다.

---

## 8. 트러블슈팅

| 증상 | 원인·해결 |
|---|---|
| 도구가 안 보임 | MCP 서버 미등록/`uvx` 부재. `pip install uv` 후 앱 재시작. |
| `count(n)` = 0 | 그래프 미적재. §1의 `load` 실행. |
| 화면→DB 추적이 0건 | 스키마 힌트 미공급이라 LLM이 경계 허브 패턴(`IMPLEMENTS_METHOD`)을 모름 → 스킬을 쓰거나 §4 최소 힌트 제공. 또는 빌드 커버리지 한계(루트 README §0). |
| 결과가 동명으로 뒤섞임 | `name`은 짧아 충돌. `fullName`/`pk`로 좁히라고 지시. 서비스는 클라/서버로 2개씩 보이는 게 정상. |
| 쓰기 쿼리 시도 | 읽기전용 도구만 노출(`read_neo4j_cypher`). 적재는 별도 쓰기 계정으로 CLI에서만. |
| `summary`가 비거나 검색 0건 | 해당 노드를 아직 enrich 안 함(부분 커버리지). README 루트 §6 `enrich`로 대상을 더 처리. "코드에 없음" 아님. |

> 적재는 `neo4j`/쓰기 계정으로, MCP(LLM)는 `reader`로 — 권한을 분리한다.
> 의미(`summary`)는 보조(advisory)·부분 커버리지 — 구조·이름과 모순되면 소스가 ground truth.
