using CityPrint.Core.Osm;

namespace CityPrint.Core.Map;

public sealed class MapFeature
{
    public required int Index { get; init; }
    public required OsmElement Element { get; init; }
    public required Category Category { get; init; }
    public required GeometryKind Kind { get; init; }

    /// <summary>Polylines (lines), rings (areas, even-odd) or a single coordinate (points); x,y interleaved in metres.</summary>
    public required IReadOnlyList<double[]> Parts { get; init; }

    /// <summary>For areas: the last N parts are inner rings (holes).</summary>
    public int HoleCount { get; init; }

    public double MinX { get; private set; }
    public double MinY { get; private set; }
    public double MaxX { get; private set; }
    public double MaxY { get; private set; }

    /// <summary>OSM layer: bridges default to 1, tunnels to -1.</summary>
    public int Layer { get; init; }
    public bool IsBridge { get; init; }
    public bool IsTunnel { get; init; }
    public string? Name { get; init; }
    public int Lanes { get; init; }
    public double WidthMetres { get; init; }
    public uint? Colour { get; init; }

    public double LengthMetres { get; private set; }
    public double AreaSqMetres { get; private set; }

    public CategoryStyle Style => MapStyle.Of(Category);

    public string DisplayName => Name ?? $"{Style.Name} {Element.Id}";

    public double CentreX => (MinX + MaxX) / 2;
    public double CentreY => (MinY + MaxY) / 2;

    internal void ComputeMetrics()
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        double length = 0, area = 0;
        foreach (var p in Parts)
        {
            for (int i = 0; i < p.Length; i += 2)
            {
                double x = p[i], y = p[i + 1];
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
                if (i >= 2)
                {
                    double dx = x - p[i - 2], dy = y - p[i - 1];
                    length += Math.Sqrt(dx * dx + dy * dy);
                }
            }
        }
        MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
        LengthMetres = Kind == GeometryKind.Line ? length : 0;
        if (Kind == GeometryKind.Area)
        {
            for (int r = 0; r < Parts.Count; r++)
            {
                double a = Math.Abs(RingArea(Parts[r]));
                area += HoleCount > 0 && r >= Parts.Count - HoleCount ? -a : a;
            }
            AreaSqMetres = Math.Max(0, area);
        }
    }

    private static double RingArea(double[] p)
    {
        double s = 0;
        for (int i = 2; i < p.Length; i += 2) s += p[i - 2] * p[i + 1] - p[i] * p[i - 1];
        return s / 2;
    }

    /// <summary>Distance from (x,y) to this feature in metres; 0 when inside an area.</summary>
    public double DistanceTo(double x, double y)
    {
        if (Kind == GeometryKind.Point)
        {
            var p = Parts[0];
            return Math.Sqrt((p[0] - x) * (p[0] - x) + (p[1] - y) * (p[1] - y));
        }
        if (Kind == GeometryKind.Area && Contains(x, y)) return 0;

        double best = double.MaxValue;
        foreach (var p in Parts)
            for (int i = 2; i < p.Length; i += 2)
                best = Math.Min(best, SegmentDistance(x, y, p[i - 2], p[i - 1], p[i], p[i + 1]));
        return best;
    }

    public bool Contains(double x, double y)
    {
        if (x < MinX || x > MaxX || y < MinY || y > MaxY) return false;
        bool inside = false;
        foreach (var p in Parts)
        {
            for (int i = 0, j = p.Length - 2; i < p.Length; j = i, i += 2)
            {
                double xi = p[i], yi = p[i + 1], xj = p[j], yj = p[j + 1];
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
            }
        }
        return inside;
    }

    private static double SegmentDistance(double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax, dy = by - ay;
        double len2 = dx * dx + dy * dy;
        double t = len2 == 0 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / len2, 0, 1);
        double cx = ax + t * dx - px, cy = ay + t * dy - py;
        return Math.Sqrt(cx * cx + cy * cy);
    }
}
