using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CityPrint.Core.Map;
using CityPrint.Core.Poster;

namespace CityPrint.App.Rendering;

/// <summary>Draws a poster in page units (1/96 in); the preview scales it down, PNG export scales it by DPI.</summary>
public static class PosterRenderer
{
    public static void Render(DrawingContext ctx, PosterSpec spec, MapScene? scene, Func<Category, bool> isVisible, IReadOnlyList<LegendEntry> legend)
    {
        var l = PosterLayout.Compute(spec);
        var theme = spec.Theme;
        var map = ToRect(l.Map);
        ctx.FillRectangle(MapRenderer.Brush(theme.Background), ToRect(l.Page));

        using (ctx.PushClip(map))
        using (ctx.PushTransform(Matrix.CreateTranslation(map.X, map.Y)))
        {
            var vp = new Viewport(spec.CentreX, spec.CentreY, l.Scale(spec), map.Width, map.Height);
            MapRenderer.Render(ctx, scene, vp, theme, new MapRenderOptions
            {
                RealWidths = spec.RealWidths,
                Labels = spec.Labels,
                LabelScale = spec.LabelScale,
                // Print is read up close: one page unit is ~0.26 mm, so show street-level detail sooner.
                DetailBoost = 3,
                ScaleBar = false,
                NorthArrow = false,
                IsVisible = isVisible,
            });
        }

        if (l.Fade is { } fade)
        {
            var bg = Color.FromUInt32(theme.Background);
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, bg.R, bg.G, bg.B), 0),
                    new GradientStop(Color.FromArgb(217, bg.R, bg.G, bg.B), 0.55),
                    new GradientStop(bg, 1),
                },
            };
            ctx.FillRectangle(brush, ToRect(fade));
        }

        if (l.Frame)
            ctx.DrawRectangle(new Pen(MapRenderer.Brush(theme.PlaceText), Math.Max(0.75, l.Margin * 0.012)), map);

        DrawText(ctx, spec, l);
        if (spec.ScaleBar) DrawScaleBar(ctx, spec, l);
        if (spec.Legend && legend.Count > 0) DrawLegend(ctx, spec, l, legend);
    }

    private static void DrawText(DrawingContext ctx, PosterSpec spec, PosterLayout l)
    {
        var theme = spec.Theme;
        void Line(string text, double size, double top, uint colour, FontWeight weight, double spacing, double opacity)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            // Letter spacing: FormattedText has no tracking, so lay out character by character.
            double width = Tracked(text, size, colour, weight, spacing, out var glyphs);
            double x = l.TextX - width * l.TextAlign;
            using var _ = opacity < 1 ? ctx.PushOpacity(opacity) : default(DrawingContext.PushedState?);
            foreach (var (g, dx) in glyphs) ctx.DrawText(g, new Point(x + dx, top));
        }
        Line(l.Uppercase ? spec.Title.ToUpperInvariant() : spec.Title, l.TitleSize, l.TitleY, theme.PlaceText, FontWeight.Bold, l.LetterSpacing, 1);
        Line(spec.Subtitle, l.SubtitleSize, l.SubtitleY, theme.LabelText, FontWeight.Medium, l.LetterSpacing * 0.25, 0.85);
        Line(spec.Caption, l.CaptionSize, l.CaptionY, theme.LabelText, FontWeight.Medium, l.CaptionSize * 0.18, 0.6);
    }

    private static double Tracked(string text, double size, uint colour, FontWeight weight, double spacing, out List<(FormattedText, double)> glyphs)
    {
        glyphs = new List<(FormattedText, double)>();
        if (spacing < 0.01)
        {
            var whole = MapRenderer.Text(text, size, colour, weight);
            glyphs.Add((whole, 0));
            return whole.WidthIncludingTrailingWhitespace;
        }
        double x = 0;
        foreach (var ch in text.EnumerateRunes())
        {
            var ft = MapRenderer.Text(ch.ToString(), size, colour, weight);
            glyphs.Add((ft, x));
            x += ft.WidthIncludingTrailingWhitespace + spacing;
        }
        return Math.Max(0, x - spacing);
    }

    private static void DrawScaleBar(DrawingContext ctx, PosterSpec spec, PosterLayout l)
    {
        double scale = l.Scale(spec);
        var (len, px) = SvgExporter.ScaleBarLength(l.Map.W * 0.18, scale);
        double x = l.Map.Right - l.Margin * 0.4 - px, y = l.Map.Bottom - l.Margin * 0.4;
        var ink = MapRenderer.Brush(spec.Theme.PlaceText);
        var halo = new Pen(MapRenderer.Brush(spec.Theme.LabelHalo), 4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var pen = new Pen(ink, 1.25);
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(x, y - 4), false);
            g.LineTo(new Point(x, y));
            g.LineTo(new Point(x + px, y));
            g.LineTo(new Point(x + px, y - 4));
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, halo, geo);
        ctx.DrawGeometry(null, pen, geo);
        var ft = MapRenderer.Text(MapStats.FormatLength(len), 8, spec.Theme.PlaceText, FontWeight.SemiBold);
        var origin = new Point(x + (px - ft.Width) / 2, y - 6 - ft.Height);
        if (ft.BuildGeometry(origin) is { } tg) ctx.DrawGeometry(null, new Pen(MapRenderer.Brush(spec.Theme.LabelHalo), 2.5, lineJoin: PenLineJoin.Round), tg);
        ctx.DrawText(ft, origin);
    }

    private static void DrawLegend(DrawingContext ctx, PosterSpec spec, PosterLayout l, IReadOnlyList<LegendEntry> legend)
    {
        var theme = spec.Theme;
        double size = Math.Max(7, l.Margin * 0.16), row = size * 1.7, pad = size * 0.9, chip = size * 1.6;
        var texts = legend.Select(e => MapRenderer.Text(e.Label, size, theme.LabelText, FontWeight.Medium)).ToList();
        double w = pad * 2 + chip + size * 0.7 + texts.Max(t => t.Width);
        var header = MapRenderer.Text("LINES", size * 0.8, theme.LabelText, FontWeight.Bold);
        double h = pad * 2 + header.Height + size * 0.4 + row * legend.Count;
        var box = new Rect(l.LegendBox.X, l.LegendBox.Y, w, h);

        var bg = Color.FromUInt32(theme.Background);
        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(225, bg.R, bg.G, bg.B)), new Pen(MapRenderer.Brush(theme.Frame), 0.75), box, size * 0.4, size * 0.4);
        ctx.DrawText(header, new Point(box.X + pad, box.Y + pad));
        double y = box.Y + pad + header.Height + size * 0.4;
        for (int i = 0; i < legend.Count; i++)
        {
            double cy = y + row * i + row / 2;
            ctx.DrawLine(new Pen(MapRenderer.Brush(legend[i].Colour), size * 0.5, lineCap: PenLineCap.Round),
                new Point(box.X + pad, cy), new Point(box.X + pad + chip, cy));
            ctx.DrawText(texts[i], new Point(box.X + pad + chip + size * 0.7, cy - texts[i].Height / 2));
        }
    }

    /// <summary>Renders the poster to a PNG at the spec's DPI.</summary>
    public static void SavePng(string path, PosterSpec spec, MapScene? scene, Func<Category, bool> isVisible, IReadOnlyList<LegendEntry> legend)
    {
        var (w, h) = spec.PixelSize;
        using var bmp = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        using (var ctx = bmp.CreateDrawingContext())
        using (ctx.PushTransform(Matrix.CreateScale(spec.Dpi / 96.0, spec.Dpi / 96.0)))
            Render(ctx, spec, scene, isVisible, legend);
        bmp.Save(path, PngBitmapEncoderOptions.Default);
    }

    private static Rect ToRect(Box b) => new(b.X, b.Y, b.W, b.H);
}
