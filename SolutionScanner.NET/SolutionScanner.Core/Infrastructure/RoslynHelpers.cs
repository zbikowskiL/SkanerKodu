using Microsoft.CodeAnalysis;
using SolutionScanner.Core.Models;

namespace SolutionScanner.Core.Infrastructure;

/// <summary>Pomocnicze operacje na symbolach i typach Roslyn (niezależne od języka C#/VB).</summary>
public static class RoslynHelpers
{
    /// <summary>Format nazwy CLR: pełna przestrzeń nazw, bez słów kluczowych C#, Nullable&lt;T&gt; rozwinięte.</summary>
    public static readonly SymbolDisplayFormat ClrFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.ExpandNullable);

    /// <summary>Format nazwy składowej: Typ.Metoda(parametry).</summary>
    public static readonly SymbolDisplayFormat MemberFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType | SymbolDisplayMemberOptions.IncludeParameters,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>Krótka nazwa symbolu z typem zawierającym (np. Kredyt.Oprocentowanie).</summary>
    public static readonly SymbolDisplayFormat QualifiedNameFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public static string DisplayType(ITypeSymbol? type) =>
        type is null ? string.Empty : type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

    public static string ClrName(ITypeSymbol? type) =>
        type is null ? string.Empty : type.ToDisplayString(ClrFormat);

    public static string QualifiedName(ISymbol symbol)
    {
        try
        {
            return symbol switch
            {
                ILocalSymbol or IParameterSymbol or IRangeVariableSymbol => $"{MemberName(symbol.ContainingSymbol)}: {symbol.Name}",
                _ => symbol.ToDisplayString(QualifiedNameFormat)
            };
        }
        catch
        {
            return symbol.Name;
        }
    }

    public static string MemberName(ISymbol? symbol)
    {
        while (symbol is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction }) symbol = symbol.ContainingSymbol;
        if (symbol is null || symbol is INamespaceSymbol) return string.Empty;
        try { return symbol.ToDisplayString(MemberFormat); }
        catch { return symbol.Name; }
    }

    /// <summary>
    /// Stabilny klucz symbolu w obrębie jednej analizy. Dla składowych – identyfikator dokumentacji
    /// (M:/P:/F:/T:), dla zmiennych lokalnych i parametrów – nazwa + plik + pozycja deklaracji.
    /// </summary>
    public static string SymbolKey(ISymbol symbol)
    {
        var original = symbol.OriginalDefinition;
        if (original is not ILocalSymbol and not IParameterSymbol and not IRangeVariableSymbol and not ILabelSymbol)
        {
            var id = original.GetDocumentationCommentId();
            if (!string.IsNullOrEmpty(id)) return id;
        }
        var loc = original.Locations.FirstOrDefault(l => l.IsInSource);
        return loc is null
            ? $"{original.Kind}:{original.ToDisplayString()}"
            : $"{original.Kind}:{original.Name}@{loc.SourceTree?.FilePath}:{loc.SourceSpan.Start}";
    }

    public static ElementKind KindOf(ISymbol? symbol) => symbol switch
    {
        null => ElementKind.Unknown,
        ILocalSymbol l => l.IsConst ? ElementKind.Constant : ElementKind.LocalVariable,
        IRangeVariableSymbol => ElementKind.LocalVariable,
        IFieldSymbol f when f.ContainingType?.TypeKind == TypeKind.Enum => ElementKind.EnumMember,
        IFieldSymbol f => f.IsConst ? ElementKind.Constant : ElementKind.Field,
        IPropertySymbol => ElementKind.Property,
        IParameterSymbol => ElementKind.Parameter,
        IMethodSymbol m => m.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor ? ElementKind.Constructor : ElementKind.Method,
        IEventSymbol => ElementKind.Event,
        INamedTypeSymbol t => t.TypeKind switch
        {
            TypeKind.Class => t.IsRecord ? ElementKind.Record : ElementKind.Class,
            TypeKind.Struct => t.IsRecord ? ElementKind.Record : ElementKind.Struct,
            TypeKind.Interface => ElementKind.Interface,
            TypeKind.Enum => ElementKind.Enum,
            TypeKind.Delegate => ElementKind.Delegate,
            TypeKind.Module => ElementKind.Class,
            _ => ElementKind.Unknown
        },
        ITypeParameterSymbol => ElementKind.TypeParameter,
        INamespaceSymbol => ElementKind.Namespace,
        ILabelSymbol => ElementKind.Label,
        _ => ElementKind.Unknown
    };

    public static ITypeSymbol? TypeOf(ISymbol? symbol) => symbol switch
    {
        ILocalSymbol l => l.Type,
        IFieldSymbol f => f.Type,
        IPropertySymbol p => p.Type,
        IParameterSymbol p => p.Type,
        IMethodSymbol m => m.ReturnType,
        IEventSymbol e => e.Type,
        _ => null
    };

    /// <summary>Dla Nullable&lt;T&gt; zwraca T, w pozostałych przypadkach typ bez zmian.</summary>
    public static ITypeSymbol? Underlying(ITypeSymbol? type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n ? n.TypeArguments[0] : type;

    public static bool IsNullableValueType(ITypeSymbol? type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

    public static bool IsDecimal(ITypeSymbol? t) => Underlying(t)?.SpecialType == SpecialType.System_Decimal;

    public static bool IsBinaryFloatingPoint(ITypeSymbol? t) =>
        Underlying(t)?.SpecialType is SpecialType.System_Double or SpecialType.System_Single;

    public static bool IsIntegral(ITypeSymbol? t) => Underlying(t)?.SpecialType is
        SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16 or
        SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or
        SpecialType.System_Char;

    public static bool IsNumeric(ITypeSymbol? t) => IsIntegral(t) || IsDecimal(t) || IsBinaryFloatingPoint(t);

    /// <summary>Liczba bitów typu całkowitego (do oceny przepełnień).</summary>
    public static int IntegralBits(ITypeSymbol? t) => Underlying(t)?.SpecialType switch
    {
        SpecialType.System_SByte or SpecialType.System_Byte => 8,
        SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Char => 16,
        SpecialType.System_Int32 or SpecialType.System_UInt32 => 32,
        SpecialType.System_Int64 or SpecialType.System_UInt64 => 64,
        _ => 0
    };

    public static string TypeCategory(ITypeSymbol? type)
    {
        var t = Underlying(type);
        if (t is null) return string.Empty;
        if (t.TypeKind == TypeKind.Error) return "Nierozpoznany";
        return t.SpecialType switch
        {
            SpecialType.System_Decimal => "Dziesiętny (decimal)",
            SpecialType.System_Double or SpecialType.System_Single => "Zmiennoprzecinkowy binarny",
            SpecialType.System_Boolean => "Logiczny",
            SpecialType.System_String or SpecialType.System_Char => "Tekstowy",
            SpecialType.System_DateTime => "Data/czas",
            _ when IsIntegral(t) => "Całkowity",
            _ when t.TypeKind == TypeKind.Enum => "Wyliczeniowy (enum)",
            _ when t.TypeKind == TypeKind.Array => "Tablica",
            _ when t is INamedTypeSymbol { IsGenericType: true } => "Generyczny",
            _ when t.Name is "DateTimeOffset" or "TimeSpan" or "DateOnly" or "TimeOnly" => "Data/czas",
            _ when t.Name == "Guid" => "Identyfikator (Guid)",
            _ when t.Locations.Any(l => l.IsInSource) => "Typ własny",
            _ => "Typ biblioteczny"
        };
    }

    public static string AccessibilityText(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Protected => "protected",
        Accessibility.Private => "private",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        _ => "-"
    };

    /// <summary>Czy kompilacja projektu ma rozpoznaną bibliotekę bazową (warunek wiarygodnej analizy semantycznej).</summary>
    public static bool HasCoreLibrary(Compilation? compilation)
    {
        if (compilation is null) return false;
        var obj = compilation.GetSpecialType(SpecialType.System_Object);
        return obj.TypeKind != TypeKind.Error;
    }

    /// <summary>Nazwa projektu bez sufiksu docelowej platformy dodawanego przez MSBuildWorkspace (np. „Proj(net8.0)”).</summary>
    public static string CleanProjectName(string name)
    {
        var idx = name.LastIndexOf('(');
        return idx > 0 && name.EndsWith(')') ? name[..idx] : name;
    }
}
