using CityPrint.Core.Osm;

namespace CityPrint.Core.Map;

public sealed record StatRow(string Section, string Label, string Value);

public static class MapStats
{
    public static List<StatRow> Compute(MapModel model)
    {
        var doc = model.Document;
        var rows = new List<StatRow>();
        void Add(string s, string l, string v) => rows.Add(new(s, l, v));

        Add("File", "Name", doc.FilePath is null ? "—" : Path.GetFileName(doc.FilePath));
        Add("File", "Size", FormatBytes(doc.FileSize));
        Add("File", "Generator", doc.Generator ?? "—");
        Add("File", "Parsed in", $"{doc.ParseTime.TotalMilliseconds:N0} ms");
        Add("Elements", "Nodes", doc.Nodes.Count.ToString("N0"));
        Add("Elements", "Ways", doc.Ways.Count.ToString("N0"));
        Add("Elements", "Relations", doc.Relations.Count.ToString("N0"));
        Add("Elements", "Duplicates skipped", (doc.DuplicateNodes + doc.DuplicateWays + doc.DuplicateRelations).ToString("N0"));
        Add("Elements", "Conflicting duplicates", doc.ConflictingDuplicates.ToString("N0"));
        Add("Elements", "Rendered features", model.Features.Count.ToString("N0"));

        Add("Extent", "Width", FormatLength(model.MaxX - model.MinX));
        Add("Extent", "Height", FormatLength(model.MaxY - model.MinY));

        var lines = model.Features.Where(f => f.Kind == GeometryKind.Line).ToList();
        var roads = lines.Where(f => MapStyle.IsRoad(f.Category)).ToList();
        Add("Roads", "Total length", FormatLength(roads.Sum(f => f.LengthMetres)));
        Add("Roads", "Lane-km", $"{roads.Sum(f => f.LengthMetres * Math.Max(1, f.Lanes)) / 1000:N1}");
        Add("Roads", "Bridges", FormatLength(roads.Where(f => f.IsBridge).Sum(f => f.LengthMetres)));
        Add("Roads", "Tunnels", FormatLength(roads.Where(f => f.IsTunnel).Sum(f => f.LengthMetres)));
        Add("Roads", "Named streets", roads.Select(f => f.Name).Where(n => n != null).Distinct().Count().ToString("N0"));
        foreach (var g in roads.GroupBy(f => f.Category).OrderByDescending(g => g.Key))
            Add("Roads", MapStyle.Of(g.Key).Name, FormatLength(g.Sum(f => f.LengthMetres)));

        foreach (var g in lines.Where(f => !MapStyle.IsRoad(f.Category)).GroupBy(f => f.Category).OrderBy(g => g.Key))
            Add("Networks", MapStyle.Of(g.Key).Name, FormatLength(g.Sum(f => f.LengthMetres)));

        foreach (var g in model.Features.Where(f => f.Kind == GeometryKind.Area).GroupBy(f => f.Category).OrderBy(g => g.Key))
            Add("Areas", MapStyle.Of(g.Key).Name, $"{g.Count():N0} · {FormatArea(g.Sum(f => f.AreaSqMetres))}");

        foreach (var g in model.Features.Where(f => f.Kind == GeometryKind.Point).GroupBy(f => f.Category).OrderBy(g => g.Key))
            Add("Points", MapStyle.Of(g.Key).Name, g.Count().ToString("N0"));

        var keys = doc.Nodes.Values.Cast<OsmElement>().Concat(doc.Ways.Values).Concat(doc.Relations.Values)
            .SelectMany(e => e.Tags.Keys)
            .GroupBy(k => k)
            .OrderByDescending(g => g.Count())
            .Take(40);
        foreach (var g in keys)
            Add("Tag keys", g.Key, g.Count().ToString("N0"));

        return rows;
    }

    public static string FormatLength(double m) => m >= 1000 ? $"{m / 1000:N2} km" : $"{m:N0} m";

    public static string FormatArea(double m2) => m2 >= 1_000_000 ? $"{m2 / 1_000_000:N2} km²" : m2 >= 10_000 ? $"{m2 / 10_000:N2} ha" : $"{m2:N0} m²";

    public static string FormatBytes(long b) => b >= 1 << 20 ? $"{b / (double)(1 << 20):N2} MB" : $"{b / 1024.0:N0} KB";
}
