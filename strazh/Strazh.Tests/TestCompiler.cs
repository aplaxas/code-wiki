using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Strazh.Tests;

public static class TestCompiler
{
    /// <summary>단일 소스를 컴파일해 (구문트리, 의미모델)을 반환.</summary>
    public static (SyntaxTree tree, SemanticModel model) Compile(string source)
    {
        var trees = new[] { CSharpSyntaxTree.ParseText(source, path: "Source.cs") };
        var compilation = CreateCompilation(trees);
        return (trees[0], compilation.GetSemanticModel(trees[0]));
    }

    /// <summary>여러 소스를 한 컴파일에 넣어 각 (구문트리, 의미모델)을 반환.</summary>
    public static IReadOnlyList<(SyntaxTree tree, SemanticModel model)> CompileMany(params string[] sources)
    {
        var trees = sources.Select((s, i) => CSharpSyntaxTree.ParseText(s, path: $"Source{i}.cs")).ToArray();
        var compilation = CreateCompilation(trees);
        return trees.Select(t => (t, compilation.GetSemanticModel(t))).ToList();
    }

    private static CSharpCompilation CreateCompilation(SyntaxTree[] trees)
    {
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException(
                "TRUSTED_PLATFORM_ASSEMBLIES is not set. Ensure the test host is running on .NET Core/5+.");
        var refs = tpa.Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll"))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
        return CSharpCompilation.Create(
            "TestAssembly",
            trees,
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
