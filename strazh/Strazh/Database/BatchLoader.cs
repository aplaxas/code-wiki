using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Neo4j.Driver;

namespace Strazh.Database;

public static class BatchLoader
{
    /// <summary>(주 라벨 a, 주 라벨 b, 관계타입) 그룹별 UNWIND MERGE Cypher 생성.</summary>
    public static string MergeCypher(string labelA, string labelB, string relType) =>
        $"UNWIND $batch AS row " +
        $"MERGE (a:{labelA} {{ pk: row.a.pk }}) SET a += row.a.props, a.name = row.a.name " +
        $"MERGE (b:{labelB} {{ pk: row.b.pk }}) SET b += row.b.props, b.name = row.b.name " +
        $"MERGE (a)-[r:{relType}]->(b) SET r += row.rel.props";

    /// <summary>row 객체 목록을 (labelA,labelB,relType)로 그룹핑해 배치 적재.</summary>
    public static async Task LoadAsync(
        IAsyncSession session,
        IReadOnlyList<IDictionary<string, object>> rows,
        bool wipe,
        int batchSize = 5000)
    {
        if (wipe)
            await session.RunAsync("MATCH (n) DETACH DELETE n;");

        var groups = rows.GroupBy(r =>
        {
            var a = (IDictionary<string, object>)r["a"];
            var b = (IDictionary<string, object>)r["b"];
            var rel = (IDictionary<string, object>)r["rel"];
            var la = ((IList<object>)a["labels"])[0].ToString()!;
            var lb = ((IList<object>)b["labels"])[0].ToString()!;
            var rt = rel["type"].ToString()!;
            return (la, lb, rt);
        });

        foreach (var g in groups)
        {
            var cypher = MergeCypher(g.Key.la, g.Key.lb, g.Key.rt);
            foreach (var chunk in g.Chunk(batchSize))
            {
                var parameters = new Dictionary<string, object> { ["batch"] = chunk };
                await session.RunAsync(cypher, parameters);
            }
        }
    }
}
