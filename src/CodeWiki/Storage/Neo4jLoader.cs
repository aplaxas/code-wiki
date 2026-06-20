using System.Threading.Tasks;
using CodeWiki.Model;
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

    public async ValueTask DisposeAsync() => await _driver.DisposeAsync();
}
