using Strazh.Database;
using Xunit;

namespace Strazh.Tests;

public class BatchLoaderRowTests
{
    [Fact]
    public void Builds_secondary_label_set_cypher()
    {
        var cypher = BatchLoader.RoleLabelCypher("ViewModel");
        Assert.Contains("MATCH (n { pk: $pk })", cypher);
        Assert.Contains("SET n:ViewModel", cypher);
    }
}
