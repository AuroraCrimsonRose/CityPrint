namespace CityPrint.Core.Carto;

/// <summary>
/// Traces where a raster crosses a threshold (marching squares with linear interpolation), giving
/// closed rings whose edges follow the data smoothly rather than the pixel staircase.
/// </summary>
public static class Contours
{
    /// <summary>
    /// Rings around every region where <paramref name="value"/> exceeds <paramref name="level"/>, in
    /// pixel coordinates (x right, y down, pixel centres on whole numbers). The raster is treated as
    /// surrounded by low values, so regions touching the edge close along it. With y flipped to point
    /// north, regions run clockwise and holes counter-clockwise.
    /// </summary>
    public static List<double[]> Trace(int width, int height, Func<int, int, double> value, double level)
    {
        // Sample with a one-pixel border of "below" so every ring closes.
        double V(int x, int y) => x < 0 || y < 0 || x >= width || y >= height ? level - 1 : value(x, y);

        // Segment ends are keyed by the grid edge they lie on, so neighbouring cells meet exactly.
        // Edge keys: horizontal edge between (x,y)-(x+1,y) and vertical edge between (x,y)-(x,y+1).
        var next = new Dictionary<long, long>();
        var point = new Dictionary<long, (double X, double Y)>();
        long Key(int x, int y, bool vertical) => (((long)(y + 1) * (width + 2) + (x + 1)) << 1) | (vertical ? 1L : 0L);

        long Edge(int x, int y, int which, double v00, double v10, double v11, double v01)
        {
            // which: 0 top, 1 right, 2 bottom, 3 left (cell spans (x,y)-(x+1,y+1)).
            (int ax, int ay, double va, int bx, int by, double vb, bool vertical, int kx, int ky) = which switch
            {
                0 => (x, y, v00, x + 1, y, v10, false, x, y),
                1 => (x + 1, y, v10, x + 1, y + 1, v11, true, x + 1, y),
                2 => (x, y + 1, v01, x + 1, y + 1, v11, false, x, y + 1),
                _ => (x, y, v00, x, y + 1, v01, true, x, y),
            };
            long k = Key(kx, ky, vertical);
            if (!point.ContainsKey(k))
            {
                double t = Math.Clamp((level - va) / (vb - va), 0, 1);
                point[k] = (ax + (bx - ax) * t, ay + (by - ay) * t);
            }
            return k;
        }

        void Segment(long from, long to) => next[from] = to;

        for (int y = -1; y < height; y++)
        {
            for (int x = -1; x < width; x++)
            {
                double v00 = V(x, y), v10 = V(x + 1, y), v11 = V(x + 1, y + 1), v01 = V(x, y + 1);
                int c = (v00 > level ? 8 : 0) | (v10 > level ? 4 : 0) | (v11 > level ? 2 : 0) | (v01 > level ? 1 : 0);
                if (c is 0 or 15) continue;
                long E(int which) => Edge(x, y, which, v00, v10, v11, v01);
                // Segments keep the "above" region on their right in pixel space (y down), so once y is
                // flipped to point north, regions come out clockwise and holes counter-clockwise.
                switch (c)
                {
                    case 1: Segment(E(3), E(2)); break;
                    case 2: Segment(E(2), E(1)); break;
                    case 3: Segment(E(3), E(1)); break;
                    case 4: Segment(E(1), E(0)); break;
                    case 6: Segment(E(2), E(0)); break;
                    case 7: Segment(E(3), E(0)); break;
                    case 8: Segment(E(0), E(3)); break;
                    case 9: Segment(E(0), E(2)); break;
                    case 11: Segment(E(0), E(1)); break;
                    case 12: Segment(E(1), E(3)); break;
                    case 13: Segment(E(1), E(2)); break;
                    case 14: Segment(E(2), E(3)); break;
                    case 5 or 10:
                        // Saddle: the centre value decides whether the two "above" corners connect.
                        bool joined = (v00 + v10 + v11 + v01) / 4 > level;
                        if (c == 5)
                        {
                            if (joined) { Segment(E(3), E(0)); Segment(E(1), E(2)); }
                            else { Segment(E(3), E(2)); Segment(E(1), E(0)); }
                        }
                        else
                        {
                            if (joined) { Segment(E(0), E(1)); Segment(E(2), E(3)); }
                            else { Segment(E(0), E(3)); Segment(E(2), E(1)); }
                        }
                        break;
                }
            }
        }

        var rings = new List<double[]>();
        var visited = new HashSet<long>();
        foreach (var start in next.Keys)
        {
            if (visited.Contains(start)) continue;
            var ring = new List<double>();
            long k = start;
            while (visited.Add(k))
            {
                var (px, py) = point[k];
                ring.Add(px);
                ring.Add(py);
                if (!next.TryGetValue(k, out k)) break;
            }
            if (ring.Count < 6) continue;
            ring.Add(ring[0]);
            ring.Add(ring[1]);
            rings.Add(ring.ToArray());
        }
        return rings;
    }

    /// <summary>Signed area of a closed ring (positive when counter-clockwise in y-up coordinates).</summary>
    public static double SignedArea(double[] p)
    {
        double s = 0;
        for (int i = 2; i < p.Length; i += 2) s += p[i - 2] * p[i + 1] - p[i] * p[i - 1];
        return s / 2;
    }

    /// <summary>Douglas–Peucker simplification of a closed ring; keeps the first point.</summary>
    public static double[] Simplify(double[] p, double tolerance)
    {
        int n = p.Length / 2;
        if (n < 5) return p;
        var keep = new bool[n];
        keep[0] = keep[n - 1] = true;
        // Split the ring at its farthest point from the start so both halves are open polylines.
        int far = 0;
        double best = -1;
        for (int i = 1; i < n - 1; i++)
        {
            double d = (p[2 * i] - p[0]) * (p[2 * i] - p[0]) + (p[2 * i + 1] - p[1]) * (p[2 * i + 1] - p[1]);
            if (d > best) { best = d; far = i; }
        }
        keep[far] = true;
        var stack = new Stack<(int, int)>([(0, far), (far, n - 1)]);
        double tol2 = tolerance * tolerance;
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            double ax = p[2 * a], ay = p[2 * a + 1], bx = p[2 * b], by = p[2 * b + 1];
            double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
            int index = -1;
            double max = tol2;
            for (int i = a + 1; i < b; i++)
            {
                double px = p[2 * i] - ax, py = p[2 * i + 1] - ay;
                double t = len2 == 0 ? 0 : Math.Clamp((px * dx + py * dy) / len2, 0, 1);
                double ex = px - t * dx, ey = py - t * dy, d = ex * ex + ey * ey;
                if (d > max) { max = d; index = i; }
            }
            if (index < 0) continue;
            keep[index] = true;
            stack.Push((a, index));
            stack.Push((index, b));
        }
        var r = new List<double>();
        for (int i = 0; i < n; i++)
            if (keep[i]) { r.Add(p[2 * i]); r.Add(p[2 * i + 1]); }
        return r.ToArray();
    }
}
