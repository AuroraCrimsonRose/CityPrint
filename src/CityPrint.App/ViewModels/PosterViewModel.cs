using CommunityToolkit.Mvvm.ComponentModel;
using CityPrint.App.Controls;
using CityPrint.Core.Map;
using CityPrint.Core.Osm;
using CityPrint.Core.Poster;

namespace CityPrint.App.ViewModels;

/// <summary>Poster designer state; edits write through to <see cref="Spec"/> and repaint the preview.</summary>
public sealed partial class PosterViewModel : ObservableObject
{
    private readonly AppState _app = AppState.Current;

    public PosterSpec Spec { get; } = new();

    public event Action? Changed;

    public IReadOnlyList<MapTheme> Themes => MapThemes.All;
    public IReadOnlyList<PaperSize> Papers => PaperSize.All;
    public IReadOnlyList<PosterLayoutStyle> Layouts { get; } = Enum.GetValues<PosterLayoutStyle>();
    public IReadOnlyList<int> DpiOptions { get; } = [96, 150, 200, 300];

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _caption = "";
    [ObservableProperty] private MapTheme _theme;
    [ObservableProperty] private PaperSize _paper;
    [ObservableProperty] private bool _landscape;
    [ObservableProperty] private PosterLayoutStyle _layout;
    [ObservableProperty] private bool _labels;
    [ObservableProperty] private double _labelScale = 1;
    [ObservableProperty] private bool _scaleBar;
    [ObservableProperty] private bool _legend;
    [ObservableProperty] private bool _realWidths = true;
    [ObservableProperty] private bool _useLayerVisibility;
    [ObservableProperty] private int _dpi;
    [ObservableProperty] private string _sizeText = "";

    public IReadOnlyList<LegendEntry> LegendEntries { get; private set; } = [];

    public PosterViewModel()
    {
        var p = _app.Settings.Poster;
        _theme = MapThemes.ById(p.MapTheme);
        _paper = PaperSize.ByName(p.Paper);
        _landscape = p.Landscape;
        _layout = Enum.TryParse<PosterLayoutStyle>(p.Layout, out var l) ? l : PosterLayoutStyle.Gallery;
        _labels = p.Labels;
        _scaleBar = p.ShowScale;
        _dpi = DpiOptions.Contains(p.Dpi) ? p.Dpi : 200;

        var model = _app.Model;
        _title = PosterContent.CityName(model?.Document.FilePath);
        _subtitle = "Cities: Skylines II";
        _caption = model != null ? PosterContent.DefaultCaption(model) : "";
        Sync();
        FitCity();
    }

    public MapScene? Scene => _app.Scene;

    public bool IsVisible(Category c) => !UseLayerVisibility || _app.IsVisible(c);

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(SizeText)) return;
        Sync();
        if (e.PropertyName is nameof(Theme) or nameof(Paper) or nameof(Landscape) or nameof(Layout) or nameof(Labels) or nameof(ScaleBar) or nameof(Dpi))
            SaveDefaults();
    }

    /// <summary>Screen presets have a natural orientation (a phone is portrait, a monitor landscape).</summary>
    partial void OnPaperChanged(PaperSize value)
    {
        if (value.IsScreen) Landscape = value.PixelWidth > value.PixelHeight;
    }

    private void Sync()
    {
        Spec.Title = Title;
        Spec.Subtitle = Subtitle;
        Spec.Caption = Caption;
        Spec.Theme = Theme;
        Spec.Paper = Paper;
        Spec.Landscape = Landscape;
        Spec.Style = Layout;
        Spec.Labels = Labels;
        Spec.LabelScale = LabelScale;
        Spec.ScaleBar = ScaleBar;
        Spec.Legend = Legend;
        Spec.RealWidths = RealWidths;
        Spec.Dpi = Dpi;
        LegendEntries = _app.Model is { } m ? PosterContent.Legend(m, Theme) : [];
        var (w, h) = Spec.PixelSize;
        SizeText = $"{w:N0} × {h:N0} px · {w * (long)h * 4 / 1048576.0:N0} MB in memory";
        Changed?.Invoke();
    }

    private void SaveDefaults()
    {
        var p = _app.Settings.Poster;
        p.MapTheme = Theme.Id;
        p.Paper = Paper.Name;
        p.Landscape = Landscape;
        p.Layout = Layout.ToString();
        p.Labels = Labels;
        p.ShowScale = ScaleBar;
        p.Dpi = Dpi;
        _app.Settings.Save();
    }

    public void FitCity()
    {
        var m = _app.Model;
        double half = Projection.MapSize / 2;
        double minX = m?.MinX ?? -half, maxX = m?.MaxX ?? half, minY = m?.MinY ?? -half, maxY = m?.MaxY ?? half;
        var map = PosterLayout.Compute(Spec).Map;
        Spec.CentreX = (minX + maxX) / 2;
        Spec.CentreY = (minY + maxY) / 2;
        // Span across the map width that fits the data in both directions, with a little air.
        double span = Math.Max(maxX - minX, (maxY - minY) * map.W / Math.Max(1, map.H));
        Spec.SpanMetres = Math.Max(200, span * 1.08);
        Changed?.Invoke();
    }

    public void UseCurrentView()
    {
        if (MapView.CurrentView is not { } v) return;
        Spec.CentreX = v.X;
        Spec.CentreY = v.Y;
        var map = PosterLayout.Compute(Spec).Map;
        // Everything visible on screen ends up inside the poster's map frame.
        Spec.SpanMetres = Math.Max(200, Math.Max(v.Width, v.Height * map.W / map.H) / v.Scale);
        Changed?.Invoke();
    }

    public void Pan(double dxMetres, double dyMetres)
    {
        Spec.CentreX += dxMetres;
        Spec.CentreY += dyMetres;
        Changed?.Invoke();
    }

    public void Zoom(double factor, double anchorX, double anchorY)
    {
        double newSpan = Math.Clamp(Spec.SpanMetres / factor, 100, Projection.MapSize * 3);
        double k = newSpan / Spec.SpanMetres;
        // Keep the anchor point fixed.
        Spec.CentreX = anchorX + (Spec.CentreX - anchorX) * k;
        Spec.CentreY = anchorY + (Spec.CentreY - anchorY) * k;
        Spec.SpanMetres = newSpan;
        Changed?.Invoke();
    }

    public string SuggestedFileName => string.Join("_", (Title.Length > 0 ? Title : "poster").Split(Path.GetInvalidFileNameChars())).Replace(' ', '_');
}
