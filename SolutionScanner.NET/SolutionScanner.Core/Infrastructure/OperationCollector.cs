using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Vb = Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace SolutionScanner.Core.Infrastructure;

/// <summary>
/// Zbiera drzewa operacji (IOperation) z dokumentu. Korzeniami są ciała metod/konstruktorów/akcesorów,
/// wyrażenia „=>” właściwości, inicjalizatory pól i właściwości oraz instrukcje najwyższego poziomu.
/// Wyrażenia lambda i funkcje lokalne są zawarte w drzewach operacji metod, które je zawierają.
/// IOperation jest wspólnym (niezależnym od języka) modelem semantycznym C# i VB.
/// </summary>
public static class OperationCollector
{
    public static IReadOnlyList<IOperation> Collect(SyntaxNode root, SemanticModel model, CancellationToken ct)
    {
        var roots = new List<IOperation>();
        if (root.Language == LanguageNames.CSharp) CollectCSharp(root, model, roots, ct);
        else if (root.Language == LanguageNames.VisualBasic) CollectVisualBasic(root, model, roots, ct);

        var all = new List<IOperation>();
        foreach (var r in roots)
        {
            ct.ThrowIfCancellationRequested();
            all.AddRange(r.DescendantsAndSelf());
        }
        return all;
    }

    private static void CollectCSharp(SyntaxNode root, SemanticModel model, List<IOperation> roots, CancellationToken ct)
    {
        static bool IsRoot(SyntaxNode n) => n is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax
            or ArrowExpressionClauseSyntax or EqualsValueClauseSyntax or GlobalStatementSyntax;

        foreach (var node in root.DescendantNodes(n => n == root || !IsRoot(n)))
        {
            ct.ThrowIfCancellationRequested();
            SyntaxNode? target = node switch
            {
                BaseMethodDeclarationSyntax m when m.Body is not null || m.ExpressionBody is not null => m,
                AccessorDeclarationSyntax a when a.Body is not null || a.ExpressionBody is not null => a,
                ArrowExpressionClauseSyntax arrow when arrow.Parent is PropertyDeclarationSyntax or IndexerDeclarationSyntax => arrow,
                EqualsValueClauseSyntax eq when eq.Parent is VariableDeclaratorSyntax { Parent.Parent: BaseFieldDeclarationSyntax }
                                                  or PropertyDeclarationSyntax or ParameterSyntax => eq,
                GlobalStatementSyntax g => g.Statement,
                _ => null
            };
            if (target is null) continue;
            TryAdd(model, target, roots, ct);
        }
    }

    private static void CollectVisualBasic(SyntaxNode root, SemanticModel model, List<IOperation> roots, CancellationToken ct)
    {
        static bool IsRoot(SyntaxNode n) => n is Vb.MethodBlockBaseSyntax or Vb.EqualsValueSyntax;

        foreach (var node in root.DescendantNodes(n => n == root || !IsRoot(n)))
        {
            ct.ThrowIfCancellationRequested();
            SyntaxNode? target = node switch
            {
                Vb.MethodBlockBaseSyntax m => m,
                Vb.EqualsValueSyntax eq when eq.Parent is Vb.VariableDeclaratorSyntax { Parent: Vb.FieldDeclarationSyntax }
                                              or Vb.PropertyStatementSyntax => eq,
                _ => null
            };
            if (target is null) continue;
            TryAdd(model, target, roots, ct);
        }
    }

    private static void TryAdd(SemanticModel model, SyntaxNode node, List<IOperation> roots, CancellationToken ct)
    {
        try
        {
            var op = model.GetOperation(node, ct);
            if (op is not null) roots.Add(op);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Błąd budowy drzewa operacji dla pojedynczej składowej nie przerywa analizy dokumentu.
        }
    }
}
