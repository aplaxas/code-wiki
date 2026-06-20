using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace CodeWiki.Extraction;

public sealed class ExtractionContext
{
    public Compilation Compilation { get; }
    public string SolutionRoot { get; }
    public string SolutionName { get; }

    public ExtractionContext(Compilation c, string solutionRoot, string solutionName)
    {
        Compilation = c;
        SolutionRoot = solutionRoot;
        SolutionName = solutionName;
    }

    public IEnumerable<INamedTypeSymbol> SourceTypes()
    {
        var stack = new Stack<INamespaceOrTypeSymbol>();
        stack.Push(Compilation.Assembly.GlobalNamespace);
        while (stack.Count > 0)
        {
            foreach (var m in stack.Pop().GetMembers())
            {
                if (m is INamespaceSymbol ns)
                    stack.Push(ns);
                else if (m is INamedTypeSymbol t)
                {
                    yield return t;
                    foreach (var nt in t.GetTypeMembers())
                        stack.Push(nt);
                }
            }
        }
    }
}
