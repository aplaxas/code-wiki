using System;
using System.Collections.Generic;
using System.Linq;
using Buildalyzer;
using Buildalyzer.Environment;
using Buildalyzer.Workspaces;
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

        var manager = new AnalyzerManager(slnPath);
        var ws = new AdhocWorkspace();

        foreach (var p in manager.Projects.Values)
        {
            Compilation? comp = null;
            try
            {
                // 불변식 #1: 풀빌드(design-time 금지) — 필수: WPF .xaml.cs/ViewModel 소스 전체 캡처
                var results = p.Build(new Buildalyzer.Environment.EnvironmentOptions { DesignTime = false });
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
