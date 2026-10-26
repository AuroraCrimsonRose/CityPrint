using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using CityPrint.App.ViewModels;

namespace CityPrint.App.Views;

public partial class InspectorPanel : UserControl
{
    public InspectorPanel()
    {
        InitializeComponent();
        DataContext = AppState.Current;
    }

    private void OnZoom(object? sender, RoutedEventArgs e) => AppState.Current.ZoomToSelection();

    private void OnWholeStreet(object? sender, RoutedEventArgs e) => AppState.Current.SelectWholeStreet();

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        var s = AppState.Current;
        if (!s.HasSelection || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        string head = s.SelectedGroup is { Features.Count: > 1 } g
            ? $"{g.Title} ({string.Join(", ", g.Features.Select(f => f.Element.Key))})"
            : s.Selected!.Element.Key;
        await clipboard.SetTextAsync(head + "\n" + string.Join("\n", s.SelectedTags.Select(t => $"{t.Key}={t.Value}")));
    }
}
