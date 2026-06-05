using Strazh.Database;
using Xunit;

namespace Strazh.Tests;

public class BatchLoaderTests
{
    [Fact]
    public void Cypher_uses_unwind_and_merges_on_pk()
    {
        var cypher = BatchLoader.MergeCypher("Method", "Class", "USES");

        Assert.Contains("UNWIND $batch AS row", cypher);
        Assert.Contains("MERGE (a:Method { pk: row.a.pk })", cypher);
        Assert.Contains("MERGE (b:Class { pk: row.b.pk })", cypher);
        Assert.Contains("MERGE (a)-[r:USES]->(b)", cypher);
        Assert.Contains("SET r += row.rel.props", cypher);
    }
}
