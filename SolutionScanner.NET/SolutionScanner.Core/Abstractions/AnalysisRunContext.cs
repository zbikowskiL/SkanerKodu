using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using SolutionScanner.Core.Infrastructure;
using SolutionScanner.Core.Models;

namespace SolutionScanner.Core.Abstractions;

/// <summary>
/// Kontekst jednego uruchomienia analizy – wspólny dla wszystkich analizatorów.
/// Zawiera ustawienia, silnik fraz, wynik, logger, cache plików i rejestr dopasowanych symboli.
/// </summary>
public sealed class AnalysisRunContext
{
    private readonly ConcurrentDictionary<Type, object> _state = new();

    public AnalysisRunContext(string solutionPath, AnalysisSettings settings, AnalysisResult result, IScanLogger logger)
    {
        SolutionPath = solutionPath;
        SolutionDirectory = Path.GetDirectoryName(Path.GetFullPath(solutionPath)) ?? string.Empty;
        Settings = settings;
        Result = result;
        Logger = logger;
        Matcher = new SearchEngine(settings.Phrases, settings.CaseSensitive, settings.IgnoreDiacritics);
        FinancialMatcher = new SearchEngine(settings.FinancialKeywords, caseSensitive: false, ignoreDiacritics: true);
    }

    public string SolutionPath { get; }
    public string SolutionDirectory { get; }
    public AnalysisSettings Settings { get; }
    public AnalysisResult Result { get; }
    public IScanLogger Logger { get; }
    public SearchEngine Matcher { get; }
    public SearchEngine FinancialMatcher { get; }
    public FileContentCache Files { get; } = new();
    public MatchedSymbolRegistry Symbols { get; } = new();

    /// <summary>Solucja Roslyn (null, jeśli nie została wczytana).</summary>
    public Solution? Solution { get; set; }

    /// <summary>Ścieżki plików, które zostały przeanalizowane jako dokumenty Roslyn.</summary>
    public ConcurrentDictionary<string, byte> RoslynDocumentPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Wszystkie projekty z pliku .sln (również niewczytane przez MSBuild, np. .sqlproj).</summary>
    public IReadOnlyList<ProjectEntry> SolutionProjects { get; set; } = Array.Empty<ProjectEntry>();

    /// <summary>Stan pomocniczy jednego uruchomienia (np. fakty EF6 zbierane z wielu dokumentów).</summary>
    public T GetState<T>() where T : class, new() => (T)_state.GetOrAdd(typeof(T), _ => new T());

    public string GetRelativePath(string path) => TextUtil.GetRelativePath(SolutionDirectory, path);

    /// <summary>Czy nazwa wskazuje na kontekst finansowy (fraza użytkownika lub słowo kluczowe finansowe).</summary>
    public bool IsFinancialName(string? name) =>
        !string.IsNullOrEmpty(name) && (Matcher.IsMatch(name) || FinancialMatcher.IsMatch(name));

    public CodeLocation CreateLocation(string projectName, string filePath, int line, int column, int spanStart = -1, int spanLength = 0) =>
        new()
        {
            ProjectName = projectName,
            FilePath = filePath,
            RelativePath = GetRelativePath(filePath),
            Line = line,
            Column = column,
            SpanStart = spanStart,
            SpanLength = spanLength
        };

    public void AddLimitation(LimitationKind kind, string project, string description, CodeLocation? location = null, string relatedName = "")
    {
        Result.Limitations.Add(new AnalysisLimitation
        {
            Kind = kind,
            Project = project,
            Description = description,
            Location = location ?? CodeLocation.Empty,
            RelatedName = relatedName
        });
    }
}

/// <summary>
/// Rejestr zadeklarowanych symboli, których nazwy pasują do fraz.
/// Wypełniany podczas analizy dokumentów, wykorzystywany do wyszukiwania odwołań i przepływu danych.
/// </summary>
public sealed class MatchedSymbolRegistry
{
    private readonly ConcurrentDictionary<string, MatchedSymbol> _symbols = new(StringComparer.Ordinal);

    public void Register(ISymbol symbol, string key, IReadOnlyList<string> phrases, string projectName, CodeLocation declaration)
    {
        _symbols.TryAdd(key, new MatchedSymbol(symbol, key, phrases, projectName, declaration));
    }

    public IReadOnlyCollection<MatchedSymbol> All => _symbols.Values.ToList();

    public bool TryGet(string key, out MatchedSymbol? symbol)
    {
        var ok = _symbols.TryGetValue(key, out var s);
        symbol = s;
        return ok;
    }

    public int Count => _symbols.Count;
}

public sealed record MatchedSymbol(ISymbol Symbol, string Key, IReadOnlyList<string> Phrases, string ProjectName, CodeLocation Declaration);
