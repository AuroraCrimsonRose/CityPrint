using System.Globalization;
using CityPrint.Core.Osm;

namespace CityPrint.Core.Map;

/// <summary>Renderable features built from an <see cref="OsmDocument"/>, plus a spatial index for picking.</summary>
public sealed class MapModel
{
    private const double CellSize = 256;
    private readonly Dictionary<(int, int), List<int>> _grid = new();

    public OsmDocument Document { get; }
    public List<MapFeature> Features { get; } = new();
    public Dictionary<Category, int> CountByCategory { get; } = new();

    public double MinX { get; private set; }
    public double MinY { get; private set; }
    public double MaxX { get; private set; }
    public double MaxY { get; private set; }

    /// <summary>Distinct layers among line features, ascending; used for draw order.</summary>
    public IReadOnlyList<int> Layers { get; private set; } = [0];

    private MapModel(OsmDocument doc) => Document = doc;

    public static MapModel Build(OsmDocument doc)
    {
        var m = new MapModel(doc);
        m.BuildFeatures();
        m.BuildIndex();
        return m;
    }

    private void BuildFeatures()
    {
        var consumedWays = new HashSet<long>();

        foreach (var rel in Document.Relations.Values)
        {
            if (!rel.Is("type", "multipolygon") && !rel.Is("type", "boundary")) continue;
            var cat = Classifier.ClassifyArea(rel);
            var outer = new List<long[]>();
            var inner = new List<long[]>();
            foreach (var mem in rel.Members)
            {
                if (mem.Type != OsmType.Way || !Document.Ways.TryGetValue(mem.Ref, out var w)) continue;
                (mem.Role == "inner" ? inner : outer).Add(w.NodeRefs);
                // Member ways carrying no tags of their own exist only to shape the polygon.
                if (w.Tags.Count == 0) consumedWays.Add(w.Id);
            }
            if (cat is null) continue;
            var outerRings = JoinRings(outer).Select(Coords).Where(c => c.Length >= 6).ToList();
            var innerRings = JoinRings(inner).Select(Coords).Where(c => c.Length >= 6).ToList();
            if (outerRings.Count == 0) continue;
            Add(rel, cat.Value, GeometryKind.Area, [.. outerRings, .. innerRings], innerRings.Count);
        }

        foreach (var w in Document.Ways.Values)
        {
            if (consumedWays.Contains(w.Id) || w.NodeRefs.Length < 2) continue;
            var coords = Coords(w.NodeRefs);
            if (coords.Length < 4) continue;

            var line = Classifier.ClassifyLine(w);
            if (line is not null)
            {
                Add(w, line.Value, GeometryKind.Line, [coords]);
                continue;
            }
            var area = Classifier.ClassifyArea(w);
            if (area is not null && w.IsClosed)
                Add(w, area.Value, GeometryKind.Area, [coords]);
            else if (w.Tags.Count > 0 || area is not null)
                Add(w, Category.OtherLine, GeometryKind.Line, [coords]);
        }

        foreach (var n in Document.Nodes.Values)
        {
            if (n.Tags.Count == 0) continue;
            Add(n, Classifier.ClassifyNode(n), GeometryKind.Point, [new[] { n.X, n.Y }]);
        }

        Layers = Features.Where(f => f.Kind == GeometryKind.Line).Select(f => f.Layer).Append(0).Distinct().Order().ToList();
    }

    private void Add(OsmElement e, Category cat, GeometryKind kind, IReadOnlyList<double[]> parts, int holes = 0)
    {
        bool bridge = e.Tag("bridge") is { } b && b != "no";
        bool tunnel = e.Tag("tunnel") is { } t && t != "no";
        int layer = int.TryParse(e.Tag("layer"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)
            ? l : bridge ? 1 : tunnel ? -1 : 0;

        var style = MapStyle.Of(cat);
        int lanes = int.TryParse(e.Tag("lanes"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ln) ? ln : 0;
        double width = style.WidthMetres ?? 0;
        if (MapStyle.IsRoad(cat))
            width = (lanes > 0 ? lanes : DefaultLanes(cat)) * (style.WidthMetres ?? 3.5);

        uint? colour = null;
        if (cat == Category.TransitLine)
            colour = ParseColour(e.Tag("osm_export_route_color")) ?? MapStyle.TransitColour(e.Tag("osm_export_route"));

        var f = new MapFeature
        {
            Index = Features.Count,
            Element = e,
            Category = cat,
            Kind = kind,
            Parts = parts,
            HoleCount = holes,
            Layer = layer,
            IsBridge = bridge,
            IsTunnel = tunnel,
            Name = e.Tag("name") ?? e.Tag("osm_export_route_name") ?? e.Tag("ref"),
            Lanes = lanes,
            WidthMetres = width,
            Colour = colour,
        };
        f.ComputeMetrics();
        Features.Add(f);
        CountByCategory[cat] = CountByCategory.GetValueOrDefault(cat) + 1;
    }

    private static int DefaultLanes(Category c) => c switch
    {
        Category.Motorway or Category.Trunk => 4,
        Category.Primary or Category.Secondary => 4,
        Category.Service => 1,
        _ => 2,
    };

    private static uint? ParseColour(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim().TrimStart('#');
        if (s.Length == 6 && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return 0xFF000000 | rgb;
        return null;
    }

    private double[] Coords(long[] refs)
    {
        var list = new List<double>(refs.Length * 2);
        foreach (var r in refs)
        {
            if (!Document.Nodes.TryGetValue(r, out var n)) continue;
            list.Add(n.X);
            list.Add(n.Y);
        }
        return list.ToArray();
    }

    /// <summary>Stitches open member ways that share endpoints into rings.</summary>
    private static List<long[]> JoinRings(List<long[]> ways)
    {
        var rings = new List<long[]>();
        var open = new List<List<long>>();
        foreach (var w in ways)
        {
            if (w.Length < 2) continue;
            if (w[0] == w[^1]) rings.Add(w);
            else open.Add(new List<long>(w));
        }
        while (open.Count > 0)
        {
            var cur = open[^1];
            open.RemoveAt(open.Count - 1);
            bool grew = true;
            while (cur[0] != cur[^1] && grew)
            {
                grew = false;
                for (int i = 0; i < open.Count; i++)
                {
                    var o = open[i];
                    if (o[0] == cur[^1]) cur.AddRange(o.Skip(1));
                    else if (o[^1] == cur[^1]) cur.AddRange(Enumerable.Reverse(o).Skip(1));
                    else if (o[^1] == cur[0]) cur.InsertRange(0, o.Take(o.Count - 1));
                    else if (o[0] == cur[0]) cur.InsertRange(0, Enumerable.Reverse(o).Take(o.Count - 1));
                    else continue;
                    open.RemoveAt(i);
                    grew = true;
                    break;
                }
            }
            rings.Add(cur.ToArray());
        }
        return rings;
    }

    // ---- spatial index ---------------------------------------------------

    private void BuildIndex()
    {
        if (Features.Count == 0)
        {
            MinX = MinY = -Projection.MapSize / 2;
            MaxX = MaxY = Projection.MapSize / 2;
            return;
        }
        MinX = Features.Min(f => f.MinX);
        MinY = Features.Min(f => f.MinY);
        MaxX = Features.Max(f => f.MaxX);
        MaxY = Features.Max(f => f.MaxY);

        foreach (var f in Features)
        {
            int x0 = Cell(f.MinX), x1 = Cell(f.MaxX), y0 = Cell(f.MinY), y1 = Cell(f.MaxY);
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                {
                    if (!_grid.TryGetValue((x, y), out var list)) _grid[(x, y)] = list = new();
                    list.Add(f.Index);
                }
        }
    }

    private static int Cell(double v) => (int)Math.Floor(v / CellSize);

    /// <summary>
    /// Picks the feature under (x,y). Points beat lines beat areas; among areas the smallest wins,
    /// so a building is picked over the land-use zone it sits in.
    /// </summary>
    public MapFeature? HitTest(double x, double y, double tolerance, Func<MapFeature, bool> isVisible)
    {
        var seen = new HashSet<int>();
        MapFeature? best = null;
        double bestScore = double.MaxValue;
        int x0 = Cell(x - tolerance), x1 = Cell(x + tolerance), y0 = Cell(y - tolerance), y1 = Cell(y + tolerance);
        for (int cx = x0; cx <= x1; cx++)
            for (int cy = y0; cy <= y1; cy++)
            {
                if (!_grid.TryGetValue((cx, cy), out var list)) continue;
                foreach (var i in list)
                {
                    if (!seen.Add(i)) continue;
                    var f = Features[i];
                    if (!isVisible(f)) continue;
                    if (x < f.MinX - tolerance || x > f.MaxX + tolerance || y < f.MinY - tolerance || y > f.MaxY + tolerance) continue;

                    double d = f.DistanceTo(x, y);
                    double halfWidth = f.Kind == GeometryKind.Line ? f.WidthMetres / 2 : 0;
                    if (d - halfWidth > tolerance) continue;

                    // Rank by geometry class first, then by closeness / size.
                    double score = f.Kind switch
                    {
                        GeometryKind.Point => d,
                        GeometryKind.Line => 1e6 + Math.Max(0, d - halfWidth) - f.Layer * 0.01,
                        _ => 1e9 + f.AreaSqMetres,
                    };
                    if (score < bestScore) { bestScore = score; best = f; }
                }
            }
        return best;
    }
}
