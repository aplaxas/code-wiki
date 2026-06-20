### Task 9: `TypeExtractor` 확장 — `DECLARES`/`CALLS`/`INSTANTIATES`

**Files:** Modify `Extraction/TypeExtractor.cs`; Test add to `TypeExtractorTests.cs`

**Interfaces:** 같은 `TypeExtractor`가 메서드 노드 + `DECLARES`(타입→메서드) + `CALLS`(메서드→메서드, 인터페이스 직행 포함) + `INSTANTIATES`(메서드→생성타입)도 산출.

- [ ] **Step 1: 실패 테스트(추가)**
```csharp
[Fact] public void DeclaresAndCallsInterfaceDirect() {
    var g = Run(@"namespace N {
        public interface ISvc { void Do(); }
        public class Vm { private ISvc _s; public void Handler() { _s.Do(); } }
    }");
    var handler = g.Nodes.Single(n => n.Name=="Handler");
    var vm = g.Nodes.Single(n => n.Name=="Vm");
    var doMethod = g.Nodes.Single(n => n.Name=="Do");
    Assert.Contains(g.Edges, e => e.Type==Rel.Declares && e.FromPk==vm.Pk && e.ToPk==handler.Pk);
    Assert.Contains(g.Edges, e => e.Type==Rel.Calls && e.FromPk==handler.Pk && e.ToPk==doMethod.Pk); // 인터페이스 메서드로 직행
}
[Fact] public void Instantiates() {
    var g = Run("namespace N { public class A {} public class B { public void M(){ var a = new A(); } } }");
    var m = g.Nodes.Single(n=>n.Name=="M"); var a = g.Nodes.Single(n=>n.Name=="A");
    Assert.Contains(g.Edges, e => e.Type==Rel.Instantiates && e.FromPk==m.Pk && e.ToPk==a.Pk);
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter TypeExtractorTests` → FAIL(새 2건)

- [ ] **Step 3: 구현(Extract 루프 내, 노드 추가 직후 삽입)**
```csharp
foreach (var m in t.GetMembers().OfType<IMethodSymbol>()) {
    if (m.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet
        or MethodKind.EventAdd or MethodKind.EventRemove) continue;
    var mNode = SymbolNodes.ForMethod(m);
    graph.AddNode(mNode);
    graph.AddEdge(new Edge(Rel.Declares, node.Pk, mNode.Pk, Empty));
    foreach (var sr in m.DeclaringSyntaxReferences) {
        var syntax = sr.GetSyntax();
        var model = ctx.Compilation.GetSemanticModel(syntax.SyntaxTree);
        foreach (var inv in syntax.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>())
            if (model.GetSymbolInfo(inv).Symbol is IMethodSymbol callee) {
                var cn = SymbolNodes.ForMethod(callee.OriginalDefinition);
                graph.AddNode(cn);
                graph.AddEdge(new Edge(Rel.Calls, mNode.Pk, cn.Pk, Empty));
            }
        foreach (var oc in syntax.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ObjectCreationExpressionSyntax>())
            if (model.GetSymbolInfo(oc).Symbol is IMethodSymbol ctor && ctor.ContainingType is INamedTypeSymbol created) {
                var crn = SymbolNodes.ForType(created, _roles);
                if (crn != null) { graph.AddNode(crn); graph.AddEdge(new Edge(Rel.Instantiates, mNode.Pk, crn.Pk, Empty)); }
            }
    }
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter TypeExtractorTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "feat(codewiki): TypeExtractor DECLARES/CALLS/INSTANTIATES"`

---

