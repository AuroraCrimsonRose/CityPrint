using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CityPrint.App.Services;
using CityPrint.Core.Map;
using CityPrint.Core.Osm;

namespace CityPrint.App.ViewModels;

public sealed partial class LayerItem : ObservableObject
{
    public required Category Category { get; init; }
    public required string Name { get; init; }
    public required string Group { get; init; }
    public bool IsFirstInGroup { get; init; }

    [ObservableProperty] private IBrush _swatch = Brushes.Transparent;
    [ObservableProperty] private int _count;
    [ObservableProperty] private bool _isVisible = true;

    public event Action? VisibilityChanged;
    partial void OnIsVisibleChanged(bool value) => VisibilityChanged?.Invoke();
}

public sealed record TagRow(string Key, string Value);

public sealed record StatGroup(string Name, IReadOnlyList<StatRow> Rows);

public sealed record DiagnosticRow(DiagnosticLevel Level, string Message, string? ElementKey)
{
    public string LevelText => Level.ToString().ToUpperInvariant();
    public IBrush LevelBrush => Level switch
    {
        DiagnosticLevel.Error => Brushes.IndianRed,
        DiagnosticLevel.Warning => Brushes.Goldenrod,
        _ => Brushes.SteelBlue,
    };
}

/// <summary>Application-wide state shared by the map and every docked panel.</summary>
public sealed partial class AppState : ObservableObject
{
    public static AppState Current { get; } = new();

    public static string GameExportFolder { get; } = FindGameExportFolder();

    /// <summary>The OSM Export mod writes into the game's Unity persistent data folder.</summary>
    private static string FindGameExportFolder()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] tail = ["Colossal Order", "Cities Skylines II", "ModsData", "OSMExport"];
        if (OperatingSystem.IsMacOS())
            return Path.Combine([home, "Library", "Application Support", .. tail]);
        if (OperatingSystem.IsLinux())
        {
            // The game runs under Proton (Steam app 949230), so the folder is inside its Wine prefix.
            string localLow = Path.Combine("steamapps", "compatdata", "949230", "pfx", "drive_c", "users", "steamuser", "AppData", "LocalLow");
            foreach (var steam in new[] { ".steam/steam", ".local/share/Steam", ".var/app/com.valvesoftware.Steam/.local/share/Steam" })
            {
                var candidate = Path.Combine([home, steam, localLow, .. tail]);
                if (Directory.Exists(candidate)) return candidate;
            }
            return Path.Combine([home, ".steam/steam", localLow, .. tail]);
        }
        return Path.Combine([home, "AppData", "LocalLow", .. tail]);
    }

    public AppSettings Settings { get; }

    private readonly bool[] _visible = Enumerable.Repeat(true, Enum.GetValues<Category>().Length).ToArray();
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _reloadDebounce;

    private AppState()
    {
        Settings = AppSettings.Load();
        _mapTheme = MapThemes.ById(Settings.MapTheme);
        _showLabels = Settings.ShowLabels;
        _realWidths = Settings.RealWidths;
        _showMapBounds = Settings.ShowMapBounds;
        _showTileGrid = Settings.ShowTileGrid;
        _autoReload = Settings.AutoReload;

        string? group = null;
        foreach (var s in MapStyle.All.OrderBy(s => GroupOrder(s.Group)).ThenBy(s => s.Order))
        {
            var item = new LayerItem
            {
                Category = s.Category,
                Name = s.Name,
                Group = s.Group,
                IsFirstInGroup = s.Group != group,
            };
            group = s.Group;
            item.VisibilityChanged += () =>
            {
                _visible[(int)item.Category] = item.IsVisible;
                RenderInvalidated?.Invoke();
            };
            Layers.Add(item);
        }
        UpdateSwatches();
        RefreshRecent();
    }

    private static int GroupOrder(string g) => g switch
    {
        "Roads" => 0, "Transit" => 1, "Land use" => 2, "Nature" => 3, "Utilities" => 4, "Places" => 5, _ => 6,
    };

    // ---- events consumed by the map view --------------------------------

    public event Action? ModelChanged;
    public event Action? RenderInvalidated;
    public event Action<(double MinX, double MinY, double MaxX, double MaxY)>? ZoomToRequested;

    /// <summary>Zoom factor for the map; 0 means fit to the data.</summary>
    public event Action<double>? ZoomRequested;

    public void RequestZoom(double factor) => ZoomRequested?.Invoke(factor);

    public void InvalidateRender() => RenderInvalidated?.Invoke();

    // ---- document --------------------------------------------------------

    [ObservableProperty] private MapModel? _model;
    [ObservableProperty] private MapScene? _scene;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _status = "Open an OSM export (File ▸ Open, or drop a file on the map).";
    [ObservableProperty] private string _title = AppInfo.Name;
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private string _cursorText = "";
    [ObservableProperty] private string _scaleText = "";

    public ObservableCollection<LayerItem> Layers { get; } = new();
    public ObservableCollection<StatGroup> Stats { get; } = new();
    public ObservableCollection<DiagnosticRow> Diagnostics { get; } = new();
    public ObservableCollection<string> RecentFiles { get; } = new();

    public bool IsVisible(MapFeature f) => _visible[(int)f.Category];
    public bool IsVisible(Category c) => _visible[(int)c];

    public async Task LoadAsync(string path, bool keepView = false)
    {
        IsLoading = true;
        Status = $"Loading {Path.GetFileName(path)}…";
        try
        {
            var (model, scene, stats) = await Task.Run(() =>
            {
                var m = MapModel.Build(OsmDocument.Load(path));
                return (m, MapScene.Build(m), MapStats.Compute(m));
            });

            long? selectedId = Selected?.Element.Id;
            var selectedType = Selected?.Element.Type;

            Model = model;
            Scene = scene;
            KeepViewOnNextModel = keepView;
            FileName = Path.GetFileName(path);
            Title = $"{AppInfo.Name} — {FileName}";

            foreach (var l in Layers) l.Count = model.CountByCategory.GetValueOrDefault(l.Category);
            Stats.Clear();
            foreach (var g in stats.GroupBy(r => r.Section)) Stats.Add(new StatGroup(g.Key, g.ToList()));
            Diagnostics.Clear();
            foreach (var d in model.Document.Diagnostics) Diagnostics.Add(new(d.Level, d.Message, d.ElementKey));
            DiagnosticsSummary = Diagnostics.Count == 0 ? "No issues found" :
                $"{Diagnostics.Count(d => d.Level == DiagnosticLevel.Error)} errors, {Diagnostics.Count(d => d.Level == DiagnosticLevel.Warning)} warnings, {Diagnostics.Count(d => d.Level == DiagnosticLevel.Info)} info";

            SelectedGroup = null;
            Selected = selectedId is null ? null
                : model.Features.FirstOrDefault(f => f.Element.Id == selectedId && f.Element.Type == selectedType);
            UpdateSearch();
            Watch(path);

            if (!keepView)
            {
                Settings.AddRecent(path);
                Settings.Save();
                RefreshRecent();
            }

            var doc = model.Document;
            Status = $"{doc.Nodes.Count:N0} nodes · {doc.Ways.Count:N0} ways · {doc.Relations.Count:N0} relations · " +
                     $"{model.Features.Count:N0} features · loaded {DateTime.Now:HH:mm:ss}";
            ModelChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Status = $"Failed to load {Path.GetFileName(path)}: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Set by reloads so the map keeps its viewport instead of re-fitting.</summary>
    public bool KeepViewOnNextModel { get; set; }

    public Task ReloadAsync() =>
        Model?.Document.FilePath is { } p ? LoadAsync(p, keepView: true) : Task.CompletedTask;

    private void RefreshRecent()
    {
        RecentFiles.Clear();
        foreach (var f in Settings.RecentFiles) RecentFiles.Add(f);
        OnPropertyChanged(nameof(HasRecentFiles));
    }

    public bool HasRecentFiles => RecentFiles.Count > 0;

    public void ClearRecent()
    {
        Settings.RecentFiles.Clear();
        Settings.Save();
        RefreshRecent();
    }

    // ---- auto reload -----------------------------------------------------

    [ObservableProperty] private bool _autoReload = true;

    partial void OnAutoReloadChanged(bool value)
    {
        if (_watcher != null) _watcher.EnableRaisingEvents = value;
        Settings.AutoReload = value;
        Settings.Save();
    }

    private void Watch(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var name = Path.GetFileName(path);
        if (_watcher?.Path == dir && _watcher.Filter == name) return;
        _watcher?.Dispose();
        _watcher = new FileSystemWatcher(dir, name)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = AutoReload,
        };
        _watcher.Changed += (_, _) => ScheduleReload();
        _watcher.Created += (_, _) => ScheduleReload();
        _watcher.Renamed += (_, _) => ScheduleReload();
    }

    /// <summary>The mod writes the file in chunks; wait for it to go quiet before reloading.</summary>
    private void ScheduleReload()
    {
        _reloadDebounce?.Cancel();
        var cts = _reloadDebounce = new CancellationTokenSource();
        Task.Delay(1000, cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled) Dispatcher.UIThread.Post(() => _ = ReloadAsync());
        }, TaskScheduler.Default);
    }

    // ---- selection / inspector -------------------------------------------

    [ObservableProperty] private MapFeature? _selected;
    [ObservableProperty] private FeatureGroup? _selectedGroup;
    [ObservableProperty] private MapFeature? _hovered;
    [ObservableProperty] private string _diagnosticsSummary = "";
    [ObservableProperty] private string _inspectorTitle = "";
    [ObservableProperty] private string _inspectorSubtitle = "";
    [ObservableProperty] private string _tagsHeader = "Tags";

    public ObservableCollection<TagRow> SelectedTags { get; } = new();
    public ObservableCollection<TagRow> SelectedInfo { get; } = new();

    public bool HasSelection => Selected != null || SelectedGroup != null;

    private bool _selectingGroup;

    partial void OnSelectedChanged(MapFeature? value)
    {
        // Picking a single feature (on the map or in a group) leaves group mode unless it is the group's only member.
        if (!_selectingGroup && SelectedGroup != null && (value is null || SelectedGroup.Features.Count > 1 || SelectedGroup.Features[0] != value))
            SelectedGroup = null;
        UpdateInspector();
    }

    partial void OnSelectedGroupChanged(FeatureGroup? value) => UpdateInspector();

    public void SelectGroup(FeatureGroup g)
    {
        _selectingGroup = true;
        try
        {
            SelectedGroup = g;
            Selected = g.Features.Count == 1 ? g.Features[0] : null;
        }
        finally
        {
            _selectingGroup = false;
        }
        UpdateInspector();
    }

    private void UpdateInspector()
    {
        SelectedTags.Clear();
        SelectedInfo.Clear();
        OnPropertyChanged(nameof(HasSelection));
        RenderInvalidated?.Invoke();
        void Info(string k, string v) => SelectedInfo.Add(new(k, v));

        if (SelectedGroup is { Features.Count: > 1 } g)
        {
            InspectorTitle = g.Title;
            InspectorSubtitle = $"{g.Section} · {g.Summary}";
            TagsHeader = "Common tags";
            foreach (var (k, v) in g.Describe()) Info(k, v);
            foreach (var kv in g.CommonTags()) SelectedTags.Add(new(kv.Key, kv.Value));
            return;
        }

        var value = Selected;
        TagsHeader = "Tags";
        if (value is null)
        {
            InspectorTitle = "";
            InspectorSubtitle = "";
            return;
        }

        var e = value.Element;
        InspectorTitle = value.DisplayName;
        InspectorSubtitle = $"{value.Style.Name} · {e.Key}";
        foreach (var kv in e.Tags.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            SelectedTags.Add(new(kv.Key, kv.Value));

        Info("Element", e.Key);
        Info("Category", $"{value.Style.Name} ({value.Kind.ToString().ToLowerInvariant()})");
        if (value.Name != null) Info("Name", value.Name);
        switch (e)
        {
            case OsmWay w:
                Info("Nodes", $"{w.NodeRefs.Length:N0}{(w.IsClosed ? " (closed)" : "")}");
                break;
            case OsmRelation r:
                Info("Members", string.Join(", ", r.Members.GroupBy(m => m.Role == "" ? "(none)" : m.Role).Select(g => $"{g.Count()} {g.Key}")));
                break;
            case OsmNode n:
                Info("Lat / Lon", string.Create(CultureInfo.InvariantCulture, $"{n.Lat:F7}, {n.Lon:F7}"));
                break;
        }
        Info("Game position", string.Create(CultureInfo.InvariantCulture, $"x {value.CentreX:F1} m, z {value.CentreY:F1} m"));
        if (value.Kind == GeometryKind.Line) Info("Length", MapStats.FormatLength(value.LengthMetres));
        if (value.Kind == GeometryKind.Area) Info("Area", MapStats.FormatArea(value.AreaSqMetres));
        if (value.Kind != GeometryKind.Point)
            Info("Size", $"{MapStats.FormatLength(value.MaxX - value.MinX)} × {MapStats.FormatLength(value.MaxY - value.MinY)}");
        if (value.Lanes > 0) Info("Lanes", value.Lanes.ToString(CultureInfo.InvariantCulture));
        if (value.Layer != 0 || value.IsBridge || value.IsTunnel)
            Info("Layer", $"{value.Layer}{(value.IsBridge ? " · bridge" : "")}{(value.IsTunnel ? " · tunnel" : "")}");
        if (Model != null && value.Name != null && value.Kind == GeometryKind.Line)
        {
            string section = FeatureGroups.SectionOf(value.Category);
            int siblings = Model.Features.Count(f => f.Name == value.Name && FeatureGroups.SectionOf(f.Category) == section);
            if (siblings > 1) Info("Street", $"segment of {siblings} named \"{value.Name}\"");
        }
    }

    partial void OnHoveredChanged(MapFeature? value) => RenderInvalidated?.Invoke();

    /// <summary>A zoom the map has not been able to apply yet (it may not be laid out).</summary>
    public (double MinX, double MinY, double MaxX, double MaxY)? PendingZoom { get; set; }

    private void RequestZoomTo(double minX, double minY, double maxX, double maxY)
    {
        PendingZoom = (minX, minY, maxX, maxY);
        ZoomToRequested?.Invoke(PendingZoom.Value);
    }

    public void ZoomTo(MapFeature f)
    {
        Selected = f;
        RequestZoomTo(f.MinX, f.MinY, f.MaxX, f.MaxY);
    }

    public void ZoomTo(FeatureGroup g)
    {
        SelectGroup(g);
        RequestZoomTo(g.MinX, g.MinY, g.MaxX, g.MaxY);
    }

    /// <summary>Zooms to whatever is selected: the group if one is active, otherwise the feature.</summary>
    public void ZoomToSelection()
    {
        if (SelectedGroup is { } g) ZoomTo(g);
        else if (Selected is { } f) ZoomTo(f);
    }

    public void SelectByKey(string? key)
    {
        if (key is null || Model is null) return;
        var f = Model.Features.FirstOrDefault(f => f.Element.Key == key);
        if (f != null) ZoomTo(f);
    }

    /// <summary>Selects every segment that shares the selected street's name and category.</summary>
    public void SelectWholeStreet()
    {
        if (Model is null || Selected is not { Name: { } name } f) return;
        string section = FeatureGroups.SectionOf(f.Category);
        var g = FeatureGroups.Build(Model.Features.Where(x => x.Name == name && FeatureGroups.SectionOf(x.Category) == section)).FirstOrDefault();
        if (g != null) SelectGroup(g);
    }

    // ---- search / explorer -----------------------------------------------

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _searchSummary = "";
    [ObservableProperty] private IReadOnlyList<ExplorerNode> _explorerRoots = [];

    partial void OnSearchTextChanged(string value) => UpdateSearch();

    private void UpdateSearch()
    {
        if (Model is null) { SearchSummary = ""; ExplorerRoots = []; return; }
        var q = SearchText.Trim();

        IEnumerable<MapFeature> hits = Model.Features;
        if (q.Length > 0)
        {
            // "key=value" or "key=" searches tags; anything else matches names, ids and categories.
            int eq = q.IndexOf('=');
            if (eq > 0)
            {
                var k = q[..eq].Trim();
                var v = q[(eq + 1)..].Trim();
                hits = hits.Where(f => f.Element.Tags.TryGetValue(k, out var tv) && (v.Length == 0 || tv.Contains(v, StringComparison.OrdinalIgnoreCase)));
            }
            else
            {
                hits = hits.Where(f =>
                    (f.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    f.Element.Id.ToString(CultureInfo.InvariantCulture) == q ||
                    f.Style.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
            }
        }

        var list = hits.ToList();
        ExplorerRoots = ExplorerNode.BuildTree(list, expandAll: q.Length > 0);
        int groups = ExplorerRoots.Sum(s => s.Children.Count);
        SearchSummary = $"{list.Count:N0} feature{(list.Count == 1 ? "" : "s")} in {groups:N0} group{(groups == 1 ? "" : "s")}";
    }

    // ---- view options ----------------------------------------------------

    [ObservableProperty] private bool _showTileGrid;
    [ObservableProperty] private bool _showLabels = true;
    [ObservableProperty] private bool _showMapBounds = true;
    [ObservableProperty] private bool _realWidths = true;
    [ObservableProperty] private MapTheme _mapTheme = MapThemes.Classic;

    public IReadOnlyList<MapTheme> MapThemeList => MapThemes.All;

    partial void OnShowTileGridChanged(bool value) { Settings.ShowTileGrid = value; ViewOptionChanged(); }
    partial void OnShowLabelsChanged(bool value) { Settings.ShowLabels = value; ViewOptionChanged(); }
    partial void OnShowMapBoundsChanged(bool value) { Settings.ShowMapBounds = value; ViewOptionChanged(); }
    partial void OnRealWidthsChanged(bool value) { Settings.RealWidths = value; ViewOptionChanged(); }

    partial void OnMapThemeChanged(MapTheme value)
    {
        Settings.MapTheme = value.Id;
        UpdateSwatches();
        ViewOptionChanged();
    }

    /// <summary>Puts every preference back to its default. Recent files are kept; they have their own Clear button.</summary>
    public void ResetSettings()
    {
        var d = new AppSettings();
        Settings.AppTheme = d.AppTheme;
        Settings.OpenLastFile = d.OpenLastFile;
        Settings.Poster = d.Poster;
        MapTheme = MapThemes.ById(d.MapTheme);
        ShowLabels = d.ShowLabels;
        RealWidths = d.RealWidths;
        ShowMapBounds = d.ShowMapBounds;
        ShowTileGrid = d.ShowTileGrid;
        AutoReload = d.AutoReload;
        Appearance.Set(d.AppTheme);
        Settings.Save();
    }

    private void ViewOptionChanged()
    {
        Settings.Save();
        RenderInvalidated?.Invoke();
    }

    /// <summary>Layer swatches follow the map theme so the legend matches what is drawn.</summary>
    private void UpdateSwatches()
    {
        foreach (var l in Layers)
        {
            var s = MapStyle.Of(l.Category);
            var p = MapTheme.PaintOf(l.Category);
            uint c = s.Kind switch
            {
                GeometryKind.Area => p.Fill != 0 ? p.Fill : p.Stroke,
                GeometryKind.Point => p.Stroke != 0 ? p.Stroke : p.Fill,
                _ => p.Stroke != 0 && p.Stroke != MapTheme.Background ? p.Stroke : p.Casing,
            };
            if (l.Category == Category.TransitStop) c = MapTheme.StopStroke;
            l.Swatch = c == 0 ? Brushes.Transparent : new SolidColorBrush(Color.FromUInt32(c));
        }
    }

    public void SetAllLayers(bool visible)
    {
        foreach (var l in Layers) l.IsVisible = visible;
    }
}
