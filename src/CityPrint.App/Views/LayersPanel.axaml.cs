using Avalonia.Controls;
using Avalonia.Interactivity;
using CityPrint.App.ViewModels;

namespace CityPrint.App.Views;

public partial class LayersPanel : UserControl
{
    public LayersPanel()
    {
        InitializeComponent();
        DataContext = AppState.Current;
    }

    private void OnAll(object? sender, RoutedEventArgs e) => AppState.Current.SetAllLayers(true);
    private void OnNone(object? sender, RoutedEventArgs e) => AppState.Current.SetAllLayers(false);
}
