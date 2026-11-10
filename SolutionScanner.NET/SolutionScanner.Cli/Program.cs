using Microsoft.Extensions.DependencyInjection;
using SolutionScanner.Core;
using SolutionScanner.Core.Models;
using SolutionScanner.CSharp;
using SolutionScanner.Export;

// Użycie: SolutionScanner.Cli <plik.sln> "<frazy oddzielone ;>" <katalog_wyjściowy> [--all] [--restore]
Console.OutputEncoding = System.Text.Encoding.UTF8;
if (args.Length < 3)
{
    Console.WriteLine("Użycie: SolutionScanner.Cli <plik.sln> \"fraza1;fraza2\" <katalog_wyjściowy> [--all] [--restore]");
    return 1;
}

SolutionLoader.EnsureMsBuildRegistered();
var services = new ServiceCollection().AddScannerCore().AddScannerCSharp().BuildServiceProvider();
var orchestrator = services.GetRequiredService<IScanOrchestrator>();

var settings = new AnalysisSettings
{
    Phrases = SearchEngine.ParsePhrases(args[1]),
    AnalyzeEntireCode = args.Contains("--all"),
    RestorePackagesBeforeLoad = args.Contains("--restore")
};

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
var lastStage = "";
var progress = new Progress<AnalysisProgress>(p =>
{
    if (p.Stage != lastStage) { lastStage = p.Stage; Console.WriteLine($"[{p.Stage}] {p.Message}"); }
});

var result = await orchestrator.RunAsync(Path.GetFullPath(args[0]), settings, progress, cts.Token);
var files = new CsvExporter().ExportAll(result, args[2]);
foreach (var s in ReportGenerator.BuildSummary(result).Where(s => s.Section != "Projekty"))
    Console.WriteLine($"{s.Section} | {s.Metric}: {s.Value}");
Console.WriteLine($"Zapisano {files.Count} plików CSV w {Path.GetFullPath(args[2])}");
return result.FatalError is null ? 0 : 2;
