# [설계] CodeWiki 코어 ETL — Phase 1 (태스크·스코프)

> **코어 ETL(Roslyn→Neo4j) 컴포넌트 설계.** 21개 태스크의 책임·스코프·산출을 한눈에 본다. 전체 스펙은 [codewiki-spec.md](codewiki-spec.md), **바이트사이즈 실행 단계는 [core-etl-plan.md](core-etl-plan.md)**.
>
> 각 태스크는 red→green→refactor. 추출기는 `(ExtractionContext, Graph)`를 받아 append하는 독립 단위 — 파일 하나·테스트 하나로 격리. 위치 `src/CodeWiki/` + `src/CodeWiki.Tests/`, **net10.0**. (Phase 1 빌드 완료 후 design·plan 모두 정리 대상.)

## 진행 규칙
- 태스크 순서 = 의존 순서(기반 → 추출기 → 적재 → 오케스트레이션). 추출기(T8~T15)는 T7까지 끝나면 서로 독립이라 병렬 가능.
- 엣지/라벨 이름은 [spec §6.2](codewiki-spec.md) 정본을 따른다(`DECLARES`/`CALLS`/`INHERITS`/`IMPLEMENTS`/`INSTANTIATES`/`DECLARED_IN`).
- 매직스트링 금지 — `Labels`/`Rel` 상수만 사용.

---

## 기반 (T1~T7)

| # | 태스크 | 테스트 의도 (red) |
|---|---|---|
| 1 | **프로젝트 스캐폴드** | `CodeWiki`/`CodeWiki.Tests` 빌드·테스트 실행됨(net10.0) |
| 2 | **`Pk` (FNV-1a 64bit)** | 같은 입력→같은 해시(프로세스 불변), 다중 필드 `\|` 결합 충돌 없음 |
| 3 | **`Node`/`Edge` record + `Graph`** | `AddNode`/`AddEdge` dedup(pk·엣지키), 같은 pk 재등장 시 props 병합(빈 값이 채운 값 안 덮음) |
| 4 | **`Labels`/`Rel` 상수** | 전체 라벨·엣지 타입이 상수로 노출, 오타 시 컴파일 에러 |
| 5 | **`SymbolNodes` (Roslyn 심볼→Node 팩토리)** | 클래스/인터페이스/메서드 심볼 → 올바른 라벨·`fullName`·메서드 pk(`fullName\|args\|returnType`) |
| 6 | **`TestCompiler` 이식** | 소스 문자열 → `Compilation`+`SemanticModel` 헬퍼 동작 |
| 7 | **`RoleClassifier`** | 휴리스틱별 역할 라벨 부여(Entity/ViewModel/Controller/Service/Repository/DTO/View), 애매하면 생략 |

## 추출기 (T8~T15) — 스코프별 격리

| # | 태스크 | 산출 엣지/노드 | 테스트 의도 |
|---|---|---|---|
| 8 | **`TypeExtractor` — 상속/구현** | `INHERITS`(클래스→베이스), `IMPLEMENTS`(타입→인터페이스), `DECLARED_IN` | 베이스 클래스와 인터페이스를 **분리** 추출. 미해석 베이스(Prism `BindableBase`)면 skip |
| 9 | **`TypeExtractor` — 메서드/호출/생성** | `DECLARES`(타입→메서드), `CALLS`(메서드→메서드), `INSTANTIATES`(메서드→타입) | `IService` 타입 필드 호출이 **인터페이스 메서드로 직행**(`CALLS`) |
| 10 | **`InterfaceImplementationExtractor`** | `IMPLEMENTS_METHOD` | 클라·서버 구현이 같은 인터페이스 멤버를 가리킴(`FindImplementationForInterfaceMember`). **경계 봉합 허브** |
| 11 | **`CommandExtractor`** | `DEFINES_COMMAND`(VM→Command), `EXECUTES`(Command→핸들러) | `new DelegateCommand(ExecuteX)` 인자 메서드 참조 |
| 12 | **`TypeUsageExtractor`** | `USES_TYPE`(메서드→타입) | 파라미터/반환/필드 타입 + 객체생성(타입 레벨, 상위집합) |
| 13 | **`RepositoryUsageExtractor`** | `USES`(메서드→Entity) | 본문 `IRepository<T>` 필드 → 제네릭 인자 Entity. **물리 테이블명/DbContext 파싱은 비목표** — DAL Entity까지 |
| 14 | **`ViewModelLinker`** (후처리) | `BINDS_TO`(View→VM) | Prism `AutoWireViewModel` 네이밍(`XView`→`XViewModel`) |
| 15 | **`StructureExtractor`** (Solution 1회) | Solution/Project/Folder/File/Package, `INCLUDED_IN`/`CONTAINS`/`DEPENDS_ON` | 프로젝트·패키지 의존, 파일시스템 계층 |

## 적재·오케스트레이션 (T16~T20)

| # | 태스크 | 테스트 의도 |
|---|---|---|
| 16 | **`GraphSerializer` (Graph ↔ NDJSON)** | 노드/엣지 라운드트립 동일(직렬화→역직렬화→동치) |
| 17 | **`Neo4jLoader` + Healthcheck** | **Cypher 생성 유일 지점.** 노드 `MERGE … SET n += props` + 역할 라벨 `SET n:Role`, 엣지 그룹별 `UNWIND … MERGE`. 메모리/NDJSON 경로가 **같은 Cypher** 생성 |
| 18 | **`WorkspaceBuilder`** (불변식 캡슐화) | 풀빌드(`DesignTime=false`) + `addProjectReferences:false` + 프로젝트 try/catch. [spec §9](codewiki-spec.md) 불변식 4종을 한 곳에 가둠 |
| 19 | **`AnalysisPipeline`** | 스코프별 추출기 루프 실행 → Graph 누적. 한 프로젝트 실패가 전체를 안 죽임 |
| 20 | **`Program` CLI** | `extract -s <sln> -o <ndjson>`(Neo4j 불필요) / `load -c <db:user:pass> --ndjson <f> [--wipe]` |

## 완료 검증 (T21)

> ⚠️ **strazh 동치 diff·베이스라인 카운트는 완료 기준이 아니다.** 완료 = [spec §11](codewiki-spec.md): 3대 목적 성립 + 자기 정합성.

`C:\develop\baw\phase2\baw-phase2-platform\Vanuatu\Vanuatu.sln` 대상 실행 후:

1. **무단절 연결** — [cookbook §4-④](cookbook.md) 연결성 쿼리로 임의 ViewModel → Entity 경로 존재 확인. 대표 화면(SearchOrder) E2E가 수동 검증과 일치([cookbook §6](cookbook.md)).
2. **커버리지** — 44개 프로젝트 0 실패. **빈 스텁 없음**(고유 ViewModel 수가 비정상적으로 적으면 불변식 #2 재발 — [spec §9](codewiki-spec.md)).
3. **단일 적재 정합** — 역할 라벨이 그래프에 정상 존재(단일 경로라 누락 불가).

---

## 리스크
- **Buildalyzer 환경 의존:** 모든 NuGet(Telerik 포함) 복원·빌드되는 환경에서만 풀 커버리지. 검증은 그 환경에서.
- **`CALLS` 직행 가정:** WPF가 인터페이스 타입 필드로 호출한다는 전제(실측 확인). 구상 타입 직접 호출이 섞이면 T9에서 보정.
