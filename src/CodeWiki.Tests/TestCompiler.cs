using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeWiki.Tests;

public static class TestCompiler
{
    public static (Compilation, SemanticModel) Compile(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var refs = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location));
        var c = CSharpCompilation.Create("Test", new[] { tree }, refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return (c, c.GetSemanticModel(tree));
    }
}
