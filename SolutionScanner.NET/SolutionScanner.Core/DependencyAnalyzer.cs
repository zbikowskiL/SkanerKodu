using Microsoft.CodeAnalysis;
using SolutionScanner.Core.Abstractions;
using SolutionScanner.Core.Files;
using SolutionScanner.Core.Infrastructure;
using SolutionScanner.Core.Models;

namespace SolutionScanner.Core;

/// <summary>
/// Analiza zależności: referencje projekt → projekt (z modelu Roslyn/MSBuild), pakiety NuGet i biblioteki
/// (z plików projektów i packages.config) oraz faktyczne użycie symboli z innych projektów
/// (na podstawie odwołań znalezionych przez <see cref="SymbolAnalyzer"/>).
/// </summary>
public sealed class DependencyAnalyzer
{
    private static readonly string[] FrameworkAssemblyPrefixes = { "System", "mscorlib", "Microsoft.CSharp", "Microsoft.VisualBasic", "WindowsBase", "PresentationCore", "PresentationFramework", "netstandard" };

    public void Analyze(AnalysisRunContext ctx, IReadOnlyCollection<Project> selectedProjects)
    {
        var solution = ctx.Solution;
        var seenProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in selectedProjects)
        {
            var name = RoslynHelpers.CleanProjectName(project.Name);
            if (!seenProjects.Add(name)) continue; // projekty wieloplatformowe – raz

            if (solution is not null)
            {
                foreach (var pr in project.ProjectReferences)
                {
                    var target = solution.GetProject(pr.ProjectId);
                    if (target is null) continue;
                    ctx.Result.Dependencies.Add(new ProjectDependency
                    {
                        FromProject = name,
                        ToProject = RoslynHelpers.CleanProjectName(target.Name),
                        Kind = "Referencja projektu",
                        Details = TextUtil.GetRelativePath(ctx.SolutionDirectory, target.FilePath ?? string.Empty)
                    });
                }
            }

            AddProjectFileDependencies(ctx, name, project.FilePath);
        }

        // Projekty SQL i pozostałe niewczytane przez Roslyn
        foreach (var entry in ctx.SolutionProjects.Where(p => p.Exists && !seenProjects.Contains(p.Name)))
        {
            if (ctx.Settings.SelectedProjectPaths is not null && !ctx.Settings.SelectedProjectPaths.Contains(entry.FilePath)) continue;
            if (!entry.IsSqlProject) continue;
            seenProjects.Add(entry.Name);
            AddProjectFileDependencies(ctx, entry.Name, entry.FilePath);
        }

        AddSymbolUsageDependencies(ctx);
    }

    private static void AddProjectFileDependencies(AnalysisRunContext ctx, string projectName, string? projectPath)
    {
        if (string.IsNullOrEmpty(projectPath) || !File.Exists(projectPath)) return;
        ProjectFileInfo info;
        try
        {
            info = ProjectFileReader.Read(projectPath);
        }
        catch (Exception ex)
        {
            ctx.Logger.Warning($"Nie można odczytać pliku projektu {projectPath}: {ex.Message}");
            return;
        }

        foreach (var (id, version) in info.PackageReferences.Distinct())
        {
            ctx.Result.Dependencies.Add(new ProjectDependency
            {
                FromProject = projectName,
                ToProject = id,
                Kind = "Pakiet NuGet",
                Details = string.IsNullOrEmpty(version) ? "wersja zarządzana centralnie / nieokreślona" : "wersja " + version
            });
        }

        foreach (var asm in info.AssemblyReferences.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (FrameworkAssemblyPrefixes.Any(p => asm.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
            ctx.Result.Dependencies.Add(new ProjectDependency
            {
                FromProject = projectName,
                ToProject = asm,
                Kind = "Referencja biblioteki",
                Details = info.HintPathReferences.FirstOrDefault(h => Path.GetFileNameWithoutExtension(h).Equals(asm, StringComparison.OrdinalIgnoreCase)) is { } hint
                    ? TextUtil.GetRelativePath(ctx.SolutionDirectory, hint)
                    : "bez HintPath (GAC / brak pliku)"
            });
        }
    }

    private static void AddSymbolUsageDependencies(AnalysisRunContext ctx)
    {
        var groups = ctx.Result.References
            .Where(r => r.UsageKind != UsageKind.Declaration
                        && !string.IsNullOrEmpty(r.DeclaringProject)
                        && !string.Equals(r.Location.ProjectName, r.DeclaringProject, StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => (From: r.Location.ProjectName, To: r.DeclaringProject));

        foreach (var g in groups)
        {
            var symbols = g.Select(r => r.SymbolFullName).Distinct().OrderBy(s => s).ToList();
            ctx.Result.Dependencies.Add(new ProjectDependency
            {
                FromProject = g.Key.From,
                ToProject = g.Key.To,
                Kind = "Użycie symboli (frazy)",
                ReferenceCount = g.Count(),
                Details = TextUtil.Shorten(string.Join(", ", symbols.Take(30)) + (symbols.Count > 30 ? $" … (+{symbols.Count - 30})" : string.Empty), 1000)
            });
        }
    }
}
