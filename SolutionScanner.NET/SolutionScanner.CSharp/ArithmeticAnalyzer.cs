using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SolutionScanner.Core.Abstractions;
using SolutionScanner.Core.Infrastructure;
using SolutionScanner.Core.Models;
using SolutionScanner.CSharp.Internal;
using SolutionScanner.CSharp;

namespace SolutionScanner.CSharp;

/// <summary>
/// Analiza operacji arytmetycznych (+, −, ×, ÷, %) na typach liczbowych:
/// dzielenie całkowitoliczbowe (szczególnie przed konwersją do decimal/double), potencjalne przepełnienia
/// (mnożenie/dodawanie w typie int przed rozszerzeniem do long, kontekst unchecked) oraz obliczenia
/// na float/double w kontekście finansowym.
/// </summary>
public sealed class ArithmeticAnalyzer : IDocumentAnalyzer
{
    public string Name => "Obliczenia";

    public Task AnalyzeAsync(DocumentContext ctx)
    {
        foreach (var op in ctx.Operations)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            if (op is not IBinaryOperation binary) continue;
            var symbol = binary.OperatorKind switch
            {
                BinaryOperatorKind.Add => "+",
                BinaryOperatorKind.Subtract => "-",
                BinaryOperatorKind.Multiply => "*",
                BinaryOperatorKind.Divide => "/",
                BinaryOperatorKind.Remainder => "%",
                _ => null
            };
            if (symbol is null || !RoslynHelpers.IsNumeric(binary.Type)) continue;
            Analyze(ctx, binary, symbol);
        }
        return Task.CompletedTask;
    }

    private static void Analyze(DocumentContext ctx, IBinaryOperation binary, string op)
    {
        var target = OperationContext.GetTarget(binary, ctx.Model);
        var phrases = OperationContext.Phrases(ctx, binary, target);
        if (!ctx.InScope(phrases)) return;

        var left = OperationContext.UnwrapImplicit(binary.LeftOperand);
        var right = OperationContext.UnwrapImplicit(binary.RightOperand);
        var resultType = binary.Type!;
        var isIntegerDivision = binary.OperatorKind == BinaryOperatorKind.Divide && RoslynHelpers.IsIntegral(resultType);
        var constant = binary.ConstantValue.HasValue ? DataTypeAnalyzer.FormatConstant(binary.ConstantValue.Value) : string.Empty;

        // Konwersja wyniku do szerszego / ułamkowego typu (np. int/int → decimal, int*int → long)
        var parentConversion = binary.Parent as IConversionOperation;
        var convertedTo = parentConversion?.Type;
        var notes = new List<string>();

        if (isIntegerDivision)
        {
            notes.Add("Dzielenie całkowitoliczbowe – część ułamkowa jest odrzucana.");
            var fractionalTarget = convertedTo is not null && (RoslynHelpers.IsDecimal(convertedTo) || RoslynHelpers.IsBinaryFloatingPoint(convertedTo));
            var lossyConstant = binary.ConstantValue.HasValue && left.ConstantValue.HasValue && right.ConstantValue.HasValue
                                && Convert.ToDecimal(right.ConstantValue.Value) != 0
                                && Convert.ToDecimal(left.ConstantValue.Value) % Convert.ToDecimal(right.ConstantValue.Value) != 0;
            if (fractionalTarget || lossyConstant)
            {
                OperationContext.AddProblem(ctx, "DIV001", Severity.Warning, "Dzielenie całkowite",
                    fractionalTarget
                        ? $"Dzielenie całkowite przed konwersją do {RoslynHelpers.DisplayType(convertedTo)}"
                        : "Dzielenie całkowite stałych z resztą",
                    $"Wyrażenie „{TextUtil.Shorten(binary.Syntax.ToString(), 120)}” jest obliczane w typie {RoslynHelpers.DisplayType(resultType)} " +
                    "(np. 5 / 2 = 2), a dopiero wynik jest konwertowany. Jeśli oczekiwany jest wynik ułamkowy, rzutuj operand przed dzieleniem (np. (decimal)a / b).",
                    binary.Syntax, target.Name, phrases);
            }
            else if (OperationContext.IsFinancial(ctx, binary, target))
            {
                OperationContext.AddProblem(ctx, "DIV002", Severity.Info, "Dzielenie całkowite",
                    "Dzielenie całkowite w obliczeniach finansowych",
                    "Dzielenie w typie całkowitym odrzuca część ułamkową. Zweryfikuj, czy jest to zamierzone.",
                    binary.Syntax, target.Name, phrases);
            }
        }

        if (binary.OperatorKind is BinaryOperatorKind.Multiply or BinaryOperatorKind.Add && RoslynHelpers.IsIntegral(resultType)
            && !binary.IsChecked && !binary.ConstantValue.HasValue)
        {
            if (convertedTo is not null && RoslynHelpers.IntegralBits(convertedTo) > RoslynHelpers.IntegralBits(resultType)
                || convertedTo is not null && (RoslynHelpers.IsDecimal(convertedTo) || RoslynHelpers.IsBinaryFloatingPoint(convertedTo)))
            {
                notes.Add("Obliczenie w węższym typie przed rozszerzeniem.");
                OperationContext.AddProblem(ctx, "OVF002", Severity.Warning, "Przepełnienie",
                    $"Możliwe przepełnienie: {op} w typie {RoslynHelpers.DisplayType(resultType)} przed konwersją do {RoslynHelpers.DisplayType(convertedTo)}",
                    $"Wynik „{TextUtil.Shorten(binary.Syntax.ToString(), 120)}” jest obliczany w typie {RoslynHelpers.DisplayType(resultType)} (kontekst unchecked – przepełnienie bez wyjątku), " +
                    $"a dopiero potem rozszerzany. Rozszerz operand przed działaniem, np. (long)a * b.",
                    binary.Syntax, target.Name, phrases);
            }
            else
            {
                notes.Add("Kontekst unchecked – przepełnienie nie zgłasza wyjątku.");
            }
        }

        // Problem zgłaszany raz dla całego wyrażenia (najwyższej operacji zmiennoprzecinkowej)
        var isFloating = RoslynHelpers.IsBinaryFloatingPoint(resultType);
        var isTopFloating = isFloating && !(OperationContext.UnwrapParentheses(binary.Parent) is IBinaryOperation pb && RoslynHelpers.IsBinaryFloatingPoint(pb.Type));
        if (isTopFloating)
        {
            if (OperationContext.IsFinancial(ctx, binary, target))
            {
                OperationContext.AddProblem(ctx, "FIN002", Severity.Warning, "Typ danych",
                    $"Obliczenia na {RoslynHelpers.DisplayType(resultType)} w kontekście finansowym",
                    $"Wyrażenie „{TextUtil.Shorten(binary.Syntax.ToString(), 120)}” jest liczone w binarnej arytmetyce zmiennoprzecinkowej " +
                    "(błędy zaokrągleń kumulują się, np. 0,1 + 0,2 = 0,30000000000000004). Zweryfikuj, czy nie powinien zostać użyty decimal.",
                    binary.Syntax, target.Name, phrases);
            }
        }

        if (RoslynHelpers.IsDecimal(resultType) && binary.OperatorKind is BinaryOperatorKind.Multiply or BinaryOperatorKind.Divide)
            notes.Add(binary.OperatorKind == BinaryOperatorKind.Multiply
                ? "decimal: skala iloczynu = suma skal operandów (maks. 28)."
                : "decimal: iloraz może mieć do 28 miejsc po przecinku – rozważ jawne zaokrąglenie.");

        ctx.Result.Arithmetic.Add(new ArithmeticInfo
        {
            Phrase = DocumentContext.JoinPhrases(phrases),
            Operator = op,
            LeftType = RoslynHelpers.DisplayType(left.Type),
            RightType = RoslynHelpers.DisplayType(right.Type),
            ResultType = RoslynHelpers.DisplayType(resultType),
            Expression = TextUtil.Shorten(binary.Syntax.ToString(), 200),
            IsIntegerDivision = isIntegerDivision,
            IsChecked = binary.IsChecked,
            ConstantValue = constant,
            Notes = string.Join(" ", notes),
            ContainingMember = ctx.EnclosingMember(binary.Syntax.SpanStart),
            TargetName = target.Name,
            Language = ctx.LanguageLabel,
            CodeLine = ctx.CodeLine(binary.Syntax),
            Location = ctx.Loc(binary.Syntax)
        });
    }
}
