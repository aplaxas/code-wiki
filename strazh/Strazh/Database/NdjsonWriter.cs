using System.Collections.Generic;
using System.Text.Json;
using Strazh.Domain;

namespace Strazh.Database;

public static class NdjsonWriter
{
    public static string Serialize(Triple triple)
    {
        var obj = new Dictionary<string, object?>
        {
            ["a"] = NodeObj(triple.NodeA),
            ["b"] = NodeObj(triple.NodeB),
            ["rel"] = new Dictionary<string, object?>
            {
                ["type"] = triple.Relationship.Type,
                ["props"] = triple.Relationship.Properties,
            },
        };
        return JsonSerializer.Serialize(obj);
    }

    private static Dictionary<string, object?> NodeObj(Node node) => new()
    {
        ["pk"] = node.Pk,
        ["pk_source"] = node.FullName,
        ["name"] = node.Name,
        ["labels"] = node.AllLabels,
        ["props"] = new Dictionary<string, string>(),
    };
}
