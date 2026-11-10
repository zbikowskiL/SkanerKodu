namespace SolutionScanner.Core.Models;

/// <summary>Poziom istotności potencjalnego problemu lub różnicy.</summary>
public enum Severity
{
    Info = 0,
    Warning = 1,
    Error = 2
}

/// <summary>Rodzaj elementu kodu (C#, VB, SQL, pliki tekstowe), którego dotyczy wynik.</summary>
public enum ElementKind
{
    Unknown,
    LocalVariable,
    Field,
    Property,
    Parameter,
    Method,
    Constructor,
    Class,
    Record,
    Interface,
    Struct,
    Enum,
    EnumMember,
    Constant,
    Event,
    Delegate,
    Namespace,
    TypeParameter,
    Label,
    SqlTable,
    SqlView,
    SqlProcedure,
    SqlFunction,
    SqlColumn,
    SqlParameter,
    SqlVariable,
    SqlType,
    SqlIdentifier,
    XmlElement,
    XmlAttribute,
    XmlAttributeValue,
    JsonProperty,
    JsonValue,
    RazorIdentifier
}

/// <summary>Sposób użycia symbolu w danym miejscu kodu.</summary>
public enum UsageKind
{
    Declaration,
    Read,
    Write,
    ReadWrite,
    Invocation,
    ArgumentPass,
    Return,
    ObjectCreation,
    NameOf,
    NamedArgument,
    TypeReference,
    Unknown
}

/// <summary>Metoda, którą ustalono wynik – pozwala odróżnić wynik semantyczny od przybliżonego.</summary>
public enum AnalysisMethod
{
    /// <summary>Roslyn SemanticModel – symbol rozpoznany przez kompilator.</summary>
    Semantic,
    /// <summary>Symbol rozpoznany tylko jako kandydat (np. przeciążenia, błąd kompilacji).</summary>
    SemanticCandidate,
    /// <summary>Drzewo składniowe bez rozpoznanego symbolu.</summary>
    SyntaxOnly,
    /// <summary>Tokenizer języka (T-SQL ScriptDom, tokenizer C# dla Razor).</summary>
    Token,
    /// <summary>Struktura XML (elementy/atrybuty).</summary>
    Xml,
    /// <summary>Struktura JSON (nazwy właściwości/wartości).</summary>
    Json
}

/// <summary>Źródło informacji o precyzji / liczbie miejsc po przecinku.</summary>
public enum PrecisionSource
{
    /// <summary>Liczba miejsc po przecinku zapisana w literale w kodzie.</summary>
    Literal,
    /// <summary>Skala wartości decimal ustalona statycznie (stała czasu kompilacji).</summary>
    DecimalValueScale,
    /// <summary>Charakterystyka reprezentacji typu (decimal / float / double).</summary>
    TypeRepresentation,
    /// <summary>Precyzja i skala typu SQL.</summary>
    SqlType,
    /// <summary>Liczba miejsc wynikająca z jawnego zaokrąglenia.</summary>
    ExplicitRounding,
    /// <summary>Liczba miejsc wynikająca z formatowania wartości.</summary>
    Formatting,
    /// <summary>Precyzja/skala zadeklarowana w mapowaniu EF6.</summary>
    EfMapping
}

/// <summary>Rodzaj konwersji typu.</summary>
public enum ConversionKind
{
    Implicit,
    ExplicitCast,
    ConvertMethod,
    DecimalMethod,
    Parse,
    SqlCast,
    SqlConvert,
    SqlImplicit
}

/// <summary>Status ustalenia mapowania EF6.</summary>
public enum MappingStatus
{
    /// <summary>Mapowanie jawnie skonfigurowane (atrybut, Fluent API, EDMX).</summary>
    Explicit,
    /// <summary>Mapowanie wynikające z konwencji EF6 – encja potwierdzona, nazwa/typ z reguł konwencji.</summary>
    Convention,
    /// <summary>Mapowania nie można ustalić statycznie.</summary>
    Undetermined
}

/// <summary>Pewność kroku przepływu danych.</summary>
public enum Certainty
{
    Confirmed,
    Probable,
    Unconfirmed
}

/// <summary>Wynik porównania C# ↔ SQL.</summary>
public enum ComparisonOutcome
{
    Compatible,
    Difference,
    Undetermined
}

/// <summary>Rodzaj elementu schematu SQL.</summary>
public enum SqlItemType
{
    Table,
    View,
    Procedure,
    Function,
    Column,
    Parameter,
    Variable,
    ReturnType,
    UserDefinedType,
    Cast,
    Convert,
    Round,
    DynamicSql,
    ParseError
}

/// <summary>Rodzaj ograniczenia analizy statycznej.</summary>
public enum LimitationKind
{
    Reflection,
    Dynamic,
    DynamicSql,
    UnresolvedSymbols,
    ProjectLoad,
    FallbackProject,
    ParseError,
    FileError,
    ReferenceSearchLimit,
    Language
}
