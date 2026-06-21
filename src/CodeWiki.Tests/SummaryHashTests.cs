using CodeWiki.Semantic;
using Xunit;

namespace CodeWiki.Tests;

public class SummaryHashTests
{
    [Fact]
    public void SameInputSameHash()
        => Assert.Equal(SummaryHash.Of("abc"), SummaryHash.Of("abc"));

    [Fact]
    public void DifferentInputDifferentHash()
        => Assert.NotEqual(SummaryHash.Of("abc"), SummaryHash.Of("abd"));

    [Fact]
    public void HashIsSixteenHexChars()
        => Assert.Matches("^[0-9A-F]{16}$", SummaryHash.Of("anything"));
}
