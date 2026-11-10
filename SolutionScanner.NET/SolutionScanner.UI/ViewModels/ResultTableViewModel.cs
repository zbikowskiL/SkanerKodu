using System.Collections;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using SolutionScanner.Export;

namespace SolutionScanner.UI.ViewModels;

/// <summary>Tabela wyników z filtrowaniem tekstowym (ICollectionView – bez kopiowania danych).</summary>
public sealed partial class ResultTableViewModel : ObservableObject
{
    private string _filter = string.Empty;

    public ResultTableViewModel(string title) => Title = title;

    public string Title { get; }

    [ObservableProperty]
    private ICollectionView? _view;

    [ObservableProperty]
    private string _countText = "0";

    private int _total;

    public void SetRows(IList rows)
    {
        _total = rows.Count;
        var view = CollectionViewSource.GetDefaultView(rows);
        view.Filter = Matches;
        View = view;
        UpdateCount();
    }

    public void ApplyFilter(string filter)
    {
        _filter = filter.Trim();
        View?.Refresh();
        UpdateCount();
    }

    private bool Matches(object item) =>
        _filter.Length == 0 || (item is ReportRow row && row.FilterText.Contains(_filter, StringComparison.OrdinalIgnoreCase));

    private void UpdateCount()
    {
        if (View is null) { CountText = "0"; return; }
        var visible = _filter.Length == 0 ? _total : View.Cast<object>().Count();
        CountText = _filter.Length == 0 ? $"{_total}" : $"{visible} z {_total}";
    }
}
