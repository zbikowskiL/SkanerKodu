namespace SolutionScanner.Core.Models;

/// <summary>Projekt zadeklarowany w pliku .sln (odczytany bez MSBuild).</summary>
public sealed class ProjectEntry
{
    public string Name { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string ProjectTypeGuid { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string TargetFrameworks { get; init; } = string.Empty;
    public bool IsSdkStyle { get; init; }
    public bool IsSqlProject { get; init; }
    public bool Exists { get; init; }

    public string Directory => Path.GetDirectoryName(FilePath) ?? string.Empty;

    public string Description
    {
        get
        {
            if (!Exists) return "Brak pliku projektu";
            var style = IsSqlProject ? "projekt bazy danych" : IsSdkStyle ? "SDK" : "klasyczny (.NET Framework)";
            return string.IsNullOrEmpty(TargetFrameworks) ? $"{Language}, {style}" : $"{Language}, {style}, {TargetFrameworks}";
        }
    }
}

/// <summary>Wynik odczytu pliku .sln.</summary>
public sealed class SolutionDescriptor
{
    public string SolutionPath { get; init; } = string.Empty;
    public string SolutionDirectory => Path.GetDirectoryName(SolutionPath) ?? string.Empty;
    public IReadOnlyList<ProjectEntry> Projects { get; init; } = Array.Empty<ProjectEntry>();
}

/// <summary>Komunikat diagnostyczny z wczytywania solucji/projektów.</summary>
public sealed record LoadDiagnostic(Severity Severity, string Project, string Message);

/// <summary>Status wczytania projektu do przestrzeni roboczej Roslyn.</summary>
public sealed class ProjectLoadStatus
{
    public string Name { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public bool IsFallback { get; init; }
    public bool HasReliableSemantics { get; init; }
    public int DocumentCount { get; init; }
}
