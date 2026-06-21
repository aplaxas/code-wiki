using System.Collections.Generic;
using System.IO;
using CodeWiki.Semantic;
using Xunit;

namespace CodeWiki.Tests;

public class SemanticSerializerTests
{
    [Fact]
    public void RoundTripsRecords()
    {
        var path = Path.GetTempFileName();
        var recs = new List<SemanticRecord>
        {
            new("pk1", "검색한다", null, "페이징 필수", "ABCDEF0123456789", "claude-haiku-4-5-20251001"),
            new("pk2", "초기화", "없음", null, "0011223344556677", "claude-haiku-4-5-20251001"),
        };
        SemanticSerializer.Write(recs, path);
        var back = SemanticSerializer.Read(path);
        Assert.Equal(2, back.Count);
        Assert.Equal("검색한다", back[0].Summary);
        Assert.Null(back[0].Effects);
        Assert.Equal("없음", back[1].Effects);
        File.Delete(path);
    }
}
