using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SolutionScanner.Core.Abstractions;
using SolutionScanner.Core.Infrastructure;
using SolutionScanner.Core.Models;

namespace SolutionScanner.CSharp.Internal;

/// <summary>Symbol, do którego trafia wartość wyrażenia (cel przypisania, inicjalizacji, parametr, wartość zwracana).</summary>
internal readonly record struct TargetInfo(ISymbol? Symbol, string Name, string Key)
{
    public static TargetInfo None => new(null, string.Empty, string.Empty);
}

/// <summary>
/// Pomocnicze operacje na drzewie IOperation: ustalanie celu wartości, nazw użytych w wyrażeniu
/// i fraz/kontekstu finansowego. Wspólne dla analizatorów precyzji, zaokrągleń i konwersji.
/// </summary>
internal static class OperationContext
{
    /// <summary>
    /// Ustala symbol, który otrzymuje wartość wyrażenia: idzie w górę drzewa operacji aż do przypisania,
    /// deklaracji zmiennej, inicjalizatora pola/właściwości, argumentu metody lub instrukcji return.
    /// </summary>
    public static TargetInfo GetTarget(IOperation operation, SemanticModel model)
    {
        IOperation current = operation;
        for (var parent = operation.Parent; parent is not null; current = parent, parent = parent.Parent)
        {
            switch (parent)
            {
                case ISimpleAssignmentOperation a when a.Value == current:
                    return FromReference(a.Target);
                case ICompoundAssignmentOperation c when c.Value == current:
                    return FromReference(c.Target);
                case IVariableDeclaratorOperation d:
                    return Make(d.Symbol);
                case IFieldInitializerOperation f when f.InitializedFields.Length > 0:
                    return Make(f.InitializedFields[0]);
                case IPropertyInitializerOperation p when p.InitializedProperties.Length > 0:
                    return Make(p.InitializedProperties[0]);
                case IArgumentOperation { Parameter: { } param }:
                    return new TargetInfo(param, $"{param.ContainingSymbol?.Name}({param.Name})", RoslynHelpers.SymbolKey(param));
                case IReturnOperation:
                    var method = model.GetEnclosingSymbol(parent.Syntax.SpanStart);
                    while (method is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction }) method = method.ContainingSymbol;
                    if (method is IMethodSymbol { AssociatedSymbol: { } assoc }) method = assoc;
                    return method is null ? TargetInfo.None : new TargetInfo(method, "return " + method.Name, RoslynHelpers.SymbolKey(method));
                case IExpressionStatementOperation or IBlockOperation:
                    return TargetInfo.None;
            }
        }
        return TargetInfo.None;
    }

    private static TargetInfo FromReference(IOperation target)
    {
        var symbol = UsageClassifier.ReferencedSymbol(target);
        return symbol is null ? TargetInfo.None : Make(symbol);
    }

    private static TargetInfo Make(ISymbol symbol) => new(symbol, symbol.Name, RoslynHelpers.SymbolKey(symbol));

    /// <summary>Nazwy symboli użytych w wyrażeniu (zmienne, pola, właściwości, parametry, wywoływane metody).</summary>
    public static IEnumerable<string> ReferencedNames(IOperation operation)
    {
        foreach (var op in operation.DescendantsAndSelf())
        {
            var s = UsageClassifier.ReferencedSymbol(op);
            if (s is not null) yield return s.Name;
        }
    }

    /// <summary>Najwyższe wyrażenie zawierające operację (przed instrukcją / deklaracją / inicjalizatorem).</summary>
    public static IOperation TopExpression(IOperation operation)
    {
        var current = operation;
        while (current.Parent is { } p && p is not (IExpressionStatementOperation or IVariableDeclaratorOperation or IVariableInitializerOperation
                   or IReturnOperation or IBlockOperation or IFieldInitializerOperation or IPropertyInitializerOperation
                   or IParameterInitializerOperation or IArgumentOperation { Parent: IObjectCreationOperation }))
        {
            current = p;
        }
        return current;
    }

    /// <summary>Frazy dopasowane do celu oraz do nazw użytych w całym wyrażeniu.</summary>
    public static IReadOnlyList<string> Phrases(DocumentContext ctx, IOperation operation, TargetInfo target)
    {
        var top = TopExpression(operation);
        return ctx.Phrases(ReferencedNames(top).Append(target.Name).Append(target.Symbol?.Name));
    }

    /// <summary>Czy wyrażenie dotyczy wartości finansowej (fraza użytkownika lub słowo kluczowe finansowe).</summary>
    public static bool IsFinancial(DocumentContext ctx, IOperation operation, TargetInfo target)
    {
        if (ctx.Run.IsFinancialName(target.Symbol?.Name)) return true;
        return ReferencedNames(TopExpression(operation)).Any(ctx.Run.IsFinancialName);
    }

    /// <summary>Pomija nawiasy i niejawne konwersje idąc w górę drzewa.</summary>
    public static IOperation? UnwrapParentheses(IOperation? op)
    {
        while (op is IParenthesizedOperation or IConversionOperation { IsImplicit: true }) op = op.Parent;
        return op;
    }

    public static IOperation UnwrapImplicit(IOperation op)
    {
        while (op is IConversionOperation { IsImplicit: true } c) op = c.Operand;
        return op;
    }

    public static void AddProblem(DocumentContext ctx, string ruleId, Severity severity, string category, string title,
        string justification, SyntaxNode node, string symbol, IReadOnlyList<string> phrases)
    {
        ctx.Result.Problems.Add(new PotentialProblem
        {
            RuleId = ruleId,
            Severity = severity,
            Category = category,
            Title = title,
            Justification = justification,
            SymbolName = symbol,
            Phrase = DocumentContext.JoinPhrases(phrases),
            CodeLine = ctx.CodeLine(node),
            Location = ctx.Loc(node)
        });
    }
}
