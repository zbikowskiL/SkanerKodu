namespace SolutionScanner.Core.Models;

/// <summary>Pojedyncze wystąpienie wyszukiwanej frazy w identyfikatorze (jeden rekord = jedno wystąpienie × fraza).</summary>
public sealed class SearchHit
{
    public string Phrase { get; init; } = string.Empty;
    public string MatchedText { get; init; } = string.Empty;
    public ElementKind ElementKind { get; init; }
    public UsageKind UsageKind { get; init; }
    public string Language { get; init; } = string.Empty;
    public AnalysisMethod Method { get; init; }
    public string SymbolName { get; init; } = string.Empty;
    public string SymbolType { get; init; } = string.Empty;
    public string ContainingMember { get; init; } = string.Empty;
    public string CodeLine { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
    public CodeLocation Location { get; init; } = CodeLocation.Empty;
}

/// <summary>Deklaracja zmiennej, pola, właściwości, parametru lub stałej wraz z informacją o typie.</summary>
public sealed class VariableDeclarationInfo
{
    public string Phrase { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public ElementKind Kind { get; init; }
    public string TypeName { get; init; } = string.Empty;
    public string FullTypeName { get; init; } = string.Empty;
    public string TypeCategory { get; init; } = string.Empty;
    /// <summary>true/false gdy ustalone; null gdy kontekst nullable dla typów referencyjnych jest wyłączony.</summary>
    public bool? IsNullable { get; init; }
    public string NullabilityDescription { get; init; } = string.Empty;
    public string Accessibility { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public string ContainingType { get; init; } = string.Empty;
    public string ContainingMember { get; init; } = string.Empty;
    public string InitialValue { get; init; } = string.Empty;
    public bool IsConst { get; init; }
    public bool IsStatic { get; init; }
    public bool IsReadOnly { get; init; }
    public bool IsVar { get; init; }
    public bool IsGeneric { get; init; }
    public string GenericArguments { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public CodeLocation Location { get; init; } = CodeLocation.Empty;
    public string SymbolKey { get; init; } = string.Empty;
}

/// <summary>Odwołanie do symbolu odnalezione przez SymbolFinder.FindReferencesAsync (lub deklaracja symbolu).</summary>
public sealed class SymbolReferenceInfo
{
    public string Phrase { get; init; } = string.Empty;
    public string SymbolName { get; init; } = string.Empty;
    public string SymbolFullName { get; init; } = string.Empty;
    public ElementKind ElementKind { get; init; }
    public UsageKind UsageKind { get; init; }
    public string ContainingMember { get; init; } = string.Empty;
    public string CodeLine { get; init; } = string.Empty;
    public string DeclaringProject { get; init; } = string.Empty;
    public CodeLocation DeclarationLocation { get; init; } = CodeLocation.Empty;
    public bool IsCandidate { get; init; }
    public bool IsImplicit { get; init; }
    public string Note { get; init; } = string.Empty;
    public CodeLocation Location { get; init; } = CodeLocation.Empty;
    public string SymbolKey { get; init; } = string.Empty;
}

/// <summary>Zależność pomiędzy projektami lub projekt → pakiet / biblioteka.</summary>
public sealed class ProjectDependency
{
    public string FromProject { get; init; } = string.Empty;
    public string ToProject { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public int ReferenceCount { get; init; }
}

/// <summary>Informacja o precyzji / liczbie miejsc po przecinku.</summary>
public sealed class PrecisionInfo
{
    public string Phrase { get; init; } = string.Empty;
    public PrecisionSource Source { get; init; }
    public string ElementName { get; init; } = string.Empty;
    public string TypeName { get; init; } = string.Empty;
    public string ValueText { get; init; } = string.Empty;
    /// <summary>Liczba miejsc po przecinku zapisana w literale / wynikająca z zaokrąglenia lub formatu.</summary>
    public int? DecimalPlaces { get; init; }
    public int? Precision { get; init; }
    public int? Scale { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string CodeLine { get; init; } = string.Empty;
    public string TargetSymbolKey { get; init; } = string.Empty;
    public CodeLocation Location { get; init; } = CodeLocation.Empty;
}

/// <summary>Jawne zaokrąglenie wartości (Math.Round, decimal.Round, ROUND w SQL itp.).</summary>
public sealed class RoundingInfo
{
    public string Phrase { get; init; } = string.Empty;
    public string Method { get; init; } = string.Empty;
    public int? Digits { get; init; }
    public string DigitsExpression { get; init; } = string.Empty;
    public string MidpointMode { get; init; } = string.Empty;
    public bool IsDefaultMidpoint { get; init; }
    public string ArgumentType { get; init; } = string.Empty;
    public string ValueExpression { get; init; } = string.Empty;
    public string TargetName { get; init; } = string.Empty;
    public string TargetSymbolKey { get; init; } = string.Empty;
    public string ContainingMember { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string CodeLine { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
    public CodeLocation Location { get; init; } = CodeLocation.Empty;
}

/// <summary>Konwersja typu (jawna, niejawna, Convert.*, Parse, CAST/CONVERT).</summary>
public sealed class ConversionInfo
{
    public string Phrase { get; init; } = string.Empty;
    public ConversionKind Kind { get; init; }
    public string FromType { get; init; } = string.Empty;
    public string ToType { get; init; } = string.Empty;
    public string Method { get; init; } = string.Empty;
    public string Expression { get; init; } = string.Empty;
    public bool IsImplicit { get; init; }
    public bool IsConstant { get; init; }
    public bool IsPotentialPrecisionLoss { get; init; }
    public bool IsPotentialOverflow { get; init; }
    public string Description { get; init; } = string.Empty;
    public string ContainingMember { get; init; } = string.Empty;
    public string TargetName { get; init; } = string.Empty;
    public string TargetSymbolKey { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string CodeLine { get; init; } = string.Empty;
    public CodeLocation Location { get; init; } = CodeLocation.Empty;
}

/// <summary>Operacja arytmetyczna na typach liczbowych.</summary>
public sealed class ArithmeticInfo
{
    public string Phrase { get; init; } = string.Empty;
    public string Operator { get; init; } = string.Empty;
    public string LeftType { get; init; } = string.Empty;
    public string RightType { get; init; } = string.Empty;
    public string ResultType { get; init; } = string.Empty;
    public string Expression { get; init; } = string.Empty;
    public bool IsIntegerDivision { get; init; }
    public bool IsChecked { get; init; }
    public string ConstantValue { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public string ContainingMember { get; init; } = string.Empty;
    public string TargetName { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string CodeLine { get; init; } = string.Empty;
    public CodeLocation Location { get; init; } = CodeLocation.Empty;
}

/// <summary>Element schematu SQL (tabela, kolumna, parametr, zmienna, CAST, ROUND...).</summary>
public sealed class SqlSchemaItem
{
    public string Phrase { get; init; } = string.Empty;
    public SqlItemType ItemType { get; init; }
    public string Operation { get; init; } = string.Empty;
    public string Schema { get; init; } = string.Empty;
    public string ObjectName { get; init; } = string.Empty;
    public string ItemName { get; init; } = string.Empty;
    public SqlDataTypeInfo? DataType { get; init; }
    public bool? IsNullable { get; init; }
    public string DefaultValue { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public string Source { get; init; } = "SQL";
    public string CodeLine { get; init; } = string.Empty;
    public CodeLocation Location { get; init; } = CodeLocation.Empty;

    public string QualifiedObjectName => string.IsNullOrEmpty(Schema) ? ObjectName : $"{Schema}.{ObjectName}";
}

/// <summary>Mapowanie EF6: klasa/właściwość C# ↔ tabela/kolumna SQL.</summary>
public sealed class EfMapping
{
    public string Phrase { get; init; } = string.Empty;
    public string Framework { get; init; } = "EF6";
    public string MappingKind { get; init; } = "Właściwość";
    public string ContextName { get; init; } = string.Empty;
    public string EntityClass { get; init; } = string.Empty;
    public string EntityClassFullName { get; init; } = string.Empty;
    public string PropertyName { get; init; } = string.Empty;
    public string CSharpType { get; init; } = string.Empty;
    public bool IsCSharpNullable { get; init; }
    public string TableSchema { get; init; } = string.Empty;
    public string TableName { get; init; } = string.Empty;
    public string TableNameSource { get; init; } = string.Empty;
    public string ColumnName { get; init; } = string.Empty;
    public string ColumnNameSource { get; init; } = string.Empty;
    public string ConfiguredSqlType { get; init; } = string.Empty;
    public int? Precision { get; init; }
    public int? Scale { get; init; }
    public string PrecisionSource { get; init; } = string.Empty;
    public int? MaxLength { get; init; }
    public bool IsMaxLength { get; init; }
    public bool? IsUnicode { get; init; }
    public bool? IsRequired { get; init; }
    public string RequiredSource { get; init; } = string.Empty;
    public bool IsNotMapped { get; init; }
    public MappingStatus Status { get; init; }
    public string Evidence { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public string PropertySymbolKey { get; init; } = string.Empty;
    public CodeLocation Location { get; init; } = CodeLocation.Empty;
    public CodeLocation ConfigurationLocation { get; init; } = CodeLocation.Empty;

    public string QualifiedTableName => string.IsNullOrEmpty(TableSchema) ? TableName : $"{TableSchema}.{TableName}";
}

/// <summary>Wynik porównania właściwości C# (z mapowaniem EF6) z kolumną SQL.</summary>
public sealed class MappingComparison
{
    public string Phrase { get; init; } = string.Empty;
    public ComparisonOutcome Outcome { get; init; }
    public Severity Severity { get; init; }
    public string Category { get; init; } = string.Empty;
    public string EntityClass { get; init; } = string.Empty;
    public string PropertyName { get; init; } = string.Empty;
    public string CSharpType { get; init; } = string.Empty;
    public string EfSqlType { get; init; } = string.Empty;
    public string SqlTable { get; init; } = string.Empty;
    public string SqlColumn { get; init; } = string.Empty;
    public string SqlType { get; init; } = string.Empty;
    public string CSharpNullable { get; init; } = string.Empty;
    public string SqlNullable { get; init; } = string.Empty;
    public MappingStatus MappingStatus { get; init; }
    public string Description { get; init; } = string.Empty;
    public string PropertySymbolKey { get; init; } = string.Empty;
    public CodeLocation CSharpLocation { get; init; } = CodeLocation.Empty;
    public CodeLocation SqlLocation { get; init; } = CodeLocation.Empty;
}

/// <summary>Krok przepływu danych dla jednego symbolu źródłowego.</summary>
public sealed class DataFlowStep
{
    public string FlowId { get; init; } = string.Empty;
    public string Phrase { get; init; } = string.Empty;
    public string RootSymbol { get; init; } = string.Empty;
    public int StepNumber { get; init; }
    public int Depth { get; init; }
    public string StepKind { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public Certainty Certainty { get; init; }
    public bool IsFlowComplete { get; init; }
    public string IncompletenessReason { get; init; } = string.Empty;
    public string CodeLine { get; init; } = string.Empty;
    public CodeLocation Location { get; init; } = CodeLocation.Empty;
}

/// <summary>Potencjalny problem wymagający weryfikacji (nie jest dowiedzionym błędem).</summary>
public sealed class PotentialProblem
{
    public string RuleId { get; init; } = string.Empty;
    public Severity Severity { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Justification { get; init; } = string.Empty;
    public string SymbolName { get; init; } = string.Empty;
    public string Phrase { get; init; } = string.Empty;
    public string CodeLine { get; init; } = string.Empty;
    public CodeLocation Location { get; init; } = CodeLocation.Empty;
}

/// <summary>Miejsce, w którym analiza statyczna jest niepełna lub przybliżona.</summary>
public sealed class AnalysisLimitation
{
    public LimitationKind Kind { get; init; }
    public string Project { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string RelatedName { get; init; } = string.Empty;
    public CodeLocation Location { get; init; } = CodeLocation.Empty;
}

/// <summary>Pozycja podsumowania analizy.</summary>
public sealed record SummaryItem(string Section, string Metric, string Value);
