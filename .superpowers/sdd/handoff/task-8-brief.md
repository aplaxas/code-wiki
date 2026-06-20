### Task 8: `ExtractionContext` + `IExtractor` + `TypeExtractor`(타입/상속/구현)

**Files:** Create `Extraction/ExtractionContext.cs`, `Extraction/IExtractor.cs`, `Extraction/TypeExtractor.cs`; Test `TypeExtractorTests.cs`

**Interfaces:**
- Produces `ExtractionContext(Compilation, string solutionRoot, string solutionName)` with `IEnumerable<INamedTypeSymbol> SourceTypes()`.
- Produces `interface IExtractor { void Extract(ExtractionContext ctx, Graph graph); }`.
- Produces `TypeExtractor(RoleClassifier)` — 본 태스크 범위: 노드 + `DECLARED_IN` + `INHERITS` + `IMPLEMENTS` + 역할 라벨.

- [ ] **Step 1: 실패 테스트**
```csharp
using System.Linq; using CodeWiki.Extraction; using CodeWiki.Model; using CodeWiki.Roslyn; using Xunit;
public class TypeExtractorTests {
    static Graph Run(string src) {
        var (c,_) = TestCompiler.Compile(src);
        var g = new Graph();
        new TypeExtractor(new RoleClassifier()).Extract(new ExtractionContext(c,"/","T"), g);
        return g;
    }
    [Fact] public void EmitsClassAndInterfaceImplementsEdge() {
        var g = Run("namespace N { public interface IFoo {} public class Foo : IFoo {} }");
        var foo = g.Nodes.Single(n => n.Name=="Foo");
        var ifoo = g.Nodes.Single(n => n.Name=="IFoo");
        Assert.Equal(Labels.Class, foo.Label); Assert.Equal(Labels.Interface, ifoo.Label);
        Assert.Contains(g.Edges, e => e.Type==Rel.Implements && e.FromPk==foo.Pk && e.ToPk==ifoo.Pk);
    }
    [Fact] public void InheritsEdge() {
        var g = Run("namespace N { public class B {} public class D : B {} }");
        var d = g.Nodes.Single(n=>n.Name=="D"); var b = g.Nodes.Single(n=>n.Name=="B");
        Assert.Contains(g.Edges, e => e.Type==Rel.Inherits && e.FromPk==d.Pk && e.ToPk==b.Pk);
    }
    [Fact] public void UnresolvedBaseSkipped() {  // 불변식 #3
        var g = Run("namespace N { public class Vm : BindableBase {} }"); // BindableBase 미참조
        Assert.DoesNotContain(g.Edges, e => e.Type==Rel.Inherits);
    }
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter TypeExtractorTests` → FAIL

- [ ] **Step 3: 구현**
```csharp
// ExtractionContext.cs
using System.Collections.Generic; using Microsoft.CodeAnalysis;
namespace CodeWiki.Extraction;
public sealed class ExtractionContext {
    public Compilation Compilation { get; }
    public string SolutionRoot { get; }
    public string SolutionName { get; }
    public ExtractionContext(Compilation c, string solutionRoot, string solutionName)
        { Compilation = c; SolutionRoot = solutionRoot; SolutionName = solutionName; }
    public IEnumerable<INamedTypeSymbol> SourceTypes() {
        var stack = new Stack<INamespaceOrTypeSymbol>();
        stack.Push(Compilation.Assembly.GlobalNamespace);
        while (stack.Count > 0) {
            foreach (var m in stack.Pop().GetMembers()) {
                if (m is INamespaceSymbol ns) stack.Push(ns);
                else if (m is INamedTypeSymbol t) { yield return t; foreach (var nt in t.GetTypeMembers()) stack.Push(nt); }
            }
        }
    }
}
```
```csharp
// IExtractor.cs
using CodeWiki.Model;
namespace CodeWiki.Extraction;
public interface IExtractor { void Extract(ExtractionContext ctx, Graph graph); }
```
```csharp
// TypeExtractor.cs
using System.Collections.Generic; using System.Linq; using CodeWiki.Model; using CodeWiki.Roslyn; using Microsoft.CodeAnalysis;
namespace CodeWiki.Extraction;
public sealed class TypeExtractor : IExtractor {
    private static readonly IReadOnlyDictionary<string,string> Empty = new Dictionary<string,string>();
    private readonly RoleClassifier _roles;
    public TypeExtractor(RoleClassifier roles) => _roles = roles;
    public void Extract(ExtractionContext ctx, Graph graph) {
        foreach (var t in ctx.SourceTypes()) {
            var node = SymbolNodes.ForType(t, _roles);
            if (node == null) continue;
            graph.AddNode(node);
            foreach (var loc in t.Locations.Where(l => l.IsInSource)) {
                var path = loc.SourceTree?.FilePath;
                if (string.IsNullOrEmpty(path)) continue;
                var file = FileNodes.ForPath(path, ctx.SolutionRoot);
                graph.AddNode(file);
                graph.AddEdge(new Edge(Rel.DeclaredIn, node.Pk, file.Pk, Empty));
            }
            if (t.BaseType is { TypeKind: TypeKind.Class, SpecialType: SpecialType.None } bt) {
                var bn = SymbolNodes.ForType(bt, _roles);
                if (bn != null) { graph.AddNode(bn); graph.AddEdge(new Edge(Rel.Inherits, node.Pk, bn.Pk, Empty)); }
            }
            foreach (var iface in t.Interfaces) {
                var inode = SymbolNodes.ForType(iface, _roles);
                if (inode == null) continue;
                graph.AddNode(inode);
                graph.AddEdge(new Edge(Rel.Implements, node.Pk, inode.Pk, Empty));
            }
        }
    }
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter TypeExtractorTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "feat(codewiki): TypeExtractor 노드+DECLARED_IN+INHERITS+IMPLEMENTS"`

---

