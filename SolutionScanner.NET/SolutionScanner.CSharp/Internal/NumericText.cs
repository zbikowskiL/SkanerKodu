using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace SolutionScanner.CSharp.Internal;

/// <summary>
/// Analiza tekstu literałów liczbowych i formatów liczbowych .NET.
/// </summary>
internal static class NumericText
{
    private static readonly Regex StandardFormat = new(@"^(?<spec>[CcDdEeFfGgNnPpRrXxBb])(?<digits>\d{0,2})$", RegexOptions.Compiled);

    /// <summary>Wynik analizy literału: liczba miejsc po przecinku zapisanych w kodzie, mantysa i wykładnik dziesiętny.</summary>
    internal readonly record struct LiteralDigits(int DecimalPlaces, BigInteger Mantissa, int Exponent10, bool IsInteger);

    /// <summary>
    /// Parsuje literał C#/VB (np. 5.2500m, 1_000.50, 1.5e-3, 2.5D w VB) i zwraca liczbę miejsc po przecinku zapisaną w kodzie.
    /// Dla zapisu wykładniczego liczba miejsc = cyfry części ułamkowej − wykładnik (min. 0).
    /// Zwraca null dla literałów szesnastkowych/binarnych.
    /// </summary>
    public static LiteralDigits? ParseLiteral(string literal)
    {
        var text = literal.Replace("_", string.Empty).Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || text.StartsWith("0b", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("&H", StringComparison.OrdinalIgnoreCase) || text.StartsWith("&O", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("&B", StringComparison.OrdinalIgnoreCase))
            return null;

        // usunięcie sufiksów typu (C#: m, d, f, u, l; VB: D, R, F, S, I, L, US, UI, UL, @, !, #, %, &)
        var end = text.Length;
        while (end > 0 && !char.IsDigit(text[end - 1]) && text[end - 1] != '.') end--;
        text = text[..end];
        if (text.Length == 0) return null;

        var exponent = 0;
        var eIndex = text.IndexOfAny(new[] { 'e', 'E' });
        if (eIndex >= 0)
        {
            if (!int.TryParse(text[(eIndex + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent)) return null;
            text = text[..eIndex];
        }

        var dot = text.IndexOf('.');
        var intPart = dot >= 0 ? text[..dot] : text;
        var fracPart = dot >= 0 ? text[(dot + 1)..] : string.Empty;
        var digits = (intPart + fracPart).TrimStart('0');
        if (digits.Length == 0) digits = "0";
        if (!BigInteger.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var mantissa)) return null;

        var exp10 = exponent - fracPart.Length;
        var places = Math.Max(0, fracPart.Length - exponent);
        return new LiteralDigits(places, mantissa, exp10, dot < 0 && eIndex < 0);
    }

    /// <summary>
    /// Czy wartość dziesiętna (mantysa × 10^exp10) ma dokładną reprezentację w binarnym typie zmiennoprzecinkowym
    /// o podanej liczbie bitów mantysy (double: 53, float: 24). Algorytm: wartość jest dokładna wtedy, gdy
    /// po skróceniu ułamka mianownik jest potęgą 2, a licznik (bez czynników 2) mieści się w mantysie.
    /// Zakres wykładnika nie jest sprawdzany (pomijalne dla literałów spotykanych w kodzie biznesowym).
    /// </summary>
    public static bool IsExactlyRepresentable(LiteralDigits literal, int mantissaBits)
    {
        var n = literal.Mantissa;
        if (n.IsZero) return true;
        if (literal.Exponent10 >= 0)
        {
            n *= BigInteger.Pow(10, literal.Exponent10);
        }
        else
        {
            var d = -literal.Exponent10;
            var five = BigInteger.Pow(5, d);
            if (!(n % five).IsZero) return false; // mianownik zawiera czynnik 5 – brak dokładnej reprezentacji binarnej
            n /= five;
        }
        while (!n.IsZero && n.IsEven) n >>= 1;
        return n.GetBitLength() <= mantissaBits;
    }

    /// <summary>Skala wartości decimal (liczba cyfr po przecinku zapisana w strukturze decimal).</summary>
    public static int DecimalScale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0xFF;

    /// <summary>
    /// Analizuje format liczbowy .NET (standardowy: N2, F4, C, P, E; niestandardowy: 0.00, #,##0.0000).
    /// Zwraca liczbę miejsc po przecinku (null gdy format nie określa miejsc, np. G, R) i opis.
    /// </summary>
    public static (int? Places, string Description) ParseFormat(string format)
    {
        var f = format.Trim();
        var m = StandardFormat.Match(f);
        if (m.Success)
        {
            var spec = char.ToUpperInvariant(m.Groups["spec"].Value[0]);
            int? digits = m.Groups["digits"].Value.Length > 0 ? int.Parse(m.Groups["digits"].Value, CultureInfo.InvariantCulture) : null;
            return spec switch
            {
                'N' => (digits ?? 2, digits is null ? "Format N – liczba miejsc z kultury (NumberDecimalDigits, zwykle 2)" : $"Format N{digits} – {digits} miejsc, separatory tysięcy"),
                'F' => (digits ?? 2, digits is null ? "Format F – liczba miejsc z kultury (zwykle 2)" : $"Format F{digits} – {digits} miejsc po przecinku"),
                'C' => (digits ?? 2, digits is null ? "Format C – waluta, liczba miejsc z kultury (zwykle 2)" : $"Format C{digits} – waluta, {digits} miejsc"),
                'P' => (digits ?? 2, $"Format P – procent (wartość × 100), {(digits?.ToString() ?? "domyślnie 2")} miejsc"),
                'E' => (digits ?? 6, $"Format E – notacja wykładnicza, {(digits?.ToString() ?? "6")} miejsc mantysy"),
                'D' or 'X' or 'B' => (0, $"Format {spec} – tylko liczby całkowite"),
                'G' => (null, "Format G – liczba cyfr znaczących, nie miejsc po przecinku"),
                'R' => (null, "Format R – reprezentacja pozwalająca odtworzyć wartość (round-trip)"),
                _ => (null, "Format standardowy")
            };
        }

        // Format niestandardowy – analizujemy pierwszą sekcję (do ';'), pomijając tekst w cudzysłowach i znaki ucieczki
        var section = new System.Text.StringBuilder();
        var inQuote = '\0';
        for (var i = 0; i < f.Length; i++)
        {
            var c = f[i];
            if (inQuote != '\0') { if (c == inQuote) inQuote = '\0'; continue; }
            if (c is '\'' or '"') { inQuote = c; continue; }
            if (c == '\\') { i++; continue; }
            if (c == ';') break;
            section.Append(c);
        }
        var s = section.ToString();
        if (!s.Contains('0') && !s.Contains('#')) return (null, "Format nieliczbowy / nierozpoznany");
        var dot = s.IndexOf('.');
        if (dot < 0) return (0, $"Format niestandardowy „{format}” – bez miejsc po przecinku");
        var after = s[(dot + 1)..];
        var zeros = after.Count(c => c == '0');
        var hashes = after.Count(c => c == '#');
        var description = hashes == 0
            ? $"Format niestandardowy „{format}” – dokładnie {zeros} miejsc po przecinku"
            : $"Format niestandardowy „{format}” – od {zeros} do {zeros + hashes} miejsc po przecinku";
        return (zeros + hashes, description);
    }

    /// <summary>Wyodrębnia elementy formatu złożonego, np. "{0:N2} zł {1,10:0.00}" → (0,"N2"), (1,"0.00").</summary>
    public static IEnumerable<(int Index, string Format)> ParseCompositeFormat(string composite)
    {
        var i = 0;
        while (i < composite.Length)
        {
            var c = composite[i];
            if (c == '{')
            {
                if (i + 1 < composite.Length && composite[i + 1] == '{') { i += 2; continue; }
                var close = composite.IndexOf('}', i + 1);
                if (close < 0) yield break;
                var item = composite[(i + 1)..close];
                var colon = item.IndexOf(':');
                var indexPart = (colon >= 0 ? item[..colon] : item).Split(',')[0].Trim();
                if (colon >= 0 && int.TryParse(indexPart, NumberStyles.None, CultureInfo.InvariantCulture, out var idx))
                    yield return (idx, item[(colon + 1)..]);
                i = close + 1;
                continue;
            }
            i++;
        }
    }
}
