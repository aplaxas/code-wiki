using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeWiki.Cli;

public static class VanuatuLayout
{
    public static IReadOnlyList<string> ListClientModuleProjects(string root)
    {
        var dir = Path.Combine(root, "Client", "Module");
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        return Directory.GetDirectories(dir)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    public static IReadOnlyList<string> ListViewModels(string projectDir)
    {
        var dir = Path.Combine(projectDir, "ViewModels");
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        return Directory.GetFiles(dir, "*ViewModel.cs")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    public static IReadOnlyList<(string Folder, string Name)> ListServiceInterfaces(string root)
    {
        var baseDir = Path.Combine(root, "Domain", "Vanuatu.Service");
        var result = new List<(string, string)>();
        if (!Directory.Exists(baseDir)) return result;
        foreach (var folder in Directory.GetDirectories(baseDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var folderName = Path.GetFileName(folder);
            if (folderName is "bin" or "obj") continue;
            foreach (var f in Directory.GetFiles(folder, "I*.cs").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                result.Add((folderName, Path.GetFileNameWithoutExtension(f)));
        }
        return result;
    }
}
