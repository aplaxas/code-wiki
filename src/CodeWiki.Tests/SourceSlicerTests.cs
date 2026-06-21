using System.IO;
using CodeWiki.Semantic;
using Xunit;

namespace CodeWiki.Tests;

public class SourceSlicerTests
{
    [Fact]
    public void SliceReturnsInclusiveLineRange()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "a\nb\nc\nd\n");
        Assert.Equal("b\nc", SourceSlicer.Slice(path, 2, 3));
        File.Delete(path);
    }

    [Fact]
    public void WholeFileReturnsAll()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "x\ny");
        Assert.Equal("x\ny", SourceSlicer.WholeFile(path));
        File.Delete(path);
    }
}
