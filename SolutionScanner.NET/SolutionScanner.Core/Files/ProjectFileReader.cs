using System.Xml.Linq;
using SolutionScanner.Core.Infrastructure;

namespace SolutionScanner.Core.Files;

/// <summary>Dane odczytane bezpośrednio z pliku projektu (bez ewaluacji MSBuild).</summary>
public sealed class ProjectFileInfo
{
    public string FilePath { get; init; } = string.Empty;
    public bool IsSdkStyle { get; init; }
    public string TargetFrameworks { get; init; } = string.Empty;
    public List<string> CompileItems { get; } = new();
    public List<string> ProjectReferences { get; } = new();
    public List<string> HintPathReferences { get; } = new();
    public List<string> AssemblyReferences { get; } = new();
    public List<(string Id, string Version)> PackageReferences { get; } = new();
}

/// <summary>
/// Odczytuje plik projektu MSBuild jako XML. Używany do listy projektów, zależności pakietów
/// oraz do awaryjnego wczytania projektu, gdy MSBuildWorkspace nie może go załadować.
/// Ograniczenie: nie ewaluuje warunków, importów ani właściwości MSBuild ($(...)).
/// </summary>
public static class ProjectFileReader
{
    public static ProjectFileInfo Read(string projectPath)
    {
        var doc = XDocument.Load(projectPath);
        var root = doc.Root ?? throw new InvalidDataException("Pusty plik projektu.");
        var dir = Path.GetDirectoryName(projectPath)!;

        var isSdk = root.Attribute("Sdk") is not null
                    || root.Elements().Any(e => e.Name.LocalName == "Sdk")
                    || root.Elements().Any(e => e.Name.LocalName == "Import" && ((string?)e.Attribute("Sdk")) is not null);

        string Prop(string name) => root.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() ?? string.Empty;

        var tfm = Prop("TargetFrameworks");
        if (tfm.Length == 0) tfm = Prop("TargetFramework");
        if (tfm.Length == 0)
        {
            var v = Prop("TargetFrameworkVersion");
            if (v.Length > 0) tfm = ".NET Framework " + v.TrimStart('v');
        }

        var info = new ProjectFileInfo { FilePath = projectPath, IsSdkStyle = isSdk, TargetFrameworks = tfm };

        foreach (var item in root.Descendants())
        {
            var include = (string?)item.Attribute("Include");
            if (string.IsNullOrWhiteSpace(include)) continue;
            switch (item.Name.LocalName)
            {
                case "Compile":
                    info.CompileItems.AddRange(ExpandInclude(dir, include));
                    break;
                case "ProjectReference":
                    if (!include.Contains("$(")) info.ProjectReferences.Add(TextUtil.NormalizePath(Path.Combine(dir, include)));
                    break;
                case "Reference":
                    var hint = item.Elements().FirstOrDefault(e => e.Name.LocalName == "HintPath")?.Value.Trim();
                    if (!string.IsNullOrEmpty(hint) && !hint.Contains("$("))
                    {
                        var full = TextUtil.NormalizePath(Path.Combine(dir, hint));
                        if (File.Exists(full)) info.HintPathReferences.Add(full);
                    }
                    info.AssemblyReferences.Add(include.Split(',')[0].Trim());
                    break;
                case "PackageReference":
                    var version = (string?)item.Attribute("Version")
                                  ?? item.Elements().FirstOrDefault(e => e.Name.LocalName == "Version")?.Value
                                  ?? string.Empty;
                    info.PackageReferences.Add((include, version.Trim()));
                    break;
            }
        }

        var packagesConfig = Path.Combine(dir, "packages.config");
        if (File.Exists(packagesConfig))
        {
            try
            {
                var pc = XDocument.Load(packagesConfig);
                foreach (var p in pc.Descendants().Where(e => e.Name.LocalName == "package"))
                    info.PackageReferences.Add(((string?)p.Attribute("id") ?? "?", (string?)p.Attribute("version") ?? string.Empty));
            }
            catch
            {
                // uszkodzony packages.config nie blokuje analizy
            }
        }

        return info;
    }

    private static IEnumerable<string> ExpandInclude(string dir, string include)
    {
        foreach (var part in include.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Contains("$(")) continue;
            var normalized = part.Replace('\\', Path.DirectorySeparatorChar);
            if (!normalized.Contains('*'))
            {
                yield return TextUtil.NormalizePath(Path.Combine(dir, normalized));
                continue;
            }

            var recursive = normalized.Contains("**");
            var pattern = Path.GetFileName(normalized);
            var baseDir = Path.GetDirectoryName(normalized.Replace("**" + Path.DirectorySeparatorChar, string.Empty).Replace("**", string.Empty)) ?? string.Empty;
            var searchDir = Path.Combine(dir, baseDir);
            if (!Directory.Exists(searchDir)) continue;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(searchDir, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }
            foreach (var f in files) yield return TextUtil.NormalizePath(f);
        }
    }
}
