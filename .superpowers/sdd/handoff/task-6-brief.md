### Task 6: `TestCompiler` 이식

**Files:** Create `src/CodeWiki.Tests/TestCompiler.cs`; Test `TestCompilerTests.cs`

**Interfaces:** Produces `(Compilation, SemanticModel) TestCompiler.Compile(string source)` — 참조 어셈블리 자동 포함(이후 모든 추출기 테스트가 사용).

- [ ] **Step 1: 실패 테스트**
```csharp
using System.Linq; using Microsoft.CodeAnalysis; using Xunit;
public class TestCompilerTests {
    [Fact] public void CompilesAndResolvesSymbol() {
        var (c, _) = TestCompiler.Compile("namespace N { public class Foo {} }");
        Assert.NotEmpty(c.GetSymbolsWithName("Foo"));
    }
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter TestCompilerTests` → FAIL

- [ ] **Step 3: 구현**
```csharp
using System; using System.Linq; using Microsoft.CodeAnalysis; using Microsoft.CodeAnalysis.CSharp;
public static class TestCompiler {
    public static (Compilation, SemanticModel) Compile(string source) {
        var tree = CSharpSyntaxTree.ParseText(source);
        var refs = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location));
        var c = CSharpCompilation.Create("Test", new[]{tree}, refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return (c, c.GetSemanticModel(tree));
    }
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter TestCompilerTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "test(codewiki): TestCompiler 소스문자열→Compilation 헬퍼"`

---

