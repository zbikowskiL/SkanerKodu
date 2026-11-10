using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SolutionScanner.Core.Models;
using CsSyntax = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SolutionScanner.Core.Infrastructure;

/// <summary>
/// Klasyfikuje sposób użycia symbolu w miejscu odwołania (odczyt, zapis, odczyt i zapis, wywołanie,
/// przekazanie argumentu, zwrócenie wartości).
/// <para>
/// Algorytm wykorzystuje model operacji Roslyn (IOperation), a nie samą składnię:
/// od węzła identyfikatora przechodzimy w górę (maks. 3 poziomy) do operacji odwołującej się do symbolu
/// (ILocalReference/IParameterReference/IFieldReference/IPropertyReference/IInvocation/IObjectCreation),
/// a następnie badamy operację nadrzędną: lewa strona przypisania = zapis, przypisanie złożone/++ = odczyt i zapis,
/// argument ref/out = odczyt i zapis / zapis, argument zwykły = przekazanie argumentu, return = zwrócenie wartości.
/// </para>
/// </summary>
public static class UsageClassifier
{
    public static UsageKind Classify(SemanticModel model, SyntaxNode identifierNode, ISymbol? symbol, CancellationToken ct = default)
    {
        if (symbol is null) return UsageKind.Unknown;

        // nameof(...) i argumenty nazwane (C#)
        if (identifierNode.Parent is CsSyntax.NameColonSyntax) return UsageKind.NamedArgument;
        if (IsInsideNameOf(identifierNode)) return UsageKind.NameOf;

        if (symbol is ITypeSymbol or INamespaceSymbol)
        {
            return IsObjectCreationType(identifierNode) ? UsageKind.ObjectCreation : UsageKind.TypeReference;
        }

        var reference = FindReferencingOperation(model, identifierNode, symbol, ct);
        if (reference is null)
        {
            return symbol is IMethodSymbol ? UsageKind.Invocation : UsageKind.Read;
        }

        if (reference is IInvocationOperation) return UsageKind.Invocation;
        if (reference is IObjectCreationOperation) return UsageKind.ObjectCreation;
        if (reference is IMethodReferenceOperation) return UsageKind.Read;

        return ClassifyValueUsage(reference);
    }

    /// <summary>Klasyfikacja użycia wartości (operacja odwołania do zmiennej/pola/właściwości/parametru).</summary>
    public static UsageKind ClassifyValueUsage(IOperation reference)
    {
        var current = reference;
        var parent = current.Parent;

        // Przejście przez niejawne konwersje i nawiasy – nie zmieniają rodzaju użycia
        while (parent is IConversionOperation { IsImplicit: true } or IParenthesizedOperation)
        {
            current = parent;
            parent = current.Parent;
        }

        switch (parent)
        {
            case ISimpleAssignmentOperation assign when assign.Target == current:
                return UsageKind.Write;
            case ICompoundAssignmentOperation compound when compound.Target == current:
            case ICoalesceAssignmentOperation coalesce when coalesce.Target == current:
            case IIncrementOrDecrementOperation:
                return UsageKind.ReadWrite;
            case IDeconstructionAssignmentOperation deconstruction when IsWithin(current, deconstruction.Target):
                return UsageKind.Write;
            case ITupleOperation tuple when tuple.Parent is IDeconstructionAssignmentOperation d && IsWithin(tuple, d.Target):
                return UsageKind.Write;
            case IArgumentOperation argument:
                return argument.Parameter?.RefKind switch
                {
                    RefKind.Out => UsageKind.Write,
                    RefKind.Ref => UsageKind.ReadWrite,
                    _ => UsageKind.ArgumentPass
                };
            case IReturnOperation:
                return UsageKind.Return;
            case IBlockOperation { IsImplicit: true } block when block.Parent is IAnonymousFunctionOperation or null:
                return UsageKind.Return; // lambda / wyrażenie „=>” zwracające wartość
            case IMemberReferenceOperation or IInvocationOperation:
                return UsageKind.Read; // odczyt jako instancja (np. kredyt.Oprocentowanie.ToString())
        }

        return UsageKind.Read;
    }

    /// <summary>Wyszukuje operację odwołującą się do symbolu, idąc w górę od węzła identyfikatora.</summary>
    public static IOperation? FindReferencingOperation(SemanticModel model, SyntaxNode node, ISymbol symbol, CancellationToken ct = default)
    {
        var current = node;
        for (var depth = 0; depth < 4 && current is not null; depth++)
        {
            IOperation? op;
            try { op = model.GetOperation(current, ct); }
            catch { op = null; }
            if (op is not null && ReferencesSymbol(op, symbol)) return op;
            current = current.Parent;
        }
        return null;
    }

    public static ISymbol? ReferencedSymbol(IOperation op) => op switch
    {
        ILocalReferenceOperation l => l.Local,
        IParameterReferenceOperation p => p.Parameter,
        IFieldReferenceOperation f => f.Field,
        IPropertyReferenceOperation p => p.Property,
        IEventReferenceOperation e => e.Event,
        IMethodReferenceOperation m => m.Method,
        IInvocationOperation i => i.TargetMethod,
        IObjectCreationOperation c => c.Constructor,
        _ => null
    };

    private static bool ReferencesSymbol(IOperation op, ISymbol symbol)
    {
        var referenced = ReferencedSymbol(op);
        if (referenced is null) return false;
        if (SymbolEqualityComparer.Default.Equals(referenced.OriginalDefinition, symbol.OriginalDefinition)) return true;
        // odwołanie do akcesora właściwości / konstruktora typu
        if (symbol is INamedTypeSymbol type && referenced is IMethodSymbol ctor && ctor.MethodKind == MethodKind.Constructor)
            return SymbolEqualityComparer.Default.Equals(ctor.ContainingType.OriginalDefinition, type.OriginalDefinition);
        return referenced is IMethodSymbol { AssociatedSymbol: { } assoc } &&
               SymbolEqualityComparer.Default.Equals(assoc.OriginalDefinition, symbol.OriginalDefinition);
    }

    private static bool IsWithin(IOperation op, IOperation container)
    {
        for (var o = op; o is not null; o = o.Parent)
            if (o == container) return true;
        return false;
    }

    private static bool IsInsideNameOf(SyntaxNode node)
    {
        for (var n = node.Parent; n is not null && n is not CsSyntax.StatementSyntax; n = n.Parent)
        {
            if (n is CsSyntax.InvocationExpressionSyntax inv && inv.Expression is CsSyntax.IdentifierNameSyntax { Identifier.ValueText: "nameof" })
                return true;
        }
        return false;
    }

    private static bool IsObjectCreationType(SyntaxNode node)
    {
        var n = node;
        while (n.Parent is CsSyntax.QualifiedNameSyntax or CsSyntax.GenericNameSyntax or CsSyntax.AliasQualifiedNameSyntax) n = n.Parent;
        return n.Parent is CsSyntax.ObjectCreationExpressionSyntax creation && creation.Type == n;
    }
}
