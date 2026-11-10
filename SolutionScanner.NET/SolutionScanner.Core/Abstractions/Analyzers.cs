namespace SolutionScanner.Core.Abstractions;

/// <summary>
/// Analizator dokumentu Roslyn (C# / VB) wczytanego do przestrzeni roboczej.
/// Wywoływany równolegle dla wielu dokumentów – implementacja musi być bezstanowa
/// (stan pojedynczego uruchomienia przechowuj w <see cref="AnalysisRunContext.GetState{T}"/>).
/// </summary>
public interface IDocumentAnalyzer
{
    string Name { get; }
    Task AnalyzeAsync(DocumentContext context);
}

/// <summary>Analizator pliku spoza modelu Roslyn (SQL, XML, JSON, EDMX, Razor, pliki .cs poza projektami).</summary>
public interface IFileAnalyzer
{
    string Name { get; }

    /// <summary>Czy analizator obsługuje plik o podanej ścieżce (sprawdzane przed odczytem treści).</summary>
    bool CanAnalyze(FileContext context);

    Task AnalyzeAsync(FileContext context);
}

/// <summary>Analizator uruchamiany po przetworzeniu wszystkich dokumentów i plików (porównania, przepływ danych, reguły).</summary>
public interface IPostAnalyzer
{
    string Name { get; }

    /// <summary>Kolejność wykonania (rosnąco).</summary>
    int Order { get; }

    Task RunAsync(AnalysisRunContext context, CancellationToken cancellationToken);
}
