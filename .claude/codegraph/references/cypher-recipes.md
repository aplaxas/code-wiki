# Cypher recipe cookbook (Vanuatu code graph)

Every query here was run against the live graph. Adapt the anchor name/prefix; keep the
traversal shape. Parameters use `$name` — substitute the literal, or pass via `params`.

**Always first resolve the anchor** so you query the node you mean. Return the **signature**
too — `name` alone is ambiguous (overloads + same-named handlers across VMs):
```cypher
MATCH (n {name:$X})
RETURN labels(n) AS labels, n.fullName AS fullName,
       n.arguments AS args, n.returnType AS ret   // disambiguate overloads by signature
ORDER BY fullName
```

**Drop framework noise.** `INVOKE`/`CONSTRUCT` edges point at `System.*`/`Microsoft.*`/`Telerik.*`
methods too (63% / 47% of those edges). On any traversal over those edges that isn't already
prefix-pinned to one layer, add the domain filter on the traversed/target method:
```cypher
WHERE m.fullName STARTS WITH 'Shefa' OR m.fullName STARTS WITH 'Torba'
   OR m.fullName STARTS WITH 'Vanuatu' OR m.fullName STARTS WITH 'Penama'
```

Layer-tagging snippet reused across recipes (paste into RETURN):
```cypher
CASE WHEN m.fullName STARTS WITH 'Torba.Workbench' THEN 'Controller'
     WHEN m.fullName STARTS WITH 'Torba.Service'   THEN 'Server.Service'
     WHEN m.fullName STARTS WITH 'Torba.DAL'       THEN 'Repository'
     WHEN m.fullName STARTS WITH 'Vanuatu.Service'  THEN 'Service.Interface'
     WHEN m.fullName STARTS WITH 'Shefa.Module'     THEN 'Client.ViewModel'
     WHEN m.fullName STARTS WITH 'Shefa.Service'    THEN 'Client.RestAPI'
     WHEN m.fullName STARTS WITH 'Shefa.Core'       THEN 'Client.Core'
     ELSE 'Other' END AS layer
```

---

## ① Change impact — "이 타입 건드리면 어디가 깨지나"

**Who references a type (DTO/Entity/Class), grouped by layer.** Covers param/return/local
(`USES_TYPE`), entity operation (`USES`), and instantiation (`CONSTRUCT`).

```cypher
MATCH (m:Method)-[r:USES_TYPE|USES|CONSTRUCT]->(t {name:$TYPE})
RETURN type(r) AS rel,
  CASE WHEN m.fullName STARTS WITH 'Torba.Workbench' THEN 'Controller'
       WHEN m.fullName STARTS WITH 'Torba.Service'   THEN 'Server.Service'
       WHEN m.fullName STARTS WITH 'Torba.DAL'       THEN 'Repository'
       WHEN m.fullName STARTS WITH 'Vanuatu.Service'  THEN 'Service.Interface'
       WHEN m.fullName STARTS WITH 'Shefa.Module'     THEN 'Client.ViewModel'
       WHEN m.fullName STARTS WITH 'Shefa.Service'    THEN 'Client.RestAPI'
       ELSE 'Other' END AS layer,
  collect(DISTINCT m.fullName)[0..50] AS members, count(DISTINCT m) AS n
ORDER BY n DESC
```

**Property-level filter DTO blast radius** (e.g. `SearchInvoiceFilter` — what reads it as a
parameter/return type). Same as above with `$TYPE='SearchInvoiceFilter'`; the `USES_TYPE`
rows are the methods whose signatures change if a property type changes.

**Direct entity dependents** ("어�a 서비스가 Customer 테이블에 의존"):
```cypher
MATCH (m:Method)-[:USES]->(e:Entity {name:$ENTITY})
WHERE m.fullName STARTS WITH 'Torba.'           // server side only
RETURN m.fullName AS serverMethod ORDER BY serverMethod
```

**Output:** table grouped by layer (`Layer | Member | rel`), then a "blast radius" line with
counts per layer. Caveat: a property *type* change impacts callers transitively — if asked for
the transitive set, add one `INVOKE` hop back from each `USES_TYPE` method (bounded), and
**domain-filter the callers** (`(caller)-[:INVOKE]->(m) WHERE caller.fullName STARTS WITH 'Shefa'
OR … 'Torba' OR … 'Vanuatu' OR … 'Penama'`) so framework callers don't inflate the radius.

---

## ② Screen → DB E2E trace — "이 버튼 누르면 무슨 일이 일어나나"

**Full chain View → ViewModel → Command → handler → service interface → server impl → entity.**
This is the cookbook's canonical "boundary stitch": the handler INVOKEs the `Vanuatu.Service`
interface method, the server impl `IMPLEMENTS_METHOD` that same interface node, and the impl
**directly** `USES` the DB entity (that's the precise meaning of `USES` — a server method
touching an entity via its `IRepository<T>` field). Do NOT chase entities through extra INVOKE
hops or `USES_TYPE` — `USES` is the right, tight edge.

```cypher
MATCH (vm:ViewModel)-[:DEFINES_COMMAND]->(c:Command)-[:EXECUTES]->(h:Method)
WHERE vm.name = $VM AND c.name = $COMMAND          // e.g. 'SearchOrderViewModel','SearchCommand'
MATCH (h)-[:INVOKE*1..4]->(svc:Method)
WHERE svc.fullName STARTS WITH 'Vanuatu.Service'
OPTIONAL MATCH (svc)<-[:IMPLEMENTS_METHOD]-(impl:Method)
  WHERE impl.fullName STARTS WITH 'Torba.'
OPTIONAL MATCH (impl)-[:USES]->(e:Entity)
RETURN h.name AS handler, svc.fullName AS serviceCall,
       impl.fullName AS serverImpl, collect(DISTINCT e.name)[0..12] AS entities
```
If `entities` is empty, the server method reaches its table only through a helper it INVOKEs —
widen with `(impl)-[:INVOKE*1..2]->(:Method)-[:USES]->(e:Entity)` as a fallback (still `USES`,
not `USES_TYPE`).

**Which Commands a ViewModel defines + their handlers** (use case ②.2):
```cypher
MATCH (vm:ViewModel {name:$VM})-[:DEFINES_COMMAND]->(c:Command)-[:EXECUTES]->(h:Method)
RETURN c.name AS command, h.name AS handler ORDER BY command
```

**Start from a View** (use case ②.3): `MATCH (v:View {name:$VIEW})-[:BINDS_TO]->(vm)` then
reuse the chain above from `vm`.

**Output:** a Mermaid `graph TD` (View→ViewModel→Command→handler→serviceCall→serverImpl→Entity,
edges labelled with the relationship), plus the indented text trace. If `serverImpl` is null,
the call stayed client-side (e.g. dialog/excel service) — say so. Entities are inferred via
the service method's type usage and may include closely-related DTOs.

---

## ③ Module structure — "이 모듈 안에 뭐가 있나"

Scope a module by `fullName STARTS WITH 'Shefa.Module.<Area>'` (no Project→Class edge exists).

**Inventory (Views / ViewModels / Commands):**
```cypher
MATCH (n) WHERE n.fullName STARTS WITH $MODULE AND (n:View OR n:ViewModel OR n:Command)
RETURN [l IN labels(n) WHERE l IN ['View','ViewModel','Command']][0] AS kind,
       count(*) AS n, collect(n.name)[0..80] AS items
```

**View ↔ ViewModel BINDS_TO match table:**
```cypher
MATCH (v:View)-[:BINDS_TO]->(vm:ViewModel) WHERE vm.fullName STARTS WITH $MODULE
RETURN v.name AS view, vm.name AS viewModel ORDER BY view
```

**Orphan ViewModels (no inbound BINDS_TO)** — candidate dead/unbound VMs:
```cypher
MATCH (vm:ViewModel) WHERE vm.fullName STARTS WITH $MODULE
  AND NOT (:View)-[:BINDS_TO]->(vm)
RETURN vm.name AS orphanViewModel ORDER BY orphanViewModel
```
Two caveats before calling any of these "dead code":
- Names ending `...VMFilter` are filter/criteria holders bound as nested objects, not
  top-level DataContexts — usually view-less by design. Flag them separately.
- **`BINDS_TO` is matched only within a project** (cookbook §5.6). A ViewModel whose `*View`
  lives in a *different* project is a **false orphan**. So for each candidate, Grep the whole
  solution for the matching `*View` (`<vmName without 'Model'>` → `*View`) before concluding
  it's unbound. Report orphans as "candidates pending source check," not confirmed dead code.

**Module → server services it depends on** (use case ③.2):
```cypher
MATCH (m:Method)-[:USES_TYPE|INVOKE]->(svc)
WHERE m.fullName STARTS WITH $MODULE AND svc.fullName STARTS WITH 'Vanuatu.Service'
  AND svc.fullName CONTAINS 'Service'
RETURN DISTINCT split(svc.fullName,'.')[-2] AS serviceInterface ORDER BY serviceInterface
```

**Output:** inventory tables, the BINDS_TO match table, and a separate orphan list (real
orphans vs `*VMFilter`).

---

## ④ Boundary crossing — "클라이언트 호출이 서버 어디로 가나"

**Interface method → server implementation → entities it touches** (impl `USES` entity directly):
```cypher
MATCH (iface:Method) WHERE iface.fullName CONTAINS $IFACE_METHOD   // 'IOrderService.SearchOrdersAsync'
MATCH (iface)<-[:IMPLEMENTS_METHOD]-(impl:Method) WHERE impl.fullName STARTS WITH 'Torba.'
OPTIONAL MATCH (impl)-[:USES]->(e:Entity)
RETURN impl.fullName AS serverImpl, collect(DISTINCT e.name) AS entities
```
`USES` = the server method touches that DB entity through its `IRepository<T>` field, so it's
the authoritative "which tables" edge. Only if a method delegates to a private helper will the
entity sit one hop away — fall back to `(impl)-[:INVOKE*1..2]->(:Method)-[:USES]->(e:Entity)`.

**Broken boundary — interface methods with NO server impl** (client calls nothing on server):
```cypher
MATCH (iface:Method) WHERE iface.fullName STARTS WITH 'Vanuatu.Service' AND iface.name ENDS WITH 'Async'
WHERE NOT EXISTS {
  MATCH (iface)<-[:IMPLEMENTS_METHOD]-(impl:Method) WHERE impl.fullName STARTS WITH 'Torba.'
}
// optionally require that a client actually calls it:
AND EXISTS { MATCH (caller:Method)-[:INVOKE]->(iface) WHERE caller.fullName STARTS WITH 'Shefa.' }
RETURN iface.fullName AS unimplementedInterfaceMethod ORDER BY unimplementedInterfaceMethod
```

**Output:** `Interface.Method | Server impl | entities` table; for broken boundaries, list the
interface methods and which client(s) call them. Confirm a sampled "broken" case with Read
(the impl might exist via a base class the ingester didn't link).

---

## ⑤ DI / architecture

**DI lifetime for service interfaces:**
```cypher
MATCH (iface:Interface)-[r:REGISTERS]->(impl)
WHERE iface.name IN $IFACES                       // ['IOrderService','IPaymentService',...]
RETURN iface.name AS interface, impl.name AS implementation,
       collect(DISTINCT r.lifetime) AS lifetimes
```
Caveats:
- The same interface is often registered in multiple startups (server vs client app), so you
  may get BOTH `Singleton` and `Scoped`. `REGISTERS` has no context property to tell them
  apart — surface all distinct lifetimes and flag conflicts; resolve which app via Read of the
  relevant `Startup.cs`/`App.xaml.cs`.
- **Captive-dependency detection is not possible from the graph** (cookbook §5.2): constructor
  injection isn't modeled (`USES_TYPE` skips ctor params). You can report lifetimes and
  project-level deps, but "a Singleton captures a Scoped via its constructor" must be checked
  by Reading the registration/ctor code. Say so rather than implying the graph cleared it.

**Derived project dependencies.** The spec defines `(:Project)-[:DEPENDS_ON]->(:Project|Package)`,
but the **current load is missing it** — verify first: `CALL db.relationshipTypes()`. If absent,
derive from cross-project calls (filter out `System.*`/framework noise):
```cypher
MATCH (a:Method)-[:INVOKE]->(b:Method)
WHERE a.fullName STARTS WITH $PROJECT AND NOT b.fullName STARTS WITH $PROJECT
WITH split(b.fullName,'.')[0]+'.'+split(b.fullName,'.')[1] AS targetProject, count(*) AS calls
WHERE targetProject STARTS WITH 'Shefa' OR targetProject STARTS WITH 'Torba'
   OR targetProject STARTS WITH 'Vanuatu' OR targetProject STARTS WITH 'Penama'
RETURN targetProject, calls ORDER BY calls DESC
```
For **cycle detection**: if `DEPENDS_ON` is present (re-loaded graph), use the spec's native
query directly —
```cypher
MATCH path = (p:Project)-[:DEPENDS_ON*2..]->(p) RETURN [n IN nodes(path) | n.name] AS cycle LIMIT 20
```
If `DEPENDS_ON` is absent (current load), build the derived edge set per project pair from
cross-project `INVOKE` and look for `A→B` and `B→A` — state clearly it's a call-derived
approximation, not a declared project reference, and verify candidates against the `.csproj`
`<ProjectReference>` entries with Read.

**Output:** `Interface | Implementation | lifetime(s)` table with a conflicts column; for deps,
a `Project → Project (call count)` table and any derived cycle path, clearly marked "derived".

---

## Performance & correctness reminders

- Bound every `INVOKE*` (`*1..3`/`*1..4`); never run it undirected or unbounded on this graph.
- Resolve anchors before traversing; `CONTAINS` on `fullName` is fine for anchor lookup but
  prefer `STARTS WITH` for layer scoping (uses the prefix structure, fewer false hits).
- When a bounded query returns empty, first widen depth by one, then reconsider whether the
  edge type is right (e.g. read methods reach entities via `USES_TYPE`, not `USES`).
- Anything needing `file:line`, HTTP route/verb, or a true project reference: get the node set
  from the graph, then Read the named source to finish — and label which is which.
