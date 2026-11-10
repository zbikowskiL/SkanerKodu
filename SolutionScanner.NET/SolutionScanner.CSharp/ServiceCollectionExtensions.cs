using Microsoft.Extensions.DependencyInjection;
using SolutionScanner.Core.Abstractions;

namespace SolutionScanner.CSharp;

public static class ServiceCollectionExtensions
{
    /// <summary>Rejestruje analizatory dokumentów C#/VB (Roslyn).</summary>
    public static IServiceCollection AddScannerCSharp(this IServiceCollection services)
    {
        services.AddSingleton<IDocumentAnalyzer, RoslynAnalyzer>();
        services.AddSingleton<IDocumentAnalyzer, DataTypeAnalyzer>();
        services.AddSingleton<IDocumentAnalyzer, PrecisionAnalyzer>();
        services.AddSingleton<IDocumentAnalyzer, RoundingAnalyzer>();
        services.AddSingleton<IDocumentAnalyzer, ConversionAnalyzer>();
        services.AddSingleton<IDocumentAnalyzer, ArithmeticAnalyzer>();
        return services;
    }
}
