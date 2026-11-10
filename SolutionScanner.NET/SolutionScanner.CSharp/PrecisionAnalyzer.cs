using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SolutionScanner.Core.Abstractions;
using SolutionScanner.Core.Infrastructure;
using SolutionScanner.Core.Models;
using SolutionScanner.CSharp.Internal;
using Cs = Microsoft.CodeAnalysis.CSharp;
using Vb = Microsoft.CodeAnalysis.VisualBasic;

namespace SolutionScanner.CSharp;

/// <summary>
/// Analiza precyzji i liczby miejsc po przecinku. Rozróżnia źródła informacji:
/// <list type="bullet">
/// <item><b>Literał</b> – liczba miejsc zapisana w kodzie (5.2500m → 4). Dla decimal jest to również skala wartości
/// (kompilator zachowuje zera końcowe), ale NIE maksymalna precyzja typu. Dla float/double liczba miejsc w literale
/// nie jest precyzją reprezentacji binarnej – dodatkowo sprawdzamy, czy wartość ma dokładną reprezentację binarną.</item>
/// <item><b>Skala wartości decimal</b> – dla stałych czasu kompilacji wyznaczana z wartości (decimal.GetBits).</item>
/// <item><b>Formatowanie</b> – ToString("N2"/"F4"/"0.00"), interpolacja {x:N2}, string.Format("{0:0.0000}", x).</item>
/// </list>
/// Zaokrąglenia (Math.Round itd.) analizuje <see cref="RoundingAnalyzer"/>, typy SQL – moduł SQL.
/// </summary>
public sealed class PrecisionAnalyzer : IDocumentAnalyzer
{
    public string Name => "Precyzja";

    public Task AnalyzeAsync(DocumentContext ctx)
    {
        AnalyzeLiterals(ctx);
        AnalyzeDecimalConstants(ctx);
        AnalyzeFormatting(ctx);
        return Task.CompletedTask;
    }

    private static bool IsNumericLiteral(SyntaxToken token) => token.Language == LanguageNames.CSharp
        ? token.RawKind == (int)Cs.SyntaxKind.NumericLiteralToken
        : token.RawKind is (int)Vb.SyntaxKind.FloatingLiteralToken or (int)Vb.SyntaxKind.DecimalLiteralToken or (int)Vb.SyntaxKind.IntegerLiteralToken;

    private static void AnalyzeLiterals(DocumentContext ctx)
    {
        foreach (var token in ctx.Root.DescendantTokens(descendIntoTrivia: false))
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            if (!IsNumericLiteral(token) || token.Parent is null) continue;

            var typeInfo = ctx.Model.GetTypeInfo(token.Parent, ctx.CancellationToken);
            var natural = typeInfo.Type;
            var converted = typeInfo.ConvertedType ?? natural;
            var effective = RoslynHelpers.Underlying(converted);
            var isDecimal = RoslynHelpers.IsDecimal(effective);
            var isBinary = RoslynHelpers.IsBinaryFloatingPoint(effective);
            if (!isDecimal && !isBinary) continue; // literały całkowite w kontekście całkowitym nie niosą informacji o miejscach

            var parsed = NumericText.ParseLiteral(token.Text);
            if (parsed is null) continue;

            var op = ctx.Model.GetOperation(token.Parent, ctx.CancellationToken);
            var target = op is null ? TargetInfo.None : OperationContext.GetTarget(op, ctx.Model);
            var phrases = op is null ? ctx.Phrases(new[] { target.Name }) : OperationContext.Phrases(ctx, op, target);
            if (!ctx.InScope(phrases)) continue;

            var literal = parsed.Value;
            string description;
            int? scale = null;
            if (isDecimal)
            {
                if (token.Value is decimal dv) scale = NumericText.DecimalScale(dv);
                else scale = literal.DecimalPlaces;
                description = $"Literał decimal: {literal.DecimalPlaces} miejsc po przecinku zapisanych w kodzie" +
                              (scale.HasValue ? $"; skala wartości decimal = {scale}" : string.Empty) +
                              ". To nie jest maksymalna precyzja typu decimal (28–29 cyfr znaczących)." +
                              (!RoslynHelpers.IsDecimal(natural) ? $" Literał typu {RoslynHelpers.DisplayType(natural)} konwertowany do decimal." : string.Empty);
            }
            else
            {
                var bits = RoslynHelpers.Underlying(effective)!.SpecialType == SpecialType.System_Single ? 24 : 53;
                var exact = NumericText.IsExactlyRepresentable(literal, bits);
                var binaryValue = BinaryValue(token, bits);
                description = $"Literał {RoslynHelpers.DisplayType(effective)}: {literal.DecimalPlaces} miejsc zapisanych w kodzie – NIE jest to precyzja " +
                              $"reprezentacji binarnej. Wartość {(exact ? "ma dokładną" : "NIE ma dokładnej")} reprezentacji binarnej" +
                              (exact || binaryValue.Length == 0 ? "." : $" (faktycznie przechowywana: {binaryValue}).");
            }

            ctx.Result.Precision.Add(new PrecisionInfo
            {
                Phrase = DocumentContext.JoinPhrases(phrases),
                Source = PrecisionSource.Literal,
                ElementName = target.Name,
                TypeName = RoslynHelpers.DisplayType(converted),
                ValueText = token.Text,
                DecimalPlaces = literal.DecimalPlaces,
                Scale = scale,
                Description = description,
                Language = ctx.LanguageLabel,
                CodeLine = ctx.CodeLine(token.Span),
                TargetSymbolKey = target.Key,
                Location = ctx.Loc(token)
            });
        }
    }

    /// <summary>Wartość faktycznie przechowywana w double/float zapisana z 17 (9) cyframi znaczącymi.</summary>
    private static string BinaryValue(SyntaxToken token, int bits)
    {
        try
        {
            var value = Convert.ToDouble(token.Value, CultureInfo.InvariantCulture);
            return bits == 24
                ? ((float)value).ToString("G9", CultureInfo.InvariantCulture)
                : value.ToString("G17", CultureInfo.InvariantCulture);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>Skala decimal dla inicjalizatorów będących stałymi wyrażeniami (np. const decimal X = 1.10m * 2).</summary>
    private static void AnalyzeDecimalConstants(DocumentContext ctx)
    {
        foreach (var op in ctx.Operations)
        {
            IOperation? value;
            ISymbol? target;
            switch (op)
            {
                case IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator } vi:
                    value = vi.Value; target = declarator.Symbol; break;
                case IFieldInitializerOperation fi when fi.InitializedFields.Length > 0:
                    value = fi.Value; target = fi.InitializedFields[0]; break;
                case IPropertyInitializerOperation pi when pi.InitializedProperties.Length > 0:
                    value = pi.Value; target = pi.InitializedProperties[0]; break;
                default:
                    continue;
            }
            if (value is null || !RoslynHelpers.IsDecimal(value.Type) || !value.ConstantValue.HasValue) continue;
            if (OperationContext.UnwrapImplicit(value) is ILiteralOperation) continue; // pojedynczy literał – opisany wyżej
            if (value.ConstantValue.Value is not decimal d) continue;

            var phrases = ctx.Phrases(OperationContext.ReferencedNames(value).Append(target.Name));
            if (!ctx.InScope(phrases)) continue;
            var scale = NumericText.DecimalScale(d);
            ctx.Result.Precision.Add(new PrecisionInfo
            {
                Phrase = DocumentContext.JoinPhrases(phrases),
                Source = PrecisionSource.DecimalValueScale,
                ElementName = target.Name,
                TypeName = "decimal",
                ValueText = d.ToString(CultureInfo.InvariantCulture),
                DecimalPlaces = scale,
                Scale = scale,
                Description = $"Stała czasu kompilacji typu decimal – skala wartości ustalona statycznie: {scale}. " +
                              "Skala wyniku działań na decimal zależy od operandów (np. mnożenie sumuje skale).",
                Language = ctx.LanguageLabel,
                CodeLine = ctx.CodeLine(value.Syntax),
                TargetSymbolKey = RoslynHelpers.SymbolKey(target),
                Location = ctx.Loc(value.Syntax)
            });
        }
    }

    private static void AnalyzeFormatting(DocumentContext ctx)
    {
        foreach (var op in ctx.Operations)
        {
            switch (op)
            {
                // x.ToString("N2")
                case IInvocationOperation { TargetMethod.Name: "ToString", Instance: { } instance } inv
                    when RoslynHelpers.IsNumeric(instance.Type):
                {
                    var formatArg = inv.Arguments.FirstOrDefault(a => a.Parameter?.Type.SpecialType == SpecialType.System_String);
                    if (formatArg?.Value.ConstantValue is { HasValue: true, Value: string format })
                        Record(ctx, inv, instance, format, $"{instance.Syntax}.ToString(\"{format}\")");
                    break;
                }
                // $"{x:N2}"
                case IInterpolationOperation { FormatString: { } fs } interpolation
                    when fs.ConstantValue is { HasValue: true, Value: string format } && RoslynHelpers.IsNumeric(OperationContext.UnwrapImplicit(interpolation.Expression).Type):
                    Record(ctx, interpolation, OperationContext.UnwrapImplicit(interpolation.Expression), format, $"{{{interpolation.Expression.Syntax}:{format}}}");
                    break;
                // string.Format("{0:N2}", x), StringBuilder.AppendFormat, Console.WriteLine("{0:0.00}", x)
                case IInvocationOperation inv when inv.TargetMethod.Name is "Format" or "AppendFormat" or "WriteLine" or "Write":
                    AnalyzeComposite(ctx, inv);
                    break;
            }
        }
    }

    private static void AnalyzeComposite(DocumentContext ctx, IInvocationOperation inv)
    {
        var args = inv.Arguments.OrderBy(a => a.Parameter?.Ordinal ?? 0).ToList();
        var formatIndex = args.FindIndex(a => a.Parameter?.Name == "format" && a.Value.ConstantValue is { HasValue: true, Value: string });
        if (formatIndex < 0) return;
        var composite = (string)args[formatIndex].Value.ConstantValue.Value!;

        // Argumenty formatowane: kolejne parametry lub tablica params
        var values = new List<IOperation>();
        foreach (var a in args.Skip(formatIndex + 1))
        {
            if (a.ArgumentKind == ArgumentKind.ParamArray && a.Value is IArrayCreationOperation { Initializer: { } init })
                values.AddRange(init.ElementValues);
            else
                values.Add(a.Value);
        }

        foreach (var (index, format) in NumericText.ParseCompositeFormat(composite))
        {
            if (index >= values.Count) continue;
            var value = OperationContext.UnwrapImplicit(values[index]);
            if (!RoslynHelpers.IsNumeric(value.Type)) continue;
            Record(ctx, inv, value, format, $"{{{index}:{format}}} → {value.Syntax}");
        }
    }

    private static void Record(DocumentContext ctx, IOperation formattingOperation, IOperation value, string format, string expression)
    {
        var names = OperationContext.ReferencedNames(value).ToList();
        var phrases = ctx.Phrases(names);
        if (!ctx.InScope(phrases)) return;
        var (places, description) = NumericText.ParseFormat(format);
        var element = names.FirstOrDefault() ?? string.Empty;
        ctx.Result.Precision.Add(new PrecisionInfo
        {
            Phrase = DocumentContext.JoinPhrases(phrases),
            Source = PrecisionSource.Formatting,
            ElementName = element,
            TypeName = RoslynHelpers.DisplayType(value.Type),
            ValueText = TextUtil.Shorten(expression, 200),
            DecimalPlaces = places,
            Description = description + " Formatowanie wpływa wyłącznie na prezentację – nie zmienia przechowywanej wartości.",
            Language = ctx.LanguageLabel,
            CodeLine = ctx.CodeLine(formattingOperation.Syntax),
            TargetSymbolKey = UsageClassifier.ReferencedSymbol(value) is { } s ? RoslynHelpers.SymbolKey(s) : string.Empty,
            Location = ctx.Loc(formattingOperation.Syntax)
        });
    }
}
