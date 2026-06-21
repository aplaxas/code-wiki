using System.IO;
using System.Linq;
using CodeWiki.Cli;
using Xunit;

namespace CodeWiki.Tests;

public class VanuatuLayoutTests
{
    static string MakeRoot()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        Directory.CreateDirectory(Path.Combine(root, "Client", "Module", "Shefa.Module.Order", "ViewModels"));
        Directory.CreateDirectory(Path.Combine(root, "Client", "Module", "Shefa.Module.Customer"));
        File.WriteAllText(Path.Combine(root, "Client", "Module", "Shefa.Module.Order", "ViewModels", "SearchOrderViewModel.cs"), "");
        File.WriteAllText(Path.Combine(root, "Client", "Module", "Shefa.Module.Order", "ViewModels", "EditOrderViewModel.cs"), "");
        File.WriteAllText(Path.Combine(root, "Client", "Module", "Shefa.Module.Order", "ViewModels", "Helper.cs"), "");
        Directory.CreateDirectory(Path.Combine(root, "Domain", "Vanuatu.Service", "Order"));
        Directory.CreateDirectory(Path.Combine(root, "Domain", "Vanuatu.Service", "obj"));
        File.WriteAllText(Path.Combine(root, "Domain", "Vanuatu.Service", "Order", "IOrderService.cs"), "");
        File.WriteAllText(Path.Combine(root, "Domain", "Vanuatu.Service", "Order", "OrderHelper.cs"), "");
        File.WriteAllText(Path.Combine(root, "Domain", "Vanuatu.Service", "obj", "IGenerated.cs"), "");
        return root;
    }

    [Fact]
    public void ListsProjects()
    {
        var p = VanuatuLayout.ListClientModuleProjects(MakeRoot());
        Assert.Contains("Shefa.Module.Order", p);
        Assert.Contains("Shefa.Module.Customer", p);
    }

    [Fact]
    public void ListsViewModelsByName_excludingNonViewModelCs()
    {
        var root = MakeRoot();
        var vms = VanuatuLayout.ListViewModels(Path.Combine(root, "Client", "Module", "Shefa.Module.Order"));
        Assert.Equal(new[] { "EditOrderViewModel", "SearchOrderViewModel" }, vms.ToArray());
        Assert.DoesNotContain("Helper", vms);
    }

    [Fact]
    public void ListsInterfaces_byFolder_excludingObjAndNonInterface()
    {
        var ifaces = VanuatuLayout.ListServiceInterfaces(MakeRoot());
        Assert.Contains(("Order", "IOrderService"), ifaces);
        Assert.DoesNotContain(ifaces, x => x.Name == "OrderHelper");      // I*.cs 아님
        Assert.DoesNotContain(ifaces, x => x.Folder == "obj");            // obj 제외
    }

    [Fact]
    public void MissingDirsReturnEmpty()
    {
        var empty = Directory.CreateTempSubdirectory().FullName;
        Assert.Empty(VanuatuLayout.ListClientModuleProjects(empty));
        Assert.Empty(VanuatuLayout.ListViewModels(empty));
        Assert.Empty(VanuatuLayout.ListServiceInterfaces(empty));
    }
}
