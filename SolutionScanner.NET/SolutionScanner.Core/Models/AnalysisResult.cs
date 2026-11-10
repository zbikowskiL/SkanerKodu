using System.Collections;

namespace SolutionScanner.Core.Models;

/// <summary>
/// Bezpieczna wątkowo kolekcja wyników – analizatory dokumentów działają równolegle
/// i dopisują wyniki do wspólnych list.
/// </summary>
public sealed class ResultSet<T> : IReadOnlyList<T>
{
    private readonly List<T> _items = new();
    private readonly object _lock = new();

    public void Add(T item)
    {
        lock (_lock) _items.Add(item);
    }

    public void AddRange(IEnumerable<T> items)
    {
        lock (_lock) _items.AddRange(items);
    }

    public int Count
    {
        get { lock (_lock) return _items.Count; }
    }

    public T this[int index]
    {
        get { lock (_lock) return _items[index]; }
    }

    public List<T> Snapshot()
    {
        lock (_lock) return new List<T>(_items);
    }

    public void Sort(Comparison<T> comparison)
    {
        lock (_lock) _items.Sort(comparison);
    }

    public void Clear()
    {
        lock (_lock) _items.Clear();
    }

    public IEnumerator<T> GetEnumerator() => Snapshot().GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Komplet wyników jednej analizy.</summary>
public sealed class AnalysisResult
{
    public string SolutionPath { get; init; } = string.Empty;
    public AnalysisSettings Settings { get; init; } = new();
    public DateTime StartedAt { get; init; } = DateTime.Now;
    public TimeSpan Duration { get; set; }
    public bool Cancelled { get; set; }
    public string? FatalError { get; set; }
    public string? LogFilePath { get; set; }
    public string MsBuildInfo { get; set; } = string.Empty;

    public ResultSet<SearchHit> SearchHits { get; } = new();
    public ResultSet<VariableDeclarationInfo> Declarations { get; } = new();
    public ResultSet<SymbolReferenceInfo> References { get; } = new();
    public ResultSet<ProjectDependency> Dependencies { get; } = new();
    public ResultSet<PrecisionInfo> Precision { get; } = new();
    public ResultSet<RoundingInfo> Roundings { get; } = new();
    public ResultSet<ConversionInfo> Conversions { get; } = new();
    public ResultSet<ArithmeticInfo> Arithmetic { get; } = new();
    public ResultSet<SqlSchemaItem> SqlItems { get; } = new();
    public ResultSet<EfMapping> EfMappings { get; } = new();
    public ResultSet<MappingComparison> Comparisons { get; } = new();
    public ResultSet<DataFlowStep> DataFlow { get; } = new();
    public ResultSet<PotentialProblem> Problems { get; } = new();
    public ResultSet<AnalysisLimitation> Limitations { get; } = new();
    public ResultSet<LoadDiagnostic> LoadDiagnostics { get; } = new();
    public ResultSet<ProjectLoadStatus> ProjectStatuses { get; } = new();

    public int ProjectsAnalyzed { get; set; }
    public int DocumentsAnalyzed;
    public int FilesAnalyzed;
    public int FilesFailed;

    /// <summary>Porządkuje wyniki (projekt, plik, linia, kolumna) – wyniki są deterministyczne mimo równoległości.</summary>
    public void SortAll()
    {
        static int Cmp(CodeLocation a, CodeLocation b)
        {
            var c = string.Compare(a.ProjectName, b.ProjectName, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
            c = string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
            c = a.Line.CompareTo(b.Line);
            return c != 0 ? c : a.Column.CompareTo(b.Column);
        }

        SearchHits.Sort((a, b) => { var c = Cmp(a.Location, b.Location); return c != 0 ? c : string.CompareOrdinal(a.Phrase, b.Phrase); });
        Declarations.Sort((a, b) => Cmp(a.Location, b.Location));
        References.Sort((a, b) => { var c = string.CompareOrdinal(a.SymbolFullName, b.SymbolFullName); return c != 0 ? c : Cmp(a.Location, b.Location); });
        Precision.Sort((a, b) => Cmp(a.Location, b.Location));
        Roundings.Sort((a, b) => Cmp(a.Location, b.Location));
        Conversions.Sort((a, b) => Cmp(a.Location, b.Location));
        Arithmetic.Sort((a, b) => Cmp(a.Location, b.Location));
        SqlItems.Sort((a, b) => Cmp(a.Location, b.Location));
        EfMappings.Sort((a, b) => { var c = string.CompareOrdinal(a.EntityClass, b.EntityClass); return c != 0 ? c : string.CompareOrdinal(a.PropertyName, b.PropertyName); });
        Problems.Sort((a, b) => { var c = b.Severity.CompareTo(a.Severity); return c != 0 ? c : Cmp(a.Location, b.Location); });
        Limitations.Sort((a, b) => Cmp(a.Location, b.Location));
    }
}

/// <summary>Postęp analizy przekazywany do UI.</summary>
public sealed record AnalysisProgress(
    string Stage,
    string Message,
    string ProjectName = "",
    string FilePath = "",
    int Current = 0,
    int Total = 0,
    double Percent = 0);
