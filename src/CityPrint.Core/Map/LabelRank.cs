using System.Globalization;
using CityPrint.Core.Osm;

namespace CityPrint.Core.Map;

/// <summary>
/// Decides which labels win when they compete for space. Renderers place labels in rank order and
/// skip any that would overlap one already placed, so the most important names survive at every zoom.
/// </summary>
public static class LabelRank
{
    /// <summary>Place names: bigger kinds of place first, then by population when the export has it.</summary>
    public static double Place(MapFeature f)
    {
        double kind = f.Element.Tag("place") switch
        {
            "city" => 100,
            "town" => 80,
            "borough" or "suburb" => 60,
            "village" => 50,
            "quarter" => 45,
            "neighbourhood" => 40,
            "hamlet" => 30,
            _ => 20,
        };
        return kind + Math.Log10(1 + Number(f.Element, "population"));
    }

    /// <summary>Stops: stations over platforms, rail over road, then by how many lines call there.</summary>
    public static double Stop(MapFeature f, int lines)
    {
        var e = f.Element;
        double kind =
            e.Is("public_transport", "station") || e.Is("railway", "station") || e.Is("amenity", "bus_station") ? 40 :
            e.Is("railway", "platform") || e.Is("railway", "halt") || e.Tag("osm_export_stop") is "train" or "subway" ? 30 :
            e.Is("railway", "tram_stop") || e.Is("osm_export_stop", "tram") ? 20 :
            10;
        return kind + Math.Min(lines, 20);
    }

    /// <summary>Points of interest: civic buildings first, then by size or occupants when known.</summary>
    public static double Poi(MapFeature f)
    {
        var e = f.Element;
        double kind = e.Tag("amenity") switch
        {
            "hospital" or "university" or "townhall" or "college" => 60,
            "school" or "police" or "fire_station" or "library" or "place_of_worship" => 45,
            "bus_station" or "post_office" or "arts_centre" or "theatre" or "cinema" => 35,
            not null => 25,
            null when e.Has("tourism") => 40,
            null when e.Has("leisure") => 30,
            null when e.Has("office") => 20,
            null when e.Has("shop") => 15,
            _ => 10,
        };
        double size = Math.Log10(1 + f.AreaSqMetres) + Math.Log10(1 + Number(e, "population") + Number(e, "employees"));
        return kind + (f.Name != null ? 5 : 0) + size;
    }

    /// <summary>
    /// Named lines: road class first, then the total length of everything sharing the name, so a long
    /// avenue beats a short lane of the same class.
    /// <para>The export writes roads as short segments, too short to hold a name, so connected segments
    /// of the same street and class are joined into chains first. The results are label-only features;
    /// within a name the longest chain is tried first because it has the most room for the text.</para>
    /// </summary>
    public static IReadOnlyList<MapFeature> Lines(IEnumerable<MapFeature> lines) =>
        lines.GroupBy(f => f.Name!)
            .OrderByDescending(g => g.Max(f => f.Style.Order))
            .ThenByDescending(g => g.Sum(f => f.LengthMetres))
            .SelectMany(g => g.GroupBy(f => f.Category).SelectMany(Chain).OrderByDescending(f => f.LengthMetres))
            .ToList();

    private static IEnumerable<MapFeature> Chain(IEnumerable<MapFeature> segments)
    {
        var list = segments.ToList();
        if (list.Count == 1) return list;

        var first = list[0];
        return Polylines.Join(list.SelectMany(f => f.Parts)).Select(coords =>
        {
            var f = new MapFeature
            {
                Index = first.Index,
                Element = first.Element,
                Category = first.Category,
                Kind = GeometryKind.Line,
                Parts = [coords],
                Layer = first.Layer,
                Name = first.Name,
                WidthMetres = list.Max(s => s.WidthMetres),
            };
            f.ComputeMetrics();
            return f;
        });
    }

    private static double Number(OsmElement e, string key) =>
        double.TryParse(e.Tag(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : 0;
}
