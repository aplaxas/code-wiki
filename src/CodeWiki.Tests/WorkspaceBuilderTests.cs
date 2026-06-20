using System.Linq;
using CodeWiki.Pipeline;
using Xunit;

public class WorkspaceBuilderTests
{
    [Fact]
    public void MissingSolutionDoesNotThrow()
    {
        var wb = new WorkspaceBuilder();
        var result = wb.Build("Z:/does/not/exist.sln").ToList();  // 빈 결과, 예외 없음
        Assert.Empty(result);
    }
}
