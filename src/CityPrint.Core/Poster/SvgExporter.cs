using System.Globalization;
using System.Text;
using System.Xml;
using CityPrint.Core.Map;

namespace CityPrint.Core.Poster;

/// <summary>
/// Writes a poster as SVG. Map geometry is emitted in world metres inside a scaled group, so the
/// file stays editable in Inkscape/Illustrator with one group per layer.
/// </summary>
public static class SvgExporter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static void Export(string path, PosterSpec spec, MapScene scene, Func<Category, bool> isVisible, IReadOnlyList<LegendEntry>? legend = null)
    {
        using var stream = File.Create(path);
        Write(stream, spec, scene, isVisible, legend);
    }

    public static void Write(Stream stream, PosterSpec spec, MapScene scene, Func<Category, bool> isVisible, IReadOnlyList<LegendEntry>? legend = null)
    {
        var layout = PosterLayout.Compute(spec);
        var theme = spec.Theme;
        double scale = layout.Scale(spec);
        var map = layout.Map;
        var pc = new PaintContext(theme, scale, 1, spec.RealWidths, isVisible);

        using var w = XmlWriter.Create(stream, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) });
        w.WriteStartElement("svg", "http://www.w3.org/2000/svg");
        w.WriteAttributeString("version", "1.1");
        w.WriteAttributeString("width", F(spec.PageWidth / SpecUnitsPerMm) + "mm");
        w.WriteAttributeString("height", F(spec.PageHeight / SpecUnitsPerMm) + "mm");
        w.WriteAttributeString("viewBox", $"0 0 {F(spec.PageWidth)} {F(spec.PageHeight)}");

        w.WriteStartElement("defs");
        w.WriteStartElement("clipPath");
        w.WriteAttributeString("id", "map-clip");
        Rect(w, map, null);
        w.WriteEndElement();
        if (layout.Fade is { } fade)
        {
            w.WriteStartElement("linearGradient");
            w.WriteAttributeString("id", "fade");
            w.WriteAttributeString("x1", "0"); w.WriteAttributeString("y1", "0");
            w.WriteAttributeString("x2", "0"); w.WriteAttributeString("y2", "1");
            Stop(w, "0", theme.Background, 0);
            Stop(w, "0.55", theme.Background, 0.85);
            Stop(w, "1", theme.Background, 1);
            w.WriteEndElement();
        }
        w.WriteEndElement();

        Rect(w, layout.Page, theme.Background, "page");

        // ---- map ----
        w.WriteStartElement("g");
        w.WriteAttributeString("id", "map");
        w.WriteAttributeString("clip-path", "url(#map-clip)");
        // World (x, -y) → page.
        double tx = map.X + map.W / 2 - spec.CentreX * scale, ty = map.Y + map.H / 2 + spec.CentreY * scale;
        w.WriteStartElement("g");
        w.WriteAttributeString("transform", $"matrix({G(scale)} 0 0 {G(scale)} {G(tx)} {G(ty)})");

        w.WriteStartElement("g");
        w.WriteAttributeString("id", "areas");
        foreach (var b in scene.Areas)
        {
            if (MapPainter.Area(b, pc) is not { } paint) continue;
            w.WriteStartElement("path");
            w.WriteAttributeString("id", "area-" + b.Category.ToString().ToLowerInvariant() + (b.Colour != 0 ? "-" + Colour(b.Colour)[1..] : ""));
            w.WriteAttributeString("fill-rule", "evenodd");
            w.WriteAttributeString("fill", paint.Fill != 0 ? Colour(paint.Fill) : "none");
            if (Opacity(paint.Fill) is { } fo) w.WriteAttributeString("fill-opacity", fo);
            if (paint.Outline is { } o) StrokeAttributes(w, o);
            w.WriteAttributeString("d", PathData(b.Features, closed: true));
            w.WriteEndElement();
        }
        w.WriteEndElement();

        bool surfaces = MapPainter.UseSurfaces(scene, pc);
        foreach (int layer in scene.Layers)
        {
            w.WriteStartElement("g");
            w.WriteAttributeString("id", $"lines-layer{layer}");
            if (layer < 0) w.WriteAttributeString("opacity", "0.45");
            if (surfaces)
            {
                // Road outlines: edges under fills so overlapping pieces merge, as on screen.
                var painted = scene.Surfaces.Where(b => b.Layer == layer)
                    .Select(b => (Bucket: b, Paint: MapPainter.Surface(b, pc), Data: PathData(b.Features, closed: true, oriented: true)))
                    .Where(x => x.Paint != null).ToList();
                foreach (var (b, paint, d) in painted.Where(x => x.Paint!.Value.Outline != null))
                {
                    w.WriteStartElement("path");
                    w.WriteAttributeString("fill", "none");
                    StrokeAttributes(w, paint!.Value.Outline!.Value);
                    w.WriteAttributeString("d", d);
                    w.WriteEndElement();
                }
                foreach (var (b, paint, d) in painted)
                {
                    w.WriteStartElement("path");
                    w.WriteAttributeString("data-category", b.Category + " surface");
                    w.WriteAttributeString("fill", Colour(paint!.Value.Fill));
                    if (Opacity(paint.Value.Fill) is { } so) w.WriteAttributeString("fill-opacity", so);
                    w.WriteAttributeString("d", d);
                    w.WriteEndElement();
                }
            }
            var data = new Dictionary<LineBucket, string>();
            foreach (var (bucket, stroke) in MapPainter.LineLayer(scene.Lines.Where(b => b.Layer == layer), pc, surfaces ? scene.SurfaceCategories : null))
            {
                if (!data.TryGetValue(bucket, out var d)) data[bucket] = d = PathData(bucket.Features, closed: false);
                w.WriteStartElement("path");
                w.WriteAttributeString("fill", "none");
                w.WriteAttributeString("data-category", bucket.Category.ToString());
                StrokeAttributes(w, stroke);
                w.WriteAttributeString("d", d);
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }
        w.WriteEndElement(); // world transform

        // Points and labels are written in page units so text stays upright and readable.
        (double X, double Y) ToPage(double x, double y) => (tx + x * scale, ty - y * scale);

        w.WriteStartElement("g");
        w.WriteAttributeString("id", "stops");
        var stopPaint = theme.PaintOf(Category.TransitStop);
        if (isVisible(Category.TransitStop) && (stopPaint.Fill != 0 || stopPaint.Stroke != 0))
        {
            foreach (var f in scene.Points.Where(f => f.Category == Category.TransitStop))
            {
                var (x, y) = ToPage(f.Parts[0][0], f.Parts[0][1]);
                if (x < map.X || y < map.Y || x > map.Right || y > map.Bottom) continue;
                w.WriteStartElement("circle");
                w.WriteAttributeString("cx", F(x)); w.WriteAttributeString("cy", F(y));
                w.WriteAttributeString("r", F(3.5 * theme.StopScale));
                w.WriteAttributeString("fill", Colour(theme.StopFill));
                w.WriteAttributeString("stroke", Colour(theme.StopStroke));
                w.WriteAttributeString("stroke-width", F(1.2 * Math.Sqrt(theme.StopScale)));
                w.WriteEndElement();
            }
        }
        w.WriteEndElement();

        if (spec.Labels) WriteLabels(w, spec, scene, isVisible, ToPage, map, scale);
        w.WriteEndElement(); // map

        if (layout.Frame)
        {
            Rect(w, map, null, "frame", x =>
            {
                x.WriteAttributeString("fill", "none");
                x.WriteAttributeString("stroke", Colour(theme.PlaceText));
                x.WriteAttributeString("stroke-width", F(Math.Max(0.75, layout.Margin * 0.012)));
            });
        }
        if (layout.Fade is { } fb)
        {
            Rect(w, fb, null, "fade", x => x.WriteAttributeString("fill", "url(#fade)"));
        }

        WriteText(w, spec, layout);
        if (spec.ScaleBar) WriteScaleBar(w, spec, layout, scale);
        if (spec.Legend && legend is { Count: > 0 }) WriteLegend(w, spec, layout, legend);
        w.WriteEndElement();
    }

    private const double SpecUnitsPerMm = PosterSpec.UnitsPerMm;

    private static void WriteLabels(XmlWriter w, PosterSpec spec, MapScene scene, Func<Category, bool> isVisible,
        Func<double, double, (double X, double Y)> toPage, Box map, double scale)
    {
        var theme = spec.Theme;
        double k = spec.LabelScale;
        w.WriteStartElement("g");
        w.WriteAttributeString("id", "labels");
        w.WriteAttributeString("font-family", "Inter, Segoe UI, Helvetica, Arial, sans-serif");
        w.WriteAttributeString("stroke-linejoin", "round");
        w.WriteAttributeString("paint-order", "stroke");
        w.WriteAttributeString("stroke", Colour(theme.LabelHalo));
        if (Opacity(theme.LabelHalo) is { } ho) w.WriteAttributeString("stroke-opacity", ho);

        bool Inside(double x, double y) => x >= map.X && y >= map.Y && x <= map.Right && y <= map.Bottom;

        // Labels are placed in rank order and skipped when they would overlap one already placed.
        // Text widths are estimated, since the SVG viewer picks the final font.
        var occupied = new List<Box>();
        bool TryReserve(Box b)
        {
            foreach (var o in occupied)
                if (b.X < o.Right && o.X < b.Right && b.Y < o.Bottom && o.Y < b.Bottom) return false;
            occupied.Add(new Box(b.X - 1.5 * k, b.Y - 1.5 * k, b.W + 3 * k, b.H + 3 * k));
            return true;
        }
        static double TextWidth(string s, double size, bool bold) => s.Length * size * (bold ? 0.66 : 0.58);

        foreach (var f in scene.PlaceLabels.Where(f => isVisible(f.Category)))
        {
            var (x, y) = toPage(f.Parts[0][0], f.Parts[0][1]);
            if (!Inside(x, y)) continue;
            string name = f.Name!.ToUpperInvariant();
            double size = 13 * k, tw = TextWidth(name, size, true);
            if (!TryReserve(new Box(x - tw / 2, y - size / 2, tw, size))) continue;
            Text(w, name, x, y, size, theme.PlaceText, "700", "middle", 3 * k);
        }

        var stops = new List<(string Name, double X, double Y, double Size)>();
        foreach (var f in scene.StopLabels.Where(f => isVisible(f.Category)))
        {
            var (x, y) = toPage(f.Parts[0][0], f.Parts[0][1]);
            if (!Inside(x, y)) continue;
            double size = 9 * k, lx = x + 6 * theme.StopScale;
            if (TryReserve(new Box(lx, y - size / 2, TextWidth(f.Name!, size, false), size))) stops.Add((f.Name!, lx, y, size));
        }

        // One label per street, on its longest piece, following the line.
        int id = 0;
        w.WriteStartElement("defs");
        var paths = new List<(string Id, string Name, double Size)>();
        var labelled = new HashSet<string>();
        foreach (var f in scene.LabelledLines)
        {
            if (!isVisible(f.Category) || theme.PaintOf(f.Category).Stroke == 0 || !labelled.Add(f.Name!)) continue;
            string name = f.Name!;
            double size = 10.5 * k, tw = TextWidth(name, size, false);
            if (f.LengthMetres * scale < name.Length * size * 0.62 + 12) continue;
            var pts = f.Parts.OrderByDescending(p => p.Length).First();
            var (sx, sy) = toPage(pts[0], pts[1]);
            var (ex, ey) = toPage(pts[^2], pts[^1]);
            if (!Inside((sx + ex) / 2, (sy + ey) / 2)) continue;
            var (mx, my, angle) = Midpoint(pts, toPage);
            double c = Math.Abs(Math.Cos(angle)), s = Math.Abs(Math.Sin(angle));
            double bw = tw * c + size * s, bh = tw * s + size * c;
            if (!TryReserve(new Box(mx - bw / 2, my - bh / 2, bw, bh))) continue;
            bool reverse = ex < sx;
            var sb = new StringBuilder();
            for (int i = 0; i < pts.Length; i += 2)
            {
                int j = reverse ? pts.Length - 2 - i : i;
                var (px, py) = toPage(pts[j], pts[j + 1]);
                sb.Append(i == 0 ? 'M' : 'L').Append(F(px)).Append(',').Append(F(py)).Append(' ');
            }
            string pid = $"lbl{id++}";
            w.WriteStartElement("path");
            w.WriteAttributeString("id", pid);
            w.WriteAttributeString("d", sb.ToString().TrimEnd());
            w.WriteEndElement();
            paths.Add((pid, name, size));
        }
        w.WriteEndElement();
        foreach (var (pid, name, size) in paths)
        {
            w.WriteStartElement("text");
            w.WriteAttributeString("font-size", F(size));
            w.WriteAttributeString("font-weight", "600");
            w.WriteAttributeString("fill", Colour(theme.LabelText));
            w.WriteAttributeString("stroke-width", F(3 * k));
            w.WriteAttributeString("dy", F(size * 0.35));
            w.WriteStartElement("textPath");
            w.WriteAttributeString("href", "#" + pid);
            w.WriteAttributeString("startOffset", "50%");
            w.WriteAttributeString("text-anchor", "middle");
            w.WriteString(name);
            w.WriteEndElement();
            w.WriteEndElement();
        }

        foreach (var (name, x, y, size) in stops)
            Text(w, name, x, y, size, theme.StopText, "600", "start", 2.5 * k);
        w.WriteEndElement();
    }

    /// <summary>The page point halfway along a polyline, and the direction of the segment it falls on.</summary>
    private static (double X, double Y, double Angle) Midpoint(double[] pts, Func<double, double, (double X, double Y)> toPage)
    {
        var page = new List<(double X, double Y)>();
        for (int i = 0; i < pts.Length; i += 2) page.Add(toPage(pts[i], pts[i + 1]));
        double total = 0;
        for (int i = 1; i < page.Count; i++) total += Dist(page[i - 1], page[i]);
        double half = total / 2;
        for (int i = 1; i < page.Count; i++)
        {
            double d = Dist(page[i - 1], page[i]);
            if (d >= half && d > 0)
            {
                double t = half / d;
                var (a, b) = (page[i - 1], page[i]);
                return (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, Math.Atan2(b.Y - a.Y, b.X - a.X));
            }
            half -= d;
        }
        return (page[^1].X, page[^1].Y, 0);

        static double Dist((double X, double Y) a, (double X, double Y) b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
    }

    private static void WriteText(XmlWriter w, PosterSpec spec, PosterLayout l)
    {
        var theme = spec.Theme;
        string anchor = l.TextAlign == 0 ? "start" : "middle";
        w.WriteStartElement("g");
        w.WriteAttributeString("id", "title");
        w.WriteAttributeString("font-family", "Inter, Segoe UI, Helvetica, Arial, sans-serif");
        void Line(string text, double size, double top, uint colour, string weight, double spacing, double opacity)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            w.WriteStartElement("text");
            w.WriteAttributeString("x", F(l.TextX));
            w.WriteAttributeString("y", F(top + size * 0.8));
            w.WriteAttributeString("font-size", F(size));
            w.WriteAttributeString("font-weight", weight);
            w.WriteAttributeString("letter-spacing", F(spacing));
            w.WriteAttributeString("text-anchor", anchor);
            w.WriteAttributeString("fill", Colour(colour));
            if (opacity < 1) w.WriteAttributeString("fill-opacity", F(opacity));
            w.WriteString(text);
            w.WriteEndElement();
        }
        Line(l.Uppercase ? spec.Title.ToUpperInvariant() : spec.Title, l.TitleSize, l.TitleY, theme.PlaceText, "700", l.LetterSpacing, 1);
        Line(spec.Subtitle, l.SubtitleSize, l.SubtitleY, theme.LabelText, "500", l.LetterSpacing * 0.25, 0.85);
        Line(spec.Caption, l.CaptionSize, l.CaptionY, theme.LabelText, "500", l.CaptionSize * 0.18, 0.6);
        w.WriteEndElement();
    }

    private static void WriteScaleBar(XmlWriter w, PosterSpec spec, PosterLayout l, double scale)
    {
        var (len, px) = ScaleBarLength(l.Map.W * 0.18, scale);
        double x = l.Map.Right - l.Margin * 0.4 - px, y = l.Map.Bottom - l.Margin * 0.4;
        uint ink = spec.Theme.PlaceText;
        w.WriteStartElement("g");
        w.WriteAttributeString("id", "scale-bar");
        w.WriteStartElement("path");
        w.WriteAttributeString("d", $"M{F(x)},{F(y - 4)} V{F(y)} H{F(x + px)} V{F(y - 4)}");
        w.WriteAttributeString("fill", "none");
        w.WriteAttributeString("stroke", Colour(ink));
        w.WriteAttributeString("stroke-width", "1.25");
        w.WriteEndElement();
        Text(w, MapStats.FormatLength(len), x + px / 2, y - 8, 8, ink, "600", "middle", 0);
        w.WriteEndElement();
    }

    private static void WriteLegend(XmlWriter w, PosterSpec spec, PosterLayout l, IReadOnlyList<LegendEntry> legend)
    {
        var theme = spec.Theme;
        // Same metrics as the on-screen renderer; text width is estimated since SVG has no layout step.
        double size = Math.Max(7, l.Margin * 0.16), row = size * 1.7, pad = size * 0.9, chip = size * 1.6;
        double textW = legend.Max(e => e.Label.Length) * size * 0.58;
        double width = pad * 2 + chip + size * 0.7 + textW;
        double header = size * 0.8 * 1.3;
        double height = pad * 2 + header + size * 0.4 + row * legend.Count;
        double x = l.LegendBox.X, y = l.LegendBox.Y;

        w.WriteStartElement("g");
        w.WriteAttributeString("id", "legend");
        w.WriteAttributeString("font-family", "Inter, Segoe UI, Helvetica, Arial, sans-serif");
        Rect(w, new Box(x, y, width, height), theme.Background, null, r =>
        {
            r.WriteAttributeString("fill-opacity", "0.88");
            r.WriteAttributeString("rx", F(size * 0.4));
            r.WriteAttributeString("stroke", Colour(theme.Frame));
            r.WriteAttributeString("stroke-width", "0.75");
        });
        Text(w, "LINES", x + pad, y + pad + size * 0.4, size * 0.8, theme.LabelText, "700", "start", 0);
        double top = y + pad + header + size * 0.4;
        for (int i = 0; i < legend.Count; i++)
        {
            double cy = top + row * i + row / 2;
            w.WriteStartElement("path");
            w.WriteAttributeString("d", $"M{F(x + pad)},{F(cy)} H{F(x + pad + chip)}");
            w.WriteAttributeString("stroke", Colour(legend[i].Colour));
            w.WriteAttributeString("stroke-width", F(size * 0.5));
            w.WriteAttributeString("stroke-linecap", "round");
            w.WriteEndElement();
            Text(w, legend[i].Label, x + pad + chip + size * 0.7, cy, size, theme.LabelText, "500", "start", 0);
        }
        w.WriteEndElement();
    }

    /// <summary>A round length (1/2/5 × 10ⁿ m) no longer than maxPage page units.</summary>
    public static (double Metres, double PageUnits) ScaleBarLength(double maxPage, double scale)
    {
        double target = maxPage / scale;
        double pow = Math.Pow(10, Math.Floor(Math.Log10(target)));
        double len = new[] { 1, 2, 5, 10 }.Select(m => m * pow).Last(v => v <= target);
        return (len, len * scale);
    }

    // ---- helpers ---------------------------------------------------------

    private static void Text(XmlWriter w, string text, double x, double y, double size, uint colour, string weight, string anchor, double halo)
    {
        w.WriteStartElement("text");
        w.WriteAttributeString("x", F(x));
        w.WriteAttributeString("y", F(y + size * 0.35));
        w.WriteAttributeString("font-size", F(size));
        w.WriteAttributeString("font-weight", weight);
        w.WriteAttributeString("text-anchor", anchor);
        w.WriteAttributeString("fill", Colour(colour));
        w.WriteAttributeString("stroke-width", F(halo));
        w.WriteString(text);
        w.WriteEndElement();
    }

    private static void StrokeAttributes(XmlWriter w, StrokeOp s)
    {
        w.WriteAttributeString("stroke", Colour(s.Colour));
        if (Opacity(s.Colour) is { } o) w.WriteAttributeString("stroke-opacity", o);
        w.WriteAttributeString("stroke-width", F(s.Width));
        w.WriteAttributeString("stroke-linecap", s.RoundCap ? "round" : "butt");
        w.WriteAttributeString("stroke-linejoin", "round");
        if (s.Dash != null)
            w.WriteAttributeString("stroke-dasharray", string.Join(" ", s.Dash.Select(d => F(d * s.Width))));
    }

    /// <param name="oriented">Turn every outline the same way (holes the other way) for non-zero filling.</param>
    private static string PathData(IEnumerable<MapFeature> features, bool closed, bool oriented = false)
    {
        var sb = new StringBuilder();
        foreach (var f in features)
        {
            for (int r = 0; r < f.Parts.Count; r++)
            {
                var p = f.Parts[r];
                if (p.Length < 4) continue;
                bool reverse = oriented && (MapFeature.SignedArea(p) < 0) != (r >= f.Parts.Count - f.HoleCount);
                int n = p.Length / 2;
                for (int k = 0; k < n; k++)
                {
                    int i = (reverse ? n - 1 - k : k) * 2;
                    sb.Append(k == 0 ? "M" : " ").Append(F1(p[i])).Append(',').Append(F1(-p[i + 1]));
                }
                if (closed) sb.Append('Z');
                sb.Append(' ');
            }
        }
        return sb.ToString().TrimEnd();
    }

    private static void Rect(XmlWriter w, Box b, uint? fill, string? id = null, Action<XmlWriter>? extra = null)
    {
        w.WriteStartElement("rect");
        if (id != null) w.WriteAttributeString("id", id);
        w.WriteAttributeString("x", F(b.X)); w.WriteAttributeString("y", F(b.Y));
        w.WriteAttributeString("width", F(b.W)); w.WriteAttributeString("height", F(b.H));
        if (fill is { } c) w.WriteAttributeString("fill", Colour(c));
        extra?.Invoke(w);
        w.WriteEndElement();
    }

    private static void Stop(XmlWriter w, string offset, uint colour, double opacity)
    {
        w.WriteStartElement("stop");
        w.WriteAttributeString("offset", offset);
        w.WriteAttributeString("stop-color", Colour(colour));
        w.WriteAttributeString("stop-opacity", F(opacity));
        w.WriteEndElement();
    }

    private static string Colour(uint argb) => $"#{argb & 0xFFFFFF:X6}";

    private static string? Opacity(uint argb)
    {
        byte a = (byte)(argb >> 24);
        return a == 255 ? null : F(a / 255.0);
    }

    private static string F(double v) => Math.Round(v, 3).ToString(Inv);
    private static string G(double v) => v.ToString("G9", Inv);
    private static string F1(double v) => Math.Round(v, 1).ToString(Inv);
}
