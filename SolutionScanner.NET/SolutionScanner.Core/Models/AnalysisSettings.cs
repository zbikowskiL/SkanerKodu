namespace SolutionScanner.Core.Models;

/// <summary>Ustawienia pojedynczego uruchomienia analizy.</summary>
public sealed class AnalysisSettings
{
    /// <summary>Wyszukiwane frazy (fragmenty nazw). Pusta lista = brak wyszukiwania fraz.</summary>
    public List<string> Phrases { get; set; } = new();

    /// <summary>Czy porównanie fraz ma uwzględniać wielkość liter (domyślnie nie).</summary>
    public bool CaseSensitive { get; set; }

    /// <summary>Czy ignorować polskie znaki diakrytyczne („kapital” znajdzie „Kapitał”).</summary>
    public bool IgnoreDiacritics { get; set; } = true;

    /// <summary>Czy analizować pliki generowane (.g.cs, .designer.cs). Katalogi bin/obj są zawsze pomijane.</summary>
    public bool IncludeGeneratedFiles { get; set; }

    /// <summary>Czy analizować pliki SQL (.sql, projekty .sqlproj).</summary>
    public bool AnalyzeSqlFiles { get; set; } = true;

    /// <summary>Czy przeszukiwać pliki .xml, .json, .config, .edmx, .razor, .cshtml.</summary>
    public bool AnalyzeOtherFiles { get; set; } = true;

    /// <summary>Czy wyszukiwać rzeczywiste odwołania do symboli (SymbolFinder.FindReferencesAsync).</summary>
    public bool FindSymbolReferences { get; set; } = true;

    /// <summary>
    /// false = analiza typów/precyzji/konwersji tylko dla elementów powiązanych z frazami;
    /// true = analiza całego kodu wybranych projektów (dużo więcej wyników).
    /// </summary>
    public bool AnalyzeEntireCode { get; set; }

    /// <summary>Czy uruchomić „dotnet restore” przed wczytaniem solucji.</summary>
    public bool RestorePackagesBeforeLoad { get; set; }

    /// <summary>Czy analizować przepływ danych (wymaga wyszukiwania odwołań).</summary>
    public bool AnalyzeDataFlow { get; set; } = true;

    public int MaxDegreeOfParallelism { get; set; } = Math.Max(1, Environment.ProcessorCount);

    /// <summary>Limit symboli, dla których wyszukiwane są odwołania (ochrona przed bardzo długą analizą).</summary>
    public int MaxSymbolsForReferenceSearch { get; set; } = 5000;

    /// <summary>Maksymalna głębokość śledzenia przekazań parametrów w przepływie danych.</summary>
    public int DataFlowMaxDepth { get; set; } = 2;

    /// <summary>Maksymalna liczba analizowanych przepływów.</summary>
    public int MaxDataFlows { get; set; } = 2000;

    /// <summary>Ścieżki projektów wybranych do analizy. null = wszystkie projekty solucji.</summary>
    public HashSet<string>? SelectedProjectPaths { get; set; }

    /// <summary>Katalogi, których zawartość nigdy nie jest analizowana.</summary>
    public List<string> ExcludedDirectories { get; set; } = new() { "bin", "obj", ".git", ".vs", "packages", "node_modules" };

    /// <summary>
    /// Słowa kluczowe wskazujące na kontekst finansowy (używane przez reguły problemów
    /// razem z wyszukiwanymi frazami). Dopasowanie jako fragment nazwy, bez polskich znaków.
    /// </summary>
    public List<string> FinancialKeywords { get; set; } = new()
    {
        "kwot", "cena", "cen", "oprocent", "odset", "kapital", "marz", "prowizj", "saldo", "rata", "raty",
        "oplat", "podatek", "podatk", "vat", "netto", "brutto", "walut", "kurs", "kredyt", "lokat", "wplat",
        "wyplat", "naleznosc", "zaleglosc", "amount", "price", "rate", "interest", "balance", "fee",
        "money", "cost", "tax", "currency", "payment", "principal", "margin", "commission", "loan"
    };

    public AnalysisSettings Clone()
    {
        var clone = (AnalysisSettings)MemberwiseClone();
        clone.Phrases = new List<string>(Phrases);
        clone.ExcludedDirectories = new List<string>(ExcludedDirectories);
        clone.FinancialKeywords = new List<string>(FinancialKeywords);
        clone.SelectedProjectPaths = SelectedProjectPaths is null
            ? null
            : new HashSet<string>(SelectedProjectPaths, StringComparer.OrdinalIgnoreCase);
        return clone;
    }
}
