using System.Collections.Generic;
using CodeWiki.Semantic;
using CodeWiki.Storage;
using Xunit;

namespace CodeWiki.Tests;

public class SemanticApplyRowTests
{
    [Fact]
    public void RowOmitsNullFieldsAndKeepsRequired()
    {
        var rows = Neo4jLoader.SemanticRows(new[]
        {
            new SemanticRecord("pk1", "검색", null, "주의", "HASH", "model"),
        });
        var row = Assert.Single(rows);
        Assert.Equal("pk1", row["pk"]);
        var props = (Dictionary<string, object>)row["props"];
        Assert.Equal("검색", props["summary"]);
        Assert.Equal("주의", props["caveats"]);
        Assert.False(props.ContainsKey("effects"));        // null 제외
        Assert.Equal("HASH", props["summaryHash"]);
        Assert.Equal("model", props["summaryModel"]);
    }
}
