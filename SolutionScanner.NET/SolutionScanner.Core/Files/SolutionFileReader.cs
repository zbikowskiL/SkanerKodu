using System.Text.RegularExpressions;
using System.Xml.Linq;
using SolutionScanner.Core.Infrastructure;
using SolutionScanner.Core.Models;

namespace SolutionScanner.Core.Files;

/// <summary>
/// Szybki odczyt pliku .sln / .slnx bez użycia MSBuild – pozwala natychmiast pokazać listę projektów w UI
/// i obsłużyć projekty, których MSBuildWorkspace nie potrafi wczytać (np. .sqlproj).
/// </summary>
public static class SolutionFileReader
{
    private const string SolutionFolderGuid = "2150E333-8FDC-42A3-9474-1A3956D46DE8";
    private const string SqlProjectGuid = "00D1A9C2-B5F0-4AF3-8072-F6C62B433612";

    private static readonly Regex ProjectLine = new(
        "^Project\\(\"\\{(?<type>[^}]+)\\}\"\\)\\s*=\\s*\"(?<name>[^\"]+)\"\\s*,\\s*\"(?<path>[^\"]+)\"\\s*,\\s*\"\\{(?<guid>[^}]+)\\}\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static SolutionDescriptor Read(string solutionPath)
    {
        var fullPath = Path.GetFullPath(solutionPath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Nie znaleziono pliku solucji.", fullPath);
        var dir = Path.GetDirectoryName(fullPath)!;

        var entries = Path.GetExtension(fullPath).Equals(".slnx", StringComparison.OrdinalIgnoreCase)
            ? ReadSlnx(fullPath, dir)
            : ReadSln(fullPath, dir);

        return new SolutionDescriptor
        {
            SolutionPath = fullPath,
            Projects = entries.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private static IEnumerable<ProjectEntry> ReadSln(string slnPath, string dir)
    {
        var text = File.ReadAllText(slnPath);
        if (!text.Contains("Microsoft Visual Studio Solution File", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Plik nie jest prawidłowym plikiem solucji Visual Studio (.sln).");

        foreach (var rawLine in text.Split('\n'))
        {
            var m = ProjectLine.Match(rawLine.Trim());
            if (!m.Success) continue;
            var type = m.Groups["type"].Value;
            if (type.Equals(SolutionFolderGuid, StringComparison.OrdinalIgnoreCase)) continue;
            var relative = m.Groups["path"].Value;
            if (relative.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue; // projekty web site
            yield return CreateEntry(m.Groups["name"].Value, relative, dir, type);
        }
    }

    private static IEnumerable<ProjectEntry> ReadSlnx(string slnxPath, string dir)
    {
        var doc = XDocument.Load(slnxPath);
        foreach (var p in doc.Descendants().Where(e => e.Name.LocalName == "Project"))
        {
            var relative = (string?)p.Attribute("Path");
            if (string.IsNullOrEmpty(relative)) continue;
            var name = Path.GetFileNameWithoutExtension(relative);
            yield return CreateEntry(name, relative, dir, string.Empty);
        }
    }

    private static ProjectEntry CreateEntry(string name, string relative, string solutionDir, string typeGuid)
    {
        var path = TextUtil.NormalizePath(Path.Combine(solutionDir, relative.Replace('\\', Path.DirectorySeparatorChar)));
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var exists = File.Exists(path);
        var isSql = ext == ".sqlproj" || typeGuid.Equals(SqlProjectGuid, StringComparison.OrdinalIgnoreCase);
        var language = ext switch
        {
            ".csproj" => "C#",
            ".vbproj" => "VB",
            ".fsproj" => "F#",
            ".sqlproj" => "SQL",
            _ => ext.TrimStart('.').ToUpperInvariant()
        };

        ProjectFileInfo? info = null;
        if (exists && ext is ".csproj" or ".vbproj" or ".sqlproj")
        {
            try { info = ProjectFileReader.Read(path); }
            catch { /* uszkodzony plik projektu – zostanie zgłoszony podczas wczytywania */ }
        }

        return new ProjectEntry
        {
            Name = name,
            FilePath = path,
            RelativePath = relative,
            ProjectTypeGuid = typeGuid,
            Language = language,
            Exists = exists,
            IsSqlProject = isSql,
            IsSdkStyle = info?.IsSdkStyle ?? false,
            TargetFrameworks = info?.TargetFrameworks ?? string.Empty
        };
    }
}
