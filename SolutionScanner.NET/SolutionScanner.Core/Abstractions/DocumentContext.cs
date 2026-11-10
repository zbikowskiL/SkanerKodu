using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using SolutionScanner.Core.Infrastructure;
using SolutionScanner.Core.Models;

namespace SolutionScanner.Core.Abstractions;

/// <summary>
/// Kontekst analizy jednego dokumentu Roslyn. Drzewo składniowe, model semantyczny i lista operacji (IOperation)
/// są wyznaczane raz i współdzielone przez wszystkie analizatory dokumentu (cache na poziomie dokumentu).
/// </summary>
public sealed class DocumentContext
{
    private IReadOnlyList<Microsoft.CodeAnalysis.IOperation>? _operations;

    public DocumentContext(
        AnalysisRunContext run,
        Document document,
        string projectName,
        SyntaxNode root,
        SemanticModel model,
        SourceText text,
        bool hasReliableSemantics,
        CancellationToken cancellationToken)
    {
        Run = run;
        Document = document;
        ProjectName = projectName;
        Root = root;
        Model = model;
        Text = text;
        HasReliableSemantics = hasReliableSemantics;
        CancellationToken = cancellationToken;
        FilePath = document.FilePath ?? document.Name;
        RelativePath = run.GetRelativePath(FilePath);
    }

    public AnalysisRunContext Run { get; }
    public Document Document { get; }
    public Project Project => Document.Project;
    public string ProjectName { get; }
    public SyntaxNode Root { get; }
    public SemanticModel Model { get; }
    public SourceText Text { get; }
    public string FilePath { get; }
    public string RelativePath { get; }
    public string Language => Document.Project.Language;
    public string LanguageLabel => Language == LanguageNames.VisualBasic ? "VB" : "C#";
    public bool IsCSharp => Language == LanguageNames.CSharp;

    /// <summary>false, gdy projekt nie ma rozpoznanej biblioteki bazowej – wyniki semantyczne są przybliżone.</summary>
    public bool HasReliableSemantics { get; }

    public CancellationToken CancellationToken { get; }
    public AnalysisSettings Settings => Run.Settings;
    public SearchEngine Matcher => Run.Matcher;
    public AnalysisResult Result => Run.Result;

    /// <summary>Wszystkie operacje (IOperation) z ciał metod, akcesorów i inicjalizatorów dokumentu.</summary>
    public IReadOnlyList<Microsoft.CodeAnalysis.IOperation> Operations =>
        _operations ??= OperationCollector.Collect(Root, Model, CancellationToken);

    public CodeLocation Loc(TextSpan span)
    {
        var line = Text.Lines.GetLinePosition(span.Start);
        return Run.CreateLocation(ProjectName, FilePath, line.Line + 1, line.Character + 1, span.Start, span.Length);
    }

    public CodeLocation Loc(SyntaxNode node) => Loc(node.Span);
    public CodeLocation Loc(SyntaxToken token) => Loc(token.Span);

    public CodeLocation Loc(Location location)
    {
        if (location.IsInSource && location.SourceTree is not null && location.SourceTree.FilePath == FilePath) return Loc(location.SourceSpan);
        return LocationFactory.FromLocation(Run, location, ProjectName);
    }

    /// <summary>Tekst linii kodu zawierającej podany zakres (przycięty).</summary>
    public string CodeLine(TextSpan span)
    {
        var line = Text.Lines.GetLineFromPosition(span.Start);
        return TextUtil.Shorten(line.ToString().Trim());
    }

    public string CodeLine(SyntaxNode node) => CodeLine(node.Span);

    public string Shorten(SyntaxNode node, int max = 200) => TextUtil.Shorten(node.ToString(), max);

    /// <summary>Frazy dopasowane do dowolnej z podanych nazw.</summary>
    public IReadOnlyList<string> Phrases(IEnumerable<string?> names) => Run.Matcher.MatchAny(names);

    /// <summary>Czy element mieści się w zakresie analizy szczegółowej (cały kod lub dopasowane frazy).</summary>
    public bool InScope(IReadOnlyList<string> phrases) => Settings.AnalyzeEntireCode || phrases.Count > 0;

    public static string JoinPhrases(IReadOnlyList<string> phrases) => string.Join(", ", phrases);

    public string EnclosingMember(int position)
    {
        try { return RoslynHelpers.MemberName(Model.GetEnclosingSymbol(position, CancellationToken)); }
        catch { return string.Empty; }
    }
}

/// <summary>Kontekst analizy pliku spoza modelu Roslyn.</summary>
public sealed class FileContext
{
    private FileContent? _content;

    public FileContext(AnalysisRunContext run, string filePath, string projectName, bool isRoslynDocument, CancellationToken cancellationToken)
    {
        Run = run;
        FilePath = filePath;
        ProjectName = projectName;
        IsRoslynDocument = isRoslynDocument;
        CancellationToken = cancellationToken;
        RelativePath = run.GetRelativePath(filePath);
        Extension = Path.GetExtension(filePath).ToLowerInvariant();
    }

    public AnalysisRunContext Run { get; }
    public string FilePath { get; }
    public string RelativePath { get; }
    public string ProjectName { get; }
    public string Extension { get; }

    /// <summary>Czy plik został już przeanalizowany jako dokument Roslyn (dotyczy .cs/.vb).</summary>
    public bool IsRoslynDocument { get; }

    public CancellationToken CancellationToken { get; }
    public AnalysisSettings Settings => Run.Settings;
    public SearchEngine Matcher => Run.Matcher;
    public AnalysisResult Result => Run.Result;

    /// <summary>Treść pliku (wczytywana raz, współdzielona przez analizatory).</summary>
    public FileContent Content => _content ??= Run.Files.Get(FilePath);

    public CodeLocation Loc(int line, int column, int spanStart = -1, int spanLength = 0) =>
        Run.CreateLocation(ProjectName, FilePath, line, column, spanStart, spanLength);
}
