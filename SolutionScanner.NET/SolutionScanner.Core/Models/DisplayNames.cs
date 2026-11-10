namespace SolutionScanner.Core.Models;

/// <summary>Polskie nazwy wartości wyliczeniowych używane w UI i raportach.</summary>
public static class DisplayNames
{
    public static string Of(Severity s) => s switch
    {
        Severity.Info => "INFO",
        Severity.Warning => "WARNING",
        Severity.Error => "ERROR",
        _ => s.ToString()
    };

    public static string Of(ElementKind k) => k switch
    {
        ElementKind.Unknown => "Nierozpoznany",
        ElementKind.LocalVariable => "Zmienna lokalna",
        ElementKind.Field => "Pole",
        ElementKind.Property => "Właściwość",
        ElementKind.Parameter => "Parametr",
        ElementKind.Method => "Metoda",
        ElementKind.Constructor => "Konstruktor",
        ElementKind.Class => "Klasa",
        ElementKind.Record => "Rekord",
        ElementKind.Interface => "Interfejs",
        ElementKind.Struct => "Struktura",
        ElementKind.Enum => "Enum",
        ElementKind.EnumMember => "Wartość enum",
        ElementKind.Constant => "Stała",
        ElementKind.Event => "Zdarzenie",
        ElementKind.Delegate => "Delegat",
        ElementKind.Namespace => "Przestrzeń nazw",
        ElementKind.TypeParameter => "Parametr typu",
        ElementKind.Label => "Etykieta",
        ElementKind.SqlTable => "Tabela SQL",
        ElementKind.SqlView => "Widok SQL",
        ElementKind.SqlProcedure => "Procedura SQL",
        ElementKind.SqlFunction => "Funkcja SQL",
        ElementKind.SqlColumn => "Kolumna SQL",
        ElementKind.SqlParameter => "Parametr SQL",
        ElementKind.SqlVariable => "Zmienna SQL",
        ElementKind.SqlType => "Typ SQL",
        ElementKind.SqlIdentifier => "Identyfikator SQL",
        ElementKind.XmlElement => "Element XML",
        ElementKind.XmlAttribute => "Atrybut XML",
        ElementKind.XmlAttributeValue => "Wartość atrybutu XML",
        ElementKind.JsonProperty => "Właściwość JSON",
        ElementKind.JsonValue => "Wartość JSON",
        ElementKind.RazorIdentifier => "Identyfikator Razor",
        _ => k.ToString()
    };

    public static string Of(UsageKind u) => u switch
    {
        UsageKind.Declaration => "Deklaracja",
        UsageKind.Read => "Odczyt",
        UsageKind.Write => "Zapis",
        UsageKind.ReadWrite => "Odczyt i zapis",
        UsageKind.Invocation => "Wywołanie metody",
        UsageKind.ArgumentPass => "Przekazanie argumentu",
        UsageKind.Return => "Zwrócenie wartości",
        UsageKind.ObjectCreation => "Utworzenie obiektu",
        UsageKind.NameOf => "nameof",
        UsageKind.NamedArgument => "Argument nazwany",
        UsageKind.TypeReference => "Użycie typu",
        UsageKind.Unknown => "Nieustalone",
        _ => u.ToString()
    };

    public static string Of(AnalysisMethod m) => m switch
    {
        AnalysisMethod.Semantic => "Semantyczna (Roslyn)",
        AnalysisMethod.SemanticCandidate => "Semantyczna – kandydat",
        AnalysisMethod.SyntaxOnly => "Składniowa (symbol nierozpoznany)",
        AnalysisMethod.Token => "Tokenizer",
        AnalysisMethod.Xml => "Struktura XML",
        AnalysisMethod.Json => "Struktura JSON",
        _ => m.ToString()
    };

    public static string Of(PrecisionSource p) => p switch
    {
        PrecisionSource.Literal => "Literał w kodzie",
        PrecisionSource.DecimalValueScale => "Skala wartości decimal",
        PrecisionSource.TypeRepresentation => "Reprezentacja typu",
        PrecisionSource.SqlType => "Typ SQL",
        PrecisionSource.ExplicitRounding => "Jawne zaokrąglenie",
        PrecisionSource.Formatting => "Formatowanie",
        PrecisionSource.EfMapping => "Mapowanie EF6",
        _ => p.ToString()
    };

    public static string Of(ConversionKind c) => c switch
    {
        ConversionKind.Implicit => "Niejawna",
        ConversionKind.ExplicitCast => "Jawne rzutowanie",
        ConversionKind.ConvertMethod => "Convert.*",
        ConversionKind.DecimalMethod => "Metoda Decimal.*",
        ConversionKind.Parse => "Parse/TryParse",
        ConversionKind.SqlCast => "SQL CAST",
        ConversionKind.SqlConvert => "SQL CONVERT",
        ConversionKind.SqlImplicit => "SQL niejawna",
        _ => c.ToString()
    };

    public static string Of(MappingStatus s) => s switch
    {
        MappingStatus.Explicit => "Jawne",
        MappingStatus.Convention => "Konwencja EF6",
        MappingStatus.Undetermined => "Nieustalone",
        _ => s.ToString()
    };

    public static string Of(Certainty c) => c switch
    {
        Certainty.Confirmed => "Potwierdzone",
        Certainty.Probable => "Prawdopodobne",
        Certainty.Unconfirmed => "Niepotwierdzone",
        _ => c.ToString()
    };

    public static string Of(ComparisonOutcome o) => o switch
    {
        ComparisonOutcome.Compatible => "ZGODNE",
        ComparisonOutcome.Difference => "RÓŻNICA",
        ComparisonOutcome.Undetermined => "NIEUSTALONE",
        _ => o.ToString()
    };

    public static string Of(SqlItemType t) => t switch
    {
        SqlItemType.Table => "Tabela",
        SqlItemType.View => "Widok",
        SqlItemType.Procedure => "Procedura",
        SqlItemType.Function => "Funkcja",
        SqlItemType.Column => "Kolumna",
        SqlItemType.Parameter => "Parametr",
        SqlItemType.Variable => "Zmienna",
        SqlItemType.ReturnType => "Typ zwracany",
        SqlItemType.UserDefinedType => "Typ użytkownika",
        SqlItemType.Cast => "CAST",
        SqlItemType.Convert => "CONVERT",
        SqlItemType.Round => "ROUND",
        SqlItemType.DynamicSql => "Dynamiczny SQL",
        SqlItemType.ParseError => "Błąd parsowania",
        _ => t.ToString()
    };

    public static string Of(LimitationKind k) => k switch
    {
        LimitationKind.Reflection => "Refleksja",
        LimitationKind.Dynamic => "Typ dynamic",
        LimitationKind.DynamicSql => "Dynamiczny SQL",
        LimitationKind.UnresolvedSymbols => "Nierozpoznane symbole",
        LimitationKind.ProjectLoad => "Wczytywanie projektu",
        LimitationKind.FallbackProject => "Projekt wczytany awaryjnie",
        LimitationKind.ParseError => "Błąd parsowania",
        LimitationKind.FileError => "Błąd pliku",
        LimitationKind.ReferenceSearchLimit => "Limit wyszukiwania odwołań",
        LimitationKind.Language => "Ograniczenie języka",
        _ => k.ToString()
    };

    public static string YesNo(bool value) => value ? "Tak" : "Nie";

    public static string YesNo(bool? value) => value switch
    {
        true => "Tak",
        false => "Nie",
        null => "Nieustalone"
    };
}
