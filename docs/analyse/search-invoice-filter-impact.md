# SearchInvoiceFilter 타입 변경 영향도 분석

> 출처: Vanuatu 코드 지식 그래프(Neo4j) 질의 결과. 추출 기준선 2026-06-05(풀 커버리지).
> 분석 대상: `SearchInvoiceFilter`(`Vanuatu.Service.Accounting.Filter.SearchInvoiceFilter`)의 프로퍼티 타입을 바꿀 때 영향받는 ViewModel·서비스·메서드 전부.

## 요약

- **영향 모듈**: Customer · Accounting · Order (3개 WPF 모듈) + 서버(`Torba.Service.Accounting`, `Torba.Workbench`) + REST 프록시(`Shefa.Service.RestAPI`)
- **직접 영향 메서드 9개**(`USES_TYPE`), **전파 호출자 7개**(`INVOKE`)
- 클라/서버 두 `PaymentService`가 동일 `IPaymentService.SearchInvoiceAsync`를 구현하므로 네트워크 경계 양쪽이 함께 영향받음
- `SearchInvoiceVMFilter`는 Customer/Accounting/Order 3개 모듈에 각각 존재(동명 클래스 중복)

## 1차 영향 — 시그니처에 `SearchInvoiceFilter`를 직접 쓰는 메서드 (`USES_TYPE`)

타입을 파라미터/반환으로 직접 다루므로 컴파일 수준에서 직접 깨질 수 있는 메서드들.

### ViewModel (5)
| ViewModel | 메서드 | 모듈 |
|---|---|---|
| `CustomerEditViewModel` | `GetInvoiceFilter` | Customer |
| `SearchInvoiceVMFilter` | `GenerateFilter` | Customer |
| `SearchInvoiceVMFilter` | `GenerateFilter` | Accounting |
| `SearchInvoiceVMFilter` | `GenerateFilter` | Order |
| `SearchInvoiceViewModel` | `GetFilter` | Accounting |

### 인터페이스 / 서비스 / 컨트롤러 (4)
| 종류 | 타입 | 메서드 |
|---|---|---|
| Interface (경계) | `Vanuatu.Service.Accounting.IPaymentService` | `SearchInvoiceAsync` |
| Service (클라 프록시) | `Shefa.Service.RestAPI.PaymentService` | `SearchInvoiceAsync` |
| Service (서버 구현) | `Torba.Service.Accounting.PaymentService` | `SearchInvoiceAsync` |
| Controller | `Torba.Workbench.Controllers.PaymentController` | `SearchInvoice` |

## 2차 영향 — 위 메서드를 호출하는 상위 메서드 (`INVOKE` 전파)

시그니처는 안 바뀌어도 동작/데이터 흐름이 영향받는 호출자.

| 호출 ViewModel | 메서드 | 호출 대상 | 모듈 |
|---|---|---|---|
| `CustomerEditViewModel` | `ExportCustomerInvoice` | `SearchInvoiceAsync`, `GetInvoiceFilter` | Customer |
| `CustomerEditViewModel` | `GetInvoices` | `SearchInvoiceAsync`, `GetInvoiceFilter` | Customer |
| `PlaceOrderViewModel` | `GetInvoices` | `SearchInvoiceAsync`, `GenerateFilter` | Order |
| `PlaceWebOrderViewModel` | `GetInvoices` | `SearchInvoiceAsync`, `GenerateFilter` | Order |
| `PlaceMalaitaOrderViewModel` | `GetInvoices` | `SearchInvoiceAsync`, `GenerateFilter` | Order |
| `SearchInvoiceViewModel` | `ExportExcelAsync` | `SearchInvoiceAsync`, `GetFilter` | Accounting |
| `SearchInvoiceViewModel` | `GetInvoices` | `SearchInvoiceAsync`, `GetFilter` | Accounting |

## ⚠️ 한계

`USES_TYPE`은 **메서드 파라미터/반환 타입만** 잡는다(쿡북 §5-2). **생성자 주입·필드 선언·프로퍼티 타입으로만 쓰이는 곳은 누락**될 수 있으니, 위 목록을 상위집합의 출발점으로 보고 최종 확인은 컴파일러로 할 것.

## 재현용 Cypher

```cypher
// 1) 타입 노드 확인
MATCH (t) WHERE t.name CONTAINS 'SearchInvoiceFilter'
RETURN DISTINCT labels(t) AS labels, t.name, t.fullName;

// 2) 1차 영향 — USES_TYPE으로 직접 참조하는 메서드 + 소유 타입
MATCH (m:Method)-[:USES_TYPE]->(t {name:'SearchInvoiceFilter'})
OPTIONAL MATCH (owner)-[:HAVE]->(m)
RETURN labels(owner) AS ownerKind, owner.fullName AS owner, m.fullName AS method
ORDER BY owner, method;

// 3) 2차 영향 — 1차 메서드를 호출하는 상위 메서드(전파)
MATCH (m:Method)-[:USES_TYPE]->(t {name:'SearchInvoiceFilter'})
MATCH (caller:Method)-[:INVOKE]->(m)
RETURN DISTINCT caller.fullName AS caller, collect(DISTINCT m.name) AS calls
ORDER BY caller;
```
