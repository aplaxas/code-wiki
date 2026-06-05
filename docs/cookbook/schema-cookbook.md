# Vanuatu 코드 그래프 — 스키마 쿡북 (LLM용)

> 이 문서는 `mcp-neo4j-cypher`를 통해 Neo4j에 질의하는 LLM에게 **이 그래프의 스키마와 검증된 Cypher 패턴**을 주입하기 위한 것입니다. 시스템 프롬프트/컨텍스트로 제공하세요.

---

## 1. 노드 (다중 라벨)

모든 노드는 주 라벨 1개 + (해당 시) 역할 라벨 N개를 가집니다. 예: `(:Class:ViewModel)`, `(:Class:Service)`, `(:Method)`.

**주 라벨:** `Method`, `Class`, `Interface`, `Command`, `File`, `Folder`, `Solution`, `Project`, `Package`

**역할 라벨(2차):** `ViewModel`, `Controller`, `Service`, `Repository`, `Entity`, `DTO`, `View`
역할 휴리스틱 — Entity=`IBaseEntity` 구현, ViewModel=`BindableBase` 상속/`*ViewModel`, Controller=`ControllerBase`/`*Controller`, Service=`I*Service` 구현, Repository=이름에 `Repository`, DTO=`*.DTO` 네임스페이스/`*DTO`, View=`*View`(ViewModel 제외).

**공통 노드 프로퍼티:**
| 프로퍼티 | 의미 |
|---|---|
| `pk` | 안정 해시(FNV-1a) 기본키. MERGE/식별 기준 |
| `name` | 짧은 이름 (예: `SearchOrdersAsync`) |
| `fullName` | 정규화된 전체 이름 (예: `Vanuatu.Service.Order.IOrderService.SearchOrdersAsync`) |

> `Method` 노드는 시그니처(인자/반환)까지 `pk`에 반영되어 오버로드가 구분됩니다.

---

## 2. 관계 (엣지 타입)

| 타입 | 방향 (A→B) | 의미 |
|---|---|---|
| `BINDS_TO` | View → ViewModel | Prism 네이밍 컨벤션 매칭 |
| `DEFINES_COMMAND` | ViewModel(Class) → Command | VM이 Command 멤버 보유 |
| `EXECUTES` | Command → Method | Command의 핸들러(`new DelegateCommand(H)`) |
| `INVOKE` | Method → Method | 메서드 호출 (**= "CALLS"**. 타입명은 INVOKE) |
| `IMPLEMENTS_METHOD` | Method(구현) → Method(인터페이스) | **네트워크 경계 관통의 핵심** |
| `USES` | Method → Entity(Class) | 서버 메서드가 `IRepository<T>` 필드로 만지는 DB 엔티티 |
| `USES_TYPE` | Method → Type(Class/Interface) | 메서드 파라미터/반환 타입 참조 (영향도 분석) |
| `REGISTERS` | Interface → Class | DI 등록. 프로퍼티 `lifetime` ∈ {Scoped, Singleton, Transient} |
| `OF_TYPE` | Class → Type | 상속/인터페이스 구현 (타입 레벨) |
| `HAVE` | Type → Method | 타입이 메서드 보유 |
| `CONSTRUCT` | Method → Class | 메서드가 객체 생성(new) |
| `DECLARED_AT` | Type → File | 선언 위치 |
| `INCLUDED_IN` | File/Folder/Project → Folder | 파일시스템 포함 |
| `DEPENDS_ON` | Project → Project/Package | 프로젝트 의존 |
| `CONTAINS` | Solution → Project | 솔루션 구성 |

---

## 3. 🔑 경계 관통 패턴 (가장 중요)

클라이언트 WPF와 백엔드는 **공유 인터페이스 메서드 노드**로 그래프에서 봉합됩니다. 클라 프록시 메서드와 서버 서비스 메서드가 둘 다 동일 `IOrderService.X` 인터페이스 멤버를 `IMPLEMENTS_METHOD`로 가리키므로, 그 인터페이스 `Method` 노드가 다리입니다.

```cypher
// 핸들러 → (호출) → 인터페이스 메서드 ← (구현) ← 서버 서비스 메서드 → 엔티티
MATCH (h:Method)-[:INVOKE]->(im:Method)<-[:IMPLEMENTS_METHOD]-(impl:Method)
MATCH (impl)-[:USES]->(e:Entity)
RETURN h.fullName, im.fullName, impl.fullName, e.name
```

---

## 4. 활용 사례별 검증 Cypher

### ① DTO/타입 변경 영향도 (누락 없는 상위집합)
```cypher
// FilterDTO를 파라미터/반환으로 쓰는 모든 메서드 (어느 레이어든)
MATCH (m:Method)-[:USES_TYPE]->(t {name: $typeName})
OPTIONAL MATCH (owner)-[:HAVE]->(m)
RETURN labels(owner) AS ownerKind, owner.name AS owner, m.name AS method
ORDER BY owner;
```

### ② 화면 → DB E2E 추적 (단일 체인)
```cypher
MATCH (v:View {name: $viewName})-[:BINDS_TO]->(vm:ViewModel)
MATCH (vm)-[:DEFINES_COMMAND]->(cmd:Command)-[:EXECUTES]->(h:Method)
MATCH (h)-[:INVOKE*1..4]->(im:Method)<-[:IMPLEMENTS_METHOD]-(impl:Method)
MATCH (impl)-[:USES]->(e:Entity)
RETURN v.name, vm.name, cmd.name, h.name, im.fullName, impl.fullName, e.name;
```
> Mermaid 출력이 필요하면 위 결과를 `graph TD` 체인으로 변환해 제시.

### ③ DI 안티패턴 — 탐지는 Cypher가 수행
```cypher
// 등록된 인터페이스→구현 + 생명주기
MATCH (i:Interface)-[r:REGISTERS]->(impl:Class)
RETURN i.name, impl.name, r.lifetime;

// 프로젝트 순환 의존
MATCH path = (p:Project)-[:DEPENDS_ON*2..]->(p)
RETURN [n IN nodes(path) | n.name] AS cycle LIMIT 20;
```

---

## 5. ⚠️ 현재 그래프의 한계 (질의 시 유의)

1. **화면 측 부분 커버리지(환경 의존):** 서버·서비스·DTO·인터페이스(`IMPLEMENTS_METHOD`/`USES`/`USES_TYPE`/`REGISTERS`)는 전체 커버됩니다. 그러나 **WPF 모듈(`Shefa.Module.*`)의 `View`/`ViewModel`/`Command`는 환경에 따라 비거나 적습니다** — net10-windows WPF의 design-time 빌드에 Telerik 피드 인증이 필요하고, 막힌 환경에서는 모듈 소스가 일부만 캡처됩니다. 따라서 `BINDS_TO`/`EXECUTES`/`(:ViewModel)`/`(:View)` 결과가 비면 코드에 없는 게 아니라 **그 모듈이 완전 분석되지 않은 것**입니다. Telerik 피드가 인증된 환경에서 재적재하면 채워집니다.
2. **생성자 주입 DI 미반영:** `USES_TYPE`는 일반 메서드만 추출하고 **생성자 파라미터는 추출하지 않습니다.** 따라서 "Singleton이 Scoped를 생성자 주입하는 captive dependency"는 현재 그래프만으로는 완전 탐지 불가 — `REGISTERS` 생명주기 + 프로젝트 `DEPENDS_ON` 수준까지만 가능. (향후 보강 항목)
3. **라우트 문자열 경계(B)는 미구현:** HTTP 경로 기준 컨트롤러 연결은 후순위. 경계 관통은 공유 인터페이스(`IMPLEMENTS_METHOD`)로만 이뤄집니다.
4. **이름 충돌:** `name`은 짧은 이름이라 네임스페이스 간 충돌 가능. 정밀 식별은 `fullName` 또는 `pk` 사용.
5. **역할 라벨·`REGISTERS.lifetime`은 NDJSON 적재 경로에서만 채워집니다.** 반드시 `--output ndjson` → `--load-ndjson`로 적재하세요. 레거시 직접 적재 경로(`--output neo4j` 기본값)는 주 라벨만 쓰고 역할 라벨과 관계 프로퍼티(lifetime)를 누락하므로, `(:ViewModel)`/`(:Entity)` 같은 역할 라벨 질의나 `r.lifetime`이 **빈 결과**가 됩니다.
6. **`BINDS_TO`는 프로젝트 단위 매칭:** View↔VM 연결이 프로젝트 내부에서만 이뤄집니다. View와 ViewModel이 서로 다른 프로젝트에 나뉘어 있으면 연결이 누락될 수 있습니다(대부분 Prism 모듈은 같은 프로젝트라 동작). 전역 매칭은 향후 보강 항목.

---

## 6. 질의 작성 팁

- 역할 라벨로 시작점을 좁혀라: `(:ViewModel)`, `(:Controller)`, `(:Entity)`.
- 메서드 호출 체인은 `INVOKE`(가변 길이 `*1..4`)로, 경계는 `IMPLEMENTS_METHOD`로 건너라.
- 결과가 비면 §5의 커버리지 한계부터 의심하라(엣지 0 ≠ 코드에 없음).
