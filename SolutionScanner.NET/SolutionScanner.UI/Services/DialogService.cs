using System.Windows;
using Microsoft.Win32;

namespace SolutionScanner.UI.Services;

public interface IDialogService
{
    string? OpenFile(string title, string filter);
    string? SelectFolder(string title);
    void ShowError(string message);
    void ShowInfo(string message);
}

/// <summary>Okna dialogowe – wydzielone z ViewModelu (MVVM, testowalność).</summary>
public sealed class DialogService : IDialogService
{
    public string? OpenFile(string title, string filter)
    {
        var dlg = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? SelectFolder(string title)
    {
        var dlg = new OpenFolderDialog { Title = title };
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    public void ShowError(string message) => MessageBox.Show(message, "SolutionScanner.NET", MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInfo(string message) => MessageBox.Show(message, "SolutionScanner.NET", MessageBoxButton.OK, MessageBoxImage.Information);
}
