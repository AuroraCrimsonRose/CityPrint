using System.Globalization;

namespace CityPrint.Core.Map;

/// <summary>A set of features treated as one thing, e.g. every segment of "Maple Highway".</summary>
public sealed class FeatureGroup
{
    public required string Section { get; init; }
    public required string Title { get; init; }
    public required Category Category { get; init; }
    public required IReadOnlyList<MapFeature> Features { get; init; }

    public double MinX => Features.Min(f => f.MinX);
    public double MinY => Features.Min(f => f.MinY);
    public double MaxX => Features.Max(f => f.MaxX);
    public double MaxY => Features.Max(f => f.MaxY);

    public double LengthMetres => Features.Sum(f => f.LengthMetres);
    public double AreaSqMetres => Features.Sum(f => f.AreaSqMetres);

    public string Summary
    {
        get
        {
            var kind = Features[0].Kind;
            string count = $"{Features.Count:N0} {(kind == GeometryKind.Line ? "segment" : "item")}{(Features.Count == 1 ? "" : "s")}";
            return kind switch
            {
                GeometryKind.Line => $"{MapStats.FormatLength(LengthMetres)} · {count}",
                GeometryKind.Area => $"{MapStats.FormatArea(AreaSqMetres)} · {count}",
                _ => count,
            };
        }
    }

    /// <summary>Length, area, or count only; for narrow lists.</summary>
    public string ShortSummary => Features[0].Kind switch
    {
        GeometryKind.Line => MapStats.FormatLength(LengthMetres),
        GeometryKind.Area => MapStats.FormatArea(AreaSqMetres),
        _ => Features.Count.ToString("N0", CultureInfo.InvariantCulture),
    };

    /// <summary>Facts about the whole group, e.g. lane counts and speed limits across all segments.</summary>
    public List<(string Label, string Value)> Describe()
    {
        var classes = Features.GroupBy(f => f.Category).OrderByDescending(g => g.Count())
            .Select(g => Features.Count > 1 && g.Count() != Features.Count ? $"{MapStyle.Of(g.Key).Name} ×{g.Count()}" : MapStyle.Of(g.Key).Name);
        var rows = new List<(string, string)>
        {
            ("Category", string.Join(", ", classes)),
            ("Items", Features.Count.ToString("N0", CultureInfo.InvariantCulture)),
        };
        var lines = Features.Where(f => f.Kind == GeometryKind.Line).ToList();
        if (lines.Count > 0)
        {
            rows.Add(("Total length", MapStats.FormatLength(LengthMetres)));
            var lanes = lines.Where(f => f.Lanes > 0).Select(f => f.Lanes).Distinct().Order().ToList();
            if (lanes.Count > 0) rows.Add(("Lanes", string.Join(", ", lanes)));
            var speeds = lines.Select(f => f.Element.Tag("maxspeed")).Where(s => s != null).Distinct().Order().ToList();
            if (speeds.Count > 0) rows.Add(("Speed limits", string.Join(", ", speeds) + " km/h"));
            int oneway = lines.Count(f => f.Element.Is("oneway", "yes"));
            if (oneway > 0) rows.Add(("One-way segments", $"{oneway} of {lines.Count}"));
            var bridges = lines.Where(f => f.IsBridge).ToList();
            if (bridges.Count > 0) rows.Add(("Bridges", $"{bridges.Count} · {MapStats.FormatLength(bridges.Sum(f => f.LengthMetres))}"));
            var tunnels = lines.Where(f => f.IsTunnel).ToList();
            if (tunnels.Count > 0) rows.Add(("Tunnels", $"{tunnels.Count} · {MapStats.FormatLength(tunnels.Sum(f => f.LengthMetres))}"));
        }
        if (Features.Any(f => f.Kind == GeometryKind.Area))
            rows.Add(("Total area", MapStats.FormatArea(AreaSqMetres)));
        rows.Add(("Extent", $"{MapStats.FormatLength(MaxX - MinX)} × {MapStats.FormatLength(MaxY - MinY)}"));
        return rows;
    }

    /// <summary>Tags every member shares, which for a street is usually its defining tags.</summary>
    public List<KeyValuePair<string, string>> CommonTags()
    {
        var first = Features[0].Element.Tags;
        return first
            .Where(kv => Features.All(f => f.Element.Tags.TryGetValue(kv.Key, out var v) && v == kv.Value))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToList();
    }
}

public static class FeatureGroups
{
    public static readonly string[] SectionOrder =
        ["Roads", "Paths", "Rail & transit", "Stops & stations", "Places", "Points of interest", "Land use", "Nature", "Utilities", "Other"];

    public static string SectionOf(Category c) => c switch
    {
        _ when MapStyle.IsRoad(c) => "Roads",
        Category.Footway or Category.Cycleway => "Paths",
        Category.Rail or Category.Tram or Category.Subway or Category.LightRail or Category.Platform
            or Category.Ferry or Category.TransitLine or Category.Runway or Category.Taxiway => "Rail & transit",
        Category.TransitStop => "Stops & stations",
        Category.Place => "Places",
        Category.Poi => "Points of interest",
        Category.Residential or Category.Commercial or Category.Retail or Category.Industrial or Category.Office
            or Category.OtherLanduse or Category.Pedestrian or Category.Building or Category.Farmland => "Land use",
        Category.Water or Category.Forest or Category.Park or Category.Waterway or Category.Contour or Category.Tree => "Nature",
        Category.Power => "Utilities",
        _ => "Other",
    };

    // Streets and transit lines are split into many ways; stops/places can repeat per platform.
    private static bool ConsolidatesByName(MapFeature f) =>
        f.Kind == GeometryKind.Line || f.Category is Category.TransitStop or Category.Place;

    /// <summary>
    /// Groups features: named lines become one group per (name, category) so a street's segments
    /// consolidate; unnamed lines and areas group by category; points group by category.
    /// </summary>
    public static List<FeatureGroup> Build(IEnumerable<MapFeature> features)
    {
        var groups = new List<FeatureGroup>();
        foreach (var bySection in features.GroupBy(f => SectionOf(f.Category)))
        {
            // A named street keeps its identity when it changes class (e.g. highway → street), so
            // named features group by name alone; unnamed ones group by category.
            foreach (var g in bySection.GroupBy(f => ConsolidatesByName(f) && f.Name != null ? (Category: (Category?)null, f.Name) : (f.Category, Name: null)))
            {
                var list = g.OrderBy(f => f.Element.Id).ToList();
                // Represent the group by its dominant class: longest total length, else most members.
                var category = g.Key.Category ?? list.GroupBy(f => f.Category)
                    .OrderByDescending(c => c.Sum(f => f.LengthMetres)).ThenByDescending(c => c.Count()).First().Key;
                string style = MapStyle.Of(category).Name;
                string title = g.Key.Name ?? (list[0].Kind == GeometryKind.Line ? $"Unnamed {style.ToLowerInvariant()}" : style);
                groups.Add(new FeatureGroup { Section = bySection.Key, Title = title, Category = category, Features = list });
            }
        }
        return groups
            .OrderBy(g => Array.IndexOf(SectionOrder, g.Section))
            .ThenBy(g => g.Title.StartsWith("Unnamed", StringComparison.Ordinal))
            .ThenByDescending(g => MapStyle.Of(g.Category).Order)
            .ThenBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
