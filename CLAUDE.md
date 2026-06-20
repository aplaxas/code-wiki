# CLAUDE.md

code-wiki 저장소에서 작업할 때의 운영 가이드. 응답은 **한국어**로.

## 한 줄 요약

거대 C# 솔루션 **Vanuatu**(WPF + ASP.NET Core)를 Roslyn으로 분석해 **Neo4j 코드 지식 그래프**로 적재하고, MCP·Browser로 화면→DB End-to-End 흐름을 추적하게 하는 ETL. **Vanuatu 전용·가독성 제1**로 새로 쓰는 프로젝트가 **CodeWiki**다.

```
Vanuatu.sln ──(Roslyn 추출)──▶ graph.ndjson ──(UNWIND 배치 MERGE)──▶ Neo4j ──(mcp-neo4j-cypher / Browser)──▶ LLM·사람
```

## 현재 상태 (중요)

- **CodeWiki 코어 ETL Phase 1 완료(2026-06-20).** `src/CodeWiki/`(net10.0) 구현·통합검증 끝. 단위테스트 42/42, Vanuatu.sln 실측 **21,300 노드 / 72,522 엣지 / 42 프로젝트 0 실패**, 빈 스텁 없음(ViewModel 499), 커맨드 90.4% 백엔드 허브 도달, SearchOrder E2E가 Entity까지 연결, 적재 ~14초(공유 `:Node` 라벨 + pk 인덱스).
- **CodeWiki가 이제 그래프 생성의 정본 경로.** `out/graph.ndjson`이 산출물. strazh는 *처음 Neo4j를 접한 MIT 참조 프로젝트*일 뿐 — 종속 0(클린룸). 검증 끝났으니 `strazh/` 디렉터리는 정리 대상.
- 후속(차단 아님): NU1903 transitive 취약점 패키지 핀, CALLS 프레임워크 노이즈 도메인 필터(필요시), Phase 2(시맨틱 주입) — [docs/_future/semantic-injection.md](docs/_future/semantic-injection.md).

## 문서 (단일 출처)

| 문서 | 역할 |
|---|---|
| [docs/codewiki-spec.md](docs/codewiki-spec.md) | **설계 정본** — 왜·무엇·어떻게(문제·3대목적·스키마·추출기·완료기준) |
| [docs/cookbook.md](docs/cookbook.md) | **질의·학습** — Neo4j 이해(SQL대조) + 검증 Cypher + Browser 내비게이션 |
| [docs/core-etl-design.md](docs/core-etl-design.md) | **한시** — Phase 1 코어 ETL 태스크·스코프 설계 |
| [docs/core-etl-plan.md](docs/core-etl-plan.md) | **한시** — Phase 1 바이트사이즈 TDD 실행 계획. 빌드 완료 후 design·plan 정리 |
| [docs/_future/semantic-injection.md](docs/_future/semantic-injection.md) | Phase 2(시맨틱 주입) 요약 — 코어 ETL 완료 후 진행 |

## Vanuatu 분석 불변식 (CodeWiki가 반드시 지킬 것)

strazh 트리비아가 아니라, Vanuatu를 Roslyn+Buildalyzer로 분석하는 **모든 도구가 부딪히는 함정**이다. (상세 근거 [docs/codewiki-spec.md](docs/codewiki-spec.md) §9)

1. **풀빌드 전제** — `EnvironmentOptions { DesignTime = false }`. design-time 빌드면 WPF `.xaml.cs`/ViewModel 소스가 통째로 빈다. 모든 NuGet(Telerik 포함)이 복원·빌드되는 환경에서만 전체 커버리지.
2. **빈 스텁 방지** — `AddToWorkspace(addProjectReferences:false)`. 각 프로젝트를 자기 전체 문서로 한 번만 추가. `true`면 앱 프로젝트가 모듈을 문서 0개 스텁으로 선점 → 모듈 통째 누락. **되돌리지 말 것.**
3. **null 부모 노드 skip** — 미해석 베이스 타입(Prism `BindableBase` 등)으로 엣지를 만들지 않는다.
4. **프로젝트 단위 try/catch** — 한 프로젝트 실패가 전체를 죽이지 않게 격리.

> 재발 감시: 추출 후 고유 `ViewModel` 수가 비정상적으로 적으면 불변식 #2(빈 스텁) 재발을 의심.

## 빌드·실행

**CodeWiki (정본, net10.0).** 분석 대상은 Buildalyzer가 풀빌드(모든 NuGet/Telerik 복원 환경 필요). 추출 ~9분, 적재 ~14초.
```bash
dotnet build src/CodeWiki/CodeWiki.csproj -c Release
dotnet test                                                       # 42/42
dotnet run --project src/CodeWiki -c Release -- extract -s "<Vanuatu.sln>" -o out/graph.ndjson   # Neo4j 불필요
dotnet run --project src/CodeWiki -c Release -- load -c "neo4j:neo4j:strazhpass" --ndjson out/graph.ndjson --wipe
```
Neo4j 기동·MCP 연동은 [README.md](README.md) §1·§4. strazh 실행법은 참조 디렉터리 `strazh/`에만 보존(정리 대상).

## 비밀정보

- `.mcp.json`은 평문 `NEO4J_PASSWORD`를 담아 `.gitignore`에 있다. **커밋 금지.**

## 사용자 환경 메모

- 분석 대상: `C:\develop\baw\phase2\baw-phase2-platform\Vanuatu\Vanuatu.sln` (44개 프로젝트, net10 계열, 소스 약 2,351 .cs).
