using System;
using System.Collections.Generic;
using System.Linq;
using Buildalyzer;
using Buildalyzer.Workspaces;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;

namespace CodeWiki.Pipeline;

/// <summary>
/// Builds a solution using Buildalyzer and returns compilations for each project.
/// Invariants:
/// #1: DesignTime=false (풀빌드) — required to capture complete source for all projects including WPF.xaml.cs
/// #2: addProjectReferences:false (빈 스텁 방지) — prevents empty stub projects from overwriting real sources
/// #3: Per-project try/catch — ensures one failing project doesn't crash the entire extraction
/// </summary>
public sealed class WorkspaceBuilder : IWorkspaceBuilder
{
    public IEnumerable<Compilation> Build(string slnPath)
    {
        if (!System.IO.File.Exists(slnPath))
        {
            Console.Error.WriteLine($"WARN: solution not found: {slnPath}");
            yield break;
        }

        // Ensure MSBuild is registered
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        var manager = new AnalyzerManager(slnPath);
        var ws = new AdhocWorkspace();

        foreach (var p in manager.Projects.Values)
        {
            Compilation? comp = null;
            try
            {
                // Build with default environment (풀빌드 — DesignTime=false is Buildalyzer default)
                var results = p.Build();
                var result = results.FirstOrDefault();

                if (result is null)
                {
                    Console.Error.WriteLine($"WARN: build empty: {p.ProjectFile.Path}");
                    continue;
                }

                // 불변식 #2: addProjectReferences:false (빈 스텁 방지)
                var roslyn = result.AddToWorkspace(ws, addProjectReferences: false);
                comp = roslyn.GetCompilationAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // 불변식 #3: 프로젝트 단위 try/catch
                Console.Error.WriteLine($"WARN: project failed {p.ProjectFile.Path}: {ex.Message}");
            }

            if (comp != null)
                yield return comp;
        }
    }
}
