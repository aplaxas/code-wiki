using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Neo4j.Driver;

namespace Strazh.Database;

public static class BatchLoader
{
    /// <summary>(주 라벨 a, 주 라벨 b, 관계타입) 그룹별 UNWIND MERGE Cypher 생성.</summary>
    public static string MergeCypher(string labelA, string labelB, string relType) =>
        $"UNWIND $batch AS row " +
        $"MERGE (a:{labelA} {{ pk: row.a.pk }}) SET a += row.a.props, a.name = row.a.name, a.fullName = row.a.pk_source " +
        $"MERGE (b:{labelB} {{ pk: row.b.pk }}) SET b += row.b.props, b.name = row.b.name, b.fullName = row.b.pk_source " +
        $"MERGE (a)-[r:{relType}]->(b) SET r += row.rel.props";

    /// <summary>보조(역할) 라벨을 pk 기준으로 SET하는 Cypher 생성.</summary>
    public static string RoleLabelCypher(string role) =>
        $"MATCH (n {{ pk: $pk }}) SET n:{role}";

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

    /// <summary>NDJSON 파일을 읽어 배치 적재하고 보조(역할) 라벨을 SET 한다.</summary>
    public static async Task LoadFileAsync(IAsyncSession session, string ndjsonPath, bool wipe)
    {
        var rows = new List<IDictionary<string, object>>();
        foreach (var line in System.IO.File.ReadLines(ndjsonPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            rows.Add((IDictionary<string, object>)Convert(doc.RootElement)!);
        }
        await LoadAsync(session, rows, wipe);
        await ApplyRoleLabelsAsync(session, rows);
    }

    /// <summary>각 노드의 보조 라벨(labels[1..])을 pk 기준으로 SET. (pk,role) 중복 제거.</summary>
    private static async Task ApplyRoleLabelsAsync(IAsyncSession session, IReadOnlyList<IDictionary<string, object>> rows)
    {
        var done = new HashSet<string>();
        foreach (var row in rows)
        {
            foreach (var key in new[] { "a", "b" })
            {
                var node = (IDictionary<string, object>)row[key];
                var pk = node["pk"]?.ToString();
                if (pk == null) continue;
                var labels = (IList<object>)node["labels"];
                for (var i = 1; i < labels.Count; i++) // skip primary label at [0]
                {
                    var role = labels[i]?.ToString();
                    if (role == null) continue;
                    if (!done.Add($"{pk}:{role}")) continue;
                    await session.RunAsync(RoleLabelCypher(role), new Dictionary<string, object> { ["pk"] = pk });
                }
            }
        }
    }

    /// <summary>System.Text.Json JsonElement를 Dictionary/List/원시값 그래프로 변환 (Neo4j 파라미터 호환).</summary>
    private static object? Convert(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Object =>
            (object)el.EnumerateObject().ToDictionary(p => p.Name, p => Convert(p.Value) ?? (object)string.Empty),
        JsonValueKind.Array =>
            el.EnumerateArray().Select(x => Convert(x) ?? (object)string.Empty).ToList(),
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => el.ToString(),
    };
}
