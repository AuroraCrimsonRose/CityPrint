using CityPrint.Core.Osm;

namespace CityPrint.Core.Map;

/// <summary>Areas of one category, drawn as one merged path.</summary>
public sealed class AreaBucket(Category category, IReadOnlyList<MapFeature> features, uint colour = 0)
{
    public Category Category { get; } = category;

    /// <summary>The features' own colour (zoning cells), or 0 to use the theme's.</summary>
    public uint Colour { get; } = colour;
    public IReadOnlyList<MapFeature> Features { get; } = features;

    /// <summary>Renderer-specific cached geometry (e.g. an Avalonia StreamGeometry).</summary>
    public object? Cache { get; set; }
}

/// <summary>Lines sharing everything that affects how they are stroked.</summary>
public sealed class LineBucket(Category category, int layer, bool tunnel, bool bridge, double width, uint colour, IReadOnlyList<MapFeature> features)
{
    public Category Category { get; } = category;
    public int Layer { get; } = layer;
    public bool Tunnel { get; } = tunnel;
    public bool Bridge { get; } = bridge;
    public double WidthMetres { get; } = width;
    public uint Colour { get; } = colour;
    public IReadOnlyList<MapFeature> Features { get; } = features;
    public object? Cache { get; set; }
}

/// <summary>Road outlines of one category on one layer, filled as one merged path.</summary>
public sealed class SurfaceBucket(Category category, int layer, bool tunnel, bool bridge, IReadOnlyList<MapFeature> features)
{
    public Category Category { get; } = category;
    public int Layer { get; } = layer;
    public bool Tunnel { get; } = tunnel;
    public bool Bridge { get; } = bridge;
    public IReadOnlyList<MapFeature> Features { get; } = features;
    public object? Cache { get; set; }
}

/// <summary>A model's features sorted into draw order, shared by every renderer.</summary>
public sealed class MapScene
{
    public required MapModel Model { get; init; }
    public required IReadOnlyList<AreaBucket> Areas { get; init; }
    public required IReadOnlyList<LineBucket> Lines { get; init; }

    /// <summary>Road outlines, drawn layer by layer under that layer's lines when real widths are on.</summary>
    public required IReadOnlyList<SurfaceBucket> Surfaces { get; init; }

    /// <summary>Road categories that have outlines, so their centrelines can step aside when zoomed in.</summary>
    public required IReadOnlySet<Category> SurfaceCategories { get; init; }
    public required IReadOnlyList<int> Layers { get; init; }
    public required IReadOnlyList<MapFeature> Points { get; init; }

    /// <summary>Named roads and rail lines, most important first (see <see cref="LabelRank"/>).</summary>
    public required IReadOnlyList<MapFeature> LabelledLines { get; init; }

    /// <summary>Labelled places, stops and points of interest, each most important first.</summary>
    public required IReadOnlyList<MapFeature> PlaceLabels { get; init; }
    public required IReadOnlyList<MapFeature> StopLabels { get; init; }
    public required IReadOnlyList<MapFeature> PoiLabels { get; init; }

    public static MapScene Build(MapModel model)
    {
        var areas = model.Features.Where(f => f.Kind == GeometryKind.Area)
            .GroupBy(f => (f.Category, Colour: f.Colour ?? 0u))
            .OrderBy(g => MapStyle.Of(g.Key.Category).Order)
            .Select(g => new AreaBucket(g.Key.Category, g.ToList(), g.Key.Colour))
            .ToList();

        var lines = model.Features.Where(f => f.Kind == GeometryKind.Line)
            .GroupBy(f => (f.Category, f.Layer, f.IsTunnel, f.IsBridge, Width: Math.Round(f.WidthMetres, 1), Colour: f.Colour ?? 0u))
            .OrderBy(g => g.Key.Layer).ThenBy(g => MapStyle.Of(g.Key.Category).Order)
            .Select(g => new LineBucket(g.Key.Category, g.Key.Layer, g.Key.IsTunnel, g.Key.IsBridge, g.Key.Width, g.Key.Colour, g.ToList()))
            .ToList();

        var surfaces = model.Surfaces
            .GroupBy(f => (f.Category, f.Layer, f.IsTunnel, f.IsBridge))
            .OrderBy(g => g.Key.Layer).ThenBy(g => MapStyle.Of(g.Key.Category).Order)
            .Select(g => new SurfaceBucket(g.Key.Category, g.Key.Layer, g.Key.IsTunnel, g.Key.IsBridge, g.ToList()))
            .ToList();

        // How many transport lines call at each stop node.
        var linesAtStop = new Dictionary<long, int>();
        foreach (var rel in model.Document.Relations.Values)
        {
            if (!rel.Is("type", "route")) continue;
            foreach (var id in rel.Members.Where(m => m.Type == OsmType.Node).Select(m => m.Ref).Distinct())
                linesAtStop[id] = linesAtStop.GetValueOrDefault(id) + 1;
        }
        var points = model.Features.Where(f => f.Kind == GeometryKind.Point).ToList();

        return new MapScene
        {
            Model = model,
            Areas = areas,
            Lines = lines,
            Surfaces = surfaces,
            SurfaceCategories = surfaces.Select(b => b.Category).ToHashSet(),
            Layers = lines.Select(b => b.Layer).Concat(surfaces.Select(b => b.Layer)).Distinct().Order().ToList(),
            Points = points.OrderBy(f => f.Style.Order).ToList(),
            LabelledLines = LabelRank.Lines(model.Features
                .Where(f => f.Kind == GeometryKind.Line && f.Name != null &&
                            (MapStyle.IsRoad(f.Category) || f.Category is Category.Rail or Category.Tram or Category.TransitLine))),
            PlaceLabels = points.Where(f => f.Category == Category.Place && f.Name != null)
                .OrderByDescending(LabelRank.Place).ToList(),
            StopLabels = points.Where(f => f.Category == Category.TransitStop && f.Name != null)
                .OrderByDescending(f => LabelRank.Stop(f, linesAtStop.GetValueOrDefault(f.Element.Id))).ToList(),
            PoiLabels = points.Where(f => f.Category == Category.Poi && (f.Name != null || MapPainter.PoiLabel(f) != null))
                .OrderByDescending(LabelRank.Poi).ToList(),
        };
    }
}

/// <summary>What to stroke a path with. Widths are in metres; dashes are multiples of the width.</summary>
public readonly record struct StrokeOp(uint Colour, double Width, double[]? Dash, bool RoundCap);

/// <summary>Everything about the output that affects paint decisions.</summary>
/// <param name="Scale">Output pixels per metre.</param>
/// <param name="PixelScale">Output pixels per screen pixel (1 on screen, DPI/96 for print).</param>
public sealed record PaintContext(MapTheme Theme, double Scale, double PixelScale, bool RealWidths, Func<Category, bool> IsVisible)
{
    /// <summary>One screen pixel, in metres.</summary>
    public double Px => PixelScale / Scale;

    /// <summary>Zoom independent of output resolution, used for detail thresholds.</summary>
    public double Zoom => Scale / PixelScale;
}

/// <summary>
/// Turns a theme into stroke and fill decisions. Renderers (Avalonia, SVG) only map these onto
/// their own drawing primitives, so every output looks the same.
/// </summary>
public static class MapPainter
{
    public static bool IsDashed(Category c, Paint p) => p.Dashed ?? MapStyle.Of(c).Dash != null;

    /// <summary>Stroke width in metres: real width when it is wide enough to see, otherwise a pixel minimum.</summary>
    public static double StrokeMetres(double widthMetres, double minPixels, PaintContext pc) =>
        pc.RealWidths
            ? Math.Max(widthMetres, minPixels * pc.Px)
            : Math.Max(minPixels, Math.Min(widthMetres * 0.25, 6)) * pc.Px;

    /// <summary>Fill and optional outline for an area bucket; null when nothing should be drawn.</summary>
    public static (uint Fill, StrokeOp? Outline)? Area(AreaBucket b, PaintContext pc)
    {
        if (!pc.IsVisible(b.Category)) return null;
        var p = pc.Theme.PaintOf(b.Category);
        // Zoning keeps its game colour, the theme's fill only setting how strongly it shows; buildings mix
        // their type's colour into the theme's so they still sit right on light and dark maps.
        uint fill = b.Colour == 0 || p.Fill == 0 ? p.Fill
            : b.Category == Category.Zoning ? (p.Fill & 0xFF000000) | (b.Colour & 0x00FFFFFF)
            : pc.Theme.TypeColours ? TypeColours.Blend(p.Fill, b.Colour, 0.65) : p.Fill;
        // Outline-only paints always draw their stroke; filled ones only when zoomed in.
        bool outline = p.Stroke != 0 && (p.Fill == 0 || pc.Theme.AreaOutlines && pc.Zoom > 0.25);
        if (fill == 0 && !outline) return null;
        var style = MapStyle.Of(b.Category);
        return (fill, outline ? new StrokeOp(p.Stroke, style.MinPixels * pc.Px, style.Dash, true) : null);
    }

    /// <summary>Zoom from which road outlines replace centrelines: a two-lane road is then a few pixels wide.</summary>
    public const double SurfaceZoom = 0.45;

    /// <summary>Whether road outlines are drawn (and the matching centrelines left out).</summary>
    public static bool UseSurfaces(MapScene scene, PaintContext pc) =>
        pc.RealWidths && pc.Zoom >= SurfaceZoom && scene.Surfaces.Count > 0;

    /// <summary>Fill and edge for a road outline; null when its category is hidden or unpainted.</summary>
    public static (uint Fill, StrokeOp? Outline)? Surface(SurfaceBucket b, PaintContext pc)
    {
        if (!pc.IsVisible(b.Category)) return null;
        var p = pc.Theme.PaintOf(b.Category);
        if (p.Stroke == 0) return null;
        uint edge = b.Bridge ? (pc.Theme.IsDark ? 0xFF6A707B : 0xFF404040) : p.Casing;
        // The edge is stroked under the fill, so only its outer half shows.
        return (p.Stroke, edge != 0 ? new StrokeOp(edge, 2 * Math.Max(pc.Px, 0.5), b.Tunnel ? [2, 1.5] : null, true) : null);
    }

    /// <summary>Strokes for one layer of lines, casings first so crossing roads merge cleanly.</summary>
    /// <param name="skip">Categories drawn another way (road outlines), left out here.</param>
    public static IEnumerable<(LineBucket Bucket, StrokeOp Stroke)> LineLayer(IEnumerable<LineBucket> layer, PaintContext pc, IReadOnlySet<Category>? skip = null)
    {
        var theme = pc.Theme;
        var buckets = layer.Where(b => pc.IsVisible(b.Category) && skip?.Contains(b.Category) != true).ToList();
        double px = pc.Px;

        foreach (var b in buckets)
        {
            if (b.Category == Category.TransitLine) continue;
            var p = theme.PaintOf(b.Category);
            if (p.Casing == 0 || p.Stroke == 0) continue;
            double w = Width(b, p, pc);
            double casing = w + Math.Max(1.5 * px, w * 0.15) + (b.Bridge ? 2 * px : 0);
            uint colour = b.Bridge ? (theme.IsDark ? 0xFF6A707B : 0xFF404040) : p.Casing;
            yield return (b, new StrokeOp(colour, casing, b.Tunnel ? [2, 1.5] : null, true));
        }

        foreach (var b in buckets)
        {
            var p = theme.PaintOf(b.Category);
            double w = Width(b, p, pc);
            var style = MapStyle.Of(b.Category);
            bool dashed = IsDashed(b.Category, p);
            var dash = dashed ? style.Dash ?? [3, 2] : null;

            if (b.Category == Category.TransitLine)
            {
                uint colour = theme.ModeColours && b.Colour != 0 ? b.Colour : p.Stroke;
                if (colour == 0) continue;
                uint border = p.Casing != 0 ? p.Casing : theme.ModeColours ? 0xFF000000 : 0;
                if (border != 0) yield return (b, new StrokeOp(border, w + 2 * px, null, true));
                yield return (b, new StrokeOp(colour, w, null, true));
                continue;
            }
            if (p.Stroke == 0) continue;
            // Railway look: dashed body inside a solid casing.
            if (b.Category == Category.Rail && dashed && p.Casing != 0)
                yield return (b, new StrokeOp(p.Stroke, w * 0.55, dash, false));
            else
                yield return (b, new StrokeOp(p.Stroke, w, dash, dash is null));
        }
    }

    private static double Width(LineBucket b, Paint p, PaintContext pc) =>
        StrokeMetres(b.WidthMetres, MapStyle.Of(b.Category).MinPixels, pc) * p.WidthScale;

    /// <summary>Labels shown for points of interest without a name.</summary>
    public static string? PoiLabel(MapFeature f)
    {
        foreach (var k in new[] { "amenity", "shop", "office", "tourism", "leisure" })
            if (f.Element.Tag(k) is { } v) return v == "yes" ? k : v.Replace('_', ' ');
        return null;
    }
}
