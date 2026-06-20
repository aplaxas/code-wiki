### Task 10: `InterfaceImplementationExtractor` (`IMPLEMENTS_METHOD`)

**Files:** Create `Extraction/InterfaceImplementationExtractor.cs`; Test `InterfaceImplementationExtractorTests.cs`

**Interfaces:** Produces `IMPLEMENTS_METHOD`(구현 메서드 → 인터페이스 멤버). 경계 봉합 허브.

- [ ] **Step 1: 실패 테스트**
```csharp
using System.Linq; using CodeWiki.Extraction; using CodeWiki.Model; using CodeWiki.Roslyn; using Xunit;
public class InterfaceImplementationExtractorTests {
    [Fact] public void ImplMethodPointsToInterfaceMember() {
        var (c,_) = TestCompiler.Compile(
            "namespace N { public interface ISvc { void Do(); } public class Svc : ISvc { public void Do(){} } }");
        var g = new Graph();
        new InterfaceImplementationExtractor().Extract(new ExtractionContext(c,"/","T"), g);
        var impl = g.Nodes.Single(n => n.FullName=="N.Svc.Do");
        var iface = g.Nodes.Single(n => n.FullName=="N.ISvc.Do");
        Assert.Contains(g.Edges, e => e.Type==Rel.ImplementsMethod && e.FromPk==impl.Pk && e.ToPk==iface.Pk);
    }
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter InterfaceImplementationExtractorTests` → FAIL

- [ ] **Step 3: 구현**
```csharp
using System.Linq; using CodeWiki.Model; using CodeWiki.Roslyn; using Microsoft.CodeAnalysis;
namespace CodeWiki.Extraction;
public sealed class InterfaceImplementationExtractor : IExtractor {
    private static readonly System.Collections.Generic.IReadOnlyDictionary<string,string> Empty = new System.Collections.Generic.Dictionary<string,string>();
    public void Extract(ExtractionContext ctx, Graph graph) {
        foreach (var t in ctx.SourceTypes()) {
            if (t.TypeKind != TypeKind.Class) continue;
            foreach (var iface in t.AllInterfaces)
            foreach (var member in iface.GetMembers().OfType<IMethodSymbol>()) {
                if (t.FindImplementationForInterfaceMember(member) is not IMethodSymbol impl) continue;
                if (!SymbolEqualityComparer.Default.Equals(impl.ContainingType, t)) continue;
                var implNode = SymbolNodes.ForMethod(impl);
                var ifaceNode = SymbolNodes.ForMethod(member);
                graph.AddNode(implNode); graph.AddNode(ifaceNode);
                graph.AddEdge(new Edge(Rel.ImplementsMethod, implNode.Pk, ifaceNode.Pk, Empty));
            }
        }
    }
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter InterfaceImplementationExtractorTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "feat(codewiki): InterfaceImplementationExtractor(IMPLEMENTS_METHOD 허브)"`

---

