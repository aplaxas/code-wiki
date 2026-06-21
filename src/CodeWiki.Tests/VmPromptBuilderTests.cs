using System.Collections.Generic;
using CodeWiki.Semantic;
using Xunit;

namespace CodeWiki.Tests;

public class VmPromptBuilderTests
{
    [Fact]
    public void UserPromptContainsFileAndHandlerKeys()
    {
        var req = VmPromptBuilder.Build("class VM { void SearchOrderAsync(){} }",
            new List<string> { "SearchOrderAsync", "ResetForm" });
        Assert.Contains("SearchOrderAsync", req.User);
        Assert.Contains("ResetForm", req.User);
        Assert.Contains(VmPromptBuilder.ViewModelKey, req.User);
        Assert.Contains("class VM", req.User);
    }

    [Fact]
    public void SystemPromptIsStaticAndMentionsThreeFields()
    {
        var a = VmPromptBuilder.Build("x", new List<string>());
        var b = VmPromptBuilder.Build("y", new List<string> { "H" });
        Assert.Equal(a.System, b.System);          // 캐시 가능하도록 입력 무관 정적
        Assert.Contains("summary", a.System);
        Assert.Contains("effects", a.System);
        Assert.Contains("caveats", a.System);
    }
}
