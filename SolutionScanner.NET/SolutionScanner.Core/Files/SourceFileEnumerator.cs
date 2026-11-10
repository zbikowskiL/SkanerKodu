using SolutionScanner.Core.Infrastructure;
using SolutionScanner.Core.Models;

namespace SolutionScanner.Core.Files;

/// <summary>Plik do analizy wraz z projektem, do którego należy (wg katalogu projektu).</summary>
public sealed record SourceFile(string Path, string ProjectName, string? ProjectFilePath);

/// <summary>
/// Wyszukuje pliki źródłowe w katalogu solucji i katalogach projektów.
/// Pomija katalogi bin, obj, .git, .vs, packages, node_modules (konfigurowalne) oraz – domyślnie – pliki generowane.
/// Przypisuje plik do projektu, którego katalog jest najdłuższym prefiksem ścieżki pliku.
/// </summary>
public static class SourceFileEnumerator
{
    public const string SolutionLevelProject = "(Solucja)";

    public static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".vb", ".razor", ".cshtml", ".vbhtml", ".sql", ".xml", ".json", ".config", ".edmx", ".sqlproj"
    };

    /// <summary>Czy plik jest automatycznie generowany (.g.cs, .designer.cs itp.).</summary>
    public static bool IsGeneratedFile(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".g.vb", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".designer.vb", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("TemporaryGeneratedFile_", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Czy ścieżka leży w katalogu wykluczonym (bin, obj, ...).</summary>
    public static bool IsInExcludedDirectory(string path, IEnumerable<string> excludedDirectories)
    {
        var excluded = excludedDirectories as ISet<string> ?? new HashSet<string>(excludedDirectories, StringComparer.OrdinalIgnoreCase);
        var dir = Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(dir))
        {
            if (excluded.Contains(Path.GetFileName(dir))) return true;
            dir = Path.GetDirectoryName(dir);
        }
        return false;
    }

    public static IReadOnlyList<SourceFile> Enumerate(
        string solutionDirectory,
        IReadOnlyList<ProjectEntry> projects,
        AnalysisSettings settings,
        CancellationToken ct)
    {
        var excluded = new HashSet<string>(settings.ExcludedDirectories, StringComparer.OrdinalIgnoreCase);
        var projectDirs = projects
            .Where(p => p.Exists)
            .Select(p => (Dir: TextUtil.NormalizePath(p.Directory), Project: p))
            .OrderByDescending(p => p.Dir.Length)
            .ToList();

        var roots = new List<string> { TextUtil.NormalizePath(solutionDirectory) };
        foreach (var (dir, _) in projectDirs)
        {
            if (!roots.Any(r => IsUnder(dir, r))) roots.Add(dir);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<SourceFile>();
        foreach (var root in roots)
        {
            foreach (var file in Walk(root, excluded, ct))
            {
                if (!seen.Add(file)) continue;
                if (!SupportedExtensions.Contains(Path.GetExtension(file))) continue;
                if (!settings.IncludeGeneratedFiles && IsGeneratedFile(file)) continue;

                var owner = projectDirs.FirstOrDefault(p => IsUnder(file, p.Dir));
                if (owner.Project is not null)
                {
                    if (settings.SelectedProjectPaths is not null && !settings.SelectedProjectPaths.Contains(owner.Project.FilePath)) continue;
                    result.Add(new SourceFile(file, owner.Project.Name, owner.Project.FilePath));
                }
                else
                {
                    result.Add(new SourceFile(file, SolutionLevelProject, null));
                }
            }
        }
        return result;
    }

    private static bool IsUnder(string path, string directory) =>
        path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || string.Equals(path, directory, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> Walk(string root, HashSet<string> excluded, CancellationToken ct)
    {
        if (!Directory.Exists(root)) yield break;
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            string[] files, subdirs;
            try
            {
                files = Directory.GetFiles(dir);
                subdirs = Directory.GetDirectories(dir);
            }
            catch
            {
                continue; // brak dostępu do katalogu – pomijamy
            }

            foreach (var f in files) yield return TextUtil.NormalizePath(f);
            foreach (var sub in subdirs)
            {
                var name = Path.GetFileName(sub);
                if (excluded.Contains(name)) continue;
                try
                {
                    if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) continue; // unikamy pętli dowiązań
                }
                catch
                {
                    continue;
                }
                stack.Push(sub);
            }
        }
    }
}
