using Strazh.Domain;
using Xunit;

namespace Strazh.Tests;

public class MultiLabelTests
{
    [Fact]
    public void AllLabels_returns_primary_label_when_no_roles()
    {
        var node = new ClassNode("N.Foo", "Foo");
        Assert.Equal(new[] { "Class" }, node.AllLabels);
    }

    [Fact]
    public void AllLabels_appends_role_labels_after_primary()
    {
        var node = new ClassNode("N.Order", "Order");
        node.AddRoleLabels(new[] { "Entity", "DTO" });
        Assert.Equal(new[] { "Class", "Entity", "DTO" }, node.AllLabels);
    }
}
