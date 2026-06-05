using Strazh.Domain;
using Xunit;

namespace Strazh.Tests;

public class StableKeyTests
{
    [Fact]
    public void Pk_is_deterministic_for_same_fullName()
    {
        var a = new ClassNode("N.Foo", "Foo");
        var b = new ClassNode("N.Foo", "Foo");
        Assert.Equal(a.Pk, b.Pk);
        Assert.Equal("16177116733985609327", a.Pk);
    }
}
