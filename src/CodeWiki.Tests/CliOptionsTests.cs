using CodeWiki.Cli;

namespace CodeWiki.Tests;

public class CliOptionsTests
{
    [Fact]
    public void ParsesExtract()
    {
        var o = CliOptions.Parse(new[] { "extract", "-s", "a.sln", "-o", "out.ndjson" });
        Assert.Equal("extract", o.Verb);
        Assert.Equal("a.sln", o.Solution);
        Assert.Equal("out.ndjson", o.Output);
    }

    [Fact]
    public void ParsesLoadWithWipe()
    {
        var o = CliOptions.Parse(new[] { "load", "-c", "neo4j:neo4j:pw", "--ndjson", "out.ndjson", "--wipe" });
        Assert.Equal("load", o.Verb);
        Assert.Equal("neo4j:neo4j:pw", o.Credentials);
        Assert.True(o.Wipe);
    }
}
