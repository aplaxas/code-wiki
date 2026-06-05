using System.Text.Json;
using Strazh.Database;
using Strazh.Domain;
using Xunit;

namespace Strazh.Tests;

public class NdjsonWriterTests
{
    [Fact]
    public void Serializes_triple_with_labels_and_relationship()
    {
        var triple = new TripleImplementsMethod(
            new MethodNode("N.OrderService.Search", "Search", new (string, string)[0], "int"),
            new MethodNode("N.IOrderService.Search", "Search", new (string, string)[0], "int"));

        var line = NdjsonWriter.Serialize(triple);
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        Assert.Equal("IMPLEMENTS_METHOD", root.GetProperty("rel").GetProperty("type").GetString());
        Assert.Equal("N.OrderService.Search", root.GetProperty("a").GetProperty("pk_source").GetString());
        Assert.Equal("Method", root.GetProperty("a").GetProperty("labels")[0].GetString());
    }
}
