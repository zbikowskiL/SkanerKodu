using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using SolutionScanner.Core.Abstractions;
using SolutionScanner.Core.Infrastructure;
using SolutionScanner.Core.Models;

namespace SolutionScanner.Core;

/// <summary>
/// Wyszukuje rzeczywiste odwołania do zadeklarowanych symboli, których nazwy pasują do fraz,
/// za pomocą <see cref="SymbolFinder.FindReferencesAsync(ISymbol, Solution, IImmutableSet{Document}?, CancellationToken)"/>.
/// Odwołania są wyszukiwane w dokumentach wybranych projektów, w całej solucji (również między projektami).
/// Każde odwołanie jest klasyfikowane przez <see cref="UsageClassifier"/> na podstawie modelu operacji.
/// </summary>
public sealed class SymbolAnalyzer
{
    public async Task FindReferencesAsync(AnalysisRunContext ctx, IImmutableSet<Document> scope,
        IProgress<AnalysisProgress>? progress, CancellationToken ct)
    {
        var solution = ctx.Solution;
        if (solution is null) return;

        var symbols = ctx.Symbols.All
            .OrderBy(s => s.Symbol is ILocalSymbol or IParameterSymbol ? 1 : 0) // najpierw składowe typów
            .ThenBy(s => s.Key, StringComparer.Ordinal)
            .ToList();

        if (symbols.Count > ctx.Settings.MaxSymbolsForReferenceSearch)
        {
            ctx.AddLimitation(LimitationKind.ReferenceSearchLimit, string.Empty,
                $"Liczba dopasowanych symboli ({symbols.Count}) przekracza limit {ctx.Settings.MaxSymbolsForReferenceSearch}. " +
                "Odwołania wyszukano tylko dla pierwszych symboli (składowe typów mają pierwszeństwo przed zmiennymi lokalnymi). " +
                "Zawęź frazy lub zwiększ limit w ustawieniach.");
            symbols = symbols.Take(ctx.Settings.MaxSymbolsForReferenceSearch).ToList();
        }

        var done = 0;
        var total = symbols.Count;
        var options = new ParallelOptions { MaxDegreeOfParallelism = ctx.Settings.MaxDegreeOfParallelism, CancellationToken = ct };
        var semanticCache = new ConcurrentDictionary<DocumentId, Task<SemanticModel?>>();

        await Parallel.ForEachAsync(symbols, options, async (matched, token) =>
        {
            try
            {
                await FindForSymbolAsync(ctx, solution, scope, matched, semanticCache, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ctx.Logger.Error($"Wyszukiwanie odwołań: {matched.Symbol.Name}", ex);
            }

            var n = Interlocked.Increment(ref done);
            if (n % 20 == 0 || n == total)
                progress?.Report(new AnalysisProgress("Odwołania", $"Odwołania do symboli: {n}/{total}", matched.ProjectName, string.Empty, n, total, total == 0 ? 100 : n * 100.0 / total));
        }).ConfigureAwait(false);
    }

    private static async Task FindForSymbolAsync(AnalysisRunContext ctx, Solution solution, IImmutableSet<Document> scope,
        MatchedSymbol matched, ConcurrentDictionary<DocumentId, Task<SemanticModel?>> semanticCache, CancellationToken ct)
    {
        var symbol = matched.Symbol;
        var phrases = DocumentContext.JoinPhrases(matched.Phrases);
        var fullName = RoslynHelpers.QualifiedName(symbol);
        var kind = RoslynHelpers.KindOf(symbol);

        // Rekord deklaracji
        ctx.Result.References.Add(new SymbolReferenceInfo
        {
            Phrase = phrases,
            SymbolName = symbol.Name,
            SymbolFullName = fullName,
            ElementKind = kind,
            UsageKind = UsageKind.Declaration,
            ContainingMember = RoslynHelpers.MemberName(symbol.ContainingSymbol),
            CodeLine = await LineAsync(solution, matched.Declaration, ct).ConfigureAwait(false),
            DeclaringProject = matched.ProjectName,
            DeclarationLocation = matched.Declaration,
            Location = matched.Declaration,
            SymbolKey = matched.Key
        });

        var referenced = await SymbolFinder.FindReferencesAsync(symbol, solution, scope, ct).ConfigureAwait(false);
        var seen = new HashSet<(string, int)>();
        foreach (var rs in referenced)
        {
            foreach (var location in rs.Locations)
            {
                ct.ThrowIfCancellationRequested();
                if (!location.Location.IsInSource) continue;
                var doc = location.Document;
                if (!seen.Add((doc.FilePath ?? doc.Name, location.Location.SourceSpan.Start))) continue;

                var model = await semanticCache.GetOrAdd(doc.Id, _ => doc.GetSemanticModelAsync(ct)!).ConfigureAwait(false);
                var root = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                var usage = UsageKind.Unknown;
                var member = string.Empty;
                var codeLine = string.Empty;
                if (model is not null && root is not null)
                {
                    var token = root.FindToken(location.Location.SourceSpan.Start);
                    var node = token.Parent;
                    if (node is not null)
                    {
                        usage = UsageClassifier.Classify(model, node, symbol, ct);
                        member = RoslynHelpers.MemberName(model.GetEnclosingSymbol(token.SpanStart, ct));
                    }
                    var text = await doc.GetTextAsync(ct).ConfigureAwait(false);
                    codeLine = TextUtil.Shorten(text.Lines.GetLineFromPosition(location.Location.SourceSpan.Start).ToString().Trim());
                }

                var projectName = RoslynHelpers.CleanProjectName(doc.Project.Name);
                ctx.Result.References.Add(new SymbolReferenceInfo
                {
                    Phrase = phrases,
                    SymbolName = symbol.Name,
                    SymbolFullName = fullName,
                    ElementKind = kind,
                    UsageKind = usage,
                    ContainingMember = member,
                    CodeLine = codeLine,
                    DeclaringProject = matched.ProjectName,
                    DeclarationLocation = matched.Declaration,
                    IsCandidate = location.IsCandidateLocation,
                    IsImplicit = location.IsImplicit,
                    Note = location.IsCandidateLocation ? $"Lokalizacja kandydująca ({location.CandidateReason})" :
                           SymbolEqualityComparer.Default.Equals(rs.Definition.OriginalDefinition, symbol.OriginalDefinition) ? string.Empty :
                           $"Przez powiązany symbol: {rs.Definition.ToDisplayString()}",
                    Location = LocationFactory.FromLocation(ctx, location.Location, projectName),
                    SymbolKey = matched.Key
                });
            }
        }
    }

    private static async Task<string> LineAsync(Solution solution, CodeLocation location, CancellationToken ct)
    {
        if (location.IsEmpty) return string.Empty;
        var docId = solution.GetDocumentIdsWithFilePath(location.FilePath).FirstOrDefault();
        if (docId is null) return string.Empty;
        var doc = solution.GetDocument(docId);
        if (doc is null) return string.Empty;
        var text = await doc.GetTextAsync(ct).ConfigureAwait(false);
        if (location.Line < 1 || location.Line > text.Lines.Count) return string.Empty;
        return TextUtil.Shorten(text.Lines[location.Line - 1].ToString().Trim());
    }
}
