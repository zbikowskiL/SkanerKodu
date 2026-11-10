using Microsoft.CodeAnalysis;
using SolutionScanner.Core.Infrastructure;

namespace SolutionScanner.CSharp.Internal;

/// <summary>Ocena konwersji liczbowej: możliwa utrata precyzji, możliwe przepełnienie, opis.</summary>
internal readonly record struct ConversionAssessment(bool PrecisionLoss, bool Overflow, string Description);

/// <summary>
/// Reguły konwersji typów liczbowych .NET. Ocena dotyczy typów (potencjalne ryzyko), nie konkretnych wartości –
/// dlatego wyniki są ostrzeżeniami do weryfikacji, a nie dowiedzionymi błędami.
/// </summary>
internal static class ConversionRules
{
    public static ConversionAssessment Assess(ITypeSymbol from, ITypeSymbol to)
    {
        var f = RoslynHelpers.Underlying(from)!;
        var t = RoslynHelpers.Underlying(to)!;
        var fs = f.SpecialType;
        var ts = t.SpecialType;
        var fromName = RoslynHelpers.DisplayType(from);
        var toName = RoslynHelpers.DisplayType(to);
        var nullableNote = RoslynHelpers.IsNullableValueType(from) && !RoslynHelpers.IsNullableValueType(to)
            ? " Konwersja z typu nullable – wartość null spowoduje InvalidOperationException."
            : string.Empty;

        if (fs == SpecialType.System_Decimal && ts is SpecialType.System_Double or SpecialType.System_Single)
            return new(true, false, $"{fromName} → {toName}: utrata precyzji dziesiętnej – {toName} ma ok. {(ts == SpecialType.System_Double ? "15–17" : "6–9")} cyfr znaczących i reprezentację binarną.{nullableNote}");

        if (fs is SpecialType.System_Double or SpecialType.System_Single && ts == SpecialType.System_Decimal)
            return new(true, true, $"{fromName} → {toName}: wartość binarna przekształcana do dziesiętnej (maks. ok. 15 cyfr znaczących dla double); " +
                                    $"OverflowException dla wartości > 7,9·10^28, NaN i nieskończoności.{nullableNote}");

        if (fs == SpecialType.System_Double && ts == SpecialType.System_Single)
            return new(true, true, $"double → float: utrata precyzji (ok. 7 cyfr znaczących), wartości poza zakresem float dają nieskończoność.{nullableNote}");

        if (fs == SpecialType.System_Single && ts == SpecialType.System_Double)
            return new(false, false, $"float → double: konwersja rozszerzająca, ale błąd reprezentacji float zostaje zachowany (np. 0.1f → 0.100000001490116).{nullableNote}");

        if (RoslynHelpers.IsIntegral(f) && ts == SpecialType.System_Decimal)
            return new(false, false, $"{fromName} → decimal: konwersja bez utraty wartości.{nullableNote}");

        if (RoslynHelpers.IsIntegral(f) && ts == SpecialType.System_Double)
        {
            var loss = RoslynHelpers.IntegralBits(f) > 53 - 1;
            return new(loss, false, loss
                ? $"{fromName} → double: wartości powyżej 2^53 tracą dokładność.{nullableNote}"
                : $"{fromName} → double: konwersja bez utraty wartości.{nullableNote}");
        }

        if (RoslynHelpers.IsIntegral(f) && ts == SpecialType.System_Single)
        {
            var loss = RoslynHelpers.IntegralBits(f) > 16;
            return new(loss, false, loss
                ? $"{fromName} → float: wartości powyżej 2^24 (16 777 216) tracą dokładność.{nullableNote}"
                : $"{fromName} → float: konwersja bez utraty wartości.{nullableNote}");
        }

        if ((fs is SpecialType.System_Double or SpecialType.System_Single or SpecialType.System_Decimal) && RoslynHelpers.IsIntegral(t))
            return new(true, true, $"{fromName} → {toName}: część ułamkowa jest odrzucana (rzutowanie obcina w stronę zera), " +
                                    $"wartości spoza zakresu {toName} powodują przepełnienie.{nullableNote}");

        if (RoslynHelpers.IsIntegral(f) && RoslynHelpers.IsIntegral(t))
        {
            var fb = RoslynHelpers.IntegralBits(f);
            var tb = RoslynHelpers.IntegralBits(t);
            var fromSigned = IsSigned(fs);
            var toSigned = IsSigned(ts);
            var narrowing = tb < fb || (tb == fb && fromSigned != toSigned) || (fromSigned && !toSigned);
            return narrowing
                ? new(false, true, $"{fromName} → {toName}: konwersja zawężająca – w kontekście unchecked wartość spoza zakresu zostanie „zawinięta”.{nullableNote}")
                : new(false, false, $"{fromName} → {toName}: konwersja rozszerzająca.{nullableNote}");
        }

        return new(false, false, $"{fromName} → {toName}.{nullableNote}");
    }

    private static bool IsSigned(SpecialType t) => t is SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_Int32 or SpecialType.System_Int64;

    /// <summary>Uwagi specyficzne dla metod Convert.* / Decimal.* (zaokrąglanie zamiast obcinania).</summary>
    public static string MethodNote(string typeName, string methodName, ITypeSymbol? argumentType)
    {
        var arg = RoslynHelpers.Underlying(argumentType);
        var fractional = arg?.SpecialType is SpecialType.System_Double or SpecialType.System_Single or SpecialType.System_Decimal;
        if (typeName == "System.Convert" && methodName is "ToInt16" or "ToInt32" or "ToInt64" or "ToByte" or "ToUInt16" or "ToUInt32" or "ToUInt64" or "ToSByte" && fractional)
            return "Convert." + methodName + " ZAOKRĄGLA do najbliższej liczby całkowitej metodą bankierską (2,5 → 2; 3,5 → 4), a nie obcina jak rzutowanie; OverflowException poza zakresem.";
        if (typeName == "System.Convert" && methodName == "ToDecimal" && arg?.SpecialType is SpecialType.System_Double or SpecialType.System_Single)
            return "Convert.ToDecimal(double) zwraca wartość z maks. 15 cyframi znaczącymi.";
        if (arg?.SpecialType == SpecialType.System_String)
            return "Konwersja z tekstu – wynik zależy od formatu i kultury (separator dziesiętny).";
        return string.Empty;
    }
}
