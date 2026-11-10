using System.Text;

namespace SolutionScanner.Core.Infrastructure;

/// <summary>Dziennik zdarzeń analizy.</summary>
public interface IScanLogger
{
    string? LogFilePath { get; }
    IReadOnlyList<string> RecentEntries { get; }
    event Action<string>? EntryAdded;

    /// <summary>Rozpoczyna nowy plik logu (wywoływane na początku każdej analizy).</summary>
    void StartNewLog(string sessionName);
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);
}

/// <summary>
/// Logger zapisujący do pliku %LOCALAPPDATA%\SolutionScanner.NET\logs (lub podanego katalogu)
/// i przechowujący ostatnie wpisy w pamięci dla UI. Bezpieczny wątkowo.
/// </summary>
public sealed class FileScanLogger : IScanLogger
{
    private const int MaxRecentEntries = 5000;
    private readonly object _lock = new();
    private readonly LinkedList<string> _recent = new();
    private readonly string _directory;

    public FileScanLogger(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SolutionScanner.NET", "logs");
    }

    public string? LogFilePath { get; private set; }

    public event Action<string>? EntryAdded;

    public IReadOnlyList<string> RecentEntries
    {
        get { lock (_lock) return _recent.ToList(); }
    }

    public void StartNewLog(string sessionName)
    {
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var safe = string.Concat(sessionName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                LogFilePath = Path.Combine(_directory, $"scan_{DateTime.Now:yyyyMMdd_HHmmss}_{safe}.log");
            }
            catch
            {
                LogFilePath = null; // brak możliwości zapisu logu nie może przerwać analizy
            }
            _recent.Clear();
        }
    }

    public void Info(string message) => Write("INFO", message);
    public void Warning(string message) => Write("WARN", message);

    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message} | {exception.GetType().Name}: {exception.Message}");

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}";
        lock (_lock)
        {
            _recent.AddLast(line);
            while (_recent.Count > MaxRecentEntries) _recent.RemoveFirst();
            if (LogFilePath is not null)
            {
                try { File.AppendAllText(LogFilePath, line + Environment.NewLine, Encoding.UTF8); }
                catch { /* błąd zapisu logu ignorujemy – log w pamięci pozostaje */ }
            }
        }
        EntryAdded?.Invoke(line);
    }
}
