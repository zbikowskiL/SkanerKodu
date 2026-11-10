using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SolutionScanner.Core.Abstractions;
using SolutionScanner.Core.Infrastructure;
using SolutionScanner.Core.Models;
using SolutionScanner.CSharp.Internal;

namespace SolutionScanner.CSharp;

/// <summary>
/// Analiza konwersji typów liczbowych na modelu operacji Roslyn (IConversionOperation – konwersje jawne i niejawne,
/// również ukryte w przypisaniach i argumentach), wywołań Convert.To*, Decimal.To* oraz Parse/TryParse.
/// Każda konwersja jest oceniana regułami <see cref="ConversionRules"/>; ryzykowne konwersje w kontekście
/// finansowym trafiają do potencjalnych problemów (jako ostrzeżenia, nie błędy).
/// </summary>
public sealed class ConversionAnalyzer : IDocumentAnalyzer
{
    private static readonly HashSet<string> ParseMethods = new(StringComparer.Ordinal) { "Parse", "TryParse" };

    public string Name => "Konwersje";

    public Task AnalyzeAsync(DocumentContext ctx)
    {
        foreach (var op in ctx.Operations)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            switch (op)
            {
                case IConversionOperation conversion:
                    AnalyzeConversion(ctx, conversion);
                    break;
                case IInvocationOperation invocation:
                    AnalyzeInvocation(ctx, invocation);
                    break;
            }
        }
        return Task.CompletedTask;
    }

    private static void AnalyzeConversion(DocumentContext ctx, IConversionOperation conversion)
    {
        var from = conversion.Operand.Type;
        var to = conversion.Type;
        if (from is null || to is null) return;
        if (!RoslynHelpers.IsNumeric(from) || !RoslynHelpers.IsNumeric(to)) return;
        if (SymbolEqualityComparer.Default.Equals(from, to) || conversion.Conversion.IsIdentity) return;
        if (SymbolEqualityComparer.Default.Equals(RoslynHelpers.Underlying(from), RoslynHelpers.Underlying(to))) return; // T → T? (opakowanie)

        var target = OperationContext.GetTarget(conversion, ctx.Model);
        var phrases = OperationContext.Phrases(ctx, conversion, target);
        if (!ctx.InScope(phrases)) return;

        var assessment = ConversionRules.Assess(from, to);
        var isConstant = conversion.Operand.ConstantValue.HasValue;
        var kind = conversion.IsImplicit ? ConversionKind.Implicit : ConversionKind.ExplicitCast;
        var expression = TextUtil.Shorten(conversion.Syntax.ToString(), 200);

        ctx.Result.Conversions.Add(new ConversionInfo
        {
            Phrase = DocumentContext.JoinPhrases(phrases),
            Kind = kind,
            FromType = RoslynHelpers.DisplayType(from),
            ToType = RoslynHelpers.DisplayType(to),
            Method = conversion.IsImplicit ? "niejawna" : conversion.IsChecked ? "rzutowanie (checked)" : "rzutowanie",
            Expression = expression,
            IsImplicit = conversion.IsImplicit,
            IsConstant = isConstant,
            IsPotentialPrecisionLoss = assessment.PrecisionLoss && !isConstant,
            IsPotentialOverflow = assessment.Overflow && !isConstant,
            Description = assessment.Description + (isConstant ? " Operand jest stałą – kompilator weryfikuje wartość." : string.Empty),
            ContainingMember = ctx.EnclosingMember(conversion.Syntax.SpanStart),
            TargetName = target.Name,
            TargetSymbolKey = target.Key,
            Language = ctx.LanguageLabel,
            CodeLine = ctx.CodeLine(conversion.Syntax),
            Location = ctx.Loc(conversion.Syntax)
        });

        if (isConstant) return;
        ReportProblems(ctx, conversion, from, to, assessment, target, phrases, kind == ConversionKind.ExplicitCast);
    }

    private static void ReportProblems(DocumentContext ctx, IOperation operation, ITypeSymbol from, ITypeSymbol to,
        ConversionAssessment assessment, TargetInfo target, IReadOnlyList<string> phrases, bool isExplicit)
    {
        var financial = OperationContext.IsFinancial(ctx, operation, target);
        var f = RoslynHelpers.Underlying(from)!.SpecialType;
        var t = RoslynHelpers.Underlying(to)!.SpecialType;
        var fromName = RoslynHelpers.DisplayType(from);
        var toName = RoslynHelpers.DisplayType(to);

        if (f == SpecialType.System_Decimal && t is SpecialType.System_Double or SpecialType.System_Single)
        {
            OperationContext.AddProblem(ctx, "CNV001", financial ? Severity.Warning : Severity.Info, "Konwersje",
                $"Konwersja decimal → {toName}", assessment.Description + (financial ? " Wartość w kontekście finansowym." : string.Empty),
                operation.Syntax, target.Name, phrases);
        }
        else if (f is SpecialType.System_Double or SpecialType.System_Single && t == SpecialType.System_Decimal)
        {
            OperationContext.AddProblem(ctx, "CNV002", financial ? Severity.Warning : Severity.Info, "Konwersje",
                $"Konwersja {fromName} → decimal", assessment.Description + " Wartość mogła już wcześniej utracić dokładność w typie binarnym.",
                operation.Syntax, target.Name, phrases);
        }
        else if (assessment.Overflow && isExplicit)
        {
            OperationContext.AddProblem(ctx, "OVF001", Severity.Warning, "Przepełnienie",
                $"Możliwe przepełnienie lub obcięcie: {fromName} → {toName}", assessment.Description,
                operation.Syntax, target.Name, phrases);
        }
        else if (assessment.PrecisionLoss)
        {
            OperationContext.AddProblem(ctx, "PRC001", Severity.Info, "Utrata precyzji",
                $"Możliwa utrata precyzji: {fromName} → {toName}", assessment.Description,
                operation.Syntax, target.Name, phrases);
        }
    }

    private static void AnalyzeInvocation(DocumentContext ctx, IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        var typeName = method.ContainingType?.ToDisplayString(RoslynHelpers.ClrFormat) ?? string.Empty;
        ConversionKind kind;
        if (typeName == "System.Convert" && method.Name.StartsWith("To", StringComparison.Ordinal))
            kind = ConversionKind.ConvertMethod;
        else if (typeName == "System.Decimal" && method.Name.StartsWith("To", StringComparison.Ordinal) && method.Name != "ToString")
            kind = ConversionKind.DecimalMethod;
        else if (ParseMethods.Contains(method.Name) && RoslynHelpers.IsNumeric(method.ContainingType))
            kind = ConversionKind.Parse;
        else
            return;

        var valueArg = invocation.Arguments.OrderBy(a => a.Parameter?.Ordinal ?? 0).FirstOrDefault();
        var from = valueArg is null ? null : OperationContext.UnwrapImplicit(valueArg.Value).Type;
        var to = kind == ConversionKind.Parse ? method.ContainingType : method.ReturnType;
        if (from is null || to is null) return;
        if (kind != ConversionKind.Parse && from.SpecialType != SpecialType.System_String && !RoslynHelpers.IsNumeric(from)) return;
        if (!RoslynHelpers.IsNumeric(to)) return;

        var target = OperationContext.GetTarget(invocation, ctx.Model);
        if (kind == ConversionKind.Parse && method.Name == "TryParse")
        {
            var outArg = invocation.Arguments.FirstOrDefault(a => a.Parameter?.RefKind == RefKind.Out);
            if (outArg is not null && UsageClassifier.ReferencedSymbol(OperationContext.UnwrapImplicit(outArg.Value)) is { } outSymbol)
                target = new TargetInfo(outSymbol, outSymbol.Name, RoslynHelpers.SymbolKey(outSymbol));
        }
        var phrases = OperationContext.Phrases(ctx, invocation, target);
        if (!ctx.InScope(phrases)) return;

        var fromString = from.SpecialType == SpecialType.System_String;
        var assessment = fromString
            ? new ConversionAssessment(false, true, $"string → {RoslynHelpers.DisplayType(to)}: wynik zależy od formatu tekstu; FormatException/OverflowException dla niepoprawnych danych.")
            : ConversionRules.Assess(from, to);
        var note = ConversionRules.MethodNote(typeName, method.Name, from);
        var hasCulture = invocation.Arguments.Any(a => a.Parameter?.Type.Name is "IFormatProvider" or "CultureInfo" or "NumberFormatInfo");

        ctx.Result.Conversions.Add(new ConversionInfo
        {
            Phrase = DocumentContext.JoinPhrases(phrases),
            Kind = kind,
            FromType = RoslynHelpers.DisplayType(from),
            ToType = RoslynHelpers.DisplayType(to),
            Method = $"{method.ContainingType!.Name}.{method.Name}",
            Expression = TextUtil.Shorten(invocation.Syntax.ToString(), 200),
            IsImplicit = false,
            IsConstant = valueArg!.Value.ConstantValue.HasValue,
            IsPotentialPrecisionLoss = assessment.PrecisionLoss,
            IsPotentialOverflow = assessment.Overflow,
            Description = string.Join(" ", new[] { assessment.Description, note, fromString && !hasCulture ? "Brak IFormatProvider – parsowanie wg kultury bieżącego wątku." : string.Empty }
                .Where(s => s.Length > 0)),
            ContainingMember = ctx.EnclosingMember(invocation.Syntax.SpanStart),
            TargetName = target.Name,
            TargetSymbolKey = target.Key,
            Language = ctx.LanguageLabel,
            CodeLine = ctx.CodeLine(invocation.Syntax),
            Location = ctx.Loc(invocation.Syntax)
        });

        if (valueArg.Value.ConstantValue.HasValue) return;
        if (fromString)
        {
            if (!hasCulture)
            {
                OperationContext.AddProblem(ctx, "CUL001", OperationContext.IsFinancial(ctx, invocation, target) ? Severity.Warning : Severity.Info, "Konwersje",
                    $"{method.ContainingType.Name}.{method.Name} bez IFormatProvider",
                    "Parsowanie liczby z tekstu zależy od kultury bieżącego wątku (np. „1,5” vs „1.5”). Na serwerze z inną kulturą wynik może być inny lub wystąpi FormatException. " +
                    "Rozważ CultureInfo.InvariantCulture lub jawną kulturę.",
                    invocation.Syntax, target.Name, phrases);
            }
            return;
        }

        if (note.Contains("ZAOKRĄGLA", StringComparison.Ordinal))
        {
            OperationContext.AddProblem(ctx, "CNV003", Severity.Info, "Konwersje", $"{method.ContainingType.Name}.{method.Name} zaokrągla wartość", note,
                invocation.Syntax, target.Name, phrases);
        }
        ReportProblems(ctx, invocation, from, to, assessment, target, phrases, isExplicit: true);
    }
}
