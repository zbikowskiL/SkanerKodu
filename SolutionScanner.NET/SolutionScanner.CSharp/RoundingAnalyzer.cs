using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SolutionScanner.Core;
using SolutionScanner.Core.Abstractions;
using SolutionScanner.Core.Infrastructure;
using SolutionScanner.Core.Models;
using SolutionScanner.CSharp.Internal;

namespace SolutionScanner.CSharp;

/// <summary>
/// Analiza jawnych zaokrągleń: Math.Round, MathF.Round, decimal.Round, Math.Truncate/Floor/Ceiling,
/// decimal.Truncate/Floor/Ceiling, z rozpoznaniem liczby miejsc (parametr decimals/digits) i trybu
/// MidpointRounding (domyślnie ToEven – zaokrąglenie bankierskie). Rozpoznawane są również metody
/// użytkownika o nazwach zawierających „Round”/„Zaokragl” (oznaczane jako niezweryfikowane semantycznie).
/// </summary>
public sealed class RoundingAnalyzer : IDocumentAnalyzer
{
    private static readonly HashSet<string> RoundingTypes = new(StringComparer.Ordinal) { "System.Math", "System.MathF", "System.Decimal" };
    private static readonly HashSet<string> RoundingMethods = new(StringComparer.Ordinal) { "Round", "Truncate", "Floor", "Ceiling" };

    public string Name => "Zaokrąglenia";

    public Task AnalyzeAsync(DocumentContext ctx)
    {
        foreach (var op in ctx.Operations)
        {
            if (op is not IInvocationOperation inv) continue;
            var method = inv.TargetMethod;
            var typeName = method.ContainingType?.ToDisplayString(RoslynHelpers.ClrFormat) ?? string.Empty;
            var isKnown = RoundingTypes.Contains(typeName) && RoundingMethods.Contains(method.Name);
            var normalizedName = SearchEngine.RemoveDiacritics(method.Name).ToLowerInvariant();
            var isCustom = !isKnown && method.Locations.Any(l => l.IsInSource)
                           && (normalizedName.Contains("round") || normalizedName.Contains("zaokragl"));
            if (!isKnown && !isCustom) continue;

            Analyze(ctx, inv, typeName, isKnown);
        }
        return Task.CompletedTask;
    }

    private static void Analyze(DocumentContext ctx, IInvocationOperation inv, string typeName, bool isKnown)
    {
        var method = inv.TargetMethod;
        var target = OperationContext.GetTarget(inv, ctx.Model);
        var phrases = OperationContext.Phrases(ctx, inv, target);
        if (!ctx.InScope(phrases)) return;

        var valueArg = inv.Arguments.FirstOrDefault(a => a.Parameter is { } p && RoslynHelpers.IsNumeric(p.Type) && p.Ordinal == 0)
                       ?? inv.Arguments.FirstOrDefault();
        var digitsArg = inv.Arguments.FirstOrDefault(a => a.Parameter is { } p && p.Ordinal > 0
                                                          && p.Type.SpecialType == SpecialType.System_Int32);
        var modeArg = inv.Arguments.FirstOrDefault(a => a.Parameter?.Type.Name == "MidpointRounding");

        int? digits = null;
        var digitsExpression = string.Empty;
        if (digitsArg is not null)
        {
            if (digitsArg.Value.ConstantValue is { HasValue: true, Value: int d }) digits = d;
            else digitsExpression = "nieustalone statycznie: " + digitsArg.Value.Syntax;
        }
        else if (isKnown)
        {
            digits = 0;
        }

        string mode;
        var isDefault = false;
        if (modeArg is not null)
        {
            mode = OperationContext.UnwrapImplicit(modeArg.Value) is IFieldReferenceOperation f
                ? f.Field.Name
                : "wyrażenie: " + modeArg.Value.Syntax;
        }
        else
        {
            mode = method.Name switch
            {
                "Round" when isKnown => "ToEven (domyślny – zaokrąglenie bankierskie)",
                "Truncate" => "Obcięcie w stronę zera",
                "Floor" => "W dół (Floor)",
                "Ceiling" => "W górę (Ceiling)",
                _ => "nieznany (metoda użytkownika)"
            };
            isDefault = method.Name == "Round" && isKnown;
        }

        var argumentType = valueArg is null ? string.Empty : RoslynHelpers.DisplayType(OperationContext.UnwrapImplicit(valueArg.Value).Type ?? valueArg.Value.Type);
        var methodLabel = isKnown ? $"{method.ContainingType!.Name}.{method.Name}" : RoslynHelpers.MemberName(method);
        var note = isKnown ? string.Empty : "Metoda użytkownika – sposób zaokrąglenia nie jest weryfikowany semantycznie.";

        ctx.Result.Roundings.Add(new RoundingInfo
        {
            Phrase = DocumentContext.JoinPhrases(phrases),
            Method = methodLabel,
            Digits = digits,
            DigitsExpression = digits?.ToString() ?? digitsExpression,
            MidpointMode = mode,
            IsDefaultMidpoint = isDefault,
            ArgumentType = argumentType,
            ValueExpression = valueArg is null ? string.Empty : TextUtil.Shorten(valueArg.Value.Syntax.ToString(), 200),
            TargetName = target.Name,
            TargetSymbolKey = target.Key,
            ContainingMember = ctx.EnclosingMember(inv.Syntax.SpanStart),
            Language = ctx.LanguageLabel,
            CodeLine = ctx.CodeLine(inv.Syntax),
            Note = note,
            Location = ctx.Loc(inv.Syntax)
        });

        ctx.Result.Precision.Add(new PrecisionInfo
        {
            Phrase = DocumentContext.JoinPhrases(phrases),
            Source = PrecisionSource.ExplicitRounding,
            ElementName = target.Name,
            TypeName = argumentType,
            ValueText = TextUtil.Shorten(inv.Syntax.ToString(), 200),
            DecimalPlaces = digits,
            Description = digits.HasValue
                ? $"Jawne zaokrąglenie do {digits} miejsc po przecinku ({methodLabel}, tryb: {mode})."
                : $"Jawne zaokrąglenie ({methodLabel}) – liczba miejsc {digitsExpression}.",
            Language = ctx.LanguageLabel,
            CodeLine = ctx.CodeLine(inv.Syntax),
            TargetSymbolKey = target.Key,
            Location = ctx.Loc(inv.Syntax)
        });

        if (!isKnown) return;
        var financial = OperationContext.IsFinancial(ctx, inv, target);
        if (RoslynHelpers.IsBinaryFloatingPoint(valueArg?.Value.Type) && financial)
        {
            OperationContext.AddProblem(ctx, "RND001", Severity.Warning, "Zaokrąglenia",
                $"Zaokrąglanie wartości {argumentType} w kontekście finansowym",
                $"{methodLabel} na typie {argumentType} działa na przybliżonej reprezentacji binarnej – np. Math.Round(2.675, 2) daje 2.67, " +
                "bo 2.675 jest przechowywane jako 2.67499999… Wynik nadal jest wartością binarną. Rozważ decimal.",
                inv.Syntax, target.Name, phrases);
        }
        if (isDefault && financial)
        {
            OperationContext.AddProblem(ctx, "RND002", Severity.Info, "Zaokrąglenia",
                "Domyślny tryb zaokrąglenia ToEven (bankierski)",
                $"{methodLabel} bez MidpointRounding używa MidpointRounding.ToEven: 2,345 → 2,34, a 2,355 → 2,36. " +
                "Jeśli wymagane jest zaokrąglenie „handlowe” (od połowy w górę), użyj MidpointRounding.AwayFromZero.",
                inv.Syntax, target.Name, phrases);
        }
    }
}
