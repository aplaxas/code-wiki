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

    [Fact]
    public void OptionWithoutValueDoesNotThrow()
    {
        var o = CliOptions.Parse(new[] { "extract", "-s" });
        Assert.Equal("extract", o.Verb);
        Assert.Null(o.Solution);
    }

    [Fact]
    public void OutputOptionWithoutValueDoesNotThrow()
    {
        var o = CliOptions.Parse(new[] { "extract", "-o" });
        Assert.Equal("extract", o.Verb);
        Assert.Null(o.Output);
    }

    [Fact]
    public void CredentialsOptionWithoutValueDoesNotThrow()
    {
        var o = CliOptions.Parse(new[] { "load", "-c" });
        Assert.Equal("load", o.Verb);
        Assert.Null(o.Credentials);
    }

    [Fact]
    public void NdjsonOptionWithoutValueDoesNotThrow()
    {
        var o = CliOptions.Parse(new[] { "load", "--ndjson" });
        Assert.Equal("load", o.Verb);
        Assert.Null(o.Ndjson);
    }
}
