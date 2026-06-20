### Task 2: `Pk` (FNV-1a 64bit)

**Files:** Create `src/CodeWiki/Model/Pk.cs`, Test `src/CodeWiki.Tests/PkTests.cs`

**Interfaces:** Produces `static string Pk.Of(params string[] parts)`.

- [ ] **Step 1: 실패 테스트**
```csharp
using CodeWiki.Model; using Xunit;
public class PkTests {
    [Fact] public void Deterministic() => Assert.Equal(Pk.Of("a","b"), Pk.Of("a","b"));
    [Fact] public void SeparatorAvoidsCollision() => Assert.NotEqual(Pk.Of("a","b"), Pk.Of("ab"));
    [Fact] public void DistinctInputsDiffer() => Assert.NotEqual(Pk.Of("x"), Pk.Of("y"));
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter PkTests` → FAIL(컴파일 에러: Pk 없음)

- [ ] **Step 3: 구현**
```csharp
using System.Globalization; using System.Text;
namespace CodeWiki.Model;
public static class Pk {
    public static string Of(params string[] parts) {
        const ulong Offset = 14695981039346656037UL, Prime = 1099511628211UL;
        ulong hash = Offset;
        foreach (var b in Encoding.UTF8.GetBytes(string.Join("|", parts))) { hash ^= b; hash *= Prime; }
        return hash.ToString(CultureInfo.InvariantCulture);
    }
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter PkTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "feat(codewiki): FNV-1a 안정 pk"`

---

