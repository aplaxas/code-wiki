# 구현 계획 — 시맨틱 컨텍스트 주입 (L0/L1/L2 + HTML dossier)

> 설계 근거: [docs/inject_sematic.md](../../inject_sematic.md). 라이브 그래프로 검증된 확정 설계의 TDD 태스크 분해.
> 빌드/테스트: `dotnet build strazh/Strazh/Strazh.csproj -c Release`, `dotnet test strazh/Strazh.Tests/Strazh.Tests.csproj` (xUnit).

## 불변식 (작업 중 깨면 안 되는 것)

- 풀 빌드 전제(`DesignTime=false`), `addProjectReferences:false`(빈 스텁 함정), null 노드 skip, FNV-1a `StableHash` — CLAUDE.md §함정 참조.
- 신규 결정론 속성은 전부 `NodeProperties`에 실어 **기존 트리플 적재 경로(`SET a += row.props`)로 흐르게** 한다. 신규 적재 코드는 L1/L2 요약(노드 전용 upsert)에만 필요.
- LLM 호출은 **호스트가 소스 슬라이스를 읽어 프롬프트에 주입**(API는 로컬 경로 못 읽음).
- `.mcp.json`·API 키 커밋 금지.

---

## Phase A — L0 결정론 보강 (LLM 무관, 단독으로 가치)

### A1. Method 노드에 소스 위치 부여
- **Red:** `Extractor`가 메서드 추출 시 `sourcePath`(솔루션 루트 상대·슬래시 정규화)/`startLine`/`endLine`를 채우는지 검증하는 단위 테스트(샘플 심볼 → `MethodNode.NodeProperties`에 3키 존재).
- **Green:** `IMethodSymbol.DeclaringSyntaxReferences[0].GetSyntax().GetLocation().GetLineSpan()`로 추출. 솔루션 루트는 sln 디렉터리 기준 상대화. partial/다중 선언은 body 있는 선언 우선.
- **적재:** [Nodes.cs](../../../strazh/Strazh/Domain/Nodes.cs) `MethodNode.NodeProperties`에 키 추가 → 기존 경로로 자동 적재.
- **검증:** 적재 후 `MATCH (m:Method) WHERE m.sourcePath IS NULL RETURN count(*)`가 충분히 작은지(미해석 심볼 외).

### A2. 인터페이스 메서드 `domainArea` (결정론)
- **Red:** `Vanuatu.Service.Order.IOrderService.X` → `domainArea="Order"` 매핑 테스트.
- **Green:** `fullName`에서 `Service` 다음 세그먼트 추출. 인터페이스 멤버 Method에만 부여.

### A3. ViewModel `dependsOnServices` (결정론)
- **Red:** 생성자 주입 `I*Service` 파라미터/필드 목록을 뽑는 테스트.
- **Green:** 타입의 생성자 파라미터 중 인터페이스 타입(`I...`) 수집 → 콤마 결합 문자열 또는 리스트 속성.

### A4. XAML 파싱 — uiLabel/uiSection/eventKind + EventTrigger 이벤트 보강
- **Red:** EditOrderView.xaml 픽스처로 (1) `CancelOrderCommand→uiLabel="Cancel Order"`, (2) `uiSection="General / Order Information"`, (3) `LoadedCommand`가 EventTrigger에서 포착되는지 테스트.
- **Green:** `.xaml` 파싱(System.Xml/XDocument). `Command="{Binding X}"` 바인딩 수집 → 형제 `Content`→`uiLabel`, 상위 `GroupBox`/`RadTabItem` `Header` 체인→`uiSection`. `Behaviors:EventTrigger`/`InvokeCommandAction`의 `Command` 바인딩도 수집해 Command로 등록(`eventKind`). 기존 Command 추출과 병합(중복 제거).
- **주의:** `.xaml` 경로는 `viewPath` 컨벤션으로 해석. View 노드 ↔ `.xaml` 매핑 확인.

### A5. eventKind 부여
- **Red:** Command=`command`, 생명주기 메서드(`OnNavigatedTo`/`Loaded`계열)=`lifecycle` 태깅 테스트.
- **Green:** Command 노드 `eventKind="command"`, 생명주기 이름 규칙 Method에 `eventKind="lifecycle"`.

**Phase A 종료 기준:** 적재 후 dossier 골격 Cypher(아래 D1)가 LLM 필드 없이도 화면/이벤트/라벨/섹션/백엔드사슬/소스경로를 전부 반환.

---

## Phase B — LLM 인프라 (공용)

### B1. Anthropic C# SDK 연동 + 구조화 출력 래퍼
- **Red:** 모킹/통합 토글로 "슬라이스 입력 → 스키마 검증된 객체 반환" 계약 테스트.
- **Green:** `dotnet add package Anthropic`. `claude-sonnet-4-6`, `OutputConfig.Format`=JSON schema, 시스템프롬프트+스키마 `cache_control` 캐싱, `SemaphoreSlim` 5~8 동시성, 노드 단위 try/catch 격리(실패 로그 후 진행).
- **델타 유틸:** 슬라이스 → `StableHash`(기존 FNV-1a 재사용) = `summaryHash`. `summaryModel` 상수.

### B2. 노드 전용 upsert 적재 경로
- **Red:** pk 기준 `SET n.summary=...,...`가 배열 속성(`effects`/`keyEntities`/`caveats`) 포함해 적재되는지 테스트.
- **Green:** [BatchLoader.cs](../../../strazh/Strazh/Database/BatchLoader.cs)에 `UpsertNodePropsAsync(pk, props)` 추가(`UNWIND $batch MATCH (n {pk}) SET n += row.props`).

---

## Phase C — L1/L2 enrich 명령

### C1. `--enrich-semantic` (L1 bulk)
- **Red:** 타깃 선정 쿼리가 ~505 백엔드 인터페이스 메서드(프록시 제외, impl≤2)를 반환하는지 통합 테스트.
- **Green:** [Program.cs](../../../strazh/Strazh/Program.cs)에 플래그. 타깃 Cypher(§inject_sematic 6) → 서버 impl 슬라이스 읽기 → B1 호출 → B2 upsert. 델타-스킵 적용.
- **스키마:** `summary`/`operationType`/`mutatesState`/`effects`/`keyEntities`/`caveats`.

### C2. `--enrich-mv -p <vm.cs>` (L2 자기완결)
- **Red:** 주어진 VM에 대해 (1) VM 요약, (2) 이벤트별 요약, (3) 닿는 인터페이스 메서드 L1 보강이 모두 적재되는지 통합 테스트.
- **Green:**
  1. 경로 → VM 노드 식별, `viewPath`로 `.xaml` 해석.
  2. **이벤트별 요약:** 이벤트(Command+생명주기) 핸들러 + 같은 클래스 1-hop private 헬퍼 본문 → B1 → Command/Method 노드에 `summary`.
  3. **VM 요약:** `VM.cs` 전체 + `View.xaml` 전체 + `dependsOnServices`(+선택: 이벤트 요약 grounding) → B1 → VM 노드 `summary`.
  4. **인터페이스 메서드 보강:** 이 VM 이벤트가 닿는 백엔드 인터페이스 메서드 중 미enrich분만 C1 로직 호출.
  - 전부 델타-스킵.

---

## Phase D — 산출물 (쿡북 + HTML skill)

### D1. 쿡북 dossier Cypher 레시피
- **Green:** [schema-cookbook.md](../../cookbook/schema-cookbook.md)에 `screenName` 파라미터 중첩 쿼리 추가. 반환: VM(요약·dependsOnServices·sourcePath) + events[](label·section·kind·summary + backend[](method·summary·domainArea·operationType·effects·serverPath·라인)).
- **검증:** `EditOrder`로 실행 → 58 이벤트·61 백엔드가 중첩 구조로 정확히 조립되는지 라이브 확인.

### D2. HTML dossier skill
- **Green:** 별도 skill. 입력 `screenName`(또는 VM 경로) → 그래프 가정 → D1 레시피로 `read_neo4j_cypher` → 자기완결 단일 `.html`(인라인 CSS/JS) 렌더 → 브라우저 오픈.
  - 상단: 화면명+VM요약+dependsOnServices 태그+VM 소스 점프.
  - `uiSection`별 이벤트 카드(uiLabel·eventKind·요약) → 펼치면 백엔드 인터페이스 메서드(요약·도메인·연산·부작용 + 서버 소스 점프).
  - (선택) frontend-design 스킬로 폴리시.

---

## 실행 순서·검증

1. Phase A 전체 → 적재 → 결정론 dossier 골격 확인(LLM 0).
2. Phase B → 작은 표본으로 LLM 왕복 검증.
3. C1(bulk) 또는 C2(단일 화면) → EditOrder부터.
4. D1 → D2로 EditOrder HTML 산출 → 눈으로 확인.

**회귀 가드:** 추출 후 NDJSON에서 `ViewModel` 고유 수 ~492 유지(빈 스텁 함정 #2 재발 감시), 트리플 ~53k 기준선.
