# CLAUDE.md

code-wiki 저장소에서 작업할 때의 운영 가이드. 응답은 **한국어**로.

## 한 줄 요약

거대 C# 솔루션 **Vanuatu**(WPF + ASP.NET Core)를 Roslyn으로 분석해 **Neo4j 코드 지식 그래프**로 적재하고, MCP·Browser로 화면→DB End-to-End 흐름을 추적하게 하는 ETL. **Vanuatu 전용·가독성 제1**로 새로 쓰는 프로젝트가 **CodeWiki**다.

```
Vanuatu.sln ──(Roslyn 추출)──▶ graph.ndjson ──(UNWIND 배치 MERGE)──▶ Neo4j ──(mcp-neo4j-cypher / Browser)──▶ LLM·사람
```

## 현재 상태 (중요)

- **CodeWiki는 미착수.** `src/CodeWiki/`는 아직 없다. 설계·계획 문서가 먼저 확정된 단계다(2026-06-19).
- **현 그래프는 참조 구현 strazh가 생성한 것**(`out/vanuatu.ndjson`). strazh는 *처음 Neo4j를 접한 MIT 참조 프로젝트*일 뿐 — **CodeWiki는 strazh에 종속되지 않고 클린룸으로 새로 쓴다.** strazh 산출물·스키마를 따르지 않는다.
- CodeWiki 완성·검증 후 `strazh/` 디렉터리는 제거한다.

## 문서 (단일 출처)

| 문서 | 역할 |
|---|---|
| [docs/codewiki-spec.md](docs/codewiki-spec.md) | **설계 정본** — 왜·무엇·어떻게(문제·3대목적·스키마·추출기·완료기준) |
| [docs/cookbook.md](docs/cookbook.md) | **질의·학습** — Neo4j 이해(SQL대조) + 검증 Cypher + Browser 내비게이션 |
| [docs/plan-core-etl.md](docs/plan-core-etl.md) | **한시** — Phase 1 TDD 태스크. 빌드 완료 후 삭제 |
| [docs/_future/semantic-injection.md](docs/_future/semantic-injection.md) | Phase 2(시맨틱 주입) 요약 — 코어 ETL 완료 후 진행 |

## Vanuatu 분석 불변식 (CodeWiki가 반드시 지킬 것)

strazh 트리비아가 아니라, Vanuatu를 Roslyn+Buildalyzer로 분석하는 **모든 도구가 부딪히는 함정**이다. (상세 근거 [docs/codewiki-spec.md](docs/codewiki-spec.md) §9)

1. **풀빌드 전제** — `EnvironmentOptions { DesignTime = false }`. design-time 빌드면 WPF `.xaml.cs`/ViewModel 소스가 통째로 빈다. 모든 NuGet(Telerik 포함)이 복원·빌드되는 환경에서만 전체 커버리지.
2. **빈 스텁 방지** — `AddToWorkspace(addProjectReferences:false)`. 각 프로젝트를 자기 전체 문서로 한 번만 추가. `true`면 앱 프로젝트가 모듈을 문서 0개 스텁으로 선점 → 모듈 통째 누락. **되돌리지 말 것.**
3. **null 부모 노드 skip** — 미해석 베이스 타입(Prism `BindableBase` 등)으로 엣지를 만들지 않는다.
4. **프로젝트 단위 try/catch** — 한 프로젝트 실패가 전체를 죽이지 않게 격리.

> 재발 감시: 추출 후 고유 `ViewModel` 수가 비정상적으로 적으면 불변식 #2(빈 스텁) 재발을 의심.

## 빌드·실행

**CodeWiki (목표 CLI — 구현되면):** net9.0. 분석 대상은 Buildalyzer가 풀빌드.
```bash
codewiki extract -s "<Vanuatu.sln>" -o out/graph.ndjson         # Neo4j 불필요, 파일만 생성
codewiki load -c "neo4j:neo4j:<pass>" --ndjson out/graph.ndjson --wipe
```

**현재(임시) — strazh로 그래프 생성:** CodeWiki 완성 전까지 유일한 실행 수단.
```bash
dotnet build strazh/Strazh/Strazh.csproj -c Release
dotnet test  strazh/Strazh.Tests/Strazh.Tests.csproj
dotnet run --project strazh/Strazh/Strazh.csproj -c Release -- \
  -c "neo4j:neo4j:strazhpass" -s "<Vanuatu.sln>" -t code -o ndjson --ndjson-path out/vanuatu.ndjson   # 추출
dotnet run --project strazh/Strazh/Strazh.csproj -c Release -- \
  -c "neo4j:neo4j:strazhpass" --load-ndjson out/vanuatu.ndjson -d true                                 # 적재
```

## 비밀정보

- `.mcp.json`은 평문 `NEO4J_PASSWORD`를 담아 `.gitignore`에 있다. **커밋 금지.**

## 사용자 환경 메모

- 분석 대상: `C:\develop\baw\phase2\baw-phase2-platform\Vanuatu\Vanuatu.sln` (44개 프로젝트, net10 계열, 소스 약 2,351 .cs).
