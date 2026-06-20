using CodeWiki.Model;

namespace CodeWiki.Tests;

public class ConstantsTests
{
    [Fact]
    public void LabelsExist()
    {
        Assert.Equal("Class", Labels.Class);
        Assert.Equal("ViewModel", Labels.ViewModel);
        Assert.Equal("Method", Labels.Method);
    }

    [Fact]
    public void RelsExist()
    {
        Assert.Equal("CALLS", Rel.Calls);
        Assert.Equal("IMPLEMENTS_METHOD", Rel.ImplementsMethod);
        Assert.Equal("DECLARES", Rel.Declares);
    }
}
