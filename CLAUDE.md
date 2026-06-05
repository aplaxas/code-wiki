# CLAUDE.md

이 파일은 code-wiki 저장소에서 작업할 때 Claude Code(claude.ai/code)에 대한 가이드입니다.

## 한 줄 요약

거대 C# 솔루션 **Vanuatu**(WPF + ASP.NET Core)를 Roslyn으로 분석해 **Neo4j 코드 지식 그래프**로 적재하고, MCP로 LLM이 자연어 질의하게 하는 ETL 파이프라인. MIT 라이선스 [Strazh](strazh/)를 포크·확장했다.

```
Vanuatu.sln ──(Roslyn 추출)──▶ triples.ndjson ──(배치 적재)──▶ Neo4j ──(mcp-neo4j-cypher)──▶ LLM
```

## 빌드·테스트·실행

ETL 도구 자체는 **net9.0**으로 빌드되며, 분석 대상(net10-windows WPF 등)은 Buildalyzer가 풀 빌드한다.

```bash
# 빌드
dotnet build strazh/Strazh/Strazh.csproj -c Release

# 테스트 (xUnit, 19개)
dotnet test strazh/Strazh.Tests/Strazh.Tests.csproj

# ETL 실행 — 2단계 (권장): 추출 → 적재. 자세한 옵션은 README.md §2 참고
dotnet run --project strazh/Strazh/Strazh.csproj -c Release -- `
  -c "neo4j:neo4j:strazhpass" `
  -s "C:\develop\baw\phase2\baw-phase2-platform\Vanuatu\Vanuatu.sln" `
  -t code -o ndjson --ndjson-path out/vanuatu.ndjson      # ① 추출
dotnet run --project strazh/Strazh/Strazh.csproj -c Release -- `
  -c "neo4j:neo4j:strazhpass" --load-ndjson out/vanuatu.ndjson -d true   # ② 적재
```

> **반드시 2단계(NDJSON → `--load-ndjson`) 경로를 쓸 것.** 1단계 직접 적재(`-o neo4j`, 기본값)는 **역할 라벨(`:ViewModel` 등)과 `REGISTERS.lifetime`을 누락**한다. 이건 잘 잊는 함정이다.

## 아키텍처 (코드 흐름 순서)

`Program.cs`(CLI 파싱) → `Analyzer.Analyze`(오케스트레이션) → `Extractor`(트리플 추출) → `NdjsonWriter`/`BatchLoader`(적재).

| 파일 | 책임 |
|---|---|
| [strazh/Strazh/Program.cs](strazh/Strazh/Program.cs) | CLI 옵션 파싱(`-c/-s/-p/-t/-o/--ndjson-path/--load-ndjson/-d`) |
| [strazh/Strazh/Analysis/Analyzer.cs](strazh/Strazh/Analysis/Analyzer.cs) | 프로젝트별 빌드·분석 루프, 워크스페이스 구성, NDJSON/직접적재 분기 |
| [strazh/Strazh/Analysis/Extractor.cs](strazh/Strazh/Analysis/Extractor.cs) | Roslyn 심볼 → 트리플. 모든 추출 로직의 핵심 (상속/구현/사용/Command/DI 등) |
| [strazh/Strazh/Analysis/RoleClassifier.cs](strazh/Strazh/Analysis/RoleClassifier.cs) | 타입 → 역할 라벨 휴리스틱(Entity/ViewModel/Service/...) |
| [strazh/Strazh/Domain/Nodes.cs](strazh/Strazh/Domain/Nodes.cs) | 노드 정의 + `StableHash`(FNV-1a) + 다중 라벨 |
| [strazh/Strazh/Domain/Relationships.cs](strazh/Strazh/Domain/Relationships.cs), [Triples.cs](strazh/Strazh/Domain/Triples.cs) | 엣지·트리플 정의. `Triple.ToString()`이 Cypher MERGE + dedup 키 생성 |
| [strazh/Strazh/Database/NdjsonWriter.cs](strazh/Strazh/Database/NdjsonWriter.cs), [BatchLoader.cs](strazh/Strazh/Database/BatchLoader.cs) | NDJSON 직렬화 / `UNWIND` 배치 MERGE + 역할 라벨 SET |

## 함정·불변식 (수정 시 깨지 쉬운 것)

1. **풀 빌드 전제:** `Analyzer.GetAnalysisContext`는 `EnvironmentOptions { DesignTime = false }`로 **풀 빌드**해야 WPF의 `.xaml.cs`/ViewModel 소스를 캡처한다. design-time 빌드로 바꾸면 모듈(화면 측)이 통째로 빈다. 그래서 **모든 NuGet 패키지(Telerik 포함)가 복원·빌드되는 환경**에서만 전체 커버리지가 나온다.
2. **워크스페이스 흡수 = 빈 스텁 함정:** `AddToWorkspace(addProjectReferences:true)`는 참조 프로젝트를 **같은 ProjectId의 문서 없는(혹은 일부만 있는) 스텁**으로 워크스페이스에 먼저 박아넣는다. 앱 프로젝트(`Shefa.App.BAWPos`)가 sln 순서상 맨 앞이라 모든 모듈을 스텁으로 만들어버리고, 각 모듈 차례에 그 빈 스텁이 재사용되면 **소스 194개가 0개로** 분석된다(클래스·ViewModel 통째 누락). → `GetAnalysisContext`는 **`addProjectReferences:false`로 각 프로젝트를 자기 전체 문서로 한 번만 추가**한다. 교차 참조는 빌드된 DLL 메타데이터로 해석돼 fullName이 동일하므로 `IMPLEMENTS_METHOD` 등 경계 관통 엣지는 보존(오히려 증가)된다. **`true`로 되돌리지 말 것.** (`true`로 "항상 add"하면 `The solution already contains the specified project` 크래시, 스텁 재사용하면 모듈 누락.)
3. **null 노드 = `Triple.ToString()` NRE:** 해석 안 되는 베이스 타입(예: Prism `BindableBase`)으로 `TripleOfType(class, null)`을 만들면 `ToString()`에서 크래시. 추출기는 **부모 노드 null이면 트리플을 만들지 말고 skip**한다. grouping도 트리플 단위 try/catch로 방어한다.
4. **안정 해시:** 노드 `pk`는 `GetHashCode`가 아니라 **FNV-1a `StableHash`**다(프로세스마다 달라지면 안 됨). 다중 필드 키는 `|`로 구분(충돌 방지).
5. **비밀정보:** `.mcp.json`은 평문 `NEO4J_PASSWORD`를 담아 `.gitignore`에 있다. **커밋 금지.**

## 설계 결정 (왜 이렇게)

- **경계 관통은 공유 인터페이스 메서드(`IMPLEMENTS_METHOD`)로.** 클라 프록시와 서버 구현이 동일 `I*Service.X` 인터페이스 멤버를 가리키므로 그 Method 노드가 다리. 라우트 문자열(HTTP 경로) 매칭은 후순위·미구현.
- **다중 라벨:** `(:Class:ViewModel)`처럼 주 라벨 + 역할 라벨 N개.
- **중간 NDJSON + 배치 적재:** 컴파일이 제일 느리므로 한 번 떠두고 적재를 재시도. 유지보수 단계라 wipe & reload로 충분.
- **타입 레벨 추출:** 메서드 호출(`INVOKE`)·구현은 잡되 표현식 수준 데이터플로우는 YAGNI.

## 더 알아보기

- 설계 배경·결정: [docs/vanuatu-wiki-prd.md](docs/vanuatu-wiki-prd.md)
- 구현 계획(14 태스크 TDD): [docs/superpowers/plans/2026-06-05-code-wiki-etl.md](docs/superpowers/plans/2026-06-05-code-wiki-etl.md)
- 스키마·검증된 Cypher(LLM 컨텍스트 주입용): [docs/cookbook/schema-cookbook.md](docs/cookbook/schema-cookbook.md)
- MCP 연동·예제 질문: [README.md](README.md) §4

## 사용자 환경 메모

- 응답은 **한국어**로.
- 분석 대상 Vanuatu 솔루션: `C:\develop\baw\phase2\baw-phase2-platform\Vanuatu\Vanuatu.sln`
- 실측 기준선(빈 스텁 버그 수정 후 풀 커버리지, 2026-06-05): 44/44 프로젝트, 0 실패, 약 **53k 트리플**. ViewModel 492, View 351, Command 1199, EXECUTES 1197, BINDS_TO 351, IMPLEMENTS_METHOD 4359, INVOKE 23044, Entity 378. 14개 WPF 모듈(Order/Customer/Administrator/...) 전부 채워짐.
  - ⚠️ 이전 표기(`약 30k 트리플, ViewModel 258, IMPLEMENTS 3014, BINDS_TO 28, EXECUTES 78`)는 **함정 #2 빈 스텁 버그로 12개 WPF 모듈이 통째로 누락된 과소 집계**였다. 추출 후 NDJSON에서 `ViewModel` 고유 수가 ~50이면 버그 재발을 의심할 것.
