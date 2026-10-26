using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CityPrint.App.Rendering;
using CityPrint.App.ViewModels;
using CityPrint.Core.Poster;

namespace CityPrint.App.Views;

public partial class PosterView : UserControl
{
    private static readonly FilePickerFileType Png = new("PNG image") { Patterns = ["*.png"] };
    private static readonly FilePickerFileType Svg = new("SVG vector image") { Patterns = ["*.svg"] };

    public PosterView() : this(new PosterViewModel()) { }

    public PosterView(PosterViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private PosterViewModel Vm => (PosterViewModel)DataContext!;

    private void OnFitCity(object? sender, RoutedEventArgs e) => Vm.FitCity();
    private void OnUseView(object? sender, RoutedEventArgs e) => Vm.UseCurrentView();

    private async void OnExportPng(object? sender, RoutedEventArgs e)
    {
        var path = await PickSave(Png, ".png");
        if (path is null) return;
        await Run(path, () => PosterRenderer.SavePng(path, Vm.Spec, Vm.Scene, Vm.IsVisible, Vm.LegendEntries));
    }

    private async void OnExportSvg(object? sender, RoutedEventArgs e)
    {
        if (Vm.Scene is not { } scene) return;
        var path = await PickSave(Svg, ".svg");
        if (path is null) return;
        await Run(path, () => SvgExporter.Export(path, Vm.Spec, scene, Vm.IsVisible, Vm.LegendEntries));
    }

    private async Task Run(string path, Action export)
    {
        ExportStatus.Text = "Exporting…";
        // Let the status text paint before the (synchronous, UI-thread) render.
        await Task.Delay(30);
        try
        {
            var sw = Stopwatch.StartNew();
            export();
            ExportStatus.Text = $"Saved {Path.GetFileName(path)} ({new FileInfo(path).Length / 1024.0 / 1024.0:0.0} MB, {sw.Elapsed.TotalSeconds:0.0} s)";
        }
        catch (Exception ex)
        {
            ExportStatus.Text = "Export failed: " + ex.Message;
        }
    }

    private async Task<string?> PickSave(FilePickerFileType type, string ext)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return null;
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export poster",
            SuggestedFileName = Vm.SuggestedFileName + ext,
            DefaultExtension = ext,
            FileTypeChoices = [type],
        });
        return file?.TryGetLocalPath();
    }
}
