using System.Globalization;
using Avalonia;
using Avalonia.Media;
using CityPrint.Core.Map;
using CityPrint.Core.Osm;

namespace CityPrint.App.Rendering;

/// <summary>Maps game metres onto an output surface; y grows north in the world and down on screen.</summary>
public readonly record struct Viewport(double CentreX, double CentreY, double Scale, double Width, double Height)
{
    public Point ToScreen(double x, double y) => new((x - CentreX) * Scale + Width / 2, -(y - CentreY) * Scale + Height / 2);
    public (double X, double Y) ToWorld(Point p) => ((p.X - Width / 2) / Scale + CentreX, -(p.Y - Height / 2) / Scale + CentreY);

    public Matrix WorldMatrix =>
        Matrix.CreateScale(Scale, Scale) * Matrix.CreateTranslation(Width / 2 - CentreX * Scale, Height / 2 + CentreY * Scale);

    /// <summary>The viewport that fits a world rectangle with a margin (fraction of the size kept free).</summary>
    public static Viewport Fit(double minX, double minY, double maxX, double maxY, double width, double height, double fill = 0.92) =>
        new((minX + maxX) / 2, (minY + maxY) / 2,
            Math.Min(width / Math.Max(1, maxX - minX), height / Math.Max(1, maxY - minY)) * fill, width, height);
}

public sealed class MapRenderOptions
{
    public double PixelScale { get; init; } = 1;
    public bool RealWidths { get; init; } = true;
    public bool Labels { get; init; } = true;
    public double LabelScale { get; init; } = 1;

    /// <summary>Multiplies the zoom used for label and point detail thresholds.</summary>
    public double DetailBoost { get; init; } = 1;
    public bool TileGrid { get; init; }
    public bool MapBounds { get; init; }
    public bool ScaleBar { get; init; } = true;
    public bool NorthArrow { get; init; } = true;
    public bool Background { get; init; } = true;
    public Func<Category, bool> IsVisible { get; init; } = _ => true;
    public MapFeature? Hovered { get; init; }
    public MapFeature? Selected { get; init; }
    public IReadOnlyList<MapFeature>? SelectedGroup { get; init; }
}

/// <summary>
/// Draws a <see cref="MapScene"/> with a <see cref="MapTheme"/>. Each bucket's features are merged
/// into one StreamGeometry (cached on the bucket) so a whole city draws in a few dozen calls; the
/// world transform maps metres to pixels and pen widths are in metres.
/// </summary>
public static class MapRenderer
{
    public const uint HoverColour = 0x9000B4FF, SelectColour = 0xE0FF8C00;

    private static readonly Typeface Regular = new("Inter", FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface Bold = new("Inter", FontStyle.Normal, FontWeight.Bold);

    public static void Render(DrawingContext ctx, MapScene? scene, Viewport vp, MapTheme theme, MapRenderOptions o)
    {
        if (o.Background)
            ctx.FillRectangle(Brush(theme.Background), new Rect(0, 0, vp.Width, vp.Height));

        var pc = new PaintContext(theme, vp.Scale, o.PixelScale, o.RealWidths, o.IsVisible);
        using (ctx.PushTransform(vp.WorldMatrix))
        {
            DrawMapFrame(ctx, theme, o, pc);
            if (scene != null)
            {
                foreach (var b in scene.Areas)
                {
                    if (MapPainter.Area(b, pc) is not { } paint) continue;
                    ctx.DrawGeometry(paint.Fill != 0 ? Brush(paint.Fill) : null, paint.Outline is { } s ? Pen(s) : null, AreaGeometry(b));
                }
                foreach (int layer in scene.Layers)
                {
                    using var _ = layer < 0 ? ctx.PushOpacity(0.45) : default(DrawingContext.PushedState?);
                    foreach (var (bucket, stroke) in MapPainter.LineLayer(scene.Lines.Where(b => b.Layer == layer), pc))
                        ctx.DrawGeometry(null, Pen(stroke), LineGeometry(bucket));
                }
                DrawHighlights(ctx, o, pc);
            }
        }

        if (scene != null)
        {
            DrawPoints(ctx, scene, vp, theme, o, pc);
            if (o.Labels) DrawLabels(ctx, scene, vp, theme, o, pc);
        }
        if (o.ScaleBar) DrawScaleBar(ctx, vp, theme, o);
        if (o.NorthArrow) DrawNorthArrow(ctx, vp, theme, o);
    }

    // ---- geometry --------------------------------------------------------

    private static StreamGeometry AreaGeometry(AreaBucket b) =>
        b.Cache as StreamGeometry ?? (StreamGeometry)(b.Cache = BuildGeometry(b.Features, true));

    private static StreamGeometry LineGeometry(LineBucket b) =>
        b.Cache as StreamGeometry ?? (StreamGeometry)(b.Cache = BuildGeometry(b.Features, false));

    public static StreamGeometry BuildGeometry(IEnumerable<MapFeature> features, bool filled)
    {
        var geo = new StreamGeometry();
        using var ctx = geo.Open();
        ctx.SetFillRule(FillRule.EvenOdd);
        foreach (var f in features)
        {
            foreach (var p in f.Parts)
            {
                if (p.Length < 4) continue;
                ctx.BeginFigure(new Point(p[0], -p[1]), filled);
                for (int i = 2; i < p.Length; i += 2)
                    ctx.LineTo(new Point(p[i], -p[i + 1]));
                ctx.EndFigure(filled);
            }
        }
        return geo;
    }

    // Highlight geometry is rebuilt only when the highlighted set changes.
    private static object? _highlightKey;
    private static StreamGeometry? _highlightLines, _highlightAreas;

    private static void DrawHighlights(DrawingContext ctx, MapRenderOptions o, PaintContext pc)
    {
        if (o.Hovered is { } h && h != o.Selected && o.SelectedGroup?.Contains(h) != true)
            DrawHighlight(ctx, [h], HoverColour, 3, 0x2000B4FF, pc);
        if (o.SelectedGroup is { Count: > 0 } g)
            DrawHighlight(ctx, g, SelectColour, 4, 0x40FF8C00, pc);
        else if (o.Selected is { } s)
            DrawHighlight(ctx, [s], SelectColour, 4, 0x40FF8C00, pc);
    }

    private static void DrawHighlight(DrawingContext ctx, IReadOnlyList<MapFeature> features, uint colour, double thickness, uint fill, PaintContext pc)
    {
        StreamGeometry lines, areas;
        object key = features.Count == 1 ? features[0] : features;
        if (ReferenceEquals(key, _highlightKey) && _highlightLines != null && _highlightAreas != null)
        {
            (lines, areas) = (_highlightLines, _highlightAreas);
        }
        else
        {
            lines = BuildGeometry(features.Where(f => f.Kind == GeometryKind.Line), false);
            areas = BuildGeometry(features.Where(f => f.Kind == GeometryKind.Area), true);
            // Only cache the selection (hover changes constantly and is a single feature).
            if (colour == SelectColour) (_highlightKey, _highlightLines, _highlightAreas) = (key, lines, areas);
        }
        double px = pc.Px;
        ctx.DrawGeometry(Brush(fill), new Pen(Brush(colour), thickness * px, null, PenLineCap.Round, PenLineJoin.Round), areas);
        double w = features.Where(f => f.Kind == GeometryKind.Line).Select(f => MapPainter.StrokeMetres(f.WidthMetres, f.Style.MinPixels, pc)).DefaultIfEmpty(0).Max();
        ctx.DrawGeometry(null, new Pen(Brush(colour), w + thickness * px, null, PenLineCap.Round, PenLineJoin.Round), lines);
    }

    // ---- frame & points --------------------------------------------------

    private static void DrawMapFrame(DrawingContext ctx, MapTheme theme, MapRenderOptions o, PaintContext pc)
    {
        double half = Projection.MapSize / 2, px = pc.Px;
        if (o.TileGrid)
        {
            var pen = new Pen(Brush(theme.Grid), px);
            double tile = Projection.MapSize / Projection.TileCount;
            for (int i = 1; i < Projection.TileCount; i++)
            {
                double v = -half + i * tile;
                ctx.DrawLine(pen, new Point(v, -half), new Point(v, half));
                ctx.DrawLine(pen, new Point(-half, v), new Point(half, v));
            }
        }
        if (o.MapBounds)
            ctx.DrawRectangle(null, new Pen(Brush(theme.Frame), 1.5 * px, new DashStyle([6, 4], 0)), new Rect(-half, -half, half * 2, half * 2));
    }

    private static void DrawPoints(DrawingContext ctx, MapScene scene, Viewport vp, MapTheme theme, MapRenderOptions o, PaintContext pc)
    {
        double k = o.PixelScale, zoom = pc.Zoom * o.DetailBoost;
        foreach (var f in scene.Points)
        {
            if (!o.IsVisible(f.Category)) continue;
            var p = vp.ToScreen(f.Parts[0][0], f.Parts[0][1]);
            if (p.X < -20 * k || p.Y < -20 * k || p.X > vp.Width + 20 * k || p.Y > vp.Height + 20 * k) continue;
            var paint = theme.PaintOf(f.Category);
            switch (f.Category)
            {
                case Category.Tree:
                    if (zoom < 0.3 || paint.Fill == 0) continue;
                    double r = Math.Max(1.5 * k, 4 * vp.Scale);
                    ctx.DrawEllipse(Brush(paint.Fill), null, p, r, r);
                    break;
                case Category.TransitStop:
                    if (paint.Fill == 0 && paint.Stroke == 0) continue;
                    double sr = 4.5 * k * theme.StopScale;
                    ctx.DrawEllipse(Brush(theme.StopFill), new Pen(Brush(theme.StopStroke), 1.5 * k * Math.Sqrt(theme.StopScale)), p, sr, sr);
                    break;
                case Category.Place:
                    break; // label only
                default:
                    if (zoom < 0.2 || paint.Fill == 0) continue;
                    ctx.DrawEllipse(Brush(paint.Fill), new Pen(Brush(theme.IsDark ? theme.Background : 0xFFFFFFFF), k), p, 3.5 * k, 3.5 * k);
                    break;
            }
            if (f == o.Selected || f == o.Hovered || o.SelectedGroup?.Contains(f) == true)
                ctx.DrawEllipse(null, new Pen(Brush(f == o.Hovered && f != o.Selected ? HoverColour : SelectColour), 3 * k), p, 8 * k, 8 * k);
        }
    }

    // ---- labels ----------------------------------------------------------

    private static void DrawLabels(DrawingContext ctx, MapScene scene, Viewport vp, MapTheme theme, MapRenderOptions o, PaintContext pc)
    {
        var occupied = new List<Rect>();
        double k = o.PixelScale, t = k * o.LabelScale, zoom = pc.Zoom * o.DetailBoost;
        var halo = Brush(theme.LabelHalo);

        bool TryReserve(Rect r)
        {
            if (r.Right < 0 || r.Bottom < 0 || r.X > vp.Width || r.Y > vp.Height) return false;
            foreach (var other in occupied) if (other.Intersects(r)) return false;
            occupied.Add(r.Inflate(3 * k));
            return true;
        }

        // Place names first: they matter most at every zoom level.
        foreach (var f in scene.Points.Where(f => f.Category == Category.Place && f.Name != null && o.IsVisible(f.Category)))
        {
            var p = vp.ToScreen(f.Parts[0][0], f.Parts[0][1]);
            var ft = Text(f.Name!.ToUpperInvariant(), 13 * t, theme.PlaceText, Bold);
            var r = new Rect(p.X - ft.Width / 2, p.Y - ft.Height / 2, ft.Width, ft.Height);
            if (TryReserve(r)) DrawHalo(ctx, ft, r.TopLeft, halo, 3 * t);
        }

        if (zoom > 0.25)
        {
            foreach (var f in scene.Points.Where(f => f.Category == Category.TransitStop && f.Name != null && o.IsVisible(f.Category)))
            {
                var p = vp.ToScreen(f.Parts[0][0], f.Parts[0][1]);
                var ft = Text(f.Name!, 11 * t, theme.StopText, Regular);
                var r = new Rect(p.X + 7 * k * theme.StopScale, p.Y - ft.Height / 2, ft.Width, ft.Height);
                if (TryReserve(r)) DrawHalo(ctx, ft, r.TopLeft, halo, 3 * t);
            }
        }

        if (zoom > 0.35)
        {
            var seenNames = new HashSet<string>();
            foreach (var f in scene.LabelledLines)
            {
                if (!o.IsVisible(f.Category) || theme.PaintOf(f.Category).Stroke == 0) continue;
                // One label per street name per frame keeps dual carriageways readable.
                if (zoom < 1.2 && !seenNames.Add(f.Name!)) continue;
                DrawLineLabel(ctx, vp, f, Text(f.Name!, 10.5 * t, theme.LabelText, Regular), halo, 3 * t, 16 * k, TryReserve);
            }
        }

        if (zoom > 1.2)
        {
            foreach (var f in scene.Points.Where(f => f.Category == Category.Poi && o.IsVisible(f.Category)))
            {
                if (theme.PaintOf(f.Category).Fill == 0) continue;
                var label = f.Name ?? MapPainter.PoiLabel(f);
                if (label is null) continue;
                var p = vp.ToScreen(f.Parts[0][0], f.Parts[0][1]);
                var ft = Text(label, 10 * t, theme.PoiText, Regular);
                var r = new Rect(p.X + 6 * k, p.Y - ft.Height / 2, ft.Width, ft.Height);
                if (TryReserve(r)) DrawHalo(ctx, ft, r.TopLeft, halo, 3 * t);
            }
        }
    }

    private static void DrawLineLabel(DrawingContext ctx, Viewport vp, MapFeature f, FormattedText ft, IBrush halo, double haloWidth, double padding, Func<Rect, bool> tryReserve)
    {
        // Label the longest on-screen segment, rotated to follow it and kept upright.
        double bestLen = 0;
        Point a = default, b = default;
        foreach (var part in f.Parts)
        {
            for (int i = 2; i < part.Length; i += 2)
            {
                var p0 = vp.ToScreen(part[i - 2], part[i - 1]);
                var p1 = vp.ToScreen(part[i], part[i + 1]);
                double len = Math.Sqrt((p1.X - p0.X) * (p1.X - p0.X) + (p1.Y - p0.Y) * (p1.Y - p0.Y));
                if (len > bestLen) { bestLen = len; a = p0; b = p1; }
            }
        }
        if (bestLen < ft.Width + padding) return;

        var mid = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        double angle = Math.Atan2(b.Y - a.Y, b.X - a.X);
        if (angle > Math.PI / 2) angle -= Math.PI;
        if (angle < -Math.PI / 2) angle += Math.PI;

        // Reserve an axis-aligned box around the rotated label.
        double c = Math.Abs(Math.Cos(angle)), s = Math.Abs(Math.Sin(angle));
        double bw = ft.Width * c + ft.Height * s, bh = ft.Width * s + ft.Height * c;
        if (!tryReserve(new Rect(mid.X - bw / 2, mid.Y - bh / 2, bw, bh))) return;

        var m = Matrix.CreateTranslation(-ft.Width / 2, -ft.Height / 2) * Matrix.CreateRotation(angle) * Matrix.CreateTranslation(mid.X, mid.Y);
        using (ctx.PushTransform(m))
            DrawHalo(ctx, ft, new Point(0, 0), halo, haloWidth);
    }

    public static FormattedText Text(string s, double size, uint colour, Typeface? tf = null) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf ?? Regular, size, Brush(colour));

    public static FormattedText Text(string s, double size, uint colour, FontWeight weight, string family = "Inter") =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(family, FontStyle.Normal, weight), size, Brush(colour));

    private static void DrawHalo(DrawingContext ctx, FormattedText ft, Point origin, IBrush halo, double width)
    {
        var geo = ft.BuildGeometry(origin);
        if (geo != null)
            ctx.DrawGeometry(null, new Pen(halo, width, lineJoin: PenLineJoin.Round), geo);
        ctx.DrawText(ft, origin);
    }

    // ---- overlays --------------------------------------------------------

    private static (uint Panel, uint Ink) OverlayColours(MapTheme theme) =>
        theme.IsDark ? (0xC0202328u, 0xFFE6E8ECu) : (0xC0FFFFFFu, 0xFF333333u);

    private static void DrawScaleBar(DrawingContext ctx, Viewport vp, MapTheme theme, MapRenderOptions o)
    {
        double k = o.PixelScale;
        var (panel, ink) = OverlayColours(theme);
        double target = 120 * k / vp.Scale;
        double pow = Math.Pow(10, Math.Floor(Math.Log10(target)));
        double len = new[] { 1, 2, 5, 10 }.Select(m => m * pow).Last(v => v <= target);
        double px = len * vp.Scale;
        var origin = new Point(14 * k, vp.Height - 18 * k);
        var pen = new Pen(Brush(ink), 2 * k);
        ctx.FillRectangle(Brush(panel), new Rect(origin.X - 6 * k, origin.Y - 22 * k, px + 12 * k, 30 * k), (float)(3 * k));
        ctx.DrawLine(pen, origin, new Point(origin.X + px, origin.Y));
        ctx.DrawLine(pen, origin, new Point(origin.X, origin.Y - 6 * k));
        ctx.DrawLine(pen, new Point(origin.X + px, origin.Y), new Point(origin.X + px, origin.Y - 6 * k));
        var ft = Text(MapStats.FormatLength(len), 11 * k, ink, Regular);
        ctx.DrawText(ft, new Point(origin.X + (px - ft.Width) / 2, origin.Y - 20 * k));
    }

    private static void DrawNorthArrow(DrawingContext ctx, Viewport vp, MapTheme theme, MapRenderOptions o)
    {
        double k = o.PixelScale;
        var (panel, ink) = OverlayColours(theme);
        var c = new Point(vp.Width - 26 * k, 30 * k);
        ctx.DrawEllipse(Brush(panel), null, c, 18 * k, 18 * k);
        ctx.DrawGeometry(Brush(ink), null, NorthArrowGeometry(c, k));
        var ft = Text("N", 9 * k, ink, Bold);
        ctx.DrawText(ft, new Point(c.X - ft.Width / 2, c.Y + 7 * k));
    }

    public static StreamGeometry NorthArrowGeometry(Point c, double k)
    {
        var geo = new StreamGeometry();
        using var g = geo.Open();
        g.BeginFigure(new Point(c.X, c.Y - 14 * k), true);
        g.LineTo(new Point(c.X + 7 * k, c.Y + 8 * k));
        g.LineTo(new Point(c.X, c.Y + 3 * k));
        g.LineTo(new Point(c.X - 7 * k, c.Y + 8 * k));
        g.EndFigure(true);
        return geo;
    }

    // ---- helpers ---------------------------------------------------------

    public static IBrush Brush(uint argb) => new SolidColorBrush(Color.FromUInt32(argb));

    private static Pen Pen(StrokeOp s) =>
        new(Brush(s.Colour), s.Width, s.Dash is null ? null : new DashStyle(s.Dash, 0),
            s.RoundCap ? PenLineCap.Round : PenLineCap.Flat, PenLineJoin.Round);
}
