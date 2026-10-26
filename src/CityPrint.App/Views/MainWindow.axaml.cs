using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CityPrint.App.Controls;
using CityPrint.App.Rendering;
using CityPrint.App.Services;
using CityPrint.App.ViewModels;
using CityPrint.Core.Carto;
using CityPrint.Core.Map;
using Dock.Model.Avalonia.Controls;
using Dock.Model.Core;
using DockUi = Dock.Avalonia.Controls;

namespace CityPrint.App.Views;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType OsmFiles = new("OSM XML") { Patterns = ["*.osm", "*.xml"] };
    private static readonly FilePickerFileType CartoFiles = new("Carto GeoJSON (any layer)") { Patterns = ["*.json"] };
    private static readonly FilePickerFileType ExportFiles = new("Map exports") { Patterns = ["*.osm", "*.xml", "*.json"] };
    private static readonly FilePickerFileType PngFiles = new("PNG image") { Patterns = ["*.png"] };

    private AppState State => AppState.Current;
    private int _posterCount;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = AppState.Current;
        Icon = AppIcon.Create();
        KeyDown += OnWindowKeyDown;
        // Dock's tab and pane-header templates carry Float / Dock as document / Close menus; the
        // layout is fixed, so swallow right-clicks there before those menus see them.
        AddHandler(ContextRequestedEvent, OnDockContextRequested, RoutingStrategies.Tunnel);

        BuildThemeMenu();
        BuildRecentMenu();
        State.RecentFiles.CollectionChanged += (_, _) => BuildRecentMenu();
        State.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppState.MapTheme)) BuildThemeMenu();
        };
        Appearance.Changed += UpdateAppThemeIcon;
        UpdateAppThemeIcon();
    }


    private static void OnDockContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        // The nearest control with a menu is the one that would open it. Only swallow the request
        // when that is a Dock chrome part, so text boxes inside panels keep their copy/paste menus.
        if (e.Source is not Avalonia.Visual source) return;
        var owner = source.GetSelfAndVisualAncestors().OfType<Control>()
            .FirstOrDefault(c => c.ContextMenu != null || c.ContextFlyout != null);
        if (owner?.TemplatedParent is DockUi.ToolTabStripItem or DockUi.DocumentTabStripItem
            or DockUi.ToolChromeControl)
            e.Handled = true;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers == KeyModifiers.Control;
        if (ctrl && e.Key == Key.O) OnOpen(null, e);
        else if (ctrl && e.Key == Key.G) OnOpenGameExport(null, e);
        else if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.G) OnOpenCartoExport(null, e);
        else if (ctrl && e.Key == Key.E) OnExportView(null, e);
        else if (ctrl && e.Key == Key.P) OnPoster(null, e);
        else if (ctrl && e.Key == Key.OemComma) OnSettings(null, e);
        else if (e.Key == Key.F5) OnReload(null, e);
        else if (e.Key == Key.Z && e.KeyModifiers == KeyModifiers.None && e.Source is not TextBox) State.ZoomToSelection();
        else return;
        e.Handled = true;
    }

    // ---- menus -----------------------------------------------------------

    private void BuildThemeMenu()
    {
        ThemeMenu.Items.Clear();
        foreach (var t in MapThemes.All)
        {
            var item = new MenuItem
            {
                Header = t.Name,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = t == State.MapTheme,
                GroupName = "maptheme",
            };
            ToolTip.SetTip(item, t.Description);
            item.Click += (_, _) => State.MapTheme = t;
            ThemeMenu.Items.Add(item);
        }
    }

    private void BuildRecentMenu()
    {
        RecentMenu.Items.Clear();
        foreach (var path in State.RecentFiles)
        {
            var item = new MenuItem { Header = Path.GetFileName(path) };
            ToolTip.SetTip(item, path);
            item.Click += async (_, _) =>
            {
                if (File.Exists(path) || Directory.Exists(path)) await State.LoadAsync(path);
                else State.Status = $"{path} no longer exists.";
            };
            RecentMenu.Items.Add(item);
        }
        if (RecentMenu.Items.Count > 0)
        {
            RecentMenu.Items.Add(new Separator());
            var clear = new MenuItem { Header = "Clear recent files" };
            clear.Click += (_, _) => State.ClearRecent();
            RecentMenu.Items.Add(clear);
        }
        RecentMenu.IsEnabled = RecentMenu.Items.Count > 0;
    }

    private void UpdateAppThemeIcon()
    {
        // Show the theme you would switch to.
        if (this.TryFindResource(Appearance.IsDark ? "Icon.Sun" : "Icon.Moon", out var g) && g is Geometry geo)
            AppThemeIcon.Data = geo;
    }

    // ---- file ------------------------------------------------------------

    private async void OnOpen(object? sender, RoutedEventArgs e) => await PickAndLoad(null);

    private async void OnOpenGameExport(object? sender, RoutedEventArgs e)
    {
        // The mod's file name is configurable, so take the newest export rather than assuming export.osm.
        var export = Directory.Exists(AppState.GameExportFolder)
            ? new DirectoryInfo(AppState.GameExportFolder).EnumerateFiles("*.osm").MaxBy(f => f.LastWriteTimeUtc)?.FullName
            : null;
        if (export != null) await State.LoadAsync(export);
        else await PickAndLoad(AppState.GameExportFolder);
    }

    private async void OnOpenCartoExport(object? sender, RoutedEventArgs e)
    {
        if (CartoImporter.IsCartoExport(AppState.CartoExportFolder)) await State.LoadAsync(AppState.CartoExportFolder);
        else await PickAndLoad(Directory.Exists(AppState.CartoExportFolder) ? AppState.CartoExportFolder : null);
    }

    private async Task PickAndLoad(string? startFolder)
    {
        var options = new FilePickerOpenOptions
        {
            Title = "Open OSM or Carto export",
            AllowMultiple = false,
            FileTypeFilter = [ExportFiles, OsmFiles, CartoFiles, FilePickerFileTypes.All],
        };
        var folder = startFolder ?? (State.Model?.Document.FilePath is { } p ? Path.GetDirectoryName(p) : AppState.GameExportFolder);
        if (folder != null && Directory.Exists(folder))
            options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(folder);

        var files = await StorageProvider.OpenFilePickerAsync(options);
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            await State.LoadAsync(path);
    }

    private async void OnReload(object? sender, RoutedEventArgs e) => await State.ReloadAsync();
    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    /// <summary>Saves exactly what the map shows, at 2× for crisp text.</summary>
    private async void OnExportView(object? sender, RoutedEventArgs e)
    {
        if (MapView.CurrentView is not { } v || State.Model is null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export map image",
            SuggestedFileName = Path.GetFileNameWithoutExtension(State.FileName) + "_map.png",
            DefaultExtension = ".png",
            FileTypeChoices = [PngFiles],
        });
        if (file?.TryGetLocalPath() is not { } path) return;

        const double k = 2;
        using var bmp = new RenderTargetBitmap(new Avalonia.PixelSize((int)(v.Width * k), (int)(v.Height * k)), new Avalonia.Vector(96, 96));
        using (var ctx = bmp.CreateDrawingContext())
        {
            MapRenderer.Render(ctx, State.Scene, new Viewport(v.X, v.Y, v.Scale * k, v.Width * k, v.Height * k), State.MapTheme, new MapRenderOptions
            {
                PixelScale = k,
                RealWidths = State.RealWidths,
                Labels = State.ShowLabels,
                TileGrid = State.ShowTileGrid,
                MapBounds = State.ShowMapBounds,
                IsVisible = State.IsVisible,
            });
        }
        bmp.Save(path, PngBitmapEncoderOptions.Default);
        State.Status = $"Saved {Path.GetFileName(path)} ({(int)(v.Width * k)} × {(int)(v.Height * k)} px)";
    }

    // ---- documents -------------------------------------------------------

    private void OnPoster(object? sender, RoutedEventArgs e)
    {
        if (Dock.Factory is not { } factory || Dock.Layout is not { } root) return;
        if (factory.FindDockable(root, d => d.Id == "Documents") is not IDock docs) return;
        var vm = new PosterViewModel();
        var doc = new Document
        {
            Id = $"Poster{++_posterCount}",
            Title = _posterCount == 1 ? "Poster" : $"Poster {_posterCount}",
            CanClose = true,
            CanFloat = false,
            CanDrag = false,
            CanDrop = false,
            Content = new Func<IServiceProvider, object>(_ => new PosterView(vm)),
        };
        factory.AddDockable(docs, doc);
        factory.SetActiveDockable(doc);
        factory.SetFocusedDockable(docs, doc);
    }

    // ---- view ------------------------------------------------------------

    private void OnFit(object? sender, RoutedEventArgs e) => State.RequestZoom(0);
    private void OnZoomIn(object? sender, RoutedEventArgs e) => State.RequestZoom(1.5);
    private void OnZoomOut(object? sender, RoutedEventArgs e) => State.RequestZoom(1 / 1.5);
    private void OnZoomSelection(object? sender, RoutedEventArgs e) => State.ZoomToSelection();

    private void OnToggleAppTheme(object? sender, RoutedEventArgs e) =>
        Appearance.Set(Appearance.IsDark ? AppThemeMode.Light : AppThemeMode.Dark);

    private void OnAppDark(object? sender, RoutedEventArgs e) => Appearance.Set(AppThemeMode.Dark);
    private void OnAppLight(object? sender, RoutedEventArgs e) => Appearance.Set(AppThemeMode.Light);
    private void OnAppSystem(object? sender, RoutedEventArgs e) => Appearance.Set(AppThemeMode.System);

    private async void OnSettings(object? sender, RoutedEventArgs e) => await new SettingsWindow().ShowDialog(this);

    // ---- help ------------------------------------------------------------

    private void OnOpenExportFolder(object? sender, RoutedEventArgs e) => Shell.OpenFolder(AppState.GameExportFolder);
    private void OnOpenCartoFolder(object? sender, RoutedEventArgs e) => Shell.OpenFolder(AppState.CartoExportFolder);
    private void OnOpenSettingsFolder(object? sender, RoutedEventArgs e) => Shell.OpenFolder(AppSettings.Folder);

    private async void OnAbout(object? sender, RoutedEventArgs e)
    {
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "";
        var about = new Window
        {
            Title = $"About {AppInfo.Name}",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Icon = AppIcon.Create(),
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 6,
                Children =
                {
                    new Image { Source = AppIcon.Render(64), Width = 48, Height = 48, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left },
                    new TextBlock { Text = $"{AppInfo.Name} {version}", FontSize = 18, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = "Viewer and map maker for Cities: Skylines II OSM exports.", TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = AppInfo.Credit },
                    new TextBlock { Text = "Icons: Lucide (ISC License). UI: Avalonia, Dock.", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Margin = new Avalonia.Thickness(0, 8, 0, 0) },
                },
            },
        };
        await about.ShowDialog(this);
    }
}
