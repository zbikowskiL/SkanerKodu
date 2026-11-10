using System.ComponentModel;
using System.Reflection;
using CsvHelper.Configuration.Attributes;
using SolutionScanner.Core.Models;

namespace SolutionScanner.Export;

/// <summary>Wiersz raportu – wspólny dla eksportu CSV i tabel w UI (nagłówki z atrybutu [Name]).</summary>
public abstract class ReportRow
{
    private string? _filterText;

    /// <summary>Tekst wszystkich kolumn – używany do filtrowania w UI.</summary>
    [Ignore, Browsable(false)]
    public string FilterText => _filterText ??= string.Join(" ", GetType()
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.Name != nameof(FilterText))
        .Select(p => p.GetValue(this)?.ToString() ?? string.Empty));
}

public sealed class SearchRow : ReportRow
{
    [Name("Fraza")] public string Fraza { get; init; } = "";
    [Name("Projekt")] public string Projekt { get; init; } = "";
    [Name("Plik")] public string Plik { get; init; } = "";
    [Name("Linia")] public int Linia { get; init; }
    [Name("Kolumna")] public int Kolumna { get; init; }
    [Name("Dopasowana nazwa")] public string Nazwa { get; init; } = "";
    [Name("Rodzaj elementu")] public string Rodzaj { get; init; } = "";
    [Name("Użycie")] public string Uzycie { get; init; } = "";
    [Name("Symbol")] public string Symbol { get; init; } = "";
    [Name("Typ")] public string Typ { get; init; } = "";
    [Name("Składowa")] public string Skladowa { get; init; } = "";
    [Name("Język")] public string Jezyk { get; init; } = "";
    [Name("Metoda analizy")] public string Metoda { get; init; } = "";
    [Name("Kod")] public string Kod { get; init; } = "";
    [Name("Uwagi")] public string Uwagi { get; init; } = "";
}

public sealed class DeclarationRow : ReportRow
{
    [Name("Fraza")] public string Fraza { get; init; } = "";
    [Name("Projekt")] public string Projekt { get; init; } = "";
    [Name("Plik")] public string Plik { get; init; } = "";
    [Name("Linia")] public int Linia { get; init; }
    [Name("Kolumna")] public int Kolumna { get; init; }
    [Name("Nazwa")] public string Nazwa { get; init; } = "";
    [Name("Rodzaj")] public string Rodzaj { get; init; } = "";
    [Name("Typ")] public string Typ { get; init; } = "";
    [Name("Pełny typ CLR")] public string TypClr { get; init; } = "";
    [Name("Nullable")] public string Nullable { get; init; } = "";
    [Name("Dostęp")] public string Dostep { get; init; } = "";
    [Name("Zakres")] public string Zakres { get; init; } = "";
    [Name("Wartość początkowa")] public string Wartosc { get; init; } = "";
    [Name("const")] public string Const { get; init; } = "";
    [Name("static")] public string Static { get; init; } = "";
    [Name("readonly")] public string ReadOnly { get; init; } = "";
    [Name("var")] public string Var { get; init; } = "";
}

public sealed class DataTypeRow : ReportRow
{
    [Name("Fraza")] public string Fraza { get; init; } = "";
    [Name("Projekt")] public string Projekt { get; init; } = "";
    [Name("Plik")] public string Plik { get; init; } = "";
    [Name("Linia")] public int Linia { get; init; }
    [Name("Kolumna")] public int Kolumna { get; init; }
    [Name("Nazwa")] public string Nazwa { get; init; } = "";
    [Name("Typ")] public string Typ { get; init; } = "";
    [Name("Pełny typ CLR")] public string TypClr { get; init; } = "";
    [Name("Kategoria")] public string Kategoria { get; init; } = "";
    [Name("Nullable")] public string Nullable { get; init; } = "";
    [Name("Opis nullable")] public string OpisNullable { get; init; } = "";
    [Name("Generyczny")] public string Generyczny { get; init; } = "";
    [Name("Argumenty generyczne")] public string Argumenty { get; init; } = "";
    [Name("Typ z var")] public string Var { get; init; } = "";
}

public sealed class ReferenceRow : ReportRow
{
    [Name("Fraza")] public string Fraza { get; init; } = "";
    [Name("Symbol")] public string Symbol { get; init; } = "";
    [Name("Rodzaj")] public string Rodzaj { get; init; } = "";
    [Name("Użycie")] public string Uzycie { get; init; } = "";
    [Name("Projekt")] public string Projekt { get; init; } = "";
    [Name("Plik")] public string Plik { get; init; } = "";
    [Name("Linia")] public int Linia { get; init; }
    [Name("Kolumna")] public int Kolumna { get; init; }
    [Name("Składowa")] public string Skladowa { get; init; } = "";
    [Name("Projekt deklaracji")] public string ProjektDeklaracji { get; init; } = "";
    [Name("Deklaracja")] public string Deklaracja { get; init; } = "";
    [Name("Kod")] public string Kod { get; init; } = "";
    [Name("Uwagi")] public string Uwagi { get; init; } = "";
}

public sealed class DependencyRow : ReportRow
{
    [Name("Fraza")] public string Fraza { get; init; } = "";
    [Name("Z projektu")] public string Z { get; init; } = "";
    [Name("Do")] public string Do { get; init; } = "";
    [Name("Rodzaj")] public string Rodzaj { get; init; } = "";
    [Name("Liczba odwołań")] public int Liczba { get; init; }
    [Name("Szczegóły")] public string Szczegoly { get; init; } = "";
}

public sealed class PrecisionRow : ReportRow
{
    [Name("Fraza")] public string Fraza { get; init; } = "";
    [Name("Projekt")] public string Projekt { get; init; } = "";
    [Name("Plik")] public string Plik { get; init; } = "";
    [Name("Linia")] public int Linia { get; init; }
    [Name("Kolumna")] public int Kolumna { get; init; }
    [Name("Źródło")] public string Zrodlo { get; init; } = "";
    [Name("Element")] public string Element { get; init; } = "";
    [Name("Typ")] public string Typ { get; init; } = "";
    [Name("Wartość")] public string Wartosc { get; init; } = "";
    [Name("Miejsca po przecinku")] public string Miejsca { get; init; } = "";
    [Name("Precyzja")] public string Precyzja { get; init; } = "";
    [Name("Skala")] public string Skala { get; init; } = "";
    [Name("Opis")] public string Opis { get; init; } = "";
    [Name("Kod")] public string Kod { get; init; } = "";
}

public sealed class RoundingRow : ReportRow
{
    [Name("Fraza")] public string Fraza { get; init; } = "";
    [Name("Projekt")] public string Projekt { get; init; } = "";
    [Name("Plik")] public string Plik { get; init; } = "";
    [Name("Linia")] public int Linia { get; init; }
    [Name("Kolumna")] public int Kolumna { get; init; }
    [Name("Metoda")] public string Metoda { get; init; } = "";
    [Name("Miejsca")] public string Miejsca { get; init; } = "";
    [Name("MidpointRounding")] public string Tryb { get; init; } = "";
    [Name("Typ argumentu")] public string TypArgumentu { get; init; } = "";
    [Name("Wartość")] public string Wartosc { get; init; } = "";
    [Name("Cel")] public string Cel { get; init; } = "";
    [Name("Składowa")] public string Skladowa { get; init; } = "";
    [Name("Kod")] public string Kod { get; init; } = "";
    [Name("Uwagi")] public string Uwagi { get; init; } = "";
}

public sealed class ConversionRow : ReportRow
{
    [Name("Fraza")] public string Fraza { get; init; } = "";
    [Name("Projekt")] public string Projekt { get; init; } = "";
    [Name("Plik")] public string Plik { get; init; } = "";
    [Name("Linia")] public int Linia { get; init; }
    [Name("Kolumna")] public int Kolumna { get; init; }
    [Name("Rodzaj")] public string Rodzaj { get; init; } = "";
    [Name("Z typu")] public string Z { get; init; } = "";
    [Name("Do typu")] public string Do { get; init; } = "";
    [Name("Metoda / operator")] public string Metoda { get; init; } = "";
    [Name("Utrata precyzji")] public string Utrata { get; init; } = "";
    [Name("Przepełnienie")] public string Przepelnienie { get; init; } = "";
    [Name("Cel")] public string Cel { get; init; } = "";
    [Name("Wyrażenie")] public string Wyrazenie { get; init; } = "";
    [Name("Opis")] public string Opis { get; init; } = "";
}

public sealed class DataFlowRow : ReportRow
{
    [Name("Fraza")] public string Fraza { get; init; } = "";
    [Name("Przepływ")] public string Przeplyw { get; init; } = "";
    [Name("Symbol źródłowy")] public string Symbol { get; init; } = "";
    [Name("Krok")] public int Krok { get; init; }
    [Name("Głębokość")] public int Glebokosc { get; init; }
    [Name("Rodzaj kroku")] public string Rodzaj { get; init; } = "";
    [Name("Opis")] public string Opis { get; init; } = "";
    [Name("Pewność")] public string Pewnosc { get; init; } = "";
    [Name("Projekt")] public string Projekt { get; init; } = "";
    [Name("Plik")] public string Plik { get; init; } = "";
    [Name("Linia")] public int Linia { get; init; }
    [Name("Kolumna")] public int Kolumna { get; init; }
    [Name("Przepływ kompletny")] public string Kompletny { get; init; } = "";
    [Name("Powód niekompletności")] public string Powod { get; init; } = "";
    [Name("Kod")] public string Kod { get; init; } = "";
}

public sealed class ProblemRow : ReportRow
{
    [Name("Poziom")] public string Poziom { get; init; } = "";
    [Name("Reguła")] public string Regula { get; init; } = "";
    [Name("Kategoria")] public string Kategoria { get; init; } = "";
    [Name("Tytuł")] public string Tytul { get; init; } = "";
    [Name("Uzasadnienie")] public string Uzasadnienie { get; init; } = "";
    [Name("Fraza")] public string Fraza { get; init; } = "";
    [Name("Symbol")] public string Symbol { get; init; } = "";
    [Name("Projekt")] public string Projekt { get; init; } = "";
    [Name("Plik")] public string Plik { get; init; } = "";
    [Name("Linia")] public int Linia { get; init; }
    [Name("Kolumna")] public int Kolumna { get; init; }
    [Name("Kod")] public string Kod { get; init; } = "";
}

public sealed class LimitationRow : ReportRow
{
    [Name("Rodzaj")] public string Rodzaj { get; init; } = "";
    [Name("Projekt")] public string Projekt { get; init; } = "";
    [Name("Plik")] public string Plik { get; init; } = "";
    [Name("Linia")] public int Linia { get; init; }
    [Name("Opis")] public string Opis { get; init; } = "";
    [Name("Powiązana nazwa")] public string Nazwa { get; init; } = "";
}

public sealed class SummaryRow : ReportRow
{
    [Name("Sekcja")] public string Sekcja { get; init; } = "";
    [Name("Metryka")] public string Metryka { get; init; } = "";
    [Name("Wartość")] public string Wartosc { get; init; } = "";
}

/// <summary>Zamiana modeli wyników na wiersze raportów.</summary>
public static class ReportRowMapper
{
    private static string N(int? v) => v?.ToString() ?? "";

    public static List<SearchRow> Search(AnalysisResult r) => r.SearchHits.Select(h => new SearchRow
    {
        Fraza = h.Phrase, Projekt = h.Location.ProjectName, Plik = h.Location.RelativePath, Linia = h.Location.Line, Kolumna = h.Location.Column,
        Nazwa = h.MatchedText, Rodzaj = DisplayNames.Of(h.ElementKind), Uzycie = DisplayNames.Of(h.UsageKind), Symbol = h.SymbolName,
        Typ = h.SymbolType, Skladowa = h.ContainingMember, Jezyk = h.Language, Metoda = DisplayNames.Of(h.Method), Kod = h.CodeLine, Uwagi = h.Note
    }).ToList();

    public static List<DeclarationRow> Declarations(AnalysisResult r) => r.Declarations.Select(d => new DeclarationRow
    {
        Fraza = d.Phrase, Projekt = d.Location.ProjectName, Plik = d.Location.RelativePath, Linia = d.Location.Line, Kolumna = d.Location.Column,
        Nazwa = d.Name, Rodzaj = DisplayNames.Of(d.Kind), Typ = d.TypeName, TypClr = d.FullTypeName, Nullable = DisplayNames.YesNo(d.IsNullable),
        Dostep = d.Accessibility, Zakres = d.Scope, Wartosc = d.InitialValue, Const = DisplayNames.YesNo(d.IsConst),
        Static = DisplayNames.YesNo(d.IsStatic), ReadOnly = DisplayNames.YesNo(d.IsReadOnly), Var = DisplayNames.YesNo(d.IsVar)
    }).ToList();

    public static List<DataTypeRow> DataTypes(AnalysisResult r) => r.Declarations.Select(d => new DataTypeRow
    {
        Fraza = d.Phrase, Projekt = d.Location.ProjectName, Plik = d.Location.RelativePath, Linia = d.Location.Line, Kolumna = d.Location.Column,
        Nazwa = d.Name, Typ = d.TypeName, TypClr = d.FullTypeName, Kategoria = d.TypeCategory, Nullable = DisplayNames.YesNo(d.IsNullable),
        OpisNullable = d.NullabilityDescription, Generyczny = DisplayNames.YesNo(d.IsGeneric), Argumenty = d.GenericArguments, Var = DisplayNames.YesNo(d.IsVar)
    }).ToList();

    public static List<ReferenceRow> References(AnalysisResult r) => r.References.Select(x => new ReferenceRow
    {
        Fraza = x.Phrase, Symbol = x.SymbolFullName, Rodzaj = DisplayNames.Of(x.ElementKind), Uzycie = DisplayNames.Of(x.UsageKind),
        Projekt = x.Location.ProjectName, Plik = x.Location.RelativePath, Linia = x.Location.Line, Kolumna = x.Location.Column,
        Skladowa = x.ContainingMember, ProjektDeklaracji = x.DeclaringProject, Deklaracja = x.DeclarationLocation.ToString(), Kod = x.CodeLine, Uwagi = x.Note
    }).ToList();

    public static List<DependencyRow> Dependencies(AnalysisResult r) => r.Dependencies.Select(d => new DependencyRow
    {
        Fraza = d.Kind.StartsWith("Użycie") ? string.Join(", ", r.Settings.Phrases) : "",
        Z = d.FromProject, Do = d.ToProject, Rodzaj = d.Kind, Liczba = d.ReferenceCount, Szczegoly = d.Details
    }).ToList();

    public static List<PrecisionRow> Precision(AnalysisResult r) => r.Precision.Select(p => new PrecisionRow
    {
        Fraza = p.Phrase, Projekt = p.Location.ProjectName, Plik = p.Location.RelativePath, Linia = p.Location.Line, Kolumna = p.Location.Column,
        Zrodlo = DisplayNames.Of(p.Source), Element = p.ElementName, Typ = p.TypeName, Wartosc = p.ValueText, Miejsca = N(p.DecimalPlaces),
        Precyzja = N(p.Precision), Skala = N(p.Scale), Opis = p.Description, Kod = p.CodeLine
    }).ToList();

    public static List<RoundingRow> Roundings(AnalysisResult r) => r.Roundings.Select(x => new RoundingRow
    {
        Fraza = x.Phrase, Projekt = x.Location.ProjectName, Plik = x.Location.RelativePath, Linia = x.Location.Line, Kolumna = x.Location.Column,
        Metoda = x.Method, Miejsca = x.DigitsExpression, Tryb = x.MidpointMode, TypArgumentu = x.ArgumentType, Wartosc = x.ValueExpression,
        Cel = x.TargetName, Skladowa = x.ContainingMember, Kod = x.CodeLine, Uwagi = x.Note
    }).ToList();

    public static List<ConversionRow> Conversions(AnalysisResult r) =>
        r.Conversions.Select(c => new ConversionRow
        {
            Fraza = c.Phrase, Projekt = c.Location.ProjectName, Plik = c.Location.RelativePath, Linia = c.Location.Line, Kolumna = c.Location.Column,
            Rodzaj = DisplayNames.Of(c.Kind), Z = c.FromType, Do = c.ToType, Metoda = c.Method, Utrata = DisplayNames.YesNo(c.IsPotentialPrecisionLoss),
            Przepelnienie = DisplayNames.YesNo(c.IsPotentialOverflow), Cel = c.TargetName, Wyrazenie = c.Expression, Opis = c.Description
        }).Concat(r.Arithmetic.Select(a => new ConversionRow
        {
            Fraza = a.Phrase, Projekt = a.Location.ProjectName, Plik = a.Location.RelativePath, Linia = a.Location.Line, Kolumna = a.Location.Column,
            Rodzaj = a.IsIntegerDivision ? "Dzielenie całkowite" : "Operacja arytmetyczna", Z = $"{a.LeftType} {a.Operator} {a.RightType}", Do = a.ResultType,
            Metoda = a.Operator + (a.IsChecked ? " (checked)" : ""), Utrata = DisplayNames.YesNo(a.IsIntegerDivision), Przepelnienie = "",
            Cel = a.TargetName, Wyrazenie = a.Expression, Opis = a.Notes
        })).OrderBy(c => c.Projekt).ThenBy(c => c.Plik).ThenBy(c => c.Linia).ThenBy(c => c.Kolumna).ToList();

    public static List<DataFlowRow> DataFlow(AnalysisResult r) => r.DataFlow.Select(s => new DataFlowRow
    {
        Fraza = s.Phrase, Przeplyw = s.FlowId, Symbol = s.RootSymbol, Krok = s.StepNumber, Glebokosc = s.Depth, Rodzaj = s.StepKind,
        Opis = s.Description, Pewnosc = DisplayNames.Of(s.Certainty), Projekt = s.Location.ProjectName, Plik = s.Location.RelativePath,
        Linia = s.Location.Line, Kolumna = s.Location.Column, Kompletny = DisplayNames.YesNo(s.IsFlowComplete), Powod = s.IncompletenessReason, Kod = s.CodeLine
    }).ToList();

    public static List<ProblemRow> Problems(AnalysisResult r) => r.Problems.Select(p => new ProblemRow
    {
        Poziom = DisplayNames.Of(p.Severity), Regula = p.RuleId, Kategoria = p.Category, Tytul = p.Title, Uzasadnienie = p.Justification,
        Fraza = p.Phrase, Symbol = p.SymbolName, Projekt = p.Location.ProjectName, Plik = p.Location.RelativePath,
        Linia = p.Location.Line, Kolumna = p.Location.Column, Kod = p.CodeLine
    }).ToList();

    public static List<LimitationRow> Limitations(AnalysisResult r) =>
        r.LoadDiagnostics.Select(d => new LimitationRow { Rodzaj = "Wczytywanie: " + DisplayNames.Of(d.Severity), Projekt = d.Project, Opis = d.Message })
        .Concat(r.Limitations.Select(l => new LimitationRow
        {
            Rodzaj = DisplayNames.Of(l.Kind), Projekt = l.Project, Plik = l.Location.RelativePath, Linia = l.Location.Line, Opis = l.Description, Nazwa = l.RelatedName
        })).ToList();
}
