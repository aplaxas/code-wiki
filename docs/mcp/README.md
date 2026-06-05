# MCP 연동 (읽기전용)

클라우드 LLM이 Neo4j 코드 그래프에 Cypher로 질의하도록 공식 `mcp-neo4j-cypher` 서버를 등록한다. **LLM이 그래프를 변형·삭제하지 못하도록 읽기전용 계정으로 등록**한다.

## 1. 읽기전용 Neo4j 사용자 생성
Neo4j Browser 또는 cypher-shell에서 `system` 데이터베이스에 접속해:
```cypher
:use system
CREATE USER reader SET PASSWORD 'REPLACE_ME' CHANGE NOT REQUIRED;
GRANT ROLE reader TO reader;   // Neo4j 내장 reader 롤 = 읽기 전용
```
> Community Edition은 롤 기반 권한이 제한적이다. 그 경우 별도 읽기전용 DB 인스턴스를 띄우거나, 적재 후 컨테이너를 read-only로 운용하는 방식을 고려.

## 2. MCP 서버 등록
- **Claude Desktop / Cursor:** [claude_desktop_config.example.json](claude_desktop_config.example.json)를 각 앱의 MCP 설정에 병합하고 `NEO4J_PASSWORD`를 채운다. (`uvx`가 없으면 `pip install uv` 또는 `pipx`.)
- **Claude Code:** 프로젝트 루트의 `.mcp.json`에 동일한 `mcpServers` 항목을 넣는다.

## 3. 스키마 쿡북 주입
LLM이 커스텀 라벨/엣지와 경계 조인 패턴을 알도록 [../cookbook/schema-cookbook.md](../cookbook/schema-cookbook.md)를 시스템 프롬프트 또는 프로젝트 컨텍스트(CLAUDE.md 등)로 함께 제공한다. 쿡북 없이는 LLM이 `INVOKE`/`IMPLEMENTS_METHOD` 경계 패턴을 모른 채 부정확한 Cypher를 쓰기 쉽다.

## 4. 데이터 적재 (참고)
```bash
# 1) 추출 → NDJSON (모든 프로젝트가 빌드되는 환경에서)
dotnet run --project strazh/Strazh/Strazh.csproj -c Release -- \
  -c "neo4j:neo4j:PASSWORD" -s "<...>\Vanuatu.sln" -t code -o ndjson --ndjson-path out/vanuatu.ndjson

# 2) NDJSON → Neo4j 적재 (wipe & reload)
dotnet run --project strazh/Strazh/Strazh.csproj -c Release -- \
  -c "neo4j:neo4j:PASSWORD" --load-ndjson out/vanuatu.ndjson -d true
```
적재는 `admin`/쓰기 계정으로 수행하고, MCP(LLM)는 위 `reader` 계정으로만 연결한다.
