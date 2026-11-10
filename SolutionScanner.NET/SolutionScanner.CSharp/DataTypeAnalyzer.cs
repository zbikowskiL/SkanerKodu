using System.Globalization;
using Microsoft.CodeAnalysis;
using SolutionScanner.Core.Abstractions;
using SolutionScanner.Core.Infrastructure;
using SolutionScanner.Core.Models;
using SolutionScanner.CSharp.Internal;
using CsSyntax = Microsoft.CodeAnalysis.CSharp.Syntax;
using VbSyntax = Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace SolutionScanner.CSharp;

/// <summary>
/// Analiza deklaracji zmiennych lokalnych, pól, właściwości, parametrów i stałych:
/// typ (również wynikający z <c>var</c> – ustalony przez kompilator), pełna nazwa CLR, nullable,
/// modyfikatory, zakres widoczności, wartość początkowa (jeśli jest stałą czasu kompilacji).
/// Dla typów decimal/double/float dodaje opis reprezentacji (moduł precyzji).
/// </summary>
public sealed class DataTypeAnalyzer : IDocumentAnalyzer
{
    public string Name => "Typy danych";

    public Task AnalyzeAsync(DocumentContext ctx)
    {
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var node in ctx.Root.DescendantNodes())
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            if (!IsDeclarationNode(node)) continue;

            ISymbol? symbol;
            try { symbol = ctx.Model.GetDeclaredSymbol(node, ctx.CancellationToken); }
            catch { continue; }
            if (symbol is not (ILocalSymbol or IFieldSymbol or IPropertySymbol or IParameterSymbol)) continue;
            if (symbol.IsImplicitlyDeclared || !seen.Add(symbol)) continue;
            if (symbol is IParameterSymbol { ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove } }) continue;

            var phrases = ctx.Matcher.Match(symbol.Name);
            if (!ctx.InScope(phrases)) continue;

            var info = Describe(ctx, node, symbol, phrases);
            ctx.Result.Declarations.Add(info);
            AddRepresentationInfo(ctx, symbol, info, phrases);
            CheckFinancialFloatingPoint(ctx, node, symbol, phrases);
        }
        return Task.CompletedTask;
    }

    private static bool IsDeclarationNode(SyntaxNode node) => node is
        CsSyntax.VariableDeclaratorSyntax or CsSyntax.ParameterSyntax or CsSyntax.PropertyDeclarationSyntax
        or CsSyntax.SingleVariableDesignationSyntax or CsSyntax.ForEachStatementSyntax or CsSyntax.CatchDeclarationSyntax
        or CsSyntax.EnumMemberDeclarationSyntax
        or VbSyntax.ModifiedIdentifierSyntax or VbSyntax.PropertyStatementSyntax or VbSyntax.EnumMemberDeclarationSyntax;

    internal static VariableDeclarationInfo Describe(DocumentContext ctx, SyntaxNode node, ISymbol symbol, IReadOnlyList<string> phrases)
    {
        var type = RoslynHelpers.TypeOf(symbol);
        var (isNullable, nullDescription) = DescribeNullability(type);
        var member = RoslynHelpers.MemberName(symbol.ContainingSymbol);
        var containingType = symbol.ContainingType?.ToDisplayString(RoslynHelpers.QualifiedNameFormat) ?? string.Empty;
        var kind = RoslynHelpers.KindOf(symbol);
        var named = RoslynHelpers.Underlying(type) as INamedTypeSymbol;

        var scope = symbol switch
        {
            ILocalSymbol => $"Lokalna w {member}",
            IParameterSymbol => $"Parametr {member}",
            _ => (symbol.IsStatic ? "Statyczna składowa typu " : "Składowa instancji typu ") + containingType
        };

        return new VariableDeclarationInfo
        {
            Phrase = DocumentContext.JoinPhrases(phrases),
            Name = symbol.Name,
            Kind = kind,
            TypeName = RoslynHelpers.DisplayType(type),
            FullTypeName = RoslynHelpers.ClrName(type),
            TypeCategory = RoslynHelpers.TypeCategory(type),
            IsNullable = isNullable,
            NullabilityDescription = nullDescription,
            Accessibility = symbol is ILocalSymbol or IParameterSymbol ? "-" : RoslynHelpers.AccessibilityText(symbol.DeclaredAccessibility),
            Scope = scope,
            ContainingType = containingType,
            ContainingMember = symbol is ILocalSymbol or IParameterSymbol ? member : string.Empty,
            InitialValue = InitialValue(ctx, node, symbol),
            IsConst = symbol is ILocalSymbol { IsConst: true } or IFieldSymbol { IsConst: true },
            IsStatic = symbol.IsStatic,
            IsReadOnly = symbol is IFieldSymbol { IsReadOnly: true } or IPropertySymbol { IsReadOnly: true }
                         || symbol is IParameterSymbol { RefKind: RefKind.In },
            IsVar = IsImplicitlyTyped(node),
            IsGeneric = named is { IsGenericType: true },
            GenericArguments = named is { IsGenericType: true } ? string.Join(", ", named.TypeArguments.Select(RoslynHelpers.DisplayType)) : string.Empty,
            Language = ctx.LanguageLabel,
            Location = ctx.Loc(symbol.Locations.FirstOrDefault(l => l.IsInSource && l.SourceTree == ctx.Root.SyntaxTree)?.SourceSpan ?? node.Span),
            SymbolKey = RoslynHelpers.SymbolKey(symbol)
        };
    }

    internal static (bool? IsNullable, string Description) DescribeNullability(ITypeSymbol? type)
    {
        if (type is null) return (null, "Typ nieustalony");
        if (type.TypeKind == TypeKind.Error) return (null, "Typ nierozpoznany (błąd kompilacji / brak referencji)");
        if (RoslynHelpers.IsNullableValueType(type)) return (true, "Nullable<T> – dopuszcza null");
        if (type.IsValueType) return (false, "Typ wartościowy – nie dopuszcza null");
        if (type is ITypeParameterSymbol) return (null, "Parametr typu – zależy od argumentu generycznego");
        return type.NullableAnnotation switch
        {
            NullableAnnotation.Annotated => (true, "Typ referencyjny z adnotacją ? – dopuszcza null"),
            NullableAnnotation.NotAnnotated => (false, "Typ referencyjny bez adnotacji (kontekst nullable włączony)"),
            _ => (null, "Typ referencyjny – kontekst nullable wyłączony, może przyjąć null")
        };
    }

    private static bool IsImplicitlyTyped(SyntaxNode node) => node switch
    {
        CsSyntax.VariableDeclaratorSyntax { Parent: CsSyntax.VariableDeclarationSyntax decl } => decl.Type.IsVar,
        CsSyntax.SingleVariableDesignationSyntax d => d.Parent is CsSyntax.DeclarationExpressionSyntax { Type.IsVar: true }
                                                     || d.Parent?.Parent is CsSyntax.DeclarationExpressionSyntax { Type.IsVar: true }
                                                     || d.Parent is CsSyntax.VarPatternSyntax,
        CsSyntax.ForEachStatementSyntax f => f.Type.IsVar,
        VbSyntax.ModifiedIdentifierSyntax { Parent: VbSyntax.VariableDeclaratorSyntax v } => v.AsClause is null,
        _ => false
    };

    /// <summary>Wartość początkowa: stała czasu kompilacji (dokładna wartość) albo tekst wyrażenia.</summary>
    private static string InitialValue(DocumentContext ctx, SyntaxNode node, ISymbol symbol)
    {
        if (symbol is IFieldSymbol { HasConstantValue: true } f) return FormatConstant(f.ConstantValue);
        if (symbol is ILocalSymbol { HasConstantValue: true } l) return FormatConstant(l.ConstantValue);

        SyntaxNode? valueExpression = node switch
        {
            CsSyntax.VariableDeclaratorSyntax v => v.Initializer?.Value,
            CsSyntax.PropertyDeclarationSyntax p => p.Initializer?.Value,
            CsSyntax.ParameterSyntax p => p.Default?.Value,
            CsSyntax.EnumMemberDeclarationSyntax e => e.EqualsValue?.Value,
            VbSyntax.ModifiedIdentifierSyntax { Parent: VbSyntax.VariableDeclaratorSyntax v } => v.Initializer?.Value,
            VbSyntax.ModifiedIdentifierSyntax { Parent: VbSyntax.ParameterSyntax p } => p.Default?.Value,
            VbSyntax.PropertyStatementSyntax p => p.Initializer?.Value,
            _ => null
        };
        if (valueExpression is null)
        {
            if (symbol is IParameterSymbol { HasExplicitDefaultValue: true } ps) return FormatConstant(ps.ExplicitDefaultValue);
            return string.Empty;
        }

        try
        {
            var constant = ctx.Model.GetConstantValue(valueExpression, ctx.CancellationToken);
            if (constant.HasValue) return FormatConstant(constant.Value);
        }
        catch
        {
            // brak wartości stałej – zwracamy tekst wyrażenia
        }
        return "wyrażenie: " + TextUtil.Shorten(valueExpression.ToString(), 150);
    }

    /// <summary>Formatuje stałą niezależnie od kultury; decimal zachowuje skalę (5.2500 pozostaje 5.2500).</summary>
    internal static string FormatConstant(object? value) => value switch
    {
        null => "null",
        string s => "\"" + s + "\"",
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        char c => "'" + c + "'",
        IFormattable fmt => fmt.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static void AddRepresentationInfo(DocumentContext ctx, ISymbol symbol, VariableDeclarationInfo info, IReadOnlyList<string> phrases)
    {
        var type = RoslynHelpers.Underlying(RoslynHelpers.TypeOf(symbol));
        string? description = type?.SpecialType switch
        {
            SpecialType.System_Decimal =>
                "decimal: 128-bitowy typ dziesiętny, 28–29 cyfr znaczących, skala 0–28 zależna od wartości (zachowuje zera końcowe). " +
                "Typ nie ma stałej liczby miejsc po przecinku – liczba miejsc wynika z wartości, zaokrągleń i mapowania SQL.",
            SpecialType.System_Double =>
                "double: binarny IEEE 754 (64 bity), ok. 15–17 cyfr znaczących. Liczba miejsc po przecinku nie jest cechą typu; " +
                "większość ułamków dziesiętnych (np. 0,1) jest reprezentowana w przybliżeniu.",
            SpecialType.System_Single =>
                "float: binarny IEEE 754 (32 bity), ok. 6–9 cyfr znaczących. Liczba miejsc po przecinku nie jest cechą typu; " +
                "większość ułamków dziesiętnych jest reprezentowana w przybliżeniu.",
            _ => null
        };
        if (description is null) return;

        ctx.Result.Precision.Add(new PrecisionInfo
        {
            Phrase = DocumentContext.JoinPhrases(phrases),
            Source = PrecisionSource.TypeRepresentation,
            ElementName = symbol.Name,
            TypeName = info.TypeName,
            ValueText = info.InitialValue,
            Precision = type!.SpecialType switch
            {
                SpecialType.System_Decimal => 28,
                SpecialType.System_Double => 15,
                _ => 6
            },
            Description = description + " (Precyzja = minimalna liczba gwarantowanych cyfr znaczących.)",
            Language = ctx.LanguageLabel,
            CodeLine = ctx.CodeLine(info.Location.SpanStart >= 0 ? new Microsoft.CodeAnalysis.Text.TextSpan(info.Location.SpanStart, info.Location.SpanLength) : default),
            TargetSymbolKey = info.SymbolKey,
            Location = info.Location
        });
    }

    /// <summary>Reguła: float/double w deklaracji o nazwie wskazującej na wartość finansową (WARNING, nie błąd).</summary>
    private static void CheckFinancialFloatingPoint(DocumentContext ctx, SyntaxNode node, ISymbol symbol, IReadOnlyList<string> phrases)
    {
        var type = RoslynHelpers.TypeOf(symbol);
        if (!RoslynHelpers.IsBinaryFloatingPoint(type) || !ctx.Run.IsFinancialName(symbol.Name)) return;
        OperationContext.AddProblem(ctx, "FIN001", Severity.Warning, "Typ danych",
            $"{RoslynHelpers.DisplayType(type)} dla wartości finansowej „{symbol.Name}”",
            $"Nazwa „{symbol.Name}” wskazuje na wartość finansową, a typ {RoslynHelpers.DisplayType(type)} jest binarnym typem zmiennoprzecinkowym " +
            "(np. 0,1 + 0,2 ≠ 0,3). Dla kwot i stóp procentowych zwykle stosuje się decimal. Zweryfikuj, czy przybliżenie jest akceptowalne.",
            node, RoslynHelpers.QualifiedName(symbol), phrases);
    }
}
