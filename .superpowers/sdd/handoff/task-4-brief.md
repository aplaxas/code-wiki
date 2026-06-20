### Task 4: `Labels` / `Rel` 상수

**Files:** Create `Model/Labels.cs`, `Model/Rel.cs`; Test `ConstantsTests.cs`

**Interfaces:** Produces `Labels.*`, `Rel.*` const 문자열.

- [ ] **Step 1: 실패 테스트**
```csharp
using CodeWiki.Model; using Xunit;
public class ConstantsTests {
    [Fact] public void LabelsExist() { Assert.Equal("Class", Labels.Class); Assert.Equal("ViewModel", Labels.ViewModel); Assert.Equal("Method", Labels.Method); }
    [Fact] public void RelsExist() { Assert.Equal("CALLS", Rel.Calls); Assert.Equal("IMPLEMENTS_METHOD", Rel.ImplementsMethod); Assert.Equal("DECLARES", Rel.Declares); }
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter ConstantsTests` → FAIL

- [ ] **Step 3: 구현**
```csharp
// Labels.cs
namespace CodeWiki.Model;
public static class Labels {
    public const string Class="Class", Interface="Interface", Method="Method", Command="Command",
        File="File", Folder="Folder", Solution="Solution", Project="Project", Package="Package",
        Entity="Entity", ViewModel="ViewModel", Controller="Controller", Service="Service",
        Repository="Repository", Dto="DTO", View="View";
}
```
```csharp
// Rel.cs
namespace CodeWiki.Model;
public static class Rel {
    public const string DeclaredIn="DECLARED_IN", IncludedIn="INCLUDED_IN", Contains="CONTAINS", DependsOn="DEPENDS_ON",
        Inherits="INHERITS", Implements="IMPLEMENTS", Declares="DECLARES", Calls="CALLS", Instantiates="INSTANTIATES",
        UsesType="USES_TYPE", ImplementsMethod="IMPLEMENTS_METHOD", DefinesCommand="DEFINES_COMMAND",
        Executes="EXECUTES", BindsTo="BINDS_TO", Uses="USES";
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter ConstantsTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "feat(codewiki): Labels/Rel 상수"`

---

