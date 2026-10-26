using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CityPrint.App.Services;
using CityPrint.App.ViewModels;
using CityPrint.App.Views;
using CityPrint.App.Rendering;
using CityPrint.Core.Map;
using CityPrint.Core.Poster;

namespace CityPrint.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Usage: CityPrint [file.osm] [--select way/123] [--map-theme id] [--app-theme dark|light] [--poster]
            //              [--settings] [--screenshot out.png] [--export-icon out.ico|out.icns]
            var args = desktop.Args ?? [];
            if (ArgValue(args, "--export-icon") is { } ico)
            {
                if (ico.EndsWith(".icns", StringComparison.OrdinalIgnoreCase)) AppIcon.SaveIcns(ico);
                else AppIcon.SaveIco(ico);
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Dispatcher.UIThread.Post(() => desktop.Shutdown());
                base.OnFrameworkInitializationCompleted();
                return;
            }

            AppSettings.ReadOnly = args.Contains("--screenshot") || args.Contains("--export-poster");
            var state = AppState.Current;
            var appTheme = ArgValue(args, "--app-theme") is { } at && Enum.TryParse<AppThemeMode>(at, true, out var m) ? m : state.Settings.AppTheme;
            Appearance.Apply(appTheme);
            if (ArgValue(args, "--map-theme") is { } mt) state.MapTheme = MapThemes.ById(mt);

            var window = new MainWindow();
            desktop.MainWindow = window;

            string? file = args.FirstOrDefault(a => !a.StartsWith("--") && File.Exists(a));
            bool scripted = args.Any(a => a.StartsWith("--"));
            if (file is null && !scripted && state.Settings.OpenLastFile)
                file = state.Settings.RecentFiles.FirstOrDefault(File.Exists);
            string? shot = ArgValue(args, "--screenshot");
            string? select = ArgValue(args, "--select");
            bool poster = args.Contains("--poster");

            string? posterOut = ArgValue(args, "--export-poster");

            window.Opened += async (_, _) =>
            {
                if (file != null) await state.LoadAsync(file);
                if (posterOut != null)
                {
                    ExportPoster(args, posterOut);
                    desktop.Shutdown();
                    return;
                }
                if (select != null)
                {
                    if (select.StartsWith("street:")) SelectStreet(select[7..]);
                    else state.SelectByKey(select);
                }
                if (poster) window.FindControl<Button>("PosterButton")?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Window target = window;
                if (args.Contains("--settings"))
                {
                    target = new SettingsWindow();
                    _ = target.ShowDialog(window);
                }
                if (shot != null) await CaptureAndExit(target, shot, desktop);
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Headless poster export: --export-poster out.png|out.svg [--poster-theme id] [--paper A3]
    /// [--layout Gallery|FullBleed|Minimal] [--landscape] [--labels] [--legend] [--dpi 200] [--title "…"]</summary>
    private static void ExportPoster(string[] args, string path)
    {
        var vm = new PosterViewModel();
        if (ArgValue(args, "--poster-theme") is { } t) vm.Theme = MapThemes.ById(t);
        if (ArgValue(args, "--paper") is { } p) vm.Paper = PaperSize.ByName(p);
        if (ArgValue(args, "--layout") is { } l && Enum.TryParse<PosterLayoutStyle>(l, true, out var layout)) vm.Layout = layout;
        if (ArgValue(args, "--dpi") is { } d && int.TryParse(d, out int dpi)) vm.Dpi = dpi;
        if (ArgValue(args, "--title") is { } title) vm.Title = title;
        vm.Landscape = args.Contains("--landscape");
        vm.Labels = args.Contains("--labels");
        vm.Legend = args.Contains("--legend");
        vm.FitCity();
        if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            if (vm.Scene != null) SvgExporter.Export(path, vm.Spec, vm.Scene, vm.IsVisible, vm.LegendEntries);
        }
        else
        {
            PosterRenderer.SavePng(path, vm.Spec, vm.Scene, vm.IsVisible, vm.LegendEntries);
        }
    }

    private static void SelectStreet(string name)
    {
        var s = AppState.Current;
        if (s.Model is null) return;
        var g = FeatureGroups.Build(s.Model.Features).FirstOrDefault(g => g.Title == name);
        if (g != null) s.ZoomTo(g);
    }

    private static string? ArgValue(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static async Task CaptureAndExit(Window window, string path, IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Let layout and the first frames settle.
        await Task.Delay(1500);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var size = new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height);
            using var bmp = new RenderTargetBitmap(size);
            bmp.Render(window);
            bmp.Save(path, PngBitmapEncoderOptions.Default);
        });
        desktop.Shutdown();
    }
}
