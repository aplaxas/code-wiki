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
        if (wipe) await session.RunAsync("MATCH (n) DETACH DELETE n");
        foreach (var (cypher, param) in CypherBuilder.NodeStatements(g))
            await session.RunAsync(cypher, param);
        foreach (var (cypher, param) in CypherBuilder.EdgeStatements(g))
            await session.RunAsync(cypher, param);
    }

    public async ValueTask DisposeAsync() => await _driver.DisposeAsync();
}
