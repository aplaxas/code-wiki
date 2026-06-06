---
name: codegraph
description: >-
  Query the Vanuatu codebase's neo4j code graph to answer structural/architecture
  questions FAST and completely — change-impact ("이 타입 바꾸면 어디가 깨지나"), screen→DB
  E2E traces ("이 버튼 누르면 뭐가 일어나나"), module inventories (View/ViewModel/Command),
  client→server boundary crossings (IMPLEMENTS_METHOD), and DI lifetime checks. Use this
  whenever the user asks who-uses / who-calls / where-is-X-used / impact-of-changing-X /
  trace-from-screen-to-entity / what's-in-module / orphan ViewModel / DI registration —
  even if they don't say "graph" or "neo4j". The graph holds the call graph (INVOKE),
  interface↔impl wiring (IMPLEMENTS_METHOD), type usage (USES/USES_TYPE), MVVM bindings
  (BINDS_TO/DEFINES_COMMAND/EXECUTES), and DI (REGISTERS), so it answers reachability
  questions in seconds that would otherwise need wide grep sweeps. Triggered explicitly
  with /codegraph, or proactively when a request is a structural reachability question.
---

# codegraph — Vanuatu code graph querying

The Vanuatu solution (server `Torba.*`, client `Shefa.*`, domain `Vanuatu.*`, shipping
`Penama.*`) has been ingested into a **neo4j code graph**. This skill makes querying it
reliable: the schema and the tricky traversal rules are already captured below, so you
don't burn queries rediscovering them (which is where naive attempts waste most of their
effort and still miss edges).

**Authoritative ingest spec:** `docs/schema-cookbook.md` is the spec the graph was *built*
from — it's ground truth for node/edge semantics and the ingester's known limitations. The
schema below is reconciled against it AND against the live graph. Where the two disagree
(e.g. an edge the spec defines but the current load is missing), this skill flags it — trust
`CALL db.relationshipTypes()` on the live graph over the spec for what's actually queryable.

**Prerequisite:** the `neo4j` MCP server must be connected. The tools are
`mcp__neo4j__read_neo4j_cypher` (use this — read only) and `mcp__neo4j__get_neo4j_schema`.
If they aren't loaded, run `ToolSearch` with `select:mcp__neo4j__read_neo4j_cypher`. If the
server is down, say so and fall back to Read/Grep over `c:\develop\...\Vanuatu`.

## Golden rules (read before writing any Cypher)

1. **Don't re-run `get_neo4j_schema`.** The verified schema is below — it's the whole point
   of this skill. Re-deriving it costs several queries and a timeout risk on a ~23k-edge graph.
2. **Always bound AND domain-scope variable-length traversals.** Two reasons, not one.
   *Performance:* `INVOKE*` unbounded or undirected times out on a ~23k-edge graph — use a
   directed, depth-bounded pattern (`-[:INVOKE*1..4]->`). *Correctness:* `INVOKE` and
   `CONSTRUCT` are **not** filtered to your code — the extractor emits an edge for *every*
   resolved call/`new`, so **63% of INVOKE targets (≈14.4k of 23k) and ≈47% of CONSTRUCT
   targets are framework methods** (`System.*`, `Microsoft.*`, `Telerik.*`; ≈5.6k such Method
   nodes exist). An unfiltered back-trace or forward-trace drowns in `Enumerable.Select` &
   friends. So constrain the start set and add the **domain filter** (below) on the traversed
   methods. Widen depth only if a bounded, domain-scoped query comes back empty.
3. **`IMPLEMENTS_METHOD` points impl → interface.** The same `Vanuatu.Service.*` interface is
   implemented by BOTH the server (`Torba.Service.*`) and the WPF client (`Shefa.Service.*`).
   To land on the server, traverse `(iface)<-[:IMPLEMENTS_METHOD]-(impl)` and filter
   `impl.fullName STARTS WITH 'Torba.'`. This is the #1 source of wrong answers.
4. **Graph-first, then Read to fill gaps.** The graph has no `file:line`, no HTTP route/verb,
   and no `DEPENDS_ON` edge. Use the graph to nail the *set* of nodes and the *chain*
   (completeness), then Read/Grep the named files only to recover line numbers, routes, or
   confirm a suspicious result (precision). Tell the user which facts came from the graph and
   which from source.
5. **Report what the graph can't tell you.** If the question needs something the graph doesn't
   model (see "Known blind spots"), say so explicitly rather than inventing it.
6. **Disambiguate same-named methods by signature.** Method nodes now carry `arguments` and
   `returnType` properties (and `modifiers` where the declaration had them). A bare-name anchor
   (`{name:'EditOrder'}`) often hits several overloads/owners — return `n.arguments, n.returnType`
   alongside `fullName` and pin the exact one before traversing. This is the reliable way to
   resolve the handler the user means.

**Domain filter** (paste into any `INVOKE`/`CONSTRUCT` traversal to drop framework noise):
```cypher
WHERE m.fullName STARTS WITH 'Shefa' OR m.fullName STARTS WITH 'Torba'
   OR m.fullName STARTS WITH 'Vanuatu' OR m.fullName STARTS WITH 'Penama'
```

## Verified schema (ground truth — do not rediscover)

**Node labels (15):** `Solution, Project, Folder, File, Class, Interface, Method, Controller,
Service, Repository, DTO, Entity, ViewModel, View, Command`.
Multi-label combos exist: `Class:Controller`, `Class:Service`, `Class:View`, `Class:ViewModel`,
`Class:DTO`, `Class:Entity`, `Class:DTO:Entity`, `Interface:Repository`, `Interface:DTO`.
The role labels are name/interface heuristics (`RoleClassifier`), so **`:Service` is applied to
*any* class implementing an `I*Service` interface — both the server impl (`Torba.*`) and the WPF
client proxy (`Shefa.*`)**. Never use the `:Service` label alone to pick the server side; filter
by `fullName` prefix (`Torba.`) as in golden rule #3.

**Node properties.** Every node has `name`, `fullName`, `pk`. Beyond those, **`Method` nodes
carry `arguments` (e.g. `"SearchOrderFilter filter, int page"`) and `returnType`**, and code
nodes carry `modifiers` (`public`/`static`/`async`/…) where the declaration had them
(`Package` nodes carry `version`). Use `arguments`/`returnType` to disambiguate overloads
(golden rule #6) and `modifiers` to filter API surface (e.g. `WHERE m.modifiers CONTAINS 'public'`).
There is still **no** source line, route, or HTTP-verb property anywhere. On the relationship
side, `REGISTERS` is the only one with a property: `lifetime` (`Singleton`/`Scoped`/`Transient`).

> If `m.arguments`/`m.returnType` come back null on every Method, the graph predates the
> node-props load fix — re-run the NDJSON load (`--load-ndjson`) from a current extract.

**Framework methods are nodes too.** `INVOKE`/`CONSTRUCT` are unfiltered, so the graph holds
≈5.6k `Method` nodes for `System.*`/`Microsoft.*`/`Telerik.*` etc. A bare `(:Method {name:'Select'})`
anchor can land on the BCL — always pin by `fullName` prefix. Scope traversals with the domain
filter (golden rule #6).

**Relationships (direction matters):**

| Pattern | Meaning | ~count |
|---|---|---|
| `(Method)-[:INVOKE]->(Method)` | call graph — the primary edge (**incl. framework targets; 63% non-domain**) | 22,991 |
| `(Method)-[:IMPLEMENTS_METHOD]->(Method)` | **impl → interface** | 4,357 |
| `(Method)-[:CONSTRUCT]->(Class\|DTO\|Entity)` | `new` instantiation | ~4,300 |
| `(Container)-[:HAVE]->(Method)` | Class/Service/Controller/Interface/ViewModel/View/Repository/DTO owns method | ~6,900 |
| `(Method)-[:USES]->(Entity)` | **server** method touches a DB entity via an `IRepository<T>` field — the edge for "which table does this hit" | 937 |
| `(Method)-[:USES_TYPE]->(Class\|DTO\|Entity\|Interface\|ViewModel)` | references a type as **param/return** — the edge for type-change impact | ~2,900 |
| `(ViewModel)-[:DEFINES_COMMAND]->(Command)` | MVVM command declaration | 1,195 |
| `(Command)-[:EXECUTES]->(Method)` | command → handler method | 1,197 |
| `(View)-[:BINDS_TO]->(ViewModel)` | DataContext binding | 351 |
| `(X)-[:OF_TYPE]->(Interface\|Class)` | property/field type, or class implements interface | ~1,900 |
| `(Interface)-[:REGISTERS {lifetime}]->(Service\|Class)` | DI registration | 79 |
| `(Class\|Interface\|ViewModel\|View\|DTO\|...)-[:DECLARED_AT]->(File)` | declaration file (file-level, **no line**) | ~3,000 |
| `(File)-[:INCLUDED_IN]->(Folder)`, `(Folder)-[:INCLUDED_IN]->(Folder)` | file tree | ~2,600 |
| `(Solution)-[:CONTAINS]->(Project)` | **only** Solution→Project | 44 |

**`fullName` prefix = layer/scope** (there is no `Project`→`Class` edge, so scope by prefix):

| Prefix | Layer |
|---|---|
| `Vanuatu.Service.*` | service **interface** contracts (Domain), e.g. `Vanuatu.Service.Order.IOrderService.SearchOrdersAsync` |
| `Torba.Service.*` | server service **impl** |
| `Torba.Workbench.*` | server controllers (REST endpoints) |
| `Torba.DAL.*` | repositories / EF |
| `Vanuatu.DTO.*` | DTOs |
| `Vanuatu.Core.*` | entities / base |
| `Shefa.Service.*` | WPF client REST API impls |
| `Shefa.Module.<Area>.*` | client feature module (View/ViewModel/Command) — e.g. `Shefa.Module.Order` |
| `Shefa.Core.*` | client UI framework |
| `Shefa.Malaita.*` | public partner API |

Module names (use as `STARTS WITH` scopes): `Shefa.Module.Order`, `.Accounting`, `.Customer`,
`.Product`, `.PurchaseOrder`, `.Shipment`, `.Administrator`, `.Chart`, `.ChartReport`,
`.Profile`, `.Report`, `.SalesRepresentative`, `.WebManage`, `.Test`.

## Workflow

1. **Classify the question** into one of the five recipe categories (below). Read the matching
   section of `references/cypher-recipes.md` for ready, tested Cypher templates — start from a
   template instead of composing from scratch.
2. **Resolve the anchor node first.** Before a big traversal, confirm the starting type/method
   exists and grab its exact `fullName` **plus signature**:
   `MATCH (n {name:$X}) RETURN labels(n), n.fullName, n.arguments, n.returnType`.
   Names are ambiguous (a handler `EditOrder` appears under several VMs, and overloads share a
   name) — use `arguments`/`returnType` to pin the exact node you mean, not just `fullName`.
3. **Run the scoped, bounded query.** Keep depth tight; filter by `fullName` prefix to stay in
   the right layer and to keep the graph fast.
4. **Fill gaps with Read** only where the graph is blind (line numbers, HTTP routes, confirming
   a delete is real vs commented-out). To find a method's file: `(c:Class)-[:HAVE]->(m), (c)-[:DECLARED_AT]->(f:File)`
   — Method nodes have no `DECLARED_AT` of their own.
5. **Present** in the output format for that category (below). Always state your evidence source.
6. **Persist the report** to `docs/codegraph/` (see "Persisting the report"). Do this every run, right
   after presenting — the saved file is the same content the user just saw, so they get a durable,
   linkable record without asking. Tell the user the path you wrote.

## Output formats

- **Impact / who-uses / who-calls (①, ④):** a table grouped by layer — columns
  `Layer | Class/Interface | Member | Relationship`. Then a short "blast radius" summary
  (counts per layer) and any blind-spot caveat.
- **E2E trace (②):** a **Mermaid `graph TD`** from View/Command down to Entity, plus the same
  chain as an indented text trace. One node per hop; label edges with the relationship
  (`INVOKE`, `IMPLEMENTS_METHOD`, `USES`).
- **Module inventory (③):** tables — Views, ViewModels, Commands; and a `View ↔ ViewModel`
  BINDS_TO match table with a separate "orphans" list (ViewModel with no inbound BINDS_TO).
- **DI / architecture (⑤):** a table `Interface | Implementation | lifetime`, plus explicit
  notes on anything derived (not directly modeled).

Always close with a one-line **evidence note**: which facts are graph-sourced vs Read-sourced,
and confidence on completeness.

## Persisting the report (auto-save to docs/)

After presenting, **save the same report** as a markdown file so it becomes a durable, linkable
artifact in the repo — the user shouldn't have to ask, and a graph answer is most useful when it
outlives the chat. Save every run.

**Where:** `docs/codegraph/` under the Vanuatu project root (i.e.
`c:\develop\baw\phase2\baw-phase2-platform\Vanuatu\docs\codegraph\`). This is the same `docs/`
that holds `schema-cookbook.md`. Create the `codegraph/` subfolder with the Write tool if it
doesn't exist yet — just write the file; Write creates parent folders.

**Filename:** `<YYYY-MM-DD>-<question-slug>.md`, lowercase, words joined by hyphens.
- Use the **current date** from your context (the `currentDate` system field) — script time helpers
  like `Date.now()` aren't reliable here, so read the date rather than compute it.
- The slug should capture the *subject* of the question, not the literal words: pull the key
  type/method/module and the relationship being asked. Keep it ~3–7 words.
- Example: a question about "IOrderService.SearchOrdersAsync를 구현한 서버 메서드와 만지는 엔티티" →
  `2026-06-06-iorderservice-searchorders-impl-entities.md`.
- If a file with that exact name already exists (same question re-run same day), **overwrite it** —
  the latest graph answer supersedes the stale one; that's why the date+slug name is stable rather
  than timestamped.

**File contents** — write exactly what you presented, wrapped with a small frontmatter header so
the file is self-describing when found later:

```markdown
---
question: <the user's original question, verbatim>
date: <YYYY-MM-DD>
category: <① change-impact | ② E2E trace | ③ module inventory | ④ boundary crossing | ⑤ DI>
anchors: <the resolved fullName(s) the query started from>
---

# <short title>

<the full report you presented: tables, Mermaid diagram, text trace, blast-radius summary>

## Evidence
<the evidence note: graph-sourced vs Read-sourced, completeness caveats>
```

Keep the Mermaid block and tables intact (GitHub renders them). The point is that the saved file
stands alone — someone opening it cold should understand the question, the answer, and how much to
trust it, without the surrounding conversation.

## Known blind spots (authoritative — from `docs/schema-cookbook.md` §5)

- **No `file:line`.** Declaration is file-level via `DECLARED_AT`; method bodies/line numbers
  require Read. Method nodes have no `DECLARED_AT` — go through the owning class.
- **No HTTP route/verb.** Route-string boundary linking is unimplemented by design; boundary
  crossing is modeled ONLY via the shared interface (`IMPLEMENTS_METHOD`). `[HttpPost]`/`[Route]`
  must be read from `Torba.Workbench/**/*Controller.cs`.
- **Constructor-injection DI is NOT captured.** `USES_TYPE` extracts ordinary methods only, not
  constructor parameters. So "captive dependency" (a Singleton injecting a Scoped) is
  **undetectable from the graph** — you can only get `REGISTERS.lifetime` and project-level
  `DEPENDS_ON`. For ctor-wiring questions, Read the `Startup.cs`/`App.xaml.cs`.
- **`BINDS_TO` is a pure naming-convention match.** The extractor links `XView` → `XViewModel`
  by *string* (`view.Name + "Model"`), within a single analysis pass. So a View bound to a
  differently-named VM, or a VM whose `*View` lives in another project, produces an **orphan
  that isn't one**. Treat orphan-VM results as *candidates* and confirm with a solution-wide
  Grep for the matching `*View` before calling anything dead code. (This is also why there is
  no `DataContext`-expression analysis — it's name-based, not binding-resolved.)
- **`DEPENDS_ON` / `Package` — spec vs. live mismatch.** The spec defines
  `(Project)-[:DEPENDS_ON]->(Project|Package)`, but the **currently loaded graph does not have
  it** (verify: `CALL db.relationshipTypes()`). Until reloaded, derive project deps from
  cross-project `INVOKE` (`a.fullName` prefix ≠ `b.fullName` prefix) and label it derived, or
  read the `.csproj` `<ProjectReference>`s. True project-cycle detection isn't available yet.
- **Role labels & `REGISTERS.lifetime` require the NDJSON load path.** If `(:ViewModel)`/
  `(:Entity)` queries or `r.lifetime` come back empty, suspect a legacy `--output neo4j` load,
  not absence in code (§5.5). Likewise a module that failed to build is simply missing
  (partial `BINDS_TO`/`View`/`ViewModel`) — empty edge set ≠ "not in code" (§5.1, §6).
- **Generic repository sink.** Deletes/queries funnel through `Torba.DAL.IRepository.*`; the
  specific entity isn't on the INVOKE edge. For server impls, prefer the direct
  `(impl)-[:USES]->(:Entity)` edge (that's exactly what `USES` means) over guessing from
  INVOKE chains.
- **Ingest completeness is the ceiling.** A missing INVOKE edge (raw-SQL/stored-proc,
  reflection, delegate) is invisible. For high-stakes "find ALL" questions, cross-check the
  graph's answer with a targeted Grep.

## Recipe cookbook

`references/cypher-recipes.md` has copy-ready, tested Cypher for each category:
① change impact (USES_TYPE/USES back-trace), ② screen→DB E2E, ③ module inventory + orphans,
④ boundary crossing (IMPLEMENTS_METHOD), ⑤ DI lifetime + derived project deps. Open it and
adapt the closest template — don't reinvent the traversal.
