### Task 7: `RoleClassifier`

**Files:** Create `Roslyn/RoleClassifier.cs`; Test `RoleClassifierTests.cs`

**Interfaces:** Produces `IReadOnlyList<string> RoleClassifier.Classify(INamedTypeSymbol)`.

- [ ] **Step 1: 실패 테스트**
```csharp
using System.Linq; using CodeWiki.Model; using CodeWiki.Roslyn; using Microsoft.CodeAnalysis; using Xunit;
public class RoleClassifierTests {
    static INamedTypeSymbol T(string src, string name) {
        var (c,_) = TestCompiler.Compile(src);
        return (INamedTypeSymbol)c.GetSymbolsWithName(name).Single();
    }
    [Fact] public void ViewModelByName() {
        var t = T("public class FooViewModel {}", "FooViewModel");
        Assert.Contains(Labels.ViewModel, new RoleClassifier().Classify(t));
    }
    [Fact] public void ViewByName_NotViewModel() {
        var t = T("public class FooView {}", "FooView");
        var roles = new RoleClassifier().Classify(t);
        Assert.Contains(Labels.View, roles); Assert.DoesNotContain(Labels.ViewModel, roles);
    }
    [Fact] public void PlainClassNoRole() =>
        Assert.Empty(new RoleClassifier().Classify(T("public class Foo {}", "Foo")));
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter RoleClassifierTests` → FAIL

- [ ] **Step 3: 구현**
```csharp
using System.Collections.Generic; using System.Linq; using CodeWiki.Model; using Microsoft.CodeAnalysis;
namespace CodeWiki.Roslyn;
public sealed class RoleClassifier {
    public IReadOnlyList<string> Classify(INamedTypeSymbol t) {
        var roles = new List<string>();
        var name = t.Name;
        var ns = t.ContainingNamespace?.ToDisplayString() ?? "";
        bool Inherits(string baseName) { for (var b=t.BaseType; b!=null; b=b.BaseType) if (b.Name==baseName) return true; return false; }
        bool ImplementsName(string ifaceName) => t.AllInterfaces.Any(i => i.Name == ifaceName);

        if (ImplementsName("IBaseEntity")) roles.Add(Labels.Entity);
        if (name.EndsWith("ViewModel") || Inherits("BindableBase")) roles.Add(Labels.ViewModel);
        if (name.EndsWith("Controller") || Inherits("ControllerBase")) roles.Add(Labels.Controller);
        if (t.AllInterfaces.Any(i => i.Name.StartsWith("I") && i.Name.EndsWith("Service")
            && (i.ContainingNamespace?.ToDisplayString() ?? "").StartsWith("Vanuatu.Service"))) roles.Add(Labels.Service);
        if (name.Contains("Repository")) roles.Add(Labels.Repository);
        if (ns.Contains(".DTO") || name.EndsWith("DTO")) roles.Add(Labels.Dto);
        if (name.EndsWith("View") && !name.EndsWith("ViewModel")) roles.Add(Labels.View);
        return roles;
    }
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter RoleClassifierTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "feat(codewiki): RoleClassifier 역할 라벨 휴리스틱"`

---

## 추출기 (T8~T14)

공통: `ExtractionContext`를 먼저 만든다(아래 T8 Step 0). 모든 추출기 테스트는 `TestCompiler.Compile` → `new ExtractionContext(c, "/", "Test")` → `extractor.Extract(ctx, graph)` → graph 단언.

