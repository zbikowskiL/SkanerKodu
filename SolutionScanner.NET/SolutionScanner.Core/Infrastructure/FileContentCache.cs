using System.Collections.Concurrent;
using System.Text;

namespace SolutionScanner.Core.Infrastructure;

/// <summary>
/// Cache treści plików tekstowych – każdy plik jest wczytywany z dysku tylko raz w ramach analizy,
/// nawet jeśli obsługuje go kilka analizatorów (np. EDMX: wyszukiwanie XML + mapowania).
/// Wykrywa kodowanie: BOM, poprawny UTF-8, w przeciwnym razie Windows-1250 (częste w starszych plikach SQL).
/// </summary>
public sealed class FileContentCache
{
    private const int BinaryProbeLength = 8192;
    private readonly ConcurrentDictionary<string, Lazy<FileContent>> _cache = new(StringComparer.OrdinalIgnoreCase);

    static FileContentCache()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public FileContent Get(string path) =>
        _cache.GetOrAdd(path, p => new Lazy<FileContent>(() => Load(p), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    public void Clear() => _cache.Clear();

    public static FileContent Load(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (LooksBinary(bytes)) return new FileContent(path, string.Empty, true, null, "binarny");
            var (text, encodingName) = Decode(bytes);
            return new FileContent(path, text, false, null, encodingName);
        }
        catch (Exception ex)
        {
            return new FileContent(path, string.Empty, false, ex.Message, string.Empty);
        }
    }

    public static (string Text, string EncodingName) Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), "UTF-8 BOM");
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), "UTF-16 LE");
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), "UTF-16 BE");
        try
        {
            var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
            return (strict.GetString(bytes), "UTF-8");
        }
        catch (DecoderFallbackException)
        {
            return (Encoding.GetEncoding(1250).GetString(bytes), "Windows-1250");
        }
    }

    private static bool LooksBinary(byte[] bytes)
    {
        // UTF-16 z BOM zawiera zera – nie jest binarny
        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF))) return false;
        var len = Math.Min(bytes.Length, BinaryProbeLength);
        for (var i = 0; i < len; i++)
            if (bytes[i] == 0) return true;
        return false;
    }
}

/// <summary>Treść pliku wraz z informacją o błędzie odczytu.</summary>
public sealed record FileContent(string Path, string Text, bool IsBinary, string? Error, string EncodingName)
{
    private string[]? _lines;

    public bool IsReadable => Error is null && !IsBinary;

    public string[] Lines => _lines ??= Text.Split('\n');

    /// <summary>Tekst linii (numeracja od 1), przycięty do 400 znaków.</summary>
    public string GetLine(int lineNumber)
    {
        var lines = Lines;
        if (lineNumber < 1 || lineNumber > lines.Length) return string.Empty;
        return TextUtil.Shorten(lines[lineNumber - 1].Trim());
    }
}
