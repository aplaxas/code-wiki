using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using Microsoft.CodeAnalysis;
using Strazh.Domain;
using Buildalyzer;
using Buildalyzer.Workspaces;
using System.Collections.Generic;
using System;
using System.Collections.Concurrent;
using Strazh.Database;
using static Strazh.Analysis.AnalyzerConfig;
using System.IO;
using Microsoft.Build.Construction;

namespace Strazh.Analysis
{
    public static class Analyzer
    {
        public static async Task Analyze(AnalyzerConfig config)
        {
            Console.WriteLine($"Setup analyzer...");

            var manager = config.IsSolutionBased
                ? new AnalyzerManager(config.Solution)
                : new AnalyzerManager();

            var projectAnalyzers = (config.IsSolutionBased
                ? manager.Projects.Values
                : config.Projects.Select(x => manager.GetProject(x))).ToList();

            Console.WriteLine($"Analyzer ready to analyze {projectAnalyzers.Count} project/s.");
            
            Console.WriteLine("Building workspace...");
            var context = GetAnalysisContext(manager);
            Console.WriteLine("done.");
            
            Console.WriteLine("Analyzing workspace...");
            
            var failed = new List<string>();
            for (var index = 0; index < context.Projects.Count; index++)
            {
                var projectName = context.Projects[index].Item1.Name;
                try
                {
                var triples = new List<Triple>();

                if (config.IsSolutionBased)
                {
                    var solutionRoot = GetRoot(manager.SolutionFilePath);
                    var solutionRootNode = new FolderNode(solutionRoot, solutionRoot);
                    
                    var solutionName = GetSolutionName(manager.SolutionFilePath);
                    var solutionNode = new SolutionNode(solutionName);
                    triples.Add(new TripleIncludedIn(solutionNode, solutionRootNode));
                    
                    var projectNode = new ProjectNode(GetProjectName(context.Projects[index].Item1.Name));
                    triples.Add(new TripleContains(solutionNode, projectNode));
                }

                Console.WriteLine($"+ [{index + 1}/{context.Projects.Count} {context.Projects[index].Item1.Name}: analyze - starting");
                var projectTriples = await AnalyzeProject(index + 1, context.Projects[index], config.Tier);
                Console.WriteLine($"+ [{index + 1}/{context.Projects.Count} {context.Projects[index].Item1.Name}: analyze - finished");
                
                triples.AddRange(projectTriples);
                
                Console.WriteLine($"+ [{index + 1}/{context.Projects.Count} {context.Projects[index].Item1.Name}: grouping - starting");
                // Compute the dedup/Cypher key per triple defensively: a single malformed
                // triple (e.g. an unresolved symbol producing a node that throws in ToString)
                // must not discard the whole project's triples. Skip and log the offender.
                var keyed = new List<(string key, Triple triple)>();
                foreach (var t in triples)
                {
                    try
                    {
                        keyed.Add((t.ToString(), t));
                    }
                    catch (Exception error)
                    {
                        Console.WriteLine($"WARN: skipping malformed triple [{t.NodeA?.Label} -> {t.NodeB?.Label} : {t.Relationship?.Type}]: {error.Message}");
                        try { Console.WriteLine($"  detail: {t.ToInspection()}"); } catch { /* best-effort */ }
                    }
                }
                triples = keyed.GroupBy(x => x.key).Select(x => x.First().triple).OrderBy(x => x.NodeA.Label).ToList();
                Console.WriteLine($"+ [{index + 1}/{context.Projects.Count} {context.Projects[index].Item1.Name}: grouping - finished");
                
                Console.WriteLine($"+ [{index + 1}/{context.Projects.Count} {context.Projects[index].Item1.Name}: inserting - starting");
                if (config.Output == "ndjson")
                {
                    var ndjsonPath = config.NdjsonPath;
                    var dir = System.IO.Path.GetDirectoryName(ndjsonPath);
                    if (!string.IsNullOrEmpty(dir))
                        System.IO.Directory.CreateDirectory(dir);
                    using var writer = new StreamWriter(ndjsonPath, append: index != 0);
                    foreach (var triple in triples)
                        await writer.WriteLineAsync(NdjsonWriter.Serialize(triple));
                }
                else
                {
                    await DbManager.InsertData(triples, config.Credentials, config.IsDelete && index == 0);
                }
                Console.WriteLine($"+ [{index + 1}/{context.Projects.Count} {context.Projects[index].Item1.Name}: inserting - finished");
                }
                catch (Exception ex)
                {
                    failed.Add(projectName);
                    Console.WriteLine($"WARN: project {projectName} failed, skipping its triples: {ex.GetType().Name}: {ex.Message}");
                }
            }
            if (failed.Count > 0)
                Console.WriteLine($"Completed with {failed.Count} failed project(s): {string.Join(", ", failed)}");
            context.Workspace.Dispose();
        }

        public class AnalysisContext(AdhocWorkspace workspace, List<(Project, IAnalyzerResult)> projects)
        {
            public AdhocWorkspace Workspace { get; } = workspace;
            public List<(Project, IAnalyzerResult)> Projects { get; } = projects;
        }
        
        // Based on https://github.com/phmonte/Buildalyzer/blob/9db3390b49dca033fd3f70439bab3a6327440a47/src/Buildalyzer.Workspaces/AnalyzerManagerExtensions.cs#L24-L60
        public static AnalysisContext GetAnalysisContext(IAnalyzerManager manager)
        {
            if (manager is null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            
            var projectResults = new ConcurrentBag<(Project, IAnalyzerResult)>(); 

            Console.WriteLine("Building projects - starting");
            
            var results = new List<IAnalyzerResult>();
            var skipped = new List<string>();
            foreach (var p in manager.Projects.Values)
            {
                Console.WriteLine($"Building projects - {p.ProjectFile.Name} - starting");
                var result = p.Build(new Buildalyzer.Environment.EnvironmentOptions { DesignTime = false }).FirstOrDefault();
                Console.WriteLine($"Building projects - {p.ProjectFile.Name} - finished");
                if (result == null)
                {
                    skipped.Add(p.ProjectFile.Name);
                    Console.WriteLine($"WARN: skipped {p.ProjectFile.Name} - build produced no result (not analyzed).");
                }
                else
                {
                    results.Add(result);
                }
            }
            Console.WriteLine($"Building projects - finished. {results.Count} built, {skipped.Count} skipped of {manager.Projects.Count} total.");
            if (skipped.Count > 0)
                Console.WriteLine($"Skipped projects (excluded from graph): {string.Join(", ", skipped)}");

            // Create a new workspace and add the solution (if there was one)
            AdhocWorkspace workspace = new AdhocWorkspace();
            if (!string.IsNullOrEmpty(manager.SolutionFilePath))
            {
                SolutionInfo solutionInfo = SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Default, manager.SolutionFilePath);
                workspace.AddSolution(solutionInfo);

                // Sort the projects so the order that they're added to the workspace in the same order as the solution file
                List<ProjectInSolution> projectsInOrder = [.. manager.SolutionFile.ProjectsInOrder];
                results = [.. results.OrderBy(p => projectsInOrder.FindIndex(g => g.AbsolutePath == p.ProjectFilePath))];
            }

            // Add each built result to the workspace AND register it for analysis.
            // AddToWorkspace(addProjectReferences:true) pulls a project's referenced
            // projects into the workspace too; when we later reach such a project's own
            // result it is already present. Previously those were SKIPPED, so most
            // library projects (services, modules, DTOs) were never analyzed for code.
            // Instead, reuse the already-present workspace Project handle so EVERY built
            // project is analyzed (cross-project references are already wired).
            foreach (IAnalyzerResult result in results)
            {
                var existing = workspace.CurrentSolution.Projects
                    .FirstOrDefault(p => p.FilePath == result.ProjectFilePath);
                var project = existing ?? result.AddToWorkspace(workspace, true);
                projectResults.Add((project, result));
            }

            Console.WriteLine($"Workspace ready: analyzing {projectResults.Count} project(s) ({workspace.CurrentSolution.Projects.Count()} present in workspace).");

            return new AnalysisContext(workspace, projectResults.ToList());
        }

        private static async Task<IList<Triple>> AnalyzeProject(int index, (Project project, IAnalyzerResult projectAnalyzerResult) item, Tiers mode)
        {
            Console.WriteLine($"Project #{index}:");
            var root = GetRoot(item.project.FilePath);
            var rootNode = new FolderNode(root, root);
            var projectName = GetProjectName(item.project.Name);
            Console.WriteLine($"Analyzing {projectName} project...");

            var triples = new List<Triple>();
            if (mode == Tiers.All || mode == Tiers.Project)
            {
                Console.WriteLine($"Analyzing Project tier...");
                var projectNode = new ProjectNode(projectName);
                triples.Add(new TripleIncludedIn(projectNode, rootNode));
                item.projectAnalyzerResult.ProjectReferences.ToList().ForEach(x =>
                {
                    var node = new ProjectNode(GetProjectName(x));
                    triples.Add(new TripleDependsOnProject(projectNode, node));
                });
                item.projectAnalyzerResult.PackageReferences.ToList().ForEach(x =>
                {
                    var version = x.Value.Values.FirstOrDefault(x => x.Contains(".")) ?? "none";
                    var node = new PackageNode(x.Key, x.Key, version);
                    triples.Add(new TripleDependsOnPackage(projectNode, node));
                });
                Console.WriteLine($"Analyzing Project tier complete.");
            }

            if (item.project.SupportsCompilation
                && (mode == Tiers.All || mode == Tiers.Code))
            {
                Console.WriteLine($"Analyzing Code tier...");
                var compilation = await item.project.GetCompilationAsync();
                var syntaxTreeRoot = compilation.SyntaxTrees.Where(x => !x.FilePath.Contains("obj"));
                var collectedClasses = new List<ClassNode>();
                foreach (var st in syntaxTreeRoot)
                {
                    var sem = compilation.GetSemanticModel(st);
                    Extractor.AnalyzeTree<InterfaceDeclarationSyntax>(triples, st, sem, rootNode, collectedClasses);
                    Extractor.AnalyzeTree<ClassDeclarationSyntax>(triples, st, sem, rootNode, collectedClasses);
                }
                Extractor.LinkViewsToViewModels(triples, collectedClasses);
                Console.WriteLine($"Analyzing Code tier complete.");
            }

            Console.WriteLine($"Analyzing {projectName} project complete.");
            return triples;
        }
        
        private static string GetSolutionName(string fullName)
            => fullName.Split(Path.DirectorySeparatorChar).Last().Replace(".sln", "");

        private static string GetProjectName(string fullName)
            => fullName.Split(Path.DirectorySeparatorChar).Last().Replace(".csproj", "");

        private static string GetRoot(string filePath)
            => filePath.Split(Path.DirectorySeparatorChar).Reverse().Skip(1).FirstOrDefault();
    }
}