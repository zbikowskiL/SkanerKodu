using System.Globalization;
using System.Text.RegularExpressions;

namespace SolutionScanner.Core.Models;

/// <summary>
/// Typ danych SQL Server z rozdzieloną precyzją, skalą i długością.
/// Wartości <see cref="Precision"/>/<see cref="Scale"/> są wartościami efektywnymi
/// (np. DECIMAL bez parametrów = DECIMAL(18,0), MONEY = 19,4); <see cref="IsPrecisionImplicit"/>
/// informuje, że nie zostały zapisane jawnie.
/// </summary>
public sealed record SqlDataTypeInfo
{
    public string Name { get; init; } = string.Empty;
    public int? Precision { get; init; }
    public int? Scale { get; init; }
    public int? Length { get; init; }
    public bool IsMax { get; init; }
    public bool IsPrecisionImplicit { get; init; }
    public bool IsUserDefined { get; init; }
    /// <summary>Dla typu użytkownika (CREATE TYPE ... FROM) – typ bazowy, jeżeli został rozpoznany.</summary>
    public SqlDataTypeInfo? BaseType { get; init; }

    private static readonly Regex TypePattern = new(
        @"^\s*(?:\[?(?<schema>[\w]+)\]?\.)?\[?(?<name>[A-Za-z_][\w ]*?)\]?\s*(?:\(\s*(?<a>max|\d+)\s*(?:,\s*(?<b>\d+)\s*)?\))?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> KnownTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "BIGINT","INT","SMALLINT","TINYINT","BIT","DECIMAL","NUMERIC","MONEY","SMALLMONEY","FLOAT","REAL",
        "DATE","DATETIME","DATETIME2","SMALLDATETIME","DATETIMEOFFSET","TIME","CHAR","VARCHAR","TEXT",
        "NCHAR","NVARCHAR","NTEXT","BINARY","VARBINARY","IMAGE","UNIQUEIDENTIFIER","XML","SQL_VARIANT",
        "ROWVERSION","TIMESTAMP","HIERARCHYID","GEOGRAPHY","GEOMETRY","SYSNAME","CURSOR","TABLE"
    };

    /// <summary>Tworzy opis typu z nazwą i parametrami, uzupełniając wartości domyślne SQL Server.</summary>
    public static SqlDataTypeInfo Create(string name, int? first, int? second, bool isMax)
    {
        var upper = name.Trim().ToUpperInvariant();
        int? precision = null, scale = null, length = null;
        var implicitPrecision = false;
        switch (upper)
        {
            case "DECIMAL":
            case "NUMERIC":
            case "DEC":
                upper = upper == "DEC" ? "DECIMAL" : upper;
                precision = first ?? 18;
                scale = second ?? 0;
                implicitPrecision = first is null;
                break;
            case "MONEY": precision = 19; scale = 4; implicitPrecision = true; break;
            case "SMALLMONEY": precision = 10; scale = 4; implicitPrecision = true; break;
            case "FLOAT":
                // FLOAT(n): n 1..24 => REAL (4 bajty, ~7 cyfr), 25..53 => FLOAT (8 bajtów, ~15 cyfr)
                var n = first ?? 53;
                precision = n <= 24 ? 24 : 53;
                implicitPrecision = first is null;
                break;
            case "REAL": precision = 24; implicitPrecision = true; break;
            case "BIGINT": precision = 19; scale = 0; implicitPrecision = true; break;
            case "INT": case "INTEGER": upper = "INT"; precision = 10; scale = 0; implicitPrecision = true; break;
            case "SMALLINT": precision = 5; scale = 0; implicitPrecision = true; break;
            case "TINYINT": precision = 3; scale = 0; implicitPrecision = true; break;
            case "DATETIME2": case "TIME": case "DATETIMEOFFSET":
                scale = first ?? 7; implicitPrecision = first is null; break;
            case "CHAR": case "NCHAR": case "BINARY":
                length = isMax ? null : first ?? 1; break;
            case "VARCHAR": case "NVARCHAR": case "VARBINARY":
                length = isMax ? null : first ?? 1; break;
            default:
                if (first.HasValue) length = first;
                break;
        }

        return new SqlDataTypeInfo
        {
            Name = upper,
            Precision = precision,
            Scale = scale,
            Length = length,
            IsMax = isMax,
            IsPrecisionImplicit = implicitPrecision,
            IsUserDefined = !KnownTypes.Contains(upper)
        };
    }

    /// <summary>Parsuje zapis typu, np. "decimal(18, 4)", "nvarchar(max)", "money". Zwraca null dla pustego tekstu.</summary>
    public static SqlDataTypeInfo? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = TypePattern.Match(text);
        if (!m.Success) return new SqlDataTypeInfo { Name = text.Trim().ToUpperInvariant(), IsUserDefined = true };
        var name = m.Groups["name"].Value.Trim();
        var a = m.Groups["a"].Success ? m.Groups["a"].Value : null;
        var b = m.Groups["b"].Success ? m.Groups["b"].Value : null;
        var isMax = string.Equals(a, "max", StringComparison.OrdinalIgnoreCase);
        int? first = !isMax && a is not null ? int.Parse(a, CultureInfo.InvariantCulture) : null;
        int? second = b is not null ? int.Parse(b, CultureInfo.InvariantCulture) : null;
        return Create(name, first, second, isMax);
    }

    public bool IsExactNumeric => Name is "DECIMAL" or "NUMERIC" or "MONEY" or "SMALLMONEY";
    public bool IsApproximateNumeric => Name is "FLOAT" or "REAL";
    public bool IsInteger => Name is "BIGINT" or "INT" or "SMALLINT" or "TINYINT";
    public bool IsText => Name is "CHAR" or "VARCHAR" or "TEXT" or "NCHAR" or "NVARCHAR" or "NTEXT" or "SYSNAME" or "XML";
    public bool IsUnicodeText => Name is "NCHAR" or "NVARCHAR" or "NTEXT" or "SYSNAME";

    /// <summary>Typ efektywny – dla typu użytkownika jego typ bazowy (jeżeli znany).</summary>
    public SqlDataTypeInfo Effective => IsUserDefined && BaseType is not null ? BaseType : this;

    public override string ToString()
    {
        var core = Name switch
        {
            "DECIMAL" or "NUMERIC" => $"{Name}({Precision},{Scale})",
            "FLOAT" => Precision == 24 ? "FLOAT(24)" : "FLOAT",
            "DATETIME2" or "TIME" or "DATETIMEOFFSET" => Scale is null or 7 ? Name : $"{Name}({Scale})",
            _ when IsMax => $"{Name}(MAX)",
            _ when Length.HasValue => $"{Name}({Length})",
            _ => Name
        };
        return IsUserDefined && BaseType is not null ? $"{core} → {BaseType}" : core;
    }
}
