using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CityPrint.App.Rendering;
using CityPrint.App.ViewModels;
using CityPrint.Core.Poster;

namespace CityPrint.App.Controls;

/// <summary>WYSIWYG poster preview: the page is drawn at full size in page units and scaled to fit.
/// Drag to move the map inside the frame, wheel to zoom it.</summary>
public sealed class PosterCanvas : Control
{
    private PosterViewModel? _vm;
    private Point _last;
    private bool _dragging;

    public PosterCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null) _vm.Changed -= InvalidateVisual;
        _vm = DataContext as PosterViewModel;
        if (_vm != null) _vm.Changed += InvalidateVisual;
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AppState.Current.ModelChanged += InvalidateVisual;
        AppState.Current.RenderInvalidated += InvalidateVisual;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        AppState.Current.ModelChanged -= InvalidateVisual;
        AppState.Current.RenderInvalidated -= InvalidateVisual;
    }

    /// <summary>Page-to-control scale and offset.</summary>
    private (double Scale, Point Offset) PageTransform(PosterSpec spec)
    {
        const double pad = 28;
        double s = Math.Min((Bounds.Width - 2 * pad) / spec.PageWidth, (Bounds.Height - 2 * pad) / spec.PageHeight);
        s = Math.Max(0.01, s);
        return (s, new Point((Bounds.Width - spec.PageWidth * s) / 2, (Bounds.Height - spec.PageHeight * s) / 2));
    }

    public override void Render(DrawingContext ctx)
    {
        var backdrop = this.TryFindResource("App.Bg0", ActualThemeVariant, out var b) ? b as IBrush : null;
        ctx.FillRectangle(backdrop ?? Brushes.Gray, new Rect(Bounds.Size));
        if (_vm is null) return;
        var spec = _vm.Spec;
        var (s, o) = PageTransform(spec);
        var page = new Rect(o.X, o.Y, spec.PageWidth * s, spec.PageHeight * s);

        // Soft drop shadow under the sheet.
        for (int i = 1; i <= 6; i++)
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(26 - i * 4), 0, 0, 0)), page.Inflate(i * 1.5).Translate(new Vector(0, i)), (float)(i * 1.5));

        using (ctx.PushClip(page))
        using (ctx.PushTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation(o.X, o.Y)))
            PosterRenderer.Render(ctx, spec, _vm.Scene, _vm.IsVisible, _vm.LegendEntries);
    }

    private (double X, double Y) PageToWorld(Point p)
    {
        var spec = _vm!.Spec;
        var (s, o) = PageTransform(spec);
        var l = PosterLayout.Compute(spec);
        double px = (p.X - o.X) / s, py = (p.Y - o.Y) / s;
        double scale = l.Scale(spec);
        return (spec.CentreX + (px - l.Map.CentreX) / scale, spec.CentreY - (py - (l.Map.Y + l.Map.H / 2)) / scale);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        _last = e.GetPosition(this);
        _dragging = true;
        e.Pointer.Capture(this);
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging || _vm is null) return;
        var p = e.GetPosition(this);
        var (s, _) = PageTransform(_vm.Spec);
        double scale = PosterLayout.Compute(_vm.Spec).Scale(_vm.Spec) * s;
        _vm.Pan(-(p.X - _last.X) / scale, (p.Y - _last.Y) / scale);
        _last = p;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
        Cursor = null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_vm is null) return;
        var (wx, wy) = PageToWorld(e.GetPosition(this));
        _vm.Zoom(Math.Pow(1.2, e.Delta.Y), wx, wy);
        e.Handled = true;
    }
}
