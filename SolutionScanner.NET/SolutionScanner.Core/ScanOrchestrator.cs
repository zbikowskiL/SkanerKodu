using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using SolutionScanner.Core.Abstractions;
using SolutionScanner.Core.Files;
using SolutionScanner.Core.Infrastructure;
using SolutionScanner.Core.Models;

namespace SolutionScanner.Core;

public interface IScanOrchestrator
{
    Task<AnalysisResult> RunAsync(string solutionPath, AnalysisSettings settings, IProgress<AnalysisProgress>? progress, CancellationToken cancellationToken);

    /// <summary>Analiza gotowej solucji Roslyn (np. testy jednostkowe na AdhocWorkspace).</summary>
    Task<AnalysisResult> RunOnSolutionAsync(Solution solution, string solutionPath, IReadOnlyList<ProjectEntry> projects,
        AnalysisSettings settings, IProgress<AnalysisProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>
/// Koordynuje całą analizę:
/// 1) wczytanie solucji (MSBuildWorkspace + wczytywanie awaryjne),
/// 2) równoległa analiza dokumentów Roslyn (wyszukiwanie, typy, precyzja, zaokrąglenia, konwersje, EF6),
/// 3) równoległa analiza pozostałych plików (SQL, EDMX, XML, JSON, Razor, pliki .cs spoza projektów),
/// 4) wyszukiwanie odwołań do symboli (SymbolFinder) i zależności projektów,
/// 5) analizy końcowe (porównanie C#↔SQL, przepływ danych, reguły potencjalnych problemów).
/// Błąd pojedynczego pliku/analizatora jest logowany i nie przerywa analizy. Anulowanie zwraca wyniki częściowe.
/// </summary>
public sealed class ScanOrchestrator : IScanOrchestrator
{
    private readonly ISolutionLoader _loader;
    private readonly IReadOnlyList<IDocumentAnalyzer> _documentAnalyzers;
    private readonly IReadOnlyList<IFileAnalyzer> _fileAnalyzers;
    private readonly IReadOnlyList<IPostAnalyzer> _postAnalyzers;
    private readonly SymbolAnalyzer _symbolAnalyzer;
    private readonly DependencyAnalyzer _dependencyAnalyzer;
    private readonly IScanLogger _logger;

    public ScanOrchestrator(
        ISolutionLoader loader,
        IEnumerable<IDocumentAnalyzer> documentAnalyzers,
        IEnumerable<IFileAnalyzer> fileAnalyzers,
        IEnumerable<IPostAnalyzer> postAnalyzers,
        SymbolAnalyzer symbolAnalyzer,
        DependencyAnalyzer dependencyAnalyzer,
        IScanLogger logger)
    {
        _loader = loader;
        _documentAnalyzers = documentAnalyzers.ToList();
        _fileAnalyzers = fileAnalyzers.ToList();
        _postAnalyzers = postAnalyzers.OrderBy(a => a.Order).ToList();
        _symbolAnalyzer = symbolAnalyzer;
        _dependencyAnalyzer = dependencyAnalyzer;
        _logger = logger;
    }

    public async Task<AnalysisResult> RunAsync(string solutionPath, AnalysisSettings settings, IProgress<AnalysisProgress>? progress, CancellationToken cancellationToken)
    {
        _logger.StartNewLog(Path.GetFileNameWithoutExtension(solutionPath));
        var stopwatch = Stopwatch.StartNew();
        var result = new AnalysisResult { SolutionPath = solutionPath, Settings = settings.Clone(), LogFilePath = _logger.LogFilePath };

        try
        {
            progress?.Report(new AnalysisProgress("Wczytywanie", "Odczyt pliku solucji..."));
            var descriptor = _loader.ReadDescriptor(solutionPath);
            _logger.Info($"Solucja: {solutionPath}, projektów: {descriptor.Projects.Count}");

            using var loaded = await _loader.LoadAsync(descriptor, settings, _logger, progress, cancellationToken).ConfigureAwait(false);
            result.MsBuildInfo = SolutionLoader.MsBuildDescription;
            result.LoadDiagnostics.AddRange(loaded.Diagnostics);
            result.ProjectStatuses.AddRange(loaded.Statuses);
            foreach (var d in loaded.Diagnostics.Where(d => d.Severity == Severity.Error))
                _logger.Warning($"[{d.Project}] {d.Message}");

            await AnalyzeAsync(loaded.Solution, descriptor.SolutionPath, descriptor.Projects, result, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result.Cancelled = true;
            _logger.Warning("Analiza anulowana przez użytkownika – wyniki częściowe.");
        }
        catch (Exception ex)
        {
            result.FatalError = ex.Message;
            _logger.Error("Krytyczny błąd analizy", ex);
        }

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;
        result.SortAll();
        _logger.Info($"Zakończono w {stopwatch.Elapsed:hh\\:mm\\:ss}. Wyniki wyszukiwania: {result.SearchHits.Count}, problemy: {result.Problems.Count}.");
        progress?.Report(new AnalysisProgress("Zakończono", result.Cancelled ? "Analiza anulowana" : "Analiza zakończona", Percent: 100));
        return result;
    }

    public async Task<AnalysisResult> RunOnSolutionAsync(Solution solution, string solutionPath, IReadOnlyList<ProjectEntry> projects,
        AnalysisSettings settings, IProgress<AnalysisProgress>? progress, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new AnalysisResult { SolutionPath = solutionPath, Settings = settings.Clone() };
        try
        {
            await AnalyzeAsync(solution, solutionPath, projects, result, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result.Cancelled = true;
        }
        result.Duration = stopwatch.Elapsed;
        result.SortAll();
        return result;
    }

    private async Task AnalyzeAsync(Solution solution, string solutionPath, IReadOnlyList<ProjectEntry> projects,
        AnalysisResult result, IProgress<AnalysisProgress>? progress, CancellationToken ct)
    {
        var settings = result.Settings;
        var ctx = new AnalysisRunContext(solutionPath, settings, result, _logger)
        {
            Solution = solution,
            SolutionProjects = projects
        };
        _logger.Info($"Frazy: {string.Join("; ", ctx.Matcher.Phrases)}");

        // --- Wybór projektów i dokumentów ---
        var selectedProjects = solution.Projects
            .Where(p => settings.SelectedProjectPaths is null
                        || (p.FilePath is not null && settings.SelectedProjectPaths.Contains(TextUtil.NormalizePath(p.FilePath))))
            .ToList();
        result.ProjectsAnalyzed = selectedProjects.Select(p => RoslynHelpers.CleanProjectName(p.Name)).Distinct().Count();

        var excluded = new HashSet<string>(settings.ExcludedDirectories, StringComparer.OrdinalIgnoreCase);
        var documents = new List<Document>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in selectedProjects)
        {
            foreach (var doc in project.Documents)
            {
                var path = doc.FilePath ?? doc.Name;
                if (doc.FilePath is not null && SourceFileEnumerator.IsInExcludedDirectory(doc.FilePath, excluded)) continue;
                if (!settings.IncludeGeneratedFiles && SourceFileEnumerator.IsGeneratedFile(path)) continue;
                if (!seenPaths.Add(path)) continue; // ten sam plik w wielu platformach docelowych – raz
                documents.Add(doc);
            }
        }

        // --- Wiarygodność semantyki projektów ---
        var reliability = new Dictionary<ProjectId, bool>();
        foreach (var project in selectedProjects)
        {
            ct.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            var reliable = RoslynHelpers.HasCoreLibrary(compilation);
            reliability[project.Id] = reliable;
            var status = result.ProjectStatuses.FirstOrDefault(s => string.Equals(s.FilePath, project.FilePath, StringComparison.OrdinalIgnoreCase));
            if (compilation is not null)
            {
                var errors = compilation.GetDiagnostics(ct).Count(d => d.Severity == DiagnosticSeverity.Error);
                if (errors > 0)
                {
                    ctx.AddLimitation(LimitationKind.UnresolvedSymbols, RoslynHelpers.CleanProjectName(project.Name),
                        $"Kompilacja projektu zawiera {errors} błędów (np. brakujące referencje). Symbole w miejscach błędów mogą być nierozpoznane – wyniki oznaczono metodą „składniowa/kandydat”.");
                }
            }
            if (status?.IsFallback == true)
            {
                ctx.AddLimitation(LimitationKind.FallbackProject, status.Name,
                    "Projekt wczytany awaryjnie (bez MSBuild) – referencje do bibliotek zewnętrznych mogą być nierozpoznane.");
            }
        }

        // --- Etap 2: dokumenty Roslyn ---
        var total = documents.Count;
        var processed = 0;
        var throttle = new ProgressThrottle(progress);
        var options = new ParallelOptions { MaxDegreeOfParallelism = settings.MaxDegreeOfParallelism, CancellationToken = ct };
        await Parallel.ForEachAsync(documents, options, async (doc, token) =>
        {
            var projectName = RoslynHelpers.CleanProjectName(doc.Project.Name);
            try
            {
                var root = await doc.GetSyntaxRootAsync(token).ConfigureAwait(false);
                var model = await doc.GetSemanticModelAsync(token).ConfigureAwait(false);
                var text = await doc.GetTextAsync(token).ConfigureAwait(false);
                if (root is null || model is null) return;
                if (doc.FilePath is not null) ctx.RoslynDocumentPaths.TryAdd(doc.FilePath, 0);

                var dctx = new DocumentContext(ctx, doc, projectName, root, model, text,
                    reliability.TryGetValue(doc.Project.Id, out var r) && r, token);
                foreach (var analyzer in _documentAnalyzers)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        await analyzer.AnalyzeAsync(dctx).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"{analyzer.Name}: błąd w pliku {doc.FilePath}", ex);
                        ctx.AddLimitation(LimitationKind.FileError, projectName, $"{analyzer.Name}: {ex.Message}",
                            ctx.CreateLocation(projectName, doc.FilePath ?? doc.Name, 1, 1));
                    }
                }
                Interlocked.Increment(ref result.DocumentsAnalyzed);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref result.FilesFailed);
                _logger.Error($"Błąd analizy dokumentu {doc.FilePath}", ex);
            }
            finally
            {
                var n = Interlocked.Increment(ref processed);
                throttle.Report(new AnalysisProgress("Analiza C#/VB", $"Dokument {n}/{total}", projectName, doc.FilePath ?? doc.Name, n, total,
                    total == 0 ? 100 : n * 100.0 / total), n == total);
            }
        }).ConfigureAwait(false);

        // --- Etap 3: pozostałe pliki ---
        await AnalyzeFilesAsync(ctx, progress, ct).ConfigureAwait(false);

        // --- Etap 4: odwołania i zależności ---
        if (settings.FindSymbolReferences && ctx.Symbols.Count > 0)
        {
            progress?.Report(new AnalysisProgress("Odwołania", $"Wyszukiwanie odwołań do {ctx.Symbols.Count} symboli..."));
            var scope = ImmutableHashSet.CreateRange(documents);
            await _symbolAnalyzer.FindReferencesAsync(ctx, scope, progress, ct).ConfigureAwait(false);
        }

        progress?.Report(new AnalysisProgress("Zależności", "Analiza zależności projektów..."));
        try
        {
            _dependencyAnalyzer.Analyze(ctx, selectedProjects);
        }
        catch (Exception ex)
        {
            _logger.Error("Analiza zależności", ex);
        }

        // --- Etap 5: analizy końcowe ---
        foreach (var post in _postAnalyzers)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new AnalysisProgress(post.Name, post.Name + "..."));
            try
            {
                await post.RunAsync(ctx, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error($"Analiza końcowa {post.Name}", ex);
                ctx.AddLimitation(LimitationKind.FileError, string.Empty, $"{post.Name}: {ex.Message}");
            }
        }

        ctx.Files.Clear();
    }

    private async Task AnalyzeFilesAsync(AnalysisRunContext ctx, IProgress<AnalysisProgress>? progress, CancellationToken ct)
    {
        var settings = ctx.Settings;
        var files = SourceFileEnumerator.Enumerate(ctx.SolutionDirectory, ctx.SolutionProjects, settings, ct);
        var work = new List<(SourceFile File, List<IFileAnalyzer> Analyzers, bool IsRoslyn)>();
        foreach (var file in files)
        {
            var isRoslyn = ctx.RoslynDocumentPaths.ContainsKey(file.Path);
            var fctx = new FileContext(ctx, file.Path, file.ProjectName, isRoslyn, ct);
            var analyzers = _fileAnalyzers.Where(a => a.CanAnalyze(fctx)).ToList();
            if (analyzers.Count > 0) work.Add((file, analyzers, isRoslyn));
        }

        var total = work.Count;
        var processed = 0;
        var throttle = new ProgressThrottle(progress);
        var options = new ParallelOptions { MaxDegreeOfParallelism = settings.MaxDegreeOfParallelism, CancellationToken = ct };
        await Parallel.ForEachAsync(work, options, async (item, token) =>
        {
            var fctx = new FileContext(ctx, item.File.Path, item.File.ProjectName, item.IsRoslyn, token);
            try
            {
                if (!fctx.Content.IsReadable)
                {
                    if (fctx.Content.Error is not null)
                    {
                        Interlocked.Increment(ref ctx.Result.FilesFailed);
                        ctx.AddLimitation(LimitationKind.FileError, item.File.ProjectName, "Nie można odczytać pliku: " + fctx.Content.Error,
                            fctx.Loc(1, 1));
                    }
                    return;
                }

                foreach (var analyzer in item.Analyzers)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        await analyzer.AnalyzeAsync(fctx).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref ctx.Result.FilesFailed);
                        _logger.Error($"{analyzer.Name}: błąd w pliku {item.File.Path}", ex);
                        ctx.AddLimitation(LimitationKind.FileError, item.File.ProjectName, $"{analyzer.Name}: {ex.Message}", fctx.Loc(1, 1));
                    }
                }
                Interlocked.Increment(ref ctx.Result.FilesAnalyzed);
            }
            finally
            {
                var n = Interlocked.Increment(ref processed);
                throttle.Report(new AnalysisProgress("Analiza plików", $"Plik {n}/{total}", item.File.ProjectName, item.File.Path, n, total,
                    total == 0 ? 100 : n * 100.0 / total), n == total);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>Ogranicza częstotliwość raportowania postępu (UI nie jest zalewany komunikatami).</summary>
    private sealed class ProgressThrottle
    {
        private readonly IProgress<AnalysisProgress>? _progress;
        private long _lastTicks;

        public ProgressThrottle(IProgress<AnalysisProgress>? progress) => _progress = progress;

        public void Report(AnalysisProgress value, bool force)
        {
            if (_progress is null) return;
            var now = Environment.TickCount64;
            var last = Interlocked.Read(ref _lastTicks);
            if (!force && now - last < 100) return;
            Interlocked.Exchange(ref _lastTicks, now);
            _progress.Report(value);
        }
    }
}
