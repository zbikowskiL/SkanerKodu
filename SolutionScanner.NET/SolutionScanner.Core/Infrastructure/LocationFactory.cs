using Microsoft.CodeAnalysis;
using SolutionScanner.Core.Abstractions;
using SolutionScanner.Core.Models;

namespace SolutionScanner.Core.Infrastructure;

/// <summary>Tworzenie <see cref="CodeLocation"/> z lokalizacji Roslyn.</summary>
public static class LocationFactory
{
    public static CodeLocation FromLocation(AnalysisRunContext run, Location location, string projectName)
    {
        if (!location.IsInSource || location.SourceTree is null) return CodeLocation.Empty;
        var span = location.GetLineSpan();
        return run.CreateLocation(
            projectName,
            location.SourceTree.FilePath,
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1,
            location.SourceSpan.Start,
            location.SourceSpan.Length);
    }

    /// <summary>Lokalizacja deklaracji symbolu (pierwsza lokalizacja w kodzie źródłowym).</summary>
    public static CodeLocation DeclarationOf(AnalysisRunContext run, ISymbol symbol, Solution? solution)
    {
        var loc = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (loc is null) return CodeLocation.Empty;
        var project = ProjectNameFor(solution, loc.SourceTree);
        return FromLocation(run, loc, project);
    }

    public static string ProjectNameFor(Solution? solution, SyntaxTree? tree)
    {
        if (solution is null || tree is null) return string.Empty;
        var doc = solution.GetDocument(tree);
        return doc is null ? string.Empty : RoslynHelpers.CleanProjectName(doc.Project.Name);
    }
}
