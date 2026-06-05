using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Strazh.Analysis;
using Strazh.Domain;
using Xunit;

namespace Strazh.Tests;

public class CommandTests
{
    [Fact]
    public void Links_command_to_handler_method()
    {
        var src = @"
namespace N {
  public class DelegateCommand { public DelegateCommand(System.Action a) { } }
  public class VM {
    public DelegateCommand SearchCommand { get; }
    public VM() { SearchCommand = new DelegateCommand(ExecuteSearch); }
    void ExecuteSearch() { }
  }
}";
        var (tree, model) = TestCompiler.Compile(src);
        var vm = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(c => c.Identifier.Text == "VM");
        var triples = new List<Triple>();

        Extractor.GetCommands(triples, vm, model);

        Assert.Contains(triples, t =>
            t.Relationship is ExecutesRelationship &&
            t.NodeA.FullName == "N.VM.SearchCommand" &&
            t.NodeB.FullName == "N.VM.ExecuteSearch");
    }

    [Fact]
    public void Links_command_to_handler_with_this_qualified_assignment()
    {
        var src = @"
namespace N {
  public class DelegateCommand { public DelegateCommand(System.Action a) { } }
  public class VM {
    public DelegateCommand SaveCommand { get; }
    public VM() { this.SaveCommand = new DelegateCommand(ExecuteSave); }
    void ExecuteSave() { }
  }
}";
        var (tree, model) = TestCompiler.Compile(src);
        var vm = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(c => c.Identifier.Text == "VM");
        var triples = new System.Collections.Generic.List<Triple>();

        Extractor.GetCommands(triples, vm, model);

        Assert.Contains(triples, t =>
            t.Relationship is ExecutesRelationship &&
            t.NodeA.FullName == "N.VM.SaveCommand" &&
            t.NodeB.FullName == "N.VM.ExecuteSave");
    }
}
