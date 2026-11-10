using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Operations;
using SolutionScanner.Core.Abstractions;
using SolutionScanner.Core.Infrastructure;
using SolutionScanner.Core.Models;

namespace SolutionScanner.Core;

/// <summary>
/// Analiza przepływu danych dla pól i właściwości pasujących do fraz.
/// <para>
/// Dla każdego symbolu źródłowego budowany jest łańcuch kroków: deklaracja → odczyty/zapisy (SymbolFinder) →
/// konwersje i zaokrąglenia w tych samych wyrażeniach → przekazanie jako argument do metody (parametr jest
/// śledzony dalej, do <see cref="AnalysisSettings.DataFlowMaxDepth"/> poziomów) → przypisanie do zmiennej lokalnej
/// (zmienna jest śledzona dalej) → mapowanie EF6 → kolumna SQL.
/// </para>
/// <para>
/// Ograniczenia (oznaczane jako „przepływ niepełny”): refleksja, typ dynamic, dynamiczny SQL, wartości zwracane
/// z metod (wywołujący nie są śledzeni), przekroczenie głębokości, nieustalone mapowanie EF6.
/// Powiązania niepotwierdzone semantycznie mają pewność „Prawdopodobne” lub „Niepotwierdzone”.
/// </para>
/// </summary>
public sealed class DataFlowAnalyzer : IPostAnalyzer
{
    public string Name => "Przepływ danych";
    public int Order => 200;

    public async Task RunAsync(AnalysisRunContext ctx, CancellationToken ct)
    {
        if (!ctx.Settings.AnalyzeDataFlow || ctx.Solution is null) return;

        var roots = ctx.Symbols.All
            .Where(s => s.Symbol is IFieldSymbol { IsConst: false } or IPropertySymbol)
            .OrderBy(s => s.Key, StringComparer.Ordinal)
            .ToList();
        if (roots.Count > ctx.Settings.MaxDataFlows)
        {
            ctx.AddLimitation(LimitationKind.ReferenceSearchLimit, string.Empty,
                $"Przepływ danych przeanalizowano dla {ctx.Settings.MaxDataFlows} z {roots.Count} pól/właściwości (limit).");
            roots = roots.Take(ctx.Settings.MaxDataFlows).ToList();
        }

        var state = new FlowState(ctx);
        var index = 0;
        foreach (var root in roots)
        {
            ct.ThrowIfCancellationRequested();
            index++;
            try
            {
                await BuildFlowAsync(state, root, $"F{index:00000}", ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ctx.Logger.Error($"Przepływ danych dla {root.Symbol.Name}", ex);
            }
        }
    }

    private async Task BuildFlowAsync(FlowState state, MatchedSymbol root, string flowId, CancellationToken ct)
    {
        var ctx = state.Ctx;
        var flow = new FlowBuilder(flowId, RoslynHelpers.QualifiedName(root.Symbol), DocumentContext.JoinPhrases(root.Phrases));

        flow.Add(0, "Deklaracja", $"{RoslynHelpers.DisplayType(RoslynHelpers.TypeOf(root.Symbol))} {RoslynHelpers.QualifiedName(root.Symbol)}",
            Certainty.Confirmed, root.Declaration, await state.LineAsync(root.Declaration, ct).ConfigureAwait(false));

        if (!ctx.Settings.FindSymbolReferences)
            flow.Reasons.Add("Wyszukiwanie odwołań jest wyłączone w ustawieniach.");

        var references = state.ReferencesOf(root.Key);
        var visited = new HashSet<string>(StringComparer.Ordinal) { root.Key };
        foreach (var r in references)
        {
            ct.ThrowIfCancellationRequested();
            await AddUsageStepAsync(state, flow, r.UsageKind, r.Location, r.CodeLine, r.ContainingMember, r.IsCandidate, 0, visited, ct).ConfigureAwait(false);
        }

        // Mapowanie EF6 i kolumna SQL
        var mappings = ctx.Result.EfMappings.Where(m => m.PropertySymbolKey == root.Key && m.MappingKind == "Właściwość").ToList();
        foreach (var m in mappings)
        {
            var certainty = m.Status switch
            {
                MappingStatus.Explicit => Certainty.Confirmed,
                MappingStatus.Convention => Certainty.Probable,
                _ => Certainty.Unconfirmed
            };
            flow.Add(0, "Mapowanie EF6",
                $"{m.EntityClass}.{m.PropertyName} → {(string.IsNullOrEmpty(m.TableName) ? "?" : m.QualifiedTableName)}.{(string.IsNullOrEmpty(m.ColumnName) ? "?" : m.ColumnName)} " +
                $"({DisplayNames.Of(m.Status)}; {m.Evidence})", certainty,
                m.ConfigurationLocation.IsEmpty ? m.Location : m.ConfigurationLocation, string.Empty);
            if (m.Status == MappingStatus.Undetermined) flow.Reasons.Add("Mapowanie EF6 nieustalone.");
            if (m.IsNotMapped) flow.Reasons.Add("Właściwość oznaczona jako niemapowana ([NotMapped]/Ignore).");
        }

        var comparisons = ctx.Result.Comparisons.Where(c => c.PropertySymbolKey == root.Key).ToList();
        foreach (var c in comparisons)
        {
            if (c.SqlLocation.IsEmpty)
            {
                flow.Reasons.Add("Nie znaleziono definicji kolumny w plikach SQL solucji (nie oznacza to, że kolumna nie istnieje w bazie).");
                continue;
            }
            var certainty = c.MappingStatus == MappingStatus.Explicit ? Certainty.Confirmed
                : c.MappingStatus == MappingStatus.Convention ? Certainty.Probable : Certainty.Unconfirmed;
            flow.Add(0, "Kolumna SQL", $"{c.SqlTable}.{c.SqlColumn} {c.SqlType} – porównanie: {DisplayNames.Of(c.Outcome)} ({c.Category})",
                certainty, c.SqlLocation, await state.LineAsync(c.SqlLocation, ct).ConfigureAwait(false));
        }
        if (mappings.Count > 0 && state.HasDynamicSql)
            flow.Reasons.Add("W solucji wykryto dynamiczny SQL – możliwe niewykryte odczyty/zapisy kolumny.");

        // Refleksja / dynamic
        if (state.ReflectionNames.Contains(root.Symbol.Name))
            flow.Reasons.Add($"Nazwa „{root.Symbol.Name}” występuje w wywołaniu refleksji – możliwe dodatkowe, niewykryte odwołania.");
        if (state.DynamicProjects.Contains(root.ProjectName))
            flow.Reasons.Add("W projekcie używany jest typ dynamic – odwołania dynamiczne nie są wykrywane statycznie.");

        foreach (var step in flow.Build()) ctx.Result.DataFlow.Add(step);
    }

    private async Task AddUsageStepAsync(FlowState state, FlowBuilder flow, UsageKind usage, CodeLocation location, string codeLine,
        string member, bool isCandidate, int depth, HashSet<string> visited, CancellationToken ct)
    {
        var certainty = isCandidate ? Certainty.Probable : Certainty.Confirmed;
        if (usage == UsageKind.Declaration) return;
        flow.Add(depth, DisplayNames.Of(usage), string.IsNullOrEmpty(member) ? DisplayNames.Of(usage) : $"{DisplayNames.Of(usage)} w {member}",
            certainty, location, codeLine);

        // Konwersje i zaokrąglenia obejmujące to odwołanie
        foreach (var conv in state.ConversionsContaining(location))
            flow.Add(depth, "Konwersja", $"{conv.FromType} → {conv.ToType} ({DisplayNames.Of(conv.Kind)}){(conv.IsPotentialPrecisionLoss ? " – możliwa utrata precyzji" : string.Empty)}",
                Certainty.Confirmed, conv.Location, conv.CodeLine);
        foreach (var round in state.RoundingsContaining(location))
            flow.Add(depth, "Zaokrąglenie", $"{round.Method}, miejsca: {(round.Digits?.ToString() ?? round.DigitsExpression)}, tryb: {round.MidpointMode}",
                Certainty.Confirmed, round.Location, round.CodeLine);

        if (usage == UsageKind.Return)
            flow.Reasons.Add("Wartość zwracana z metody – miejsca wywołania metody nie są śledzone dalej.");

        if (usage is not (UsageKind.ArgumentPass or UsageKind.Read or UsageKind.ReadWrite)) return;

        var next = await state.ResolveNextSymbolAsync(location, usage, ct).ConfigureAwait(false);
        if (next is null) return;
        var (symbol, description, inSource) = next.Value;

        if (!inSource)
        {
            flow.Add(depth + 1, "Przekazanie poza kod źródłowy", description, Certainty.Confirmed, location, codeLine);
            return;
        }

        var key = RoslynHelpers.SymbolKey(symbol);
        if (!visited.Add(key)) return;
        if (depth + 1 > state.Ctx.Settings.DataFlowMaxDepth)
        {
            flow.Reasons.Add($"Osiągnięto maksymalną głębokość śledzenia ({state.Ctx.Settings.DataFlowMaxDepth}).");
            return;
        }

        var declaration = LocationFactory.DeclarationOf(state.Ctx, symbol, state.Ctx.Solution);
        flow.Add(depth + 1, symbol is IParameterSymbol ? "Parametr" : "Zmienna pośrednia", description, Certainty.Confirmed, declaration,
            await state.LineAsync(declaration, ct).ConfigureAwait(false));

        var refs = await state.FindReferencesAsync(symbol, ct).ConfigureAwait(false);
        foreach (var (refUsage, refLocation, refLine, refMember) in refs)
            await AddUsageStepAsync(state, flow, refUsage, refLocation, refLine, refMember, false, depth + 1, visited, ct).ConfigureAwait(false);
    }

    /// <summary>Gromadzi kroki jednego przepływu; status kompletności jest ustalany na końcu.</summary>
    private sealed class FlowBuilder
    {
        private readonly List<(int Depth, string Kind, string Description, Certainty Certainty, CodeLocation Location, string Line)> _steps = new();
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

        public FlowBuilder(string flowId, string rootSymbol, string phrase)
        {
            FlowId = flowId;
            RootSymbol = rootSymbol;
            Phrase = phrase;
        }

        public string FlowId { get; }
        public string RootSymbol { get; }
        public string Phrase { get; }
        public SortedSet<string> Reasons { get; } = new(StringComparer.Ordinal);

        public void Add(int depth, string kind, string description, Certainty certainty, CodeLocation location, string line)
        {
            var key = $"{kind}|{location.FilePath}|{location.SpanStart}|{location.Line}|{description}";
            if (!_keys.Add(key)) return;
            _steps.Add((depth, kind, description, certainty, location, line));
        }

        public IEnumerable<DataFlowStep> Build()
        {
            var complete = Reasons.Count == 0;
            var reason = string.Join(" ", Reasons);
            var n = 0;
            foreach (var s in _steps)
            {
                yield return new DataFlowStep
                {
                    FlowId = FlowId,
                    Phrase = Phrase,
                    RootSymbol = RootSymbol,
                    StepNumber = ++n,
                    Depth = s.Depth,
                    StepKind = s.Kind,
                    Description = s.Description,
                    Certainty = s.Certainty,
                    IsFlowComplete = complete,
                    IncompletenessReason = reason,
                    CodeLine = s.Line,
                    Location = s.Location
                };
            }
        }
    }

    /// <summary>Dane współdzielone przez wszystkie przepływy (indeksy wyników, cache dokumentów).</summary>
    private sealed class FlowState
    {
        private readonly Dictionary<string, List<SymbolReferenceInfo>> _referencesByKey;
        private readonly Dictionary<string, List<ConversionInfo>> _conversionsByFile;
        private readonly Dictionary<string, List<RoundingInfo>> _roundingsByFile;

        public FlowState(AnalysisRunContext ctx)
        {
            Ctx = ctx;
            _referencesByKey = ctx.Result.References.GroupBy(r => r.SymbolKey)
                .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Location.RelativePath).ThenBy(r => r.Location.Line).ToList(), StringComparer.Ordinal);
            _conversionsByFile = ctx.Result.Conversions.GroupBy(c => c.Location.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            _roundingsByFile = ctx.Result.Roundings.GroupBy(c => c.Location.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var limitations = ctx.Result.Limitations.Snapshot();
            ReflectionNames = new HashSet<string>(limitations.Where(l => l.Kind == LimitationKind.Reflection && l.RelatedName.Length > 0).Select(l => l.RelatedName), StringComparer.Ordinal);
            DynamicProjects = new HashSet<string>(limitations.Where(l => l.Kind == LimitationKind.Dynamic).Select(l => l.Project), StringComparer.OrdinalIgnoreCase);
            HasDynamicSql = limitations.Any(l => l.Kind == LimitationKind.DynamicSql);
        }

        public AnalysisRunContext Ctx { get; }
        public HashSet<string> ReflectionNames { get; }
        public HashSet<string> DynamicProjects { get; }
        public bool HasDynamicSql { get; }

        public IReadOnlyList<SymbolReferenceInfo> ReferencesOf(string key) =>
            _referencesByKey.TryGetValue(key, out var list) ? list : (IReadOnlyList<SymbolReferenceInfo>)Array.Empty<SymbolReferenceInfo>();

        public IEnumerable<ConversionInfo> ConversionsContaining(CodeLocation location) =>
            _conversionsByFile.TryGetValue(location.FilePath, out var list)
                ? list.Where(c => c.Location.Contains(location) && !c.IsConstant)
                : Enumerable.Empty<ConversionInfo>();

        public IEnumerable<RoundingInfo> RoundingsContaining(CodeLocation location) =>
            _roundingsByFile.TryGetValue(location.FilePath, out var list)
                ? list.Where(c => c.Location.Contains(location))
                : Enumerable.Empty<RoundingInfo>();

        private async Task<(Document Doc, SemanticModel Model, SyntaxNode Root)?> GetDocumentAsync(string filePath, CancellationToken ct)
        {
            var solution = Ctx.Solution!;
            var id = solution.GetDocumentIdsWithFilePath(filePath).FirstOrDefault();
            var doc = id is null ? null : solution.GetDocument(id);
            if (doc is null) return null;
            var model = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
            var root = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            return model is null || root is null ? null : (doc, model, root);
        }

        public async Task<string> LineAsync(CodeLocation location, CancellationToken ct)
        {
            if (location.IsEmpty || Ctx.Solution is null) return string.Empty;
            var id = Ctx.Solution.GetDocumentIdsWithFilePath(location.FilePath).FirstOrDefault();
            var doc = id is null ? null : Ctx.Solution.GetDocument(id);
            if (doc is null) return Ctx.Files.Get(location.FilePath).GetLine(location.Line);
            var text = await doc.GetTextAsync(ct).ConfigureAwait(false);
            return location.Line >= 1 && location.Line <= text.Lines.Count ? TextUtil.Shorten(text.Lines[location.Line - 1].ToString().Trim()) : string.Empty;
        }

        /// <summary>
        /// Ustala symbol, do którego „przepływa” wartość w miejscu odwołania:
        /// parametr metody (przekazanie argumentu) lub zmienna lokalna (przypisanie/inicjalizacja).
        /// </summary>
        public async Task<(ISymbol Symbol, string Description, bool InSource)?> ResolveNextSymbolAsync(CodeLocation location, UsageKind usage, CancellationToken ct)
        {
            if (location.SpanStart < 0) return null;
            var docInfo = await GetDocumentAsync(location.FilePath, ct).ConfigureAwait(false);
            if (docInfo is null) return null;
            var (_, model, root) = docInfo.Value;
            var node = root.FindToken(location.SpanStart).Parent;
            if (node is null) return null;

            IOperation? op = null;
            for (var n = node; n is not null && op is null; n = n.Parent) op = model.GetOperation(n, ct);
            for (var o = op; o is not null; o = o.Parent)
            {
                switch (o)
                {
                    case IArgumentOperation { Parameter: { } p }:
                        var method = RoslynHelpers.MemberName(p.ContainingSymbol);
                        var inSource = p.Locations.Any(l => l.IsInSource);
                        return (p, inSource ? $"Przekazanie do {method}, parametr „{p.Name}” ({RoslynHelpers.DisplayType(p.Type)})"
                                            : $"Przekazanie do metody zewnętrznej {method} (parametr „{p.Name}”) – kod biblioteki nie jest analizowany", inSource);
                    case IVariableDeclaratorOperation decl:
                        return (decl.Symbol, $"Inicjalizacja zmiennej lokalnej „{decl.Symbol.Name}” ({RoslynHelpers.DisplayType(decl.Symbol.Type)})", true);
                    case ISimpleAssignmentOperation { Target: ILocalReferenceOperation local } assign when !IsWithin(op!, assign.Target):
                        return (local.Local, $"Przypisanie do zmiennej lokalnej „{local.Local.Name}” ({RoslynHelpers.DisplayType(local.Local.Type)})", true);
                    case IExpressionStatementOperation or IBlockOperation or IReturnOperation:
                        return null;
                }
            }
            return null;
        }

        public async Task<List<(UsageKind Usage, CodeLocation Location, string Line, string Member)>> FindReferencesAsync(ISymbol symbol, CancellationToken ct)
        {
            var result = new List<(UsageKind, CodeLocation, string, string)>();
            var solution = Ctx.Solution!;
            var referenced = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
            foreach (var location in referenced.SelectMany(r => r.Locations).OrderBy(l => l.Location.SourceSpan.Start))
            {
                if (!location.Location.IsInSource) continue;
                var doc = location.Document;
                var model = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
                var root = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                if (model is null || root is null) continue;
                var token = root.FindToken(location.Location.SourceSpan.Start);
                var usage = token.Parent is null ? UsageKind.Unknown : UsageClassifier.Classify(model, token.Parent, symbol, ct);
                var text = await doc.GetTextAsync(ct).ConfigureAwait(false);
                var line = TextUtil.Shorten(text.Lines.GetLineFromPosition(location.Location.SourceSpan.Start).ToString().Trim());
                var member = RoslynHelpers.MemberName(model.GetEnclosingSymbol(token.SpanStart, ct));
                result.Add((usage, LocationFactory.FromLocation(Ctx, location.Location, RoslynHelpers.CleanProjectName(doc.Project.Name)), line, member));
            }
            return result;
        }

        private static bool IsWithin(IOperation op, IOperation container)
        {
            for (var o = op; o is not null; o = o.Parent)
                if (o == container) return true;
            return false;
        }
    }
}
