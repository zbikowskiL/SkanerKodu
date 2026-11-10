using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SolutionScanner.Core;
using SolutionScanner.CSharp;
using SolutionScanner.Export;
using SolutionScanner.UI.Services;
using SolutionScanner.UI.ViewModels;

namespace SolutionScanner.UI;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        // MSBuild musi zostać zarejestrowany przed pierwszym użyciem typów MSBuild/MSBuildWorkspace.
        SolutionLoader.EnsureMsBuildRegistered();

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Nieoczekiwany błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        _services = new ServiceCollection()
            .AddScannerCore()
            .AddScannerCSharp()
            .AddSingleton<CsvExporter>()
            .AddSingleton<IDialogService, DialogService>()
            .AddSingleton<MainViewModel>()
            .AddSingleton<MainWindow>()
            .BuildServiceProvider();

        base.OnStartup(e);
        _services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
