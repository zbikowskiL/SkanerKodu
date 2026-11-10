using CommunityToolkit.Mvvm.ComponentModel;
using SolutionScanner.Core.Models;

namespace SolutionScanner.UI.ViewModels;

public sealed partial class ProjectItemViewModel : ObservableObject
{
    public ProjectItemViewModel(ProjectEntry entry) => Entry = entry;

    public ProjectEntry Entry { get; }
    public string Name => Entry.Name;
    public string Description => Entry.Description;

    [ObservableProperty]
    private bool _isSelected = true;
}
