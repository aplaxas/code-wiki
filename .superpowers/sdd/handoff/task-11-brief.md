### Task 11: `CommandExtractor` (`DEFINES_COMMAND`/`EXECUTES`)

**Files:** Create `Extraction/CommandExtractor.cs`; Test `CommandExtractorTests.cs`

**Interfaces:** Produces `Command` 노드 + `DEFINES_COMMAND`(VM→Command) + `EXECUTES`(Command→핸들러). Command pk = `Pk.Of(ownerFullName, commandName)`. Prism `new DelegateCommand(Handler)` / `DelegateCommand<T>(Handler)` 인식, `.ObservesCanExecute(...)` 체인 무시.

- [ ] **Step 1: 실패 테스트**
```csharp
using System.Linq; using CodeWiki.Extraction; using CodeWiki.Model; using CodeWiki.Roslyn; using Xunit;
public class CommandExtractorTests {
    const string Src = @"namespace N {
        public class DelegateCommand { public DelegateCommand(System.Action e){} public DelegateCommand ObservesCanExecute(System.Func<bool> f)=>this; }
        public class Vm { public DelegateCommand SearchCommand { get; }
            public Vm(){ SearchCommand = new DelegateCommand(Search).ObservesCanExecute(()=>true); }
            public void Search(){} } }";
    static Graph Run() { var (c,_) = TestCompiler.Compile(Src); var g = new Graph();
        new CommandExtractor(new RoleClassifier()).Extract(new ExtractionContext(c,"/","T"), g); return g; }
    [Fact] public void DefinesAndExecutes() {
        var g = Run();
        var vm = g.Nodes.Single(n => n.Name=="Vm");
        var cmd = g.Nodes.Single(n => n.Label==Labels.Command && n.Name=="SearchCommand");
        var handler = g.Nodes.Single(n => n.Name=="Search");
        Assert.Contains(g.Edges, e => e.Type==Rel.DefinesCommand && e.FromPk==vm.Pk && e.ToPk==cmd.Pk);
        Assert.Contains(g.Edges, e => e.Type==Rel.Executes && e.FromPk==cmd.Pk && e.ToPk==handler.Pk);
    }
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test --filter CommandExtractorTests` → FAIL

- [ ] **Step 3: 구현**
```csharp
using System.Collections.Generic; using System.Linq; using CodeWiki.Model; using CodeWiki.Roslyn;
using Microsoft.CodeAnalysis; using Microsoft.CodeAnalysis.CSharp.Syntax;
namespace CodeWiki.Extraction;
public sealed class CommandExtractor : IExtractor {
    private static readonly IReadOnlyDictionary<string,string> Empty = new Dictionary<string,string>();
    private readonly RoleClassifier _roles;
    public CommandExtractor(RoleClassifier roles) => _roles = roles;
    public void Extract(ExtractionContext ctx, Graph graph) {
        foreach (var t in ctx.SourceTypes()) {
            var owner = SymbolNodes.ForType(t, _roles);
            if (owner == null) continue;
            foreach (var sr in t.DeclaringSyntaxReferences) {
                var syntax = sr.GetSyntax();
                var model = ctx.Compilation.GetSemanticModel(syntax.SyntaxTree);
                foreach (var oc in syntax.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()) {
                    var typeName = (oc.Type as GenericNameSyntax)?.Identifier.Text
                                ?? (oc.Type as IdentifierNameSyntax)?.Identifier.Text;
                    if (typeName is null || !typeName.StartsWith("DelegateCommand")) continue;
                    var cmdName = AssignedName(oc);
                    if (cmdName is null) continue;
                    var ownerFull = owner.FullName;
                    var cmd = new Node(Labels.Command, Pk.Of(ownerFull, cmdName), cmdName,
                        ownerFull + "." + cmdName, Empty, System.Array.Empty<string>());
                    graph.AddNode(owner); graph.AddNode(cmd);
                    graph.AddEdge(new Edge(Rel.DefinesCommand, owner.Pk, cmd.Pk, Empty));
                    var arg = oc.ArgumentList?.Arguments.FirstOrDefault();
                    if (arg != null && model.GetSymbolInfo(arg.Expression).Symbol is IMethodSymbol handler) {
                        var hn = SymbolNodes.ForMethod(handler);
                        graph.AddNode(hn);
                        graph.AddEdge(new Edge(Rel.Executes, cmd.Pk, hn.Pk, Empty));
                    }
                }
            }
        }
    }
    private static string? AssignedName(ObjectCreationExpressionSyntax oc) {
        // 체인(.ObservesCanExecute) 위로 올라가며 대입/초기화 LHS 찾기
        SyntaxNode? node = oc;
        while (node is not null && node is not AssignmentExpressionSyntax && node is not VariableDeclaratorSyntax
               && node is not PropertyDeclarationSyntax) node = node.Parent;
        return node switch {
            AssignmentExpressionSyntax a => (a.Left as IdentifierNameSyntax)?.Identifier.Text
                                          ?? (a.Left as MemberAccessExpressionSyntax)?.Name.Identifier.Text,
            VariableDeclaratorSyntax v => v.Identifier.Text,
            PropertyDeclarationSyntax p => p.Identifier.Text,
            _ => null };
    }
}
```

- [ ] **Step 4: 통과 확인** — Run: `dotnet test --filter CommandExtractorTests` → PASS

- [ ] **Step 5: Commit** — `git commit -am "feat(codewiki): CommandExtractor(DEFINES_COMMAND/EXECUTES)"`

---

