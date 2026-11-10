using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SolutionScanner.Core.Abstractions;
using SolutionScanner.Core.Files;
using SolutionScanner.Core.Infrastructure;

namespace SolutionScanner.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>Rejestruje usługi rdzenia: wczytywanie solucji, orkiestrator, odwołania, zależności, przepływ danych, wyszukiwanie w XML/JSON/Razor.</summary>
    public static IServiceCollection AddScannerCore(this IServiceCollection services)
    {
        services.TryAddSingleton<IScanLogger, FileScanLogger>();
        services.AddSingleton<ISolutionLoader, SolutionLoader>();
        services.AddSingleton<SymbolAnalyzer>();
        services.AddSingleton<DependencyAnalyzer>();
        services.AddSingleton<IPostAnalyzer, DataFlowAnalyzer>();
        services.AddSingleton<IFileAnalyzer, XmlSearchAnalyzer>();
        services.AddSingleton<IFileAnalyzer, JsonSearchAnalyzer>();
        services.AddSingleton<IFileAnalyzer, RazorSearchAnalyzer>();
        services.AddSingleton<IScanOrchestrator, ScanOrchestrator>();
        return services;
    }
}
