using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SolutionScanner.Core;
using SolutionScanner.Core.Infrastructure;
using SolutionScanner.Core.Models;
using SolutionScanner.Export;
using SolutionScanner.UI.Services;

namespace SolutionScanner.UI.ViewModels;

/// <summary>Główny ViewModel: wybór solucji i projektów, frazy, ustawienia, uruchomienie/anulowanie analizy, wyniki, eksport.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IScanOrchestrator _orchestrator;
    private readonly ISolutionLoader _loader;
    private readonly CsvExporter _exporter;
    private readonly IDialogService _dialogs;
    private readonly IScanLogger _logger;
    private CancellationTokenSource? _cts;
    private AnalysisResult? _result;

    public MainViewModel(IScanOrchestrator orchestrator, ISolutionLoader loader, CsvExporter exporter, IDialogService dialogs, IScanLogger logger)
    {
        _orchestrator = orchestrator;
        _loader = loader;
        _exporter = exporter;
        _dialogs = dialogs;
        _logger = logger;
        _logger.EntryAdded += line => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            LogLines.Add(line);
            while (LogLines.Count > 2000) LogLines.RemoveAt(0);
        });
        MsBuildInfo = SolutionLoader.MsBuildDescription;
    }

    public ObservableCollection<ProjectItemViewModel> Projects { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<SummaryItem> Summary { get; } = new();

    public ResultTableViewModel SearchTable { get; } = new("Wyszukiwanie");
    public ResultTableViewModel DeclarationsTable { get; } = new("Zmienne");
    public ResultTableViewModel TypesTable { get; } = new("Typy danych");
    public ResultTableViewModel PrecisionTable { get; } = new("Precyzja");
    public ResultTableViewModel RoundingTable { get; } = new("Zaokrąglenia");
    public ResultTableViewModel ConversionTable { get; } = new("Konwersje");
    public ResultTableViewModel DependencyTable { get; } = new("Zależności");
    public ResultTableViewModel ReferenceTable { get; } = new("Odwołania");
    public ResultTableViewModel DataFlowTable { get; } = new("Przepływ danych");
    public ResultTableViewModel ProblemTable { get; } = new("Potencjalne problemy");
    public ResultTableViewModel LimitationTable { get; } = new("Ograniczenia");

    private IEnumerable<ResultTableViewModel> AllTables => new[]
    {
        SearchTable, DeclarationsTable, TypesTable, PrecisionTable, RoundingTable, ConversionTable,
        DependencyTable, ReferenceTable, DataFlowTable, ProblemTable, LimitationTable
    };

    [ObservableProperty] private string _solutionPath = string.Empty;
    [ObservableProperty] private string _phrasesText = "opr; oprocentowanie; odsetki; kapital; marza; prowizja";
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private string _msBuildInfo = string.Empty;

    // Ustawienia
    [ObservableProperty] private bool _caseSensitive;
    [ObservableProperty] private bool _ignoreDiacritics = true;
    [ObservableProperty] private bool _includeGeneratedFiles;
    [ObservableProperty] private bool _analyzeOtherFiles = true;
    [ObservableProperty] private bool _findReferences = true;
    [ObservableProperty] private bool _analyzeDataFlow = true;
    [ObservableProperty] private bool _analyzeEntireCode;
    [ObservableProperty] private bool _restorePackages;

    // Postęp
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(CancelCommand), nameof(ExportCommand), nameof(BrowseSolutionCommand))]
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _isIndeterminate;
    [ObservableProperty] private string _stageText = "Gotowy";
    [ObservableProperty] private string _currentProject = string.Empty;
    [ObservableProperty] private string _currentFile = string.Empty;
    [ObservableProperty] private string _statusText = "Wybierz plik .sln i wpisz frazy.";

    partial void OnFilterTextChanged(string value)
    {
        foreach (var t in AllTables) t.ApplyFilter(value);
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void BrowseSolution()
    {
        var path = _dialogs.OpenFile("Wybierz solucję", "Solucja Visual Studio (*.sln;*.slnx)|*.sln;*.slnx|Wszystkie pliki|*.*");
        if (path is not null) LoadSolution(path);
    }

    private void LoadSolution(string path)
    {
        try
        {
            var descriptor = _loader.ReadDescriptor(path);
            SolutionPath = descriptor.SolutionPath;
            Projects.Clear();
            foreach (var p in descriptor.Projects) Projects.Add(new ProjectItemViewModel(p) { IsSelected = p.Exists });
            StatusText = $"Wczytano listę projektów: {Projects.Count}.";
            StartCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("Nie można odczytać solucji: " + ex.Message);
        }
    }

    [RelayCommand]
    private void SelectAllProjects()
    {
        foreach (var p in Projects) p.IsSelected = true;
    }

    [RelayCommand]
    private void SelectNoProjects()
    {
        foreach (var p in Projects) p.IsSelected = false;
    }

    [RelayCommand]
    private void LoadPhrasesFromFile()
    {
        var path = _dialogs.OpenFile("Wczytaj frazy", "Plik tekstowy (*.txt)|*.txt|Wszystkie pliki|*.*");
        if (path is null) return;
        try
        {
            PhrasesText = string.Join("; ", SearchEngine.LoadPhrasesFromFile(path));
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("Nie można wczytać fraz: " + ex.Message);
        }
    }

    private bool CanEdit() => !IsRunning;
    private bool CanStart() => !IsRunning && File.Exists(SolutionPath);
    private bool CanCancel() => IsRunning;
    private bool CanExport() => !IsRunning && _result is not null;

    partial void OnSolutionPathChanged(string value) => StartCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        var phrases = SearchEngine.ParsePhrases(PhrasesText);
        if (phrases.Count == 0 && !AnalyzeEntireCode)
        {
            _dialogs.ShowError("Wpisz co najmniej jedną frazę lub włącz analizę całego kodu.");
            return;
        }
        var selected = Projects.Where(p => p.IsSelected).ToList();
        if (Projects.Count > 0 && selected.Count == 0)
        {
            _dialogs.ShowError("Zaznacz co najmniej jeden projekt.");
            return;
        }

        var settings = new AnalysisSettings
        {
            Phrases = phrases,
            CaseSensitive = CaseSensitive,
            IgnoreDiacritics = IgnoreDiacritics,
            IncludeGeneratedFiles = IncludeGeneratedFiles,
            AnalyzeOtherFiles = AnalyzeOtherFiles,
            FindSymbolReferences = FindReferences,
            AnalyzeDataFlow = AnalyzeDataFlow && FindReferences,
            AnalyzeEntireCode = AnalyzeEntireCode,
            RestorePackagesBeforeLoad = RestorePackages,
            SelectedProjectPaths = selected.Count == Projects.Count
                ? null
                : new HashSet<string>(selected.Select(p => TextUtil.NormalizePath(p.Entry.FilePath)), StringComparer.OrdinalIgnoreCase)
        };

        LogLines.Clear();
        IsRunning = true;
        IsIndeterminate = true;
        ProgressValue = 0;
        StatusText = "Analiza w toku...";
        _cts = new CancellationTokenSource();
        var progress = new Progress<AnalysisProgress>(OnProgress);
        try
        {
            // Analiza w puli wątków – UI pozostaje responsywny
            var result = await Task.Run(() => _orchestrator.RunAsync(SolutionPath, settings, progress, _cts.Token));
            ShowResult(result);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("Analiza zakończona błędem: " + ex.Message);
        }
        finally
        {
            IsRunning = false;
            IsIndeterminate = false;
            _cts.Dispose();
            _cts = null;
            ExportCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnProgress(AnalysisProgress p)
    {
        StageText = p.Stage;
        if (!string.IsNullOrEmpty(p.ProjectName)) CurrentProject = p.ProjectName;
        if (!string.IsNullOrEmpty(p.FilePath)) CurrentFile = p.FilePath;
        IsIndeterminate = p.Total == 0 && p.Percent <= 0;
        if (!IsIndeterminate) ProgressValue = p.Percent;
        StatusText = p.Message;
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cts?.Cancel();
        StatusText = "Anulowanie...";
    }

    private void ShowResult(AnalysisResult result)
    {
        _result = result;
        SearchTable.SetRows(ReportRowMapper.Search(result));
        DeclarationsTable.SetRows(ReportRowMapper.Declarations(result));
        TypesTable.SetRows(ReportRowMapper.DataTypes(result));
        PrecisionTable.SetRows(ReportRowMapper.Precision(result));
        RoundingTable.SetRows(ReportRowMapper.Roundings(result));
        ConversionTable.SetRows(ReportRowMapper.Conversions(result));
        DependencyTable.SetRows(ReportRowMapper.Dependencies(result));
        ReferenceTable.SetRows(ReportRowMapper.References(result));
        DataFlowTable.SetRows(ReportRowMapper.DataFlow(result));
        ProblemTable.SetRows(ReportRowMapper.Problems(result));
        LimitationTable.SetRows(ReportRowMapper.Limitations(result));
        if (FilterText.Length > 0) OnFilterTextChanged(FilterText);

        Summary.Clear();
        foreach (var s in ReportGenerator.BuildSummary(result)) Summary.Add(s);

        ProgressValue = 100;
        StageText = result.Cancelled ? "Anulowano" : result.FatalError is not null ? "Błąd" : "Zakończono";
        StatusText = result.FatalError is not null
            ? "Błąd: " + result.FatalError
            : $"{(result.Cancelled ? "Anulowano (wyniki częściowe)" : "Zakończono")} w {result.Duration:hh\\:mm\\:ss}. " +
              $"Wystąpień: {result.SearchHits.Count}, odwołań: {result.References.Count}, problemów: {result.Problems.Count}.";
        MsBuildInfo = result.MsBuildInfo;
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void Export()
    {
        if (_result is null) return;
        var folder = _dialogs.SelectFolder("Wybierz katalog dla raportów CSV");
        if (folder is null) return;
        try
        {
            var target = Path.Combine(folder, $"SolutionScanner_{Path.GetFileNameWithoutExtension(_result.SolutionPath)}_{DateTime.Now:yyyyMMdd_HHmmss}");
            var files = _exporter.ExportAll(_result, target);
            _dialogs.ShowInfo($"Zapisano {files.Count} plików CSV w katalogu:\n{target}");
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("Eksport nie powiódł się: " + ex.Message);
        }
    }
}
