### Task 5: `Names` + `SymbolNodes` + `FileNodes`

**Files:** Create `Roslyn/Names.cs`, `Roslyn/SymbolNodes.cs`, `Roslyn/FileNodes.cs`; Test `SymbolNodesTests.cs` (TestCompiler는 T6에서 만들지만 본 테스트는 인라인 미니 컴파일로 진행)

**Interfaces:** Produces `Names.Full(ISymbol)`, `SymbolNodes.ForType(INamedTypeSymbol, RoleClassifier)` (미해석/error 타입이면 `null`), `SymbolNodes.ForMethod(IMethodSymbol)`, `FileNodes.ForPath(string abs, string root)`. Consumes `RoleClassifier`(T7) — 본 태스크는 빈 분류기로 충분하게 설계(roles 인자 nullable 허용).

- [ ] **Step 1: 실패 테스트**
```csharp
using System.Linq; using CodeWiki.Model; using CodeWiki.Roslyn;
using Microsoft.CodeAnalysis; using Microsoft.CodeAnalysis.CSharp; using Xunit;
public class SymbolNodesTests {
    static Compilation Compile(string src) => CSharpCompilation.Create("t",
        new[]{CSharpSyntaxTree.ParseText(src)},
        new[]{MetadataReference.CreateFromFile(typeof(object).Assembly.Location)},
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    [Fact] public void TypeNodeHasFullNameAndLabel() {
        var c = Compile("namespace N { public class Foo {} }");
        var foo = (INamedTypeSymbol)c.GetSymbolsWithName("Foo").Single();
        var n = SymbolNodes.ForType(foo, null);
        Assert.Equal(Labels.Class, n!.Label);
        Assert.Equal("N.Foo", n.FullName);
    }
    [Fact] public void MethodPkIncludesSignature() {
        var c = Compile("namespace N { public class Foo { public int Bar(string s)=>1; public int Bar()=>2; } }");
        var foo = (INamedTypeSymbol)c.GetSymbolsWithName("Foo").Single();
        var ms = foo.GetMembers("Bar").OfType<IMethodSymbol>().Select(SymbolNodes.ForMethod).ToList();
        Assert.NotEqual(ms[0].Pk, ms[1].Pk);   // 오버로드 구분
    }
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter SymbolNodesTests` → FAIL

- [ ] **Step 3: 구현**
```csharp
// Names.cs
using Microsoft.CodeAnalysis;
namespace CodeWiki.Roslyn;
public static class Names {
    private static readonly SymbolDisplayFormat Fmt = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);
    public static string Full(ISymbol s) => s.ToDisplayString(Fmt);
}
```
```csharp
// SymbolNodes.cs
using System; using System.Collections.Generic; using System.Linq;
using CodeWiki.Model; using Microsoft.CodeAnalysis;
namespace CodeWiki.Roslyn;
public static class SymbolNodes {
    private static readonly IReadOnlyDictionary<string,string> Empty = new Dictionary<string,string>();
    public static Node? ForType(INamedTypeSymbol t, RoleClassifier? roles) {
        if (t.TypeKind != TypeKind.Class && t.TypeKind != TypeKind.Interface) return null;
        var label = t.TypeKind == TypeKind.Interface ? Labels.Interface : Labels.Class;
        var full = Names.Full(t);
        var roleList = roles?.Classify(t) ?? Array.Empty<string>();
        var props = t.DeclaredAccessibility != Accessibility.NotApplicable
            ? new Dictionary<string,string>{["modifiers"]=t.DeclaredAccessibility.ToString().ToLowerInvariant()} : Empty;
        return new Node(label, Pk.Of(full), t.Name, full, props, roleList);
    }
    public static Node ForMethod(IMethodSymbol m) {
        var full = Names.Full(m);
        var args = string.Join(", ", m.Parameters.Select(p => Names.Full(p.Type)));
        var ret = Names.Full(m.ReturnType);
        return new Node(Labels.Method, Pk.Of(full, args, ret), m.Name, full,
            new Dictionary<string,string>{["arguments"]=args, ["returnType"]=ret}, Array.Empty<string>());
    }
}
```
```csharp
// FileNodes.cs
using System; using System.Collections.Generic; using CodeWiki.Model;
namespace CodeWiki.Roslyn;
public static class FileNodes {
    public static Node ForPath(string abs, string root) {
        var rel = System.IO.Path.GetRelativePath(root, abs).Replace('\\','/');
        return new Node(Labels.File, Pk.Of(rel), System.IO.Path.GetFileName(rel), rel,
            new Dictionary<string,string>(), Array.Empty<string>());
    }
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter SymbolNodesTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "feat(codewiki): Names/SymbolNodes/FileNodes 심볼→노드 팩토리"`

---

