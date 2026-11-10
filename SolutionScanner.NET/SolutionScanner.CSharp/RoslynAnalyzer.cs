using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SolutionScanner.Core.Abstractions;
using SolutionScanner.Core.Infrastructure;
using SolutionScanner.Core.Models;
using Cs = Microsoft.CodeAnalysis.CSharp;
using CsSyntax = Microsoft.CodeAnalysis.CSharp.Syntax;
using Vb = Microsoft.CodeAnalysis.VisualBasic;

namespace SolutionScanner.CSharp;

/// <summary>
/// Główny analizator dokumentów Roslyn (C# i VB):
/// <list type="bullet">
/// <item>wyszukuje frazy w identyfikatorach – przechodzi po tokenach drzewa składniowego bez wchodzenia w trivia,
/// dzięki czemu komentarze (//, /* */, XML doc) są pomijane, a literały tekstowe nie są identyfikatorami;</item>
/// <item>każdy identyfikator jest rozpoznawany semantycznie (GetDeclaredSymbol / GetSymbolInfo) i klasyfikowany
/// (deklaracja, odczyt, zapis, wywołanie, przekazanie argumentu, zwrócenie wartości);</item>
/// <item>rejestruje dopasowane symbole do późniejszego wyszukiwania odwołań (SymbolFinder) i przepływu danych;</item>
/// <item>wykrywa miejsca ograniczające analizę statyczną: refleksję, dynamic, dynamiczny SQL.</item>
/// </list>
/// </summary>
public sealed class RoslynAnalyzer : IDocumentAnalyzer
{
    private static readonly HashSet<string> ReflectionMethods = new(StringComparer.Ordinal)
    {
        "GetProperty", "GetField", "GetMethod", "GetMember", "InvokeMember", "GetProperties", "GetFields", "GetMethods"
    };

    private static readonly HashSet<string> SqlExecutionMethods = new(StringComparer.Ordinal)
    {
        "SqlQuery", "ExecuteSqlCommand", "ExecuteSqlCommandAsync", "SqlQueryRaw", "FromSqlRaw", "ExecuteSqlRaw",
        "ExecuteSqlRawAsync", "Query", "QueryAsync", "Execute", "ExecuteAsync", "QueryFirstOrDefault", "QuerySingle"
    };

    public string Name => "Wyszukiwanie i symbole (Roslyn)";

    public Task AnalyzeAsync(DocumentContext ctx)
    {
        if (ctx.Matcher.HasPhrases) SearchIdentifiers(ctx);
        DetectLimitations(ctx);
        return Task.CompletedTask;
    }

    private static bool IsIdentifier(SyntaxToken token) =>
        token.Language == LanguageNames.CSharp
            ? token.RawKind == (int)Cs.SyntaxKind.IdentifierToken
            : token.RawKind == (int)Vb.SyntaxKind.IdentifierToken;

    private static void SearchIdentifiers(DocumentContext ctx)
    {
        foreach (var token in ctx.Root.DescendantTokens(descendIntoTrivia: false))
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            if (!IsIdentifier(token)) continue;
            var text = token.ValueText;
            var phrases = ctx.Matcher.Match(text);
            if (phrases.Count == 0) continue;
            if (token.Parent is CsSyntax.IdentifierNameSyntax { IsVar: true }) continue;
            if (token.Parent is null) continue;

            var (symbol, isDeclaration, method) = Resolve(ctx, token);
            var usage = isDeclaration ? UsageKind.Declaration : UsageClassifier.Classify(ctx.Model, token.Parent, symbol, ctx.CancellationToken);
            var location = ctx.Loc(token);

            if (symbol is not null && symbol.Locations.Any(l => l.IsInSource) && symbol.Kind is SymbolKind.Field or SymbolKind.Property
                    or SymbolKind.Local or SymbolKind.Parameter or SymbolKind.Method or SymbolKind.NamedType or SymbolKind.Event)
            {
                var symbolPhrases = ctx.Matcher.Match(symbol.Name);
                if (symbolPhrases.Count > 0)
                {
                    var declaration = isDeclaration ? location : LocationFactory.DeclarationOf(ctx.Run, symbol, ctx.Run.Solution);
                    ctx.Run.Symbols.Register(symbol.OriginalDefinition, RoslynHelpers.SymbolKey(symbol), symbolPhrases,
                        string.IsNullOrEmpty(declaration.ProjectName) ? ctx.ProjectName : declaration.ProjectName, declaration);
                }
            }

            var note = ctx.HasReliableSemantics ? string.Empty : "Semantyka przybliżona – projekt bez pełnych referencji";
            foreach (var phrase in phrases)
            {
                ctx.Result.SearchHits.Add(new SearchHit
                {
                    Phrase = phrase,
                    MatchedText = text,
                    ElementKind = RoslynHelpers.KindOf(symbol),
                    UsageKind = usage,
                    Language = ctx.LanguageLabel,
                    Method = method,
                    SymbolName = symbol is null ? text : RoslynHelpers.QualifiedName(symbol),
                    SymbolType = RoslynHelpers.DisplayType(symbol as ITypeSymbol is null ? RoslynHelpers.TypeOf(symbol) : null),
                    ContainingMember = ctx.EnclosingMember(token.SpanStart),
                    CodeLine = ctx.CodeLine(token.Span),
                    Note = note,
                    Location = location
                });
            }
        }
    }

    /// <summary>
    /// Rozpoznaje symbol identyfikatora. Deklaracja jest uznawana tylko wtedy, gdy lokalizacja zadeklarowanego symbolu
    /// pokrywa się z tokenem (zapobiega to uznaniu np. typu zwracanego metody za jej deklarację).
    /// </summary>
    private static (ISymbol? Symbol, bool IsDeclaration, AnalysisMethod Method) Resolve(DocumentContext ctx, SyntaxToken token)
    {
        var node = token.Parent!;
        try
        {
            var declared = ctx.Model.GetDeclaredSymbol(node, ctx.CancellationToken);
            if (declared is not null && declared.Locations.Any(l => l.IsInSource && l.SourceSpan == token.Span && l.SourceTree == token.SyntaxTree))
                return (declared, true, AnalysisMethod.Semantic);

            var info = ctx.Model.GetSymbolInfo(node, ctx.CancellationToken);
            if (info.Symbol is not null) return (info.Symbol, false, AnalysisMethod.Semantic);
            if (info.CandidateSymbols.Length > 0) return (info.CandidateSymbols[0], false, AnalysisMethod.SemanticCandidate);
        }
        catch (Exception) when (!ctx.CancellationToken.IsCancellationRequested)
        {
            // pojedynczy węzeł, którego nie da się powiązać semantycznie – wynik składniowy
        }
        return (null, IsSyntacticDeclaration(node, token), AnalysisMethod.SyntaxOnly);
    }

    private static bool IsSyntacticDeclaration(SyntaxNode node, SyntaxToken token) => node switch
    {
        CsSyntax.VariableDeclaratorSyntax v => v.Identifier == token,
        CsSyntax.ParameterSyntax p => p.Identifier == token,
        CsSyntax.PropertyDeclarationSyntax p => p.Identifier == token,
        CsSyntax.MethodDeclarationSyntax m => m.Identifier == token,
        CsSyntax.BaseTypeDeclarationSyntax t => t.Identifier == token,
        _ => false
    };

    /// <summary>Wykrywa refleksję, użycie dynamic oraz dynamicznie budowany SQL – miejsca, gdzie analiza statyczna jest niepełna.</summary>
    private static void DetectLimitations(DocumentContext ctx)
    {
        var dynamicReported = 0;
        foreach (var op in ctx.Operations)
        {
            switch (op)
            {
                case IInvocationOperation inv:
                {
                    var m = inv.TargetMethod;
                    var typeName = m.ContainingType?.ToDisplayString() ?? string.Empty;
                    if (typeName is "System.Type" or "System.Reflection.TypeInfo" && ReflectionMethods.Contains(m.Name))
                    {
                        var name = inv.Arguments.Select(a => a.Value.ConstantValue).FirstOrDefault(c => c.HasValue && c.Value is string).Value as string ?? string.Empty;
                        ctx.Run.AddLimitation(LimitationKind.Reflection, ctx.ProjectName,
                            $"Refleksja: {m.Name}({(name.Length > 0 ? $"\"{name}\"" : "…")}) – odwołania przez refleksję nie są wykrywane przez SymbolFinder.",
                            ctx.Loc(op.Syntax), name);
                    }
                    else if (typeName == "System.Activator" || (m.Name is "GetValue" or "SetValue" && typeName is "System.Reflection.PropertyInfo" or "System.Reflection.FieldInfo"))
                    {
                        ctx.Run.AddLimitation(LimitationKind.Reflection, ctx.ProjectName, $"Refleksja: {typeName}.{m.Name} – dostęp do składowych w czasie wykonania.", ctx.Loc(op.Syntax));
                    }
                    else if (SqlExecutionMethods.Contains(m.Name) && inv.Arguments.FirstOrDefault(a => a.Value.Type?.SpecialType == SpecialType.System_String) is { } sqlArg)
                    {
                        if (!sqlArg.Value.ConstantValue.HasValue)
                        {
                            ctx.Run.AddLimitation(LimitationKind.DynamicSql, ctx.ProjectName,
                                $"Dynamiczny SQL: {m.Name} z tekstem zapytania niebędącym stałą – zapytanie nie może zostać przeanalizowane statycznie.",
                                ctx.Loc(op.Syntax));
                        }
                    }
                    break;
                }
                case IObjectCreationOperation creation when creation.Constructor?.ContainingType?.Name is "SqlCommand" or "OleDbCommand" or "OdbcCommand" or "SqlDataAdapter":
                {
                    var first = creation.Arguments.FirstOrDefault();
                    if (first is not null && first.Value.Type?.SpecialType == SpecialType.System_String && !first.Value.ConstantValue.HasValue)
                        ctx.Run.AddLimitation(LimitationKind.DynamicSql, ctx.ProjectName,
                            $"Dynamiczny SQL: {creation.Constructor.ContainingType.Name} z tekstem niebędącym stałą.", ctx.Loc(op.Syntax));
                    break;
                }
                case ISimpleAssignmentOperation { Target: IPropertyReferenceOperation { Property.Name: "CommandText" } } assign when !assign.Value.ConstantValue.HasValue:
                    ctx.Run.AddLimitation(LimitationKind.DynamicSql, ctx.ProjectName,
                        "Dynamiczny SQL: CommandText przypisany z wartości niebędącej stałą.", ctx.Loc(op.Syntax));
                    break;
                case IDynamicInvocationOperation or IDynamicMemberReferenceOperation or IDynamicObjectCreationOperation or IDynamicIndexerAccessOperation:
                    if (dynamicReported++ < 20)
                        ctx.Run.AddLimitation(LimitationKind.Dynamic, ctx.ProjectName,
                            "Użycie typu dynamic – wiązanie w czasie wykonania, symbol nie jest znany statycznie.", ctx.Loc(op.Syntax));
                    break;
            }
        }
    }
}
