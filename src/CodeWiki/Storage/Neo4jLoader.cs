using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CodeWiki.Model;
using CodeWiki.Semantic;
using Neo4j.Driver;

namespace CodeWiki.Storage;

public sealed class Neo4jLoader : System.IAsyncDisposable
{
    private readonly IDriver _driver;

    public Neo4jLoader(string uri, string user, string pass) =>
        _driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, pass));

    public async Task LoadAsync(Graph g, bool wipe)
    {
        await using var session = _driver.AsyncSession();
        if (wipe)
        {
            var wipeCursor = await session.RunAsync("MATCH (n) DETACH DELETE n");
            await wipeCursor.ConsumeAsync();
        }
        var ixCursor = await session.RunAsync("CREATE INDEX node_pk IF NOT EXISTS FOR (n:Node) ON (n.pk)");
        await ixCursor.ConsumeAsync();
        foreach (var (cypher, param) in CypherBuilder.NodeStatements(g))
        {
            var cursor = await session.RunAsync(cypher, param);
            await cursor.ConsumeAsync();
        }
        foreach (var (cypher, param) in CypherBuilder.EdgeStatements(g))
        {
            var cursor = await session.RunAsync(cypher, param);
            await cursor.ConsumeAsync();
        }
    }

    public static List<Dictionary<string, object>> SemanticRows(IEnumerable<SemanticRecord> records)
    {
        var rows = new List<Dictionary<string, object>>();
        foreach (var r in records)
        {
            var props = new Dictionary<string, object>
            {
                ["summary"] = r.Summary,
                ["summaryHash"] = r.SummaryHash,
                ["summaryModel"] = r.SummaryModel,
            };
            if (!string.IsNullOrEmpty(r.Effects)) props["effects"] = r.Effects;
            if (!string.IsNullOrEmpty(r.Caveats)) props["caveats"] = r.Caveats;
            rows.Add(new Dictionary<string, object> { ["pk"] = r.Pk, ["props"] = props });
        }
        return rows;
    }

    public async Task ApplySemanticAsync(IEnumerable<SemanticRecord> records)
    {
        await using var session = _driver.AsyncSession();
        var cursor = await session.RunAsync(
            "UNWIND $rows AS row MATCH (n:Node {pk: row.pk}) SET n += row.props",
            new Dictionary<string, object> { ["rows"] = SemanticRows(records) });
        await cursor.ConsumeAsync();
    }

    public async ValueTask DisposeAsync() => await _driver.DisposeAsync();
}
