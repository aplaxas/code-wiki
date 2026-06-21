# enrich 대상 선택 TUI — 설계

> 상태: 설계 확정(2026-06-20 brainstorming). 다음: 구현 계획(writing-plans).
> 관련: [docs/codewiki-v2-spec.md](../../codewiki-v2-spec.md) (시맨틱 주입), [README.md](../../../README.md) §6.

## 목적

`enrich`로 시맨틱을 만들 때 **대상을 손으로 타이핑하지 않고 콘솔 TUI에서 골라** 실행한다.
화면(ViewModel)은 프로젝트→ViewModel 다중/전체 선택, 서버 인터페이스는 폴더별 인터페이스 단일선택→메서드 다중선택.

## 범위 / 비목표

**범위**
- `enrich` 를 **TUI 전용**으로 전환. 실행 시 대화형 선택기가 뜬다.
- 파일시스템에서 프로젝트·ViewModel·인터페이스 목록을 읽어 보여준다.
- 선택된 대상들을 기존 enrich 실행 코어로 일괄 처리.

**비목표**
- 비대화형 일괄(`--all` 등) 실행 — 추후 M2(대량)에서 별도 설계.
- 시맨틱 생성 로직 자체 변경(프롬프트·해시·사이드카·델타-스킵은 그대로).
- ViewModel 외 다른 노드 종류 enrich.

## 파일시스템 사실 (실측, 2026-06-20)

- 클라이언트 프로젝트: `Client/Module/*/` (26개, 예 `Shefa.Module.Order`, `Vanuatu.Module.Order`).
- ViewModel: `Client/Module/<project>/ViewModels/*ViewModel.cs` (예 Order 34개). 파일명에서 `.cs`를 떼면 enrich가 받는 VM 이름(예 `SearchOrderViewModel`).
- **인터페이스는 `Domain/Vanuatu.Service/<folder>/I*.cs`** (폴더 그룹: Order/Product/…). `Torba.Service/<folder>/`에는 **구현체**(`OrderService.cs` 등)만 있음 — 인터페이스 아님.
- 루트: `VANUATU_ROOT`(env > appsettings > 기본 `C:\develop\baw\phase2\baw-phase2-platform\Vanuatu`).

## CLI 표면 변경

- **삭제:** `--vm`, `--iface` 옵션과 그 분기(혼동 제거). `CliOptions`의 `Vm`/`Iface` 필드 및 해당 파싱·테스트 제거.
- **유지:** `enrich -c <db:user:pass> --semantic <out> [--model <id>]`. 이 명령이 곧 TUI를 띄운다.
- 필수 인자 검증: `-c`, `--semantic` 없으면 오류 후 반환(기존과 동일 메시지 형식).
- API 키/루트 로딩(`AppSettings`)·사이드카 병합·`ApplySemanticAsync`는 그대로.

## 컴포넌트

각 단위는 한 책임을 갖고 독립 테스트 가능.

### 1. `VanuatuLayout` (신규, 순수 FS 리더)
- `IReadOnlyList<string> ListClientModuleProjects(string root)` — `root/Client/Module`의 하위 디렉터리 이름. 없으면 빈 목록.
- `IReadOnlyList<string> ListViewModels(string projectDir)` — `projectDir/ViewModels/*ViewModel.cs`의 파일명(확장자 제거). `ViewModels` 폴더 없으면 빈 목록.
- `IReadOnlyList<(string folder, string name)> ListServiceInterfaces(string root)` — `root/Domain/Vanuatu.Service/<folder>/I*.cs`를 `(folder, 인터페이스이름)`으로. `bin`/`obj` 폴더 제외.
- 단위테스트: 임시 디렉터리에 위 구조를 만들어 검증.

### 2. `EnrichRunner` (Program enrich 블록 추출, 재사용 코어)
- 생성자 주입: `IGraphReader reader`, `ILlmClient llm`, `Neo4jLoader loader`, `string model`, `string semanticPath`, `string vanuatuRoot`.
- `Task<int> RunVmAsync(string vmName)` — 기존 VM 경로(그래프에서 dossier 조회 → 루트 조인 → 해시 → 델타-스킵 → 사이드카 병합 기록 → `ApplySemanticAsync(fresh)`). 반환=fresh 레코드 수.
- `Task<int> RunIfaceAsync(string methodName)` — 기존 iface 경로 동일 패턴.
- 사이드카 병합(기존 읽기 + fresh upsert, pk 기준 fresh 우선)은 러너 내부에서 처리.
- 단위테스트: fake `IGraphReader`(고정 dossier/unit 반환) + fake `ILlmClient`로 VM/iface 1건 실행·델타-스킵·병합 검증. (`Neo4jLoader.ApplySemanticAsync`는 통합 — 테스트에선 호출 검증을 위해 얇은 시임 또는 실제 호출 생략 가능하도록 `loader`를 nullable/주입으로.)

> 주의: 기존 `Program.cs` enrich 블록의 동작(키 가드, 루트 조인 BEFORE 해시/슬라이스, 사이드카 fresh-wins 병합)을 그대로 옮긴다. 리뷰가 확인한 불변식 유지.

### 3. `Neo4jGraphReader.ListIfaceMethods(string interfaceName)` (추가)
- 그 인터페이스가 선언하고 **Torba.Service 구현이 존재하는**(=enrich 가능) 메서드 이름들(중복 제거).
- enrich 불가 메서드(구현 없음)는 목록에서 제외해, 고르면 항상 결과가 나오게 한다.
- 통합(그래프 의존), 단위테스트 없음. TUI E2E로 검증.

### 4. `EnrichPicker` (신규, Spectre.Console TUI — 얇게)
흐름:
1. 최상위 단일선택: `화면 ViewModel` / `서버 인터페이스`.
2. **VM:** `VanuatuLayout.ListClientModuleProjects` 단일선택 → `ListViewModels` **다중선택**(전체 선택 지원) → 선택분 각각 `RunVmAsync` 루프.
3. **iface:** `ListServiceInterfaces`를 폴더 라벨과 함께 **단일선택** → `ListIfaceMethods` **다중선택** → 선택분 각각 `RunIfaceAsync` 루프.
4. 진행 표시 + 요약: `enriched N / skipped M / failed K`.
- 항목별 `try/catch` — 한 항목 실패가 루프를 죽이지 않음(실패는 failed에 집계·메시지).
- 렌더링과 선택→러너 호출 매핑을 분리해 코어는 위 단위테스트로 커버, 렌더는 얇게.

## 데이터 흐름

```
enrich -c --semantic
   └─▶ EnrichPicker (Spectre TUI)
         ├─ VanuatuLayout (FS 목록)
         ├─ Neo4jGraphReader.ListIfaceMethods (그래프, iface 경로)
         └─ 선택 → EnrichRunner.RunVm/RunIface 루프
                     └─ 기존 코어: 그래프 조회 → 슬라이스 → Haiku → 사이드카 + Neo4j upsert
```

## 의존성

- **Spectre.Console**(NuGet) 추가 — 멀티/단일 선택 프롬프트.

## 에러 처리

- API 키 없음(env/appsettings 모두) → 안내 후 반환(TUI 진입 전).
- 빈 프로젝트/ViewModel/인터페이스/메서드 목록 → "대상 없음" 안내 후 상위로.
- 항목 실행 실패 → failed 집계, 다음 항목 계속. AnthropicClient는 비-2xx 시 API 본문 포함 예외(이미 적용).

## 테스트 전략

| 대상 | 방식 |
|---|---|
| `VanuatuLayout` | 임시 디렉터리 픽스처 단위테스트(프로젝트/VM/인터페이스 목록·빈 경우) |
| `EnrichRunner` | fake `IGraphReader`+`ILlmClient` 단위테스트(VM/iface 실행·델타-스킵·병합) |
| `ListIfaceMethods` | 통합(그래프) — TUI E2E |
| `EnrichPicker` | 대화형, 수동 E2E 1회(프로젝트 선택→VM 다중→요약, 인터페이스→메서드 다중→요약) |

## 완료 기준

- `enrich -c ... --semantic ...` 실행 시 TUI가 뜨고, VM 다중선택·인터페이스 메서드 다중선택으로 시맨틱이 생성·사이드카 기록·Neo4j upsert 된다.
- 기존 enrich 동작 불변(델타-스킵·사이드카 분리·결정론/LLM 경계).
- `--vm`/`--iface`는 더 이상 존재하지 않는다.
- 단위테스트 그린 + 수동 E2E 1회 통과.
