using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace CodeWiki.Tests;

public class TestCompilerTests
{
    [Fact]
    public void CompilesAndResolvesSymbol()
    {
        var (c, _) = TestCompiler.Compile("namespace N { public class Foo {} }");
        Assert.NotEmpty(c.GetSymbolsWithName("Foo"));
    }
}
