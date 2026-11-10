using SolutionScanner.Core.Models;

namespace SolutionScanner.Export;

/// <summary>Budowa podsumowania analizy (UI i 13_Podsumowanie.csv).</summary>
public static class ReportGenerator
{
    public static List<SummaryItem> BuildSummary(AnalysisResult r)
    {
        var s = new List<SummaryItem>
        {
            new("Analiza", "Solucja", r.SolutionPath),
            new("Analiza", "Data", r.StartedAt.ToString("yyyy-MM-dd HH:mm:ss")),
            new("Analiza", "Czas trwania", r.Duration.ToString(@"hh\:mm\:ss")),
            new("Analiza", "Status", r.FatalError is not null ? "Błąd: " + r.FatalError : r.Cancelled ? "Anulowana (wyniki częściowe)" : "Zakończona"),
            new("Analiza", "MSBuild", r.MsBuildInfo),
            new("Analiza", "Frazy", string.Join("; ", r.Settings.Phrases)),
            new("Analiza", "Zakres analizy szczegółowej", r.Settings.AnalyzeEntireCode ? "Cały kod" : "Elementy powiązane z frazami"),
            new("Analiza", "Projekty przeanalizowane", r.ProjectsAnalyzed.ToString()),
            new("Analiza", "Dokumenty C#/VB", r.DocumentsAnalyzed.ToString()),
            new("Analiza", "Pozostałe pliki", r.FilesAnalyzed.ToString()),
            new("Analiza", "Pliki z błędami", r.FilesFailed.ToString()),
            new("Wyniki", "Wystąpienia fraz", r.SearchHits.Count.ToString()),
            new("Wyniki", "Deklaracje", r.Declarations.Count.ToString()),
            new("Wyniki", "Odwołania", r.References.Count.ToString()),
            new("Wyniki", "Precyzja", r.Precision.Count.ToString()),
            new("Wyniki", "Zaokrąglenia", r.Roundings.Count.ToString()),
            new("Wyniki", "Konwersje", r.Conversions.Count.ToString()),
            new("Wyniki", "Operacje arytmetyczne", r.Arithmetic.Count.ToString()),
            new("Wyniki", "Kroki przepływu danych", r.DataFlow.Count.ToString()),
            new("Wyniki", "Ograniczenia analizy", r.Limitations.Count.ToString()),
        };
        foreach (var sev in new[] { Severity.Error, Severity.Warning, Severity.Info })
            s.Add(new("Potencjalne problemy", DisplayNames.Of(sev), r.Problems.Count(p => p.Severity == sev).ToString()));
        foreach (var g in r.SearchHits.GroupBy(h => h.Phrase).OrderBy(g => g.Key))
            s.Add(new("Wystąpienia wg frazy", g.Key, g.Count().ToString()));
        foreach (var g in r.SearchHits.GroupBy(h => h.Location.ProjectName).OrderBy(g => g.Key))
            s.Add(new("Wystąpienia wg projektu", g.Key, g.Count().ToString()));
        foreach (var p in r.ProjectStatuses)
            s.Add(new("Projekty", p.Name, $"{p.Language}, {p.Status}, dokumentów: {p.DocumentCount}"));
        return s;
    }
}
