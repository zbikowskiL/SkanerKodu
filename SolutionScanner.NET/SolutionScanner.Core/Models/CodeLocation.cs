namespace SolutionScanner.Core.Models;

/// <summary>
/// Lokalizacja wyniku w pliku źródłowym. Linia i kolumna są numerowane od 1.
/// <see cref="SpanStart"/> i <see cref="SpanLength"/> (offset znakowy) służą do precyzyjnej korelacji
/// wyników między modułami (np. konwersja zawierająca odwołanie) i nie są eksportowane.
/// </summary>
public sealed record CodeLocation
{
    public string ProjectName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public int Line { get; init; }
    public int Column { get; init; }
    public int SpanStart { get; init; } = -1;
    public int SpanLength { get; init; }

    public static CodeLocation Empty { get; } = new();

    public bool IsEmpty => string.IsNullOrEmpty(FilePath);

    /// <summary>Czy zakres tej lokalizacji zawiera zakres <paramref name="other"/> (ten sam plik).</summary>
    public bool Contains(CodeLocation other)
    {
        if (SpanStart < 0 || other.SpanStart < 0) return false;
        if (!string.Equals(FilePath, other.FilePath, StringComparison.OrdinalIgnoreCase)) return false;
        return other.SpanStart >= SpanStart && other.SpanStart + other.SpanLength <= SpanStart + SpanLength;
    }

    public bool SameFileAndLine(CodeLocation other) =>
        Line == other.Line && string.Equals(FilePath, other.FilePath, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => IsEmpty ? "(brak lokalizacji)" : $"{RelativePath}({Line},{Column})";
}
