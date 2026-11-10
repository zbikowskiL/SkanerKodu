using System.ComponentModel;
using System.Windows.Controls;
using CsvHelper.Configuration.Attributes;

namespace SolutionScanner.UI.Views;

/// <summary>Tabela wyników – kolumny generowane z wierszy raportu, nagłówki z atrybutu [Name] (te same co w CSV).</summary>
public partial class ResultGrid : UserControl
{
    public ResultGrid() => InitializeComponent();

    private void OnAutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.PropertyDescriptor is not PropertyDescriptor pd) return;
        if (pd.Attributes[typeof(BrowsableAttribute)] is BrowsableAttribute { Browsable: false } || pd.Attributes[typeof(IgnoreAttribute)] is not null)
        {
            e.Cancel = true;
            return;
        }
        if (pd.Attributes[typeof(NameAttribute)] is NameAttribute name && name.Names.Length > 0) e.Column.Header = name.Names[0];
        if (e.Column is DataGridTextColumn text && pd.PropertyType == typeof(string)) text.MaxWidth = 600;
    }
}
