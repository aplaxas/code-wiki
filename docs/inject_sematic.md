# [PRD] Vanuatu 코드 지식 그래프 — 시맨틱 컨텍스트 주입 (확정 설계)

> 라이브 Neo4j 그래프를 직접 질의해 검증·확정한 설계다. 초안의 가정(파일 전수 LLM, 월 1,000불, 2티어 모델 등)은 실측으로 폐기됐다(§10). 산출물은 **화면 단위 HTML dossier**이며, 그 leaf로 "백엔드 인터페이스 메서드 의미"가 깔린다.

## 1. 한 줄 요약

화면(ViewModel)을 입력하면 **그 화면의 이벤트(버튼/loaded) → 백엔드 인터페이스 메서드 → 의미·소스위치**를 한 장의 HTML로 보여준다. 구조적 연결은 이미 그래프에 있으므로 LLM은 *의미*만 보탠다.

```
화면 EditOrder
  ├─ VM 요약 (LLM) + dependsOnServices (결정론)
  ├─ 이벤트들 (Command/loaded) : uiLabel·uiSection(결정론) + 이벤트 요약(LLM)
  │    └─ 백엔드 인터페이스 메서드 : summary/operationType/effects(LLM) + 서버 소스 점프(결정론)
  └─ HTML 렌더 (skill)
```

## 2. 핵심 설계 원칙 (전 과정 관통)

1. **구조에서 나오는 건 결정론, LLM은 의미에만.** `domainArea`·`uiLabel`·소스경로·`dependsOnServices`는 전부 결정론 추출. **"코드맵은 신뢰성이 생명."**
2. **연결은 이미 그래프에 있음.** VM→이벤트→핸들러→`INVOKE*`→인터페이스 메서드 사슬은 LLM 불필요(VM Command의 90.6%가 백엔드에 닿음, 실측).
3. **보조(advisory) 레이어.** 시맨틱 값은 탐색 가속기·검증 후보이지 권위가 아니다. 코드가 ground truth. 초안 §6.4의 "버그 없는 완벽한 코드"는 **"더 적은 탐색으로 더 정확한 출발점"**으로 톤다운.
4. **dossier는 저장이 아니라 조립.** 원자적 사실(요약·소스경로)을 쿡북 Cypher로 쿼리 시점 조립 → HTML.

## 3. 층 구조

| 층 | 타깃 | 산출 | 방식 |
|---|---|---|---|
| **L0 결정론** | 모든 Method / Command / VM | 소스경로·라인범위, `domainArea`, `dependsOnServices`, XAML `uiLabel`/`uiSection`/`eventKind` | Roslyn/XAML 추출 |
| **L1 인터페이스 메서드** | ~505 백엔드 인터페이스 메서드 | `summary`/`operationType`/`mutatesState`/`effects`/`keyEntities`/`caveats` | LLM (bulk) |
| **L2 화면 dossier** | VM 1개 + 그 이벤트들 | VM 요약, 이벤트별 요약 | LLM (on-demand) |

## 4. 타깃 (라이브 그래프 실측)

| 측정 | 값 | 함의 |
|---|---|---|
| VM Command 총수 | 1,196 | |
| 백엔드 인터페이스 메서드에 닿는 Command | 1,083 (90.6%) | **연결은 그래프에 이미 있음** |
| VM이 닿는 고유 백엔드 인터페이스 메서드 (`.Service.`, UI인프라 82 제외) | **≈ 505** | **L1 LLM 타깃** |
| Method 노드의 소스 파일 링크 | 0 (`implWithFile=0`) | **소스경로를 L0에서 새로 채워야 함** |
| View↔VM 페어링 | `BINDS_TO` 351개 | **화면↔VM 이미 연결** |
| EditOrderViewModel | 5,462줄 / Command 58 / 백엔드 61 닿음 | L2 대표 사례 |

**서버 impl 선택 규칙:** 인터페이스 메서드의 impl 중 `*.Service.RestAPI.*`는 클라 REST 프록시 → 제외, 나머지(`Vanuatu.Service.*`/`Torba.Service.*`)가 서버 구현. impl ≤2개만 채택(3개+ outlier는 마커/제네릭 인터페이스).

## 5. L0 — 결정론 패스 (Roslyn/XAML 추출 시, 신뢰성 100%)

LLM과 무관한 구조 보강. 현재 그래프에 없는 링크/메타를 채운다.

* **모든 `Method` 노드:** `sourcePath`(솔루션 루트 상대, 슬래시 정규화) + `startLine`/`endLine` (`DeclaringSyntaxReferences[0].GetSyntax().GetLocation().GetLineSpan()`). [strazh/Strazh/Domain/Nodes.cs](../strazh/Strazh/Domain/Nodes.cs) `MethodNode.NodeProperties`에 키 추가 → 기존 트리플 적재(`SET a += row.props`)에 그대로 실림.
  * **이중 가치:** L1/L2 LLM 입력 슬라이스 + AI coding 시 "구현으로 점프" 내비게이션.
* **`domainArea`** (인터페이스 메서드): 네임스페이스 세그먼트(`Vanuatu.Service.<X>.IFoo.Bar`의 `<X>`). 실측: Common 99 / Product 78 / People 72 / Order 63 / PurchaseOrder 50 / Accounting 39 / Shipping 31 / Report 27 / RMA 21 …
* **`dependsOnServices`** (ViewModel): 생성자 주입 서비스 목록(EditOrder는 ~27개). 화면의 도메인 발자국.
* **XAML 파싱** (View): `Command="{Binding XCommand}"` 바인딩 전수 수집 →
  * `uiLabel`(형제 `Content`) + `uiSection`(상위 `GroupBox`/`RadTabItem` Header)를 Command 노드에 부여.
  * **EventTrigger로 와이어된 이벤트(예: `LoadedCommand`)까지 포착** → 현 Command 추출이 놓치는 XAML-와이어 이벤트 보강.
* **`eventKind`** (Command/생명주기 Method): `command` / `lifecycle`. "loaded"는 XAML EventTrigger Command로 식별.
* **`viewPath`** (ViewModel): 네이밍 컨벤션(`ViewModels/<X>ViewModel.cs` ↔ `Views/<X>View.xaml`)으로 결정론 해석. 내비게이션 포인터(LLM 입력 아님).

## 6. L1 — 인터페이스 메서드 의미 (LLM, bulk)

* **형태:** Strazh CLI 플래그 `--enrich-semantic`. 적재된 그래프에서 타깃 ~505개 + 서버 impl `sourcePath` 추출 → 호스트가 슬라이스 읽기 → LLM → 노드 전용 upsert.
* **모델/SDK:** Claude **Sonnet 4.6**(`claude-sonnet-4-6`) 단일 + Anthropic **C# SDK** + 구조화 출력.
* **출력 스키마 (한국어):**

| 필드 | 내용 |
|---|---|
| `summary` | 이 백엔드 연산이 ERP 도메인에서 하는 일, 2~3문장 |
| `operationType` | enum: `조회`/`생성`/`수정`/`삭제`/`계산집계`/`검증`/`배치마감`/`외부연동` |
| `mutatesState` | bool — 상태를 바꾸나 |
| `effects` | enum 배열: `DB쓰기`/`외부서비스호출`/`메시지발행`/`파일생성`/`캐시변경`/`읽기전용` |
| `keyEntities` | 핵심 도메인 엔티티 이름 배열 (Entity 노드 378개와 v1.5 연결 발판) |
| `caveats` | **이 메서드 본문에 보이는** 제약/가드 배열 (BusinessRules의 저위험 사촌) |

* **규율:** `caveats`는 *단일 메서드 본문 가시 범위만*. 여러 파일에 걸친 진짜 비즈니스 규칙은 v2.

## 7. L2 — 화면 dossier (LLM, on-demand)

* **형태:** Strazh CLI 플래그 `--enrich-mv -p <ViewModel.cs 경로>`. **자기완결** — 이 VM에 대해:
  1. **VM 요약** — 입력 = `ViewModel.cs` 전체 소스 + `View.xaml` 통째(1M 컨텍스트라 통째 가능) + `dependsOnServices`.
  2. **이벤트별 요약** — 이벤트 = Command(DI+XAML와이어) + 생명주기. 입력 = 핸들러 본문 + **같은 VM 클래스 내 1-hop private 헬퍼 본문**(얇은 위임 보완).
  3. **L0 결정론**(XAML uiLabel/uiSection/eventKind, dependsOnServices, 소스경로) 적용.
  4. **이 VM의 이벤트가 닿는 백엔드 인터페이스 메서드도 없으면 즉석 L1 enrich**(v1 로직 재사용). → bulk 없이 화면 하나로 완전.
* **델타-스킵:** `summaryHash`(입력 슬라이스의 FNV-1a `StableHash`) + `summaryModel` 동일 시 LLM 생략. 화면 단위 반복이 빠름.

## 8. 산출물 — 화면 HTML dossier (skill)

* **형태:** 별도 skill. 그래프에 정보가 이미 있다고 가정하고 **쿡북 파라미터 Cypher 레시피**([docs/cookbook/schema-cookbook.md](../docs/cookbook/schema-cookbook.md))로 중첩 dossier 조회 → **자기완결 단일 `.html`**(인라인 CSS/JS) 렌더 후 브라우저로 염.
* **구성:**
  * 상단: 화면명 + VM 요약 + `dependsOnServices` 태그 + VM 소스 점프
  * 섹션별(`uiSection` = 탭/그룹박스) 이벤트 카드: `uiLabel`(버튼명) + `eventKind` + 이벤트 요약
  * 이벤트 펼침 → 백엔드 인터페이스 메서드: `summary`/`domainArea`/`operationType`/`effects` + **서버 impl 소스 점프**(`sourcePath:startLine`). ("클릭 → 맵 표시"가 이 펼침 인터랙션.)
* CLI=쓰기(enrich), skill=읽기+렌더로 역할 분리.

## 9. 신규 그래프 스키마 요약

| 노드 | 추가 속성 |
|---|---|
| `Method` (전체) | `sourcePath`, `startLine`, `endLine` |
| `Method` (인터페이스 메서드) | L1 6필드 + `summaryHash`, `summaryModel` |
| `Method` (생명주기) | `eventKind`, `summary` |
| `Command` | `uiLabel`, `uiSection`, `eventKind`, `summary` (+ XAML 누락분 보강) |
| `ViewModel`(Class) | `summary`, `dependsOnServices`, `viewPath` |

## 10. 비용 (전체 코드맵 일회성)

| 타깃 | 수 | 입력 | 대략 |
|---|---|---|---|
| L1 인터페이스 메서드 | ~505 | 서버 impl 슬라이스 | ~$5 |
| L2 이벤트 요약 | ~1,700 | 핸들러+1-hop | ~$10 |
| L2 VM 요약 | ~492 | VM 소스 + XAML | ~$30–60 |
| **합계** | | | **~$50–75 일회성** |

화면 단위(`--enrich-mv`)는 건당 수 센트. 초안의 "월 1,000불"은 두세 자릿수 과대.

## 11. 초안 대비 변경 내역

| 초안 | 확정 | 근거 |
|---|---|---|
| 2,809 파일 전수 LLM | ~505 인터페이스 메서드 + 화면 단위 on-demand | 연결은 이미 그래프(90.6%) |
| 파일/VM 단위 요약 | 인터페이스 메서드 + 이벤트 + VM 3층, 결정론/LLM 분리 | 사용자 관심사 = "VM 이벤트 → 백엔드" |
| Claude 3.5 Sonnet + Gemini 1.5 Flash 2티어 | Sonnet 4.6 단일 | 구식 모델·공급자 일원화 |
| 월 1,000불 / 30병렬 / Batches | ~$50–75 일회성 / 동시성 5~8 / 동기 | 규모 실측 |
| Summary+BusinessRules+Risk 동시 | L1/L2 Summary류 (Risk=그래프, BusinessRules=v2) | 신뢰성·할루시네이션 통제 |
| 본문/요약을 권위로 | 보조 레이어 + 결정론 소스위치 | 확률적 출력을 사실로 캐싱하는 위험 차단 |

## 12. 향후

- **v1.5:** `keyEntities` 문자열 → Entity 노드(378개) 엣지 연결 ("화면 이벤트 → 백엔드 연산 → 엔티티").
- **v2:** `BusinessRules` — 인터페이스 메서드의 1-hop 이웃(호출체인·Repository·Entity)까지 컨텍스트 확장해 여러 파일에 걸친 규칙(면세·정산 공식) 추출.
- **검색 GUI:** 화면 선택 UI (현재는 `-p` 경로 직접 지정).
- **커스텀 MCP 도구:** `get_screen_dossier(screen)` (현재는 쿡북 Cypher 레시피).
- **Risk:** 그래프 토폴로지(fan-in/out·경계관통·순환참조)에서 결정론 계산하는 별도 트랙.
