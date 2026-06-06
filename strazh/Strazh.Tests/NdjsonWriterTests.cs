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

    [Fact]
    public void Serializes_method_node_signature_props()
    {
        // The NDJSON/batch load path must carry arguments+returnType onto the node
        // (BatchLoader does `SET n += row.props`), so the graph can disambiguate
        // overloaded/same-named methods by signature — not just by bare name.
        var triple = new TripleImplementsMethod(
            new MethodNode("N.OrderService.Search", "Search",
                new[] { ("filter", "SearchOrderFilter"), ("page", "int") }, "Task<OrderDto>"),
            new MethodNode("N.IOrderService.Search", "Search", new (string, string)[0], "int"));

        var line = NdjsonWriter.Serialize(triple);
        using var doc = JsonDocument.Parse(line);
        var aProps = doc.RootElement.GetProperty("a").GetProperty("props");

        Assert.Equal("Task<OrderDto>", aProps.GetProperty("returnType").GetString());
        Assert.Equal("SearchOrderFilter filter, int page", aProps.GetProperty("arguments").GetString());
    }

    [Fact]
    public void Serializes_registers_relationship_props()
    {
        var triple = new TripleRegisters(
            new InterfaceNode("N.IOrderService", "IOrderService"),
            new ClassNode("N.OrderService", "OrderService"),
            "Scoped");

        var line = NdjsonWriter.Serialize(triple);
        using var doc = System.Text.Json.JsonDocument.Parse(line);

        Assert.Equal("REGISTERS", doc.RootElement.GetProperty("rel").GetProperty("type").GetString());
        Assert.Equal("Scoped", doc.RootElement
            .GetProperty("rel").GetProperty("props").GetProperty("lifetime").GetString());
    }
}
