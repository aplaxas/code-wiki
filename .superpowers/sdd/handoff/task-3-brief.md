### Task 3: `Node`/`Edge` record + `Graph`

**Files:** Create `Model/Node.cs`, `Model/Edge.cs`, `Model/Graph.cs`; Test `GraphTests.cs`

**Interfaces:** Produces `Node`, `Edge` records; `Graph` with `AddNode`/`AddEdge`/`Nodes`/`Edges`.

- [ ] **Step 1: 실패 테스트**
```csharp
using System.Collections.Generic; using CodeWiki.Model; using System.Linq; using Xunit;
public class GraphTests {
    static Node N(string pk, IReadOnlyDictionary<string,string> p) =>
        new("Class", pk, "n", "full", p, new[]{"ViewModel"});
    [Fact] public void DedupNodeByPk() {
        var g = new Graph();
        g.AddNode(N("1", new Dictionary<string,string>())); g.AddNode(N("1", new Dictionary<string,string>()));
        Assert.Single(g.Nodes);
    }
    [Fact] public void NonEmptyPropWinsOverEmpty() {
        var g = new Graph();
        g.AddNode(N("1", new Dictionary<string,string>{["k"]=""}));
        g.AddNode(N("1", new Dictionary<string,string>{["k"]="v"}));
        Assert.Equal("v", g.Nodes.Single().Props["k"]);
    }
    [Fact] public void DedupEdgeByFromToType() {
        var g = new Graph();
        var e = new Edge("CALLS","1","2", new Dictionary<string,string>());
        g.AddEdge(e); g.AddEdge(e);
        Assert.Single(g.Edges);
    }
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter GraphTests` → FAIL

- [ ] **Step 3: 구현**
```csharp
// Node.cs
namespace CodeWiki.Model;
public sealed record Node(string Label, string Pk, string Name, string FullName,
    System.Collections.Generic.IReadOnlyDictionary<string,string> Props,
    System.Collections.Generic.IReadOnlyList<string> Roles);
```
```csharp
// Edge.cs
namespace CodeWiki.Model;
public sealed record Edge(string Type, string FromPk, string ToPk,
    System.Collections.Generic.IReadOnlyDictionary<string,string> Props);
```
```csharp
// Graph.cs
using System.Collections.Generic; using System.Linq;
namespace CodeWiki.Model;
public sealed class Graph {
    private readonly Dictionary<string,Node> _nodes = new();
    private readonly Dictionary<string,Edge> _edges = new();
    public IReadOnlyCollection<Node> Nodes => _nodes.Values;
    public IReadOnlyCollection<Edge> Edges => _edges.Values;
    public void AddNode(Node n) => _nodes[n.Pk] = _nodes.TryGetValue(n.Pk, out var e) ? Merge(e, n) : n;
    public void AddEdge(Edge e) { var k = $"{e.FromPk}|{e.ToPk}|{e.Type}"; if (!_edges.ContainsKey(k)) _edges[k] = e; }
    private static Node Merge(Node a, Node b) {
        var props = new Dictionary<string,string>(a.Props);
        foreach (var (k,v) in b.Props) if (!string.IsNullOrEmpty(v)) props[k] = v;
        var roles = a.Roles.Concat(b.Roles).Distinct().ToList();
        return a with {
            Name = string.IsNullOrEmpty(a.Name) ? b.Name : a.Name,
            FullName = string.IsNullOrEmpty(a.FullName) ? b.FullName : a.FullName,
            Props = props, Roles = roles };
    }
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter GraphTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "feat(codewiki): Node/Edge record + Graph 빌더(dedup·props 병합)"`

---

