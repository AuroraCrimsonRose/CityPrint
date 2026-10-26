namespace CityPrint.Core.Map;

internal static class Polylines
{
    /// <summary>
    /// Joins polylines whose ends meet into as few runs as possible. Endpoints are shared nodes, so
    /// they match exactly; rounding to 0.1 m absorbs float noise.
    /// </summary>
    public static List<double[]> Join(IEnumerable<double[]> parts)
    {
        var open = parts.Where(p => p.Length >= 4).Select(p => new LinkedList<double[]>([p])).ToList();
        static (long, long) Key(double x, double y) => ((long)Math.Round(x * 10), (long)Math.Round(y * 10));
        static (long, long) Start(LinkedList<double[]> c) => Key(c.First!.Value[0], c.First.Value[1]);
        static (long, long) End(LinkedList<double[]> c) => Key(c.Last!.Value[^2], c.Last.Value[^1]);

        var result = new List<double[]>();
        while (open.Count > 0)
        {
            var cur = open[^1];
            open.RemoveAt(open.Count - 1);
            for (bool grew = true; grew;)
            {
                grew = false;
                for (int i = 0; i < open.Count; i++)
                {
                    var o = open[i];
                    if (Start(o) == End(cur)) foreach (var p in o) cur.AddLast(p);
                    else if (End(o) == End(cur)) foreach (var p in o.Reverse()) cur.AddLast(Reverse(p));
                    else if (End(o) == Start(cur)) foreach (var p in o.Reverse()) cur.AddFirst(p);
                    else if (Start(o) == Start(cur)) foreach (var p in o) cur.AddFirst(Reverse(p));
                    else continue;
                    open.RemoveAt(i);
                    grew = true;
                    break;
                }
            }
            var coords = new List<double>(cur.First!.Value);
            foreach (var p in cur.Skip(1)) coords.AddRange(p.Skip(2));
            result.Add(coords.ToArray());
        }
        return result;
    }

    private static double[] Reverse(double[] p)
    {
        var r = new double[p.Length];
        for (int i = 0; i < p.Length; i += 2)
        {
            r[p.Length - 2 - i] = p[i];
            r[p.Length - 1 - i] = p[i + 1];
        }
        return r;
    }
}
