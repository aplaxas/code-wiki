# [설계] CodeWiki — 코어 ETL 재작성 (가독성 중심)

> Strazh(MIT, Vlad Batushkov 2020)를 포크·확장한 현 ETL을, **이해하기 쉬운 데이터 중심 구조**로 처음부터 다시 쓴다. 이번 문서는 전체 재설계(L0/L1/L2 + HTML dossier) 중 **첫 sub-project = 코어 ETL(추출 + 적재)** 만 다룬다. 나머지는 각자 별도 spec→plan 사이클.

## 1. 목적·동기

- **이해 가능성이 최우선 가치.** 현 strazh는 사용자가 전체를 머릿속에 담기 어렵다. 새 코드는 작은 단위·단일 경로·최소 타입으로 "읽으면 바로 이해되는" 것을 목표로 한다.
- **라이선스 정리.** strazh는 MIT라 합법적 포크지만, 데이터 모델·구조를 새로 설계해 사실상 클린룸으로 다시 쓰고 새 라이선스로 소유한다. strazh 코드는 *동작 비교용 참고*로만 보존.
- **동치 보장.** 새 ETL은 Vanuatu.sln에서 기존과 **동일한 ~53k 트리플**을 산출해야 한다(NDJSON diff로 증명).

## 2. 범위

| 포함 (이번 sub-project) | 제외 (다음 sub-project) |
|---|---|
| Roslyn 추출(타입/메서드/상속/구현/Command/DI/Repository) | L0 결정론 보강(소스경로/domainArea/dependsOnServices/XAML) |
| 구조 추출(Solution/Project/Folder/File/Package) | L1 인터페이스 메서드 LLM enrich |
| 역할 라벨, View↔ViewModel 링크 | L2 화면 dossier LLM enrich |
| NDJSON 직렬화 + Neo4j 배치 적재(단일 경로) | HTML dossier skill |

단, **새 데이터 모델은 위 "제외" 항목을 무변경 수용**하도록 설계한다(§7 확장 지점).

## 3. 이름·위치

- 새 프로젝트명 **CodeWiki**. 위치 `src/CodeWiki/`(실행 프로젝트) + `src/CodeWiki.Tests/`(xUnit).
- 기존 `strazh/`는 동작 비교가 끝날 때까지 보존, 검증 통과 후 제거.
- 타깃 프레임워크 net9.0(현 ETL과 동일). 분석 대상은 Buildalyzer가 풀빌드.

## 4. 왜 현 strazh가 이해하기 어려운가 (재작성이 푸는 문제)

1. **클래스 폭발:** 관계 하나당 클래스 하나(`Triple*` 14종 + `*Relationship` 14종 + `Node` 9종 ≈ 37개 타입). 엣지 하나 추가에 3곳 수정.
2. **적재 경로 이중화:** `Triple.ToString()`이 Cypher를 만드는 직접 적재와 `NdjsonWriter`+`BatchLoader`의 NDJSON 적재가 분리 → 역할 라벨·`REGISTERS.lifetime` 누락 함정(CLAUDE.md 함정 #2·설계결정 참조).
3. **추출 로직이 거대 static + 확장 메서드 덩어리**(`Extractor` 382줄): 규칙의 호출 위치·산출이 한눈에 안 들어옴.

## 5. 아키텍처 (데이터 중심 + 단일 적재 경로)

### 5.1 코어 데이터 모델 — 37개 타입 → 2개 record

```csharp
record Node(string Label, string Pk, string Name, string FullName,
            IReadOnlyDictionary<string,string> Props, IReadOnlyList<string> Roles);
record Edge(string Type, string FromPk, string ToPk,
            IReadOnlyDictionary<string,string> Props);
```

- **`Pk`**: 작은 정적 유틸 `Pk.Of(params string[])` = **FNV-1a 64bit**(현 `StableHash`와 동일 알고리즘, 프로세스 불변). 다중 필드 키는 `|`로 결합(충돌 방지). 메서드 pk = `fullName|arguments|returnType`.
- **`Graph`** (현 GraphBuilder 역할): `AddNode`/`AddEdge` + pk·엣지키 기준 dedup. 같은 pk 노드 재등장 시 props 병합(빈 값이 채워진 값을 덮지 않음).
- **라벨·엣지 타입**은 매직스트링 대신 정적 상수 클래스 `Labels`(Class/Interface/Method/Command/File/Folder/Solution/Project/Package…)·`Rel`(HAVE/INVOKE/CONSTRUCT/OF_TYPE/DECLARED_AT/INCLUDED_IN/DEPENDS_ON/CONTAINS/IMPLEMENTS_METHOD/USES_TYPE/USES/EXECUTES/DEFINES_COMMAND/BINDS_TO/REGISTERS)에 모은다 — 오타 방지 + 전체 목록 한눈에.
- **가독성 이득:** 새 엣지 추가 = 추출 함수에서 `graph.AddEdge(new Edge(Rel.Invoke, a.Pk, b.Pk, props))` 한 줄.

### 5.2 폴더 구조

```
src/CodeWiki/
  CodeWiki.csproj
  Program.cs                          # CLI (extract / load 두 동사)
  Pipeline/
    AnalysisPipeline.cs               # 오케스트레이션: build → extract → write/load
    WorkspaceBuilder.cs               # Buildalyzer + AdhocWorkspace, 불변식 캡슐화
  Model/
    Node.cs  Edge.cs  Graph.cs        # record 2개 + 빌더
    Pk.cs                             # FNV-1a
    Labels.cs  Rel.cs                 # 라벨/엣지 타입 상수
  Extraction/
    ExtractionContext.cs              # compilation, semantic model, 루트 정보
    StructureExtractor.cs             # Solution 스코프
    TypeExtractor.cs                  # Type 스코프 (declared-at/상속/메서드/invoke/construct/역할)
    InterfaceImplementationExtractor.cs
    CommandExtractor.cs
    RepositoryUsageExtractor.cs       # USES + USES_TYPE
    DiRegistrationExtractor.cs        # Tree 스코프
    ViewModelLinker.cs                # Solution 후처리
    RoleClassifier.cs
  Storage/
    GraphSerializer.cs                # Graph ↔ NDJSON
    Neo4jLoader.cs                    # UNWIND 배치 MERGE — Cypher 생성 유일 지점
    Neo4jHealthcheck.cs
src/CodeWiki.Tests/
  TestCompiler.cs                     # 소스 문자열 → Compilation (strazh에서 이식)
  *ExtractorTests.cs                  # 추출기별 단위 테스트
```

### 5.3 추출 설계 — 실행 스코프로 분류

추출 규칙을 **실행 스코프**로 명시 분류해 `AnalysisPipeline` 루프를 자명하게 만든다. 각 추출기는 `(ExtractionContext ctx, Graph graph)`를 받아 append하는 독립 단위 — 파일 하나·테스트 하나로 격리.

| 스코프 | 추출기 | 산출(엣지/노드) |
|---|---|---|
| Solution 1회 | `StructureExtractor` | Solution/Project/Folder/File/Package, DEPENDS_ON(프로젝트·패키지), INCLUDED_IN, CONTAINS |
| Type 단위 | `TypeExtractor` | Class/Interface 노드, DECLARED_AT, OF_TYPE(상속), HAVE(메서드), INVOKE, CONSTRUCT, 역할 라벨 |
| Type 단위 | `InterfaceImplementationExtractor` | IMPLEMENTS_METHOD (클라↔서버 경계 관통의 다리) |
| Type 단위 | `CommandExtractor` | DEFINES_COMMAND, EXECUTES |
| Type 단위 | `RepositoryUsageExtractor` | USES(Repository\<T\>→Entity), USES_TYPE(파라미터/반환 도메인 타입) |
| Tree 1회 | `DiRegistrationExtractor` | REGISTERS(+lifetime) |
| Solution 후처리 | `ViewModelLinker` | BINDS_TO (View↔ViewModel 네이밍 컨벤션) |

"규칙이 어디 다 있나?" = 위 추출기 목록이 답.

### 5.4 불변식 캡슐화 (`WorkspaceBuilder`)

현 strazh의 함정을 한 곳에 가두고 주석으로 못박는다:

1. **풀빌드 전제:** `EnvironmentOptions { DesignTime = false }` — design-time 빌드면 WPF `.xaml.cs`/ViewModel 소스가 통째로 빈다.
2. **빈 스텁 방지:** `AddToWorkspace(addProjectReferences:false)` — 각 프로젝트를 자기 전체 문서로 한 번만 추가. `true`면 앱 프로젝트가 모듈을 문서 0개 스텁으로 선점 → 모듈 통째 누락. (되돌리지 말 것.)
3. **null 부모 노드 skip:** 미해석 베이스 타입(Prism `BindableBase` 등)으로 엣지를 만들지 않는다 — 추출기 내부에서 방어.
4. **프로젝트 단위 try/catch:** 한 프로젝트 실패가 전체를 죽이지 않게 격리(현 동작 유지).

### 5.5 단일 적재 경로 (함정 #2 구조적 소멸)

흐름은 **항상** 동일: `추출 → Graph(중립 IR) → 적재`.

- `GraphSerializer`: `Graph` ↔ NDJSON. 노드/엣지를 JSON 라인으로 덤프·로드(디버그·재시도용).
- `Neo4jLoader`: `Graph`(메모리든 NDJSON 로드든) → **UNWIND 배치 MERGE**. **Cypher 생성은 여기 한 곳뿐.**
  - 노드: `MERGE (n {pk}) SET n += props, n.name=…, n.fullName=…` + 역할 라벨 `SET n:Role`.
  - 엣지: `(from,to,type) 그룹별 UNWIND … MERGE (a)-[r:TYPE]->(b) SET r += props`.
  - 메모리 경로·NDJSON 경로가 **같은 코드**를 타므로 역할 라벨·lifetime 누락이 불가능.
- 직접 적재(`-o neo4j`) 분기 **폐기**. 단일 경로가 가독성·안정성의 핵심.

### 5.6 CLI (`Program.cs`)

두 동사로 단순화(현 권장 2단계와 동일하되 유일 경로):

```
codewiki extract -s <Vanuatu.sln> -o <out/vanuatu.ndjson>
codewiki load -c <db:user:pass> --ndjson <out/vanuatu.ndjson> [--wipe]
```

`extract`는 Neo4j 불필요(파일만 생성), `load`는 그래프 wipe & reload. (편의상 `extract`에 `--load` 합성 옵션은 둘 수 있으나 내부적으로 같은 두 단계.)

## 6. 데이터 흐름

```
Vanuatu.sln
  │  WorkspaceBuilder (Buildalyzer 풀빌드 → AdhocWorkspace, 불변식)
  ▼
프로젝트별 Compilation
  │  AnalysisPipeline: 스코프별 추출기 실행 → Graph.Add*
  ▼
Graph (Node[] + Edge[], dedup 완료)
  │  GraphSerializer.Write
  ▼
out/vanuatu.ndjson  ──(load)──▶  Neo4jLoader.Load ──▶ Neo4j
```

## 7. L0~L2 확장 지점 (다음 sub-project 수용 설계)

- **L0:** props가 Node의 dict이므로 메서드 `sourcePath/startLine/endLine`, 인터페이스 `domainArea`, VM `dependsOnServices`는 *해당 추출기에서 props에 키 추가*만 하면 `SET n += props`로 자동 적재. **XAML 추출기**(Solution 스코프 1개 신규)도 동일 패턴. 스키마 변경 0.
- **L1/L2:** enrich는 별도 명령이 적재된 그래프를 읽어 LLM 호출 후 `Neo4jLoader.UpsertNodeProps(pk, props)`로 같은 적재 경로 재사용. "시맨틱 = props 더 얹기".

## 8. 테스트 전략

- **이식:** strazh 19개 xUnit의 *의도*를 추출기 단위 테스트로 옮긴다. `TestCompiler`(소스 문자열→Compilation) 헬퍼는 가져온다.
- **추출기별 격리 테스트:** 각 추출기를 직접 겨냥 → BindsTo / Command / DiRegistration / ImplementsMethod / RepositoryUses / UsesType / MultiLabel(역할) / StableKey(Pk) / Structure.
- **적재 테스트:** `Neo4jLoader`의 Cypher·row 생성 단위 테스트(현 BatchLoaderRow/BatchLoader 테스트 대응).
- TDD(red→green→refactor)로 각 추출기 구현.

## 9. 동치 검증 (완료 기준)

새 ETL을 `C:\develop\baw\phase2\baw-phase2-platform\Vanuatu\Vanuatu.sln`에 실행 → NDJSON 덤프 후:

- 44/44 프로젝트, 0 실패.
- 트리플 ≈ 53k. 고유 `ViewModel` ≈ 492(빈 스텁 함정 재발 감시 — ~50이면 버그).
- 주요 카운트 기준선 일치: View 351, Command 1199, EXECUTES 1197, BINDS_TO 351, IMPLEMENTS_METHOD 4359, INVOKE 23044, Entity 378.
- strazh `out/vanuatu.ndjson`과 **정규화(정렬) diff**로 동등 증명.

## 10. 비목표 (YAGNI)

- 표현식 수준 데이터플로우, 라우트 문자열(HTTP 경로) 매칭 — 타입 레벨로 충분.
- 증분 적재 — 유지보수 단계라 wipe & reload로 충분.
- 직접 적재 경로 보존 — 단일 경로로 대체.

## 11. 리스크

- **동치 미세 차이:** 새 추출기가 strazh와 1:1로 안 맞아 트리플 수가 어긋날 수 있음 → 정규화 diff로 조기 발견, 추출기 단위로 좁혀 수정.
- **Buildalyzer 환경 의존:** 모든 NuGet(Telerik 포함) 복원·빌드되는 환경에서만 풀 커버리지. 검증은 그 환경에서 수행.
```
