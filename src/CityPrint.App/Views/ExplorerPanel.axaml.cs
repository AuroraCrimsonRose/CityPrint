using Avalonia.Controls;
using Avalonia.Input;
using CityPrint.App.ViewModels;

namespace CityPrint.App.Views;

public partial class ExplorerPanel : UserControl
{
    public ExplorerPanel()
    {
        InitializeComponent();
        DataContext = AppState.Current;
    }

    // Single click selects (a street highlights all of its segments); double click also zooms.
    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var s = AppState.Current;
        switch (Tree.SelectedItem)
        {
            case ExplorerNode { Feature: { } f, Group: null }:
                s.Selected = f;
                break;
            case ExplorerNode { Group: { } g }:
                s.SelectGroup(g);
                break;
        }
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        var s = AppState.Current;
        switch (Tree.SelectedItem)
        {
            case ExplorerNode { Feature: { } f, Group: null }:
                s.ZoomTo(f);
                break;
            case ExplorerNode { Group: { } g }:
                s.ZoomTo(g);
                break;
        }
    }
}
