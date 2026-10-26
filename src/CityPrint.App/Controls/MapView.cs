using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CityPrint.App.Rendering;
using CityPrint.App.ViewModels;
using CityPrint.Core.Map;
using CityPrint.Core.Osm;

namespace CityPrint.App.Controls;

/// <summary>Interactive map: pan, zoom, hover and pick. Drawing is done by <see cref="MapRenderer"/>.</summary>
public sealed class MapView : Control
{
    private const double MinScale = 0.005, MaxScale = 20;

    private readonly AppState _state = AppState.Current;

    // Static so the viewport survives Dock re-creating the document content (tab moves, floating).
    private static double _cx, _cy, _scale = 0.05;
    private static bool _hasView;

    /// <summary>The current view centre and scale, e.g. for the poster designer's "use current view".</summary>
    public static (double X, double Y, double Scale, double Width, double Height)? CurrentView { get; private set; }

    private Point _pressPos, _lastPos;
    private bool _pressed, _panning;

    public MapView()
    {
        Focusable = true;
        ClipToBounds = true;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _state.ModelChanged += OnModelChanged;
        _state.RenderInvalidated += InvalidateVisual;
        _state.ZoomToRequested += ZoomTo;
        _state.ZoomRequested += OnZoomRequested;
        if (_state.PendingZoom is { } pending) ZoomTo(pending);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _state.ModelChanged -= OnModelChanged;
        _state.RenderInvalidated -= InvalidateVisual;
        _state.ZoomToRequested -= ZoomTo;
        _state.ZoomRequested -= OnZoomRequested;
    }

    private void OnZoomRequested(double factor)
    {
        if (factor <= 0) Fit();
        else ZoomBy(factor);
    }

    private void OnModelChanged()
    {
        if (!_state.KeepViewOnNextModel || !_hasView) Fit();
        _state.KeepViewOnNextModel = false;
        InvalidateVisual();
    }

    // ---- view transform --------------------------------------------------

    private double W => Math.Max(1, Bounds.Width);
    private double H => Math.Max(1, Bounds.Height);

    private Viewport View => new(_cx, _cy, _scale, W, H);

    public void Fit()
    {
        var m = _state.Model;
        double half = Projection.MapSize / 2;
        var vp = Viewport.Fit(m?.MinX ?? -half, m?.MinY ?? -half, m?.MaxX ?? half, m?.MaxY ?? half, W, H);
        _cx = vp.CentreX;
        _cy = vp.CentreY;
        if (Bounds.Width > 0)
        {
            _scale = Math.Clamp(vp.Scale, MinScale, MaxScale);
            _hasView = true;
        }
        UpdateScaleText();
        InvalidateVisual();
    }

    public void ZoomBy(double factor) => ZoomAt(new Point(W / 2, H / 2), factor);

    private void ZoomAt(Point screen, double factor)
    {
        var (wx, wy) = View.ToWorld(screen);
        _scale = Math.Clamp(_scale * factor, MinScale, MaxScale);
        // Keep the world point under the cursor fixed.
        _cx = wx - (screen.X - W / 2) / _scale;
        _cy = wy + (screen.Y - H / 2) / _scale;
        UpdateScaleText();
        InvalidateVisual();
    }

    private void ZoomTo((double MinX, double MinY, double MaxX, double MaxY) box)
    {
        // Dock materialises the map lazily; keep the request until we have a size.
        if (Bounds.Width <= 0) return;
        _state.PendingZoom = null;
        _cx = (box.MinX + box.MaxX) / 2;
        _cy = (box.MinY + box.MaxY) / 2;
        double w = Math.Max(box.MaxX - box.MinX, 60), h = Math.Max(box.MaxY - box.MinY, 60);
        _scale = Math.Clamp(Math.Min(W / w, H / h) * 0.6, MinScale, 1.5);
        UpdateScaleText();
        InvalidateVisual();
    }

    private void UpdateScaleText() =>
        _state.ScaleText = _scale >= 1 ? $"{_scale:0.##} px/m" : $"1 px = {1 / _scale:0.#} m";

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (!_hasView && e.NewSize.Width > 0) Fit();
        if (_state.PendingZoom is { } pending) ZoomTo(pending);
    }

    // ---- rendering -------------------------------------------------------

    public override void Render(DrawingContext ctx)
    {
        var theme = _state.MapTheme;
        if (!_hasView)
        {
            ctx.FillRectangle(MapRenderer.Brush(theme.Background), new Rect(Bounds.Size));
            return;
        }
        CurrentView = (_cx, _cy, _scale, W, H);
        MapRenderer.Render(ctx, _state.Scene, View, theme, new MapRenderOptions
        {
            RealWidths = _state.RealWidths,
            Labels = _state.ShowLabels,
            TileGrid = _state.ShowTileGrid,
            MapBounds = _state.ShowMapBounds,
            IsVisible = _state.IsVisible,
            Hovered = _state.Hovered,
            Selected = _state.Selected,
            SelectedGroup = _state.SelectedGroup?.Features,
        });

        if (_state.Model is null)
        {
            var ft = MapRenderer.Text("Drop an .osm file or Carto export here, or use File ▸ Open", 15, theme.IsDark ? 0xFF8A919Cu : 0xFF7A7A7Au);
            ctx.DrawText(ft, new Point((W - ft.Width) / 2, (H - ft.Height) / 2));
        }
    }

    // ---- input -----------------------------------------------------------

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        _pressPos = _lastPos = e.GetPosition(this);
        _pressed = true;
        _panning = false;
        e.Pointer.Capture(this);
        if (e.ClickCount == 2 && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ZoomAt(_pressPos, 2);
            _pressed = false;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        var (wx, wy) = View.ToWorld(pos);
        _state.CursorText = string.Create(CultureInfo.InvariantCulture,
            $"x {wx,8:F1} m   z {wy,8:F1} m   lat {Projection.ToLat(wy):F6}   lon {Projection.ToLon(wx):F6}");

        if (_pressed)
        {
            if (!_panning && Distance(pos, _pressPos) > 3) _panning = true;
            if (_panning)
            {
                _cx -= (pos.X - _lastPos.X) / _scale;
                _cy += (pos.Y - _lastPos.Y) / _scale;
                _lastPos = pos;
                InvalidateVisual();
            }
            return;
        }
        _state.Hovered = Pick(pos);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pressed && !_panning && e.InitialPressMouseButton == MouseButton.Left)
            _state.Selected = Pick(e.GetPosition(this));
        _pressed = false;
        _panning = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _state.Hovered = null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ZoomAt(e.GetPosition(this), Math.Pow(1.25, e.Delta.Y));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.F or Key.Home: Fit(); break;
            case Key.OemPlus or Key.Add: ZoomBy(1.5); break;
            case Key.OemMinus or Key.Subtract: ZoomBy(1 / 1.5); break;
            case Key.Escape: _state.Selected = null; break;
            default: return;
        }
        e.Handled = true;
    }

    private MapFeature? Pick(Point screen)
    {
        if (_state.Model is null) return null;
        var (wx, wy) = View.ToWorld(screen);
        return _state.Model.HitTest(wx, wy, 6 / _scale, _state.IsVisible);
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    // ---- drag & drop -----------------------------------------------------

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var file = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => p != null);
        if (file != null) _ = _state.LoadAsync(file);
    }
}
