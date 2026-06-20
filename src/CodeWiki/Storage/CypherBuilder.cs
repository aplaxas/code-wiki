using System.Collections.Generic;
using System.Linq;
using CodeWiki.Model;

namespace CodeWiki.Storage;

public static class CypherBuilder
{
    public static IEnumerable<(string cypher, Dictionary<string, object> param)> NodeStatements(Graph g)
    {
        foreach (var grp in g.Nodes.GroupBy(n => n.Label + ":" + string.Join(":", n.Roles)))
        {
            var first = grp.First();
            var labels = ":" + first.Label + (first.Roles.Count > 0 ? ":" + string.Join(":", first.Roles) : "");
            var cypher = $"UNWIND $rows AS row MERGE (n{labels} {{pk: row.pk}}) " +
                         "SET n += row.props, n.name = row.name, n.fullName = row.fullName";
            var rows = grp.Select(n => new Dictionary<string, object>
            {
                ["pk"] = n.Pk,
                ["name"] = n.Name,
                ["fullName"] = n.FullName,
                ["props"] = n.Props.ToDictionary(p => p.Key, p => (object)p.Value)
            }).ToList();
            yield return (cypher, new Dictionary<string, object> { ["rows"] = rows });
        }
    }

    public static IEnumerable<(string cypher, Dictionary<string, object> param)> EdgeStatements(Graph g)
    {
        foreach (var grp in g.Edges.GroupBy(e => e.Type))
        {
            var cypher = $"UNWIND $rows AS row MATCH (a {{pk: row.from}}) MATCH (b {{pk: row.to}}) " +
                         $"MERGE (a)-[r:{grp.Key}]->(b) SET r += row.props";
            var rows = grp.Select(e => new Dictionary<string, object>
            {
                ["from"] = e.FromPk,
                ["to"] = e.ToPk,
                ["props"] = e.Props.ToDictionary(p => p.Key, p => (object)p.Value)
            }).ToList();
            yield return (cypher, new Dictionary<string, object> { ["rows"] = rows });
        }
    }
}
