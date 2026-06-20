using CodeWiki.Model;

namespace CodeWiki.Tests;

public class PkTests
{
    [Fact]
    public void Deterministic() => Assert.Equal(Pk.Of("a", "b"), Pk.Of("a", "b"));

    [Fact]
    public void SeparatorAvoidsCollision() => Assert.NotEqual(Pk.Of("a", "b"), Pk.Of("ab"));

    [Fact]
    public void DistinctInputsDiffer() => Assert.NotEqual(Pk.Of("x"), Pk.Of("y"));
}
