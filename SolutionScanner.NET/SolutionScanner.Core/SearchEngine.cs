using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace SolutionScanner.Core;

/// <summary>
/// Silnik dopasowania fraz do nazw (identyfikatorów).
/// <para>
/// Algorytm: fraza i identyfikator są normalizowane (opcjonalnie usunięcie znaków diakrytycznych,
/// opcjonalnie zamiana na małe litery), a następnie sprawdzane jest zawieranie frazy w identyfikatorze
/// (dopasowanie fragmentu nazwy). Fraza „opr” znajdzie więc „Oprocentowanie”, „ObliczOprocentowanie” i „oprKredytu”.
/// </para>
/// <para>
/// Wyniki dla identyfikatora są cache'owane – w dużych solucjach te same nazwy powtarzają się tysiące razy.
/// </para>
/// </summary>
public sealed class SearchEngine
{
    private static readonly char[] Separators = { ';', ',', '\r', '\n', '\t', ' ' };
    private readonly (string Original, string Normalized)[] _phrases;
    private readonly ConcurrentDictionary<string, string[]> _cache = new(StringComparer.Ordinal);

    public SearchEngine(IEnumerable<string> phrases, bool caseSensitive = false, bool ignoreDiacritics = true)
    {
        CaseSensitive = caseSensitive;
        IgnoreDiacritics = ignoreDiacritics;
        _phrases = phrases
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .Distinct(caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase)
            .Select(p => (p, Normalize(p)))
            .ToArray();
    }

    public bool CaseSensitive { get; }
    public bool IgnoreDiacritics { get; }
    public bool HasPhrases => _phrases.Length > 0;
    public IReadOnlyList<string> Phrases => _phrases.Select(p => p.Original).ToArray();

    /// <summary>Zwraca listę fraz zawartych w nazwie (pusta lista = brak dopasowania).</summary>
    public IReadOnlyList<string> Match(string? identifier)
    {
        if (string.IsNullOrEmpty(identifier) || _phrases.Length == 0) return Array.Empty<string>();
        return _cache.GetOrAdd(identifier, id =>
        {
            var normalized = Normalize(id);
            List<string>? result = null;
            foreach (var (original, norm) in _phrases)
            {
                if (normalized.Contains(norm, StringComparison.Ordinal))
                    (result ??= new List<string>()).Add(original);
            }
            return result?.ToArray() ?? Array.Empty<string>();
        });
    }

    public bool IsMatch(string? identifier) => Match(identifier).Count > 0;

    /// <summary>Zwraca frazy dopasowane do dowolnej z podanych nazw (bez powtórzeń, w kolejności fraz).</summary>
    public IReadOnlyList<string> MatchAny(IEnumerable<string?> identifiers)
    {
        if (_phrases.Length == 0) return Array.Empty<string>();
        HashSet<string>? found = null;
        foreach (var id in identifiers)
        {
            foreach (var p in Match(id)) (found ??= new HashSet<string>(StringComparer.Ordinal)).Add(p);
        }
        if (found is null) return Array.Empty<string>();
        return _phrases.Select(p => p.Original).Where(found.Contains).ToArray();
    }

    public string Normalize(string text)
    {
        var s = IgnoreDiacritics ? RemoveDiacritics(text) : text;
        return CaseSensitive ? s : s.ToLowerInvariant();
    }

    /// <summary>Usuwa znaki diakrytyczne (w tym ł/Ł, które nie rozkładają się w normalizacji Unicode).</summary>
    public static string RemoveDiacritics(string text)
    {
        if (text.All(c => c < 128)) return text;
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c switch { 'ł' => 'l', 'Ł' => 'L', 'đ' => 'd', 'Đ' => 'D', 'ø' => 'o', 'Ø' => 'O', _ => c });
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Dzieli tekst na frazy. Separatory: średnik, przecinek, nowa linia, tabulator, spacja
    /// (identyfikatory nie zawierają spacji, więc spacja również rozdziela frazy).
    /// Linie zaczynające się od '#' są traktowane jako komentarz.
    /// </summary>
    public static List<string> ParsePhrases(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        var withoutComments = string.Join('\n', text.Split('\n').Where(l => !l.TrimStart().StartsWith('#')));
        return withoutComments
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Wczytuje frazy z pliku TXT (UTF-8 z/bez BOM, również UTF-16 z BOM).</summary>
    public static List<string> LoadPhrasesFromFile(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8); // wykrywa BOM UTF-8/UTF-16
        return ParsePhrases(text);
    }

    /// <summary>
    /// Wyznacza tokeny przypominające identyfikatory w zwykłym tekście (np. wartości atrybutów XML).
    /// Zwraca token i jego offset w tekście.
    /// </summary>
    public static IEnumerable<(string Token, int Offset)> EnumerateIdentifierTokens(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            if (char.IsLetter(text[i]) || text[i] == '_')
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                yield return (text[start..i], start);
            }
            else
            {
                i++;
            }
        }
    }
}
