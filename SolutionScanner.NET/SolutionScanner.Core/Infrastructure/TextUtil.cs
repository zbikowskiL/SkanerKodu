namespace SolutionScanner.Core.Infrastructure;

/// <summary>Pomocnicze operacje tekstowe.</summary>
public static class TextUtil
{
    public const int MaxCodeLength = 400;

    public static string Shorten(string? text, int max = MaxCodeLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var singleLine = text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        return singleLine.Length <= max ? singleLine : singleLine[..(max - 1)] + "…";
    }

    /// <summary>Przelicza offset na (linia, kolumna) numerowane od 1.</summary>
    public static (int Line, int Column) GetLineColumn(string text, int offset)
    {
        var line = 1;
        var lineStart = 0;
        var limit = Math.Min(offset, text.Length);
        for (var i = 0; i < limit; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }
        return (line, offset - lineStart + 1);
    }

    /// <summary>Tablica offsetów początków linii – szybkie przeliczanie offset → linia dla dużych plików.</summary>
    public static int[] BuildLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n') starts.Add(i + 1);
        return starts.ToArray();
    }

    public static (int Line, int Column) GetLineColumn(int[] lineStarts, int offset)
    {
        var idx = Array.BinarySearch(lineStarts, offset);
        if (idx < 0) idx = ~idx - 1;
        return (idx + 1, offset - lineStarts[idx] + 1);
    }

    public static string GetRelativePath(string baseDirectory, string path)
    {
        if (string.IsNullOrEmpty(baseDirectory) || string.IsNullOrEmpty(path)) return path;
        try
        {
            return Path.GetRelativePath(baseDirectory, path);
        }
        catch
        {
            return path;
        }
    }

    public static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return path; }
    }
}
