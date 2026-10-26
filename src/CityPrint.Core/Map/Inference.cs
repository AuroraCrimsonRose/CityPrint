using CityPrint.Core.Carto;
using CityPrint.Core.Osm;

namespace CityPrint.Core.Map;

/// <summary>
/// Guesses features the exports don't carry: parks (empty blocks enclosed by streets) and shopping
/// streets (streets whose frontage is mostly commercial). Works on a coarse raster of the map.
/// </summary>
internal static class Inference
{
    private const double Cell = 8;

    // Cell codes: what occupies the ground.
    private const byte Free = 0, MajorRoad = 1, LocalRoad = 2, Taken = 3;

    private const double MinParkArea = 3_000, MaxParkArea = 250_000;

    /// <summary>Share of a block's edge that must be street (rather than the backs of lots) for it to count as a park.</summary>
    private const double MinStreetEdge = 0.55;

    /// <summary>Share that must be local streets, so the leftover land inside a motorway interchange is not a park.</summary>
    private const double MinLocalEdge = 0.35;

    /// <summary>Share of a street's length that must have commercial frontage.</summary>
    private const double MinShopFrontage = 0.5;

    public sealed record Result(List<(OsmWay Element, double[][] Rings, int Holes)> Parks, List<(OsmWay Element, double[] Line)> ShoppingStreets);

    public static Result Run(IReadOnlyList<MapFeature> features)
    {
        var parks = new List<(OsmWay, double[][], int)>();
        var shops = new List<(OsmWay, double[])>();
        var roads = features.Where(f => f.Kind == GeometryKind.Line && IsBarrier(f.Category) && !f.IsTunnel && !f.IsBridge).ToList();
        if (roads.Count == 0) return new(parks, shops);

        double minX = roads.Min(f => f.MinX) - 64, minY = roads.Min(f => f.MinY) - 64;
        int w = (int)Math.Ceiling((roads.Max(f => f.MaxX) + 64 - minX) / Cell);
        int h = (int)Math.Ceiling((roads.Max(f => f.MaxY) + 64 - minY) / Cell);
        var grid = new Raster(w, h, minX, minY);

        foreach (var f in features.Where(f => f.Kind == GeometryKind.Area && Occupies(f.Category)))
        {
            grid.FillPolygon(f, grid.Ground, Taken);
            if (IsCommercial(f)) grid.FillPolygon(f, grid.Commercial, 1);
        }
        foreach (var f in roads)
            grid.StampLine(f.Parts, Math.Max(f.WidthMetres / 2, 6), IsLocal(f.Category) ? LocalRoad : MajorRoad);

        long id = -1;
        foreach (var rings in FindParks(grid))
        {
            var tags = new Dictionary<string, string> { ["leisure"] = "park", ["cityprint:inferred"] = "park" };
            int holes = rings.Count - 1;
            parks.Add((new OsmWay { Id = id--, Tags = tags }, rings.ToArray(), holes));
        }

        foreach (var f in features.Where(f => f.Kind == GeometryKind.Line && IsShopStreetCandidate(f)))
        {
            if (!HasShopFrontage(grid, f)) continue;
            var tags = new Dictionary<string, string> { ["cityprint:inferred"] = "shopping street" };
            if (f.Name != null) tags["name"] = f.Name;
            if (f.Layer != 0) tags["layer"] = f.Layer.ToString(System.Globalization.CultureInfo.InvariantCulture);
            shops.Add((new OsmWay { Id = id--, Tags = tags }, f.Parts[0]));
        }
        return new(parks, shops);
    }

    private static bool IsBarrier(Category c) => MapStyle.IsRoad(c) || c is Category.Rail or Category.Tram or Category.Subway;

    private static bool IsLocal(Category c) => c is Category.Service or Category.Street or Category.Tertiary or Category.Secondary or Category.Primary;

    private static bool Occupies(Category c) => c is Category.Water or Category.Farmland or Category.Forest or Category.Park
        or Category.Residential or Category.Commercial or Category.Retail or Category.Industrial or Category.Office
        or Category.OtherLanduse or Category.Zoning or Category.Pedestrian or Category.Building;

    private static bool IsCommercial(MapFeature f) => f.Category switch
    {
        Category.Commercial or Category.Retail => true,
        Category.Zoning => f.Element.Tag("zoning")?.Contains("Commercial", StringComparison.OrdinalIgnoreCase) == true,
        Category.Building => f.Element.Tag("building") is "commercial" or "retail" || f.Element.Has("shop"),
        _ => false,
    };

    private static bool IsShopStreetCandidate(MapFeature f) =>
        f.Category is Category.Street or Category.Tertiary or Category.Secondary or Category.Primary or Category.Pedestrian
        && !f.IsTunnel && f.LengthMetres >= 40 && f.Parts.Count == 1;

    /// <summary>Walks the street, looking a little way off each side for commercial lots.</summary>
    private static bool HasShopFrontage(Raster grid, MapFeature f)
    {
        var p = f.Parts[0];
        int samples = 0, hits = 0;
        double half = f.WidthMetres / 2;
        for (int i = 2; i < p.Length; i += 2)
        {
            double ax = p[i - 2], ay = p[i - 1], dx = p[i] - ax, dy = p[i + 1] - ay;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6) continue;
            double nx = -dy / len, ny = dx / len;
            for (double t = Cell / 2; t < len; t += Cell)
            {
                double x = ax + dx * t / len, y = ay + dy * t / len;
                samples++;
                foreach (double d in new[] { half + 6, half + 14, half + 22 })
                {
                    if (grid.IsCommercial(x + nx * d, y + ny * d) || grid.IsCommercial(x - nx * d, y - ny * d))
                    {
                        hits++;
                        break;
                    }
                }
            }
        }
        return samples > 0 && hits >= samples * MinShopFrontage;
    }

    /// <summary>Free regions not touching the map edge, of park size, and mostly bordered by streets.</summary>
    private static IEnumerable<List<double[]>> FindParks(Raster g)
    {
        var label = new int[g.W * g.H];
        var queue = new Queue<int>();
        int next = 0;
        for (int start = 0; start < label.Length; start++)
        {
            if (g.Ground[start] != Free || label[start] != 0) continue;
            int id = ++next;
            label[start] = id;
            queue.Enqueue(start);
            int cells = 0, major = 0, local = 0, taken = 0;
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = 0, y1 = 0;
            bool edge = false;
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                int x = c % g.W, y = c / g.W;
                cells++;
                x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                if (x == 0 || y == 0 || x == g.W - 1 || y == g.H - 1) { edge = true; continue; }
                foreach (int n in (ReadOnlySpan<int>)[c - 1, c + 1, c - g.W, c + g.W])
                {
                    switch (g.Ground[n])
                    {
                        case Free:
                            if (label[n] == 0) { label[n] = id; queue.Enqueue(n); }
                            break;
                        case MajorRoad: major++; break;
                        case LocalRoad: local++; break;
                        default: taken++; break;
                    }
                }
            }
            double area = cells * Cell * Cell;
            if (edge || area < MinParkArea || area > MaxParkArea) continue;
            int border = major + local + taken;
            if (major + local < MinStreetEdge * border || local < MinLocalEdge * border) continue;

            int bw = x1 - x0 + 1, bh = y1 - y0 + 1;
            var rings = Contours.Trace(bw, bh, (x, y) => label[(y0 + y) * g.W + x0 + x] == id ? 1 : 0, 0.5)
                .Select(r => Contours.Simplify(r, 0.5))
                .Where(r => r.Length >= 8 && Math.Abs(Contours.SignedArea(r)) >= 4)
                .Select(r =>
                {
                    var world = new double[r.Length];
                    for (int i = 0; i < r.Length; i += 2)
                    {
                        world[i] = g.MinX + (x0 + r[i] + 0.5) * Cell;
                        world[i + 1] = g.MinY + (y0 + r[i + 1] + 0.5) * Cell;
                    }
                    return world;
                })
                .OrderByDescending(r => Math.Abs(MapFeature.SignedArea(r)))
                .ToList();
            if (rings.Count > 0) yield return rings;
        }
    }

    /// <summary>Row 0 is the southern edge; cell (x,y) covers MinX + x·Cell … and MinY + y·Cell ….</summary>
    private sealed class Raster(int w, int h, double minX, double minY)
    {
        public readonly int W = w, H = h;
        public readonly double MinX = minX, MinY = minY;
        public readonly byte[] Ground = new byte[w * h];
        public readonly byte[] Commercial = new byte[w * h];

        public bool IsCommercial(double x, double y)
        {
            int cx = (int)Math.Floor((x - MinX) / Cell), cy = (int)Math.Floor((y - MinY) / Cell);
            return cx >= 0 && cy >= 0 && cx < W && cy < H && Commercial[cy * W + cx] != 0;
        }

        /// <summary>Even-odd scanline fill at cell centres.</summary>
        public void FillPolygon(MapFeature f, byte[] target, byte value)
        {
            int ya = Math.Max(0, (int)Math.Floor((f.MinY - MinY) / Cell)), yb = Math.Min(H - 1, (int)Math.Ceiling((f.MaxY - MinY) / Cell));
            var xs = new List<double>();
            for (int row = ya; row <= yb; row++)
            {
                double y = MinY + (row + 0.5) * Cell;
                xs.Clear();
                foreach (var p in f.Parts)
                    for (int i = 0, j = p.Length - 2; i < p.Length; j = i, i += 2)
                    {
                        double yi = p[i + 1], yj = p[j + 1];
                        if ((yi > y) != (yj > y)) xs.Add(p[i] + (y - yi) * (p[j] - p[i]) / (yj - yi));
                    }
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int xa = Math.Max(0, (int)Math.Ceiling((xs[k] - MinX) / Cell - 0.5));
                    int xb = Math.Min(W - 1, (int)Math.Floor((xs[k + 1] - MinX) / Cell - 0.5));
                    for (int x = xa; x <= xb; x++) target[row * W + x] = value;
                }
            }
        }

        /// <summary>Marks every cell within <paramref name="radius"/> of the polyline, overwriting land use.</summary>
        public void StampLine(IReadOnlyList<double[]> parts, double radius, byte value)
        {
            int r = (int)Math.Ceiling(radius / Cell);
            double r2 = (radius / Cell) * (radius / Cell);
            foreach (var p in parts)
                for (int i = 2; i < p.Length; i += 2)
                {
                    double ax = (p[i - 2] - MinX) / Cell, ay = (p[i - 1] - MinY) / Cell;
                    double bx = (p[i] - MinX) / Cell, by = (p[i + 1] - MinY) / Cell;
                    int steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(bx - ax), Math.Abs(by - ay)) * 2));
                    for (int s = 0; s <= steps; s++)
                    {
                        double cx = ax + (bx - ax) * s / steps, cy = ay + (by - ay) * s / steps;
                        int ix = (int)Math.Floor(cx), iy = (int)Math.Floor(cy);
                        for (int y = iy - r; y <= iy + r; y++)
                            for (int x = ix - r; x <= ix + r; x++)
                            {
                                if (x < 0 || y < 0 || x >= W || y >= H) continue;
                                double ddx = x + 0.5 - cx, ddy = y + 0.5 - cy;
                                if (ddx * ddx + ddy * ddy > r2) continue;
                                // A major road never gets downgraded where a local one crosses it.
                                ref byte g = ref Ground[y * W + x];
                                if (g != MajorRoad) g = value;
                            }
                    }
                }
        }
    }
}
