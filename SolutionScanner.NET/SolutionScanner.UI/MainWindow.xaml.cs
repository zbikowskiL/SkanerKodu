using System.Windows;
using SolutionScanner.UI.ViewModels;

namespace SolutionScanner.UI;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
