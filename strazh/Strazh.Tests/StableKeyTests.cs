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

    [Fact]
    public void Package_pk_is_deterministic_and_separated()
    {
        var a = new PackageNode("Newtonsoft.Json", "Newtonsoft.Json", "13.0.0");
        var b = new PackageNode("Newtonsoft.Json", "Newtonsoft.Json", "13.0.0");
        Assert.Equal(a.Pk, b.Pk);
        Assert.Equal("11543004957276216214", a.Pk);
    }
}
