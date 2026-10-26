using Avalonia.Controls;
using Avalonia.Input;
using CityPrint.App.ViewModels;

namespace CityPrint.App.Views;

public partial class DiagnosticsPanel : UserControl
{
    public DiagnosticsPanel()
    {
        InitializeComponent();
        DataContext = AppState.Current;
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (List.SelectedItem is DiagnosticRow d) AppState.Current.SelectByKey(d.ElementKey);
    }
}
