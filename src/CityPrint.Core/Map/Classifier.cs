using CityPrint.Core.Osm;

namespace CityPrint.Core.Map;

/// <summary>Maps OSM tags (as written by the OSM Export mod) to display categories.</summary>
public static class Classifier
{
    public static Category ClassifyNode(OsmNode n)
    {
        var t = n.Tags;
        if (t.ContainsKey("osm_export_stop")) return Category.TransitStop;
        if (t.TryGetValue("public_transport", out var pt) && pt is "platform" or "stop_position" or "station") return Category.TransitStop;
        if (t.TryGetValue("railway", out var rw) && rw is "station" or "halt" or "tram_stop" or "platform") return Category.TransitStop;
        if (t.TryGetValue("highway", out var hw) && hw is "bus_stop" or "platform") return Category.TransitStop;
        if (n.Is("amenity", "bus_station")) return Category.TransitStop;
        if (t.ContainsKey("place")) return Category.Place;
        if (n.Is("natural", "tree")) return Category.Tree;
        if (t.ContainsKey("amenity") || t.ContainsKey("shop") || t.ContainsKey("office") || t.ContainsKey("tourism")
            || t.ContainsKey("leisure") || t.ContainsKey("historic") || t.ContainsKey("power") || t.ContainsKey("aeroway"))
            return Category.Poi;
        return Category.OtherPoint;
    }

    /// <summary>Returns the line category for a way, or null when its tags describe an area.</summary>
    public static Category? ClassifyLine(OsmElement e)
    {
        var t = e.Tags;
        if (t.ContainsKey("osm_export_route")) return Category.TransitLine;
        if (t.ContainsKey("contour")) return Category.Contour;

        if (t.TryGetValue("highway", out var hw))
        {
            if (hw == "pedestrian" && (e.Is("area", "yes") || IsClosed(e))) return null;
            return hw switch
            {
                "motorway" => Category.Motorway,
                "motorway_link" => Category.MotorwayLink,
                "trunk" => Category.Trunk,
                "trunk_link" => Category.TrunkLink,
                "primary" or "primary_link" => Category.Primary,
                "secondary" or "secondary_link" => Category.Secondary,
                "tertiary" or "tertiary_link" => Category.Tertiary,
                "residential" or "unclassified" or "living_street" or "road" => Category.Street,
                "service" => Category.Service,
                "footway" or "path" or "steps" or "pedestrian" or "sidewalk" or "track" or "bridleway" => Category.Footway,
                "cycleway" => Category.Cycleway,
                "platform" => Category.Platform,
                _ => Category.OtherLine,
            };
        }
        if (t.TryGetValue("railway", out var rw))
        {
            return rw switch
            {
                "rail" => Category.Rail,
                "tram" => Category.Tram,
                "subway" => Category.Subway,
                "light_rail" or "monorail" or "narrow_gauge" => Category.LightRail,
                "platform" => Category.Platform,
                _ => Category.OtherLine,
            };
        }
        if (t.TryGetValue("power", out var pw) && pw is "line" or "minor_line" or "cable") return Category.Power;
        if (e.Is("seamark:type", "ferry_route") || e.Is("route", "ferry")) return Category.Ferry;
        if (t.TryGetValue("aeroway", out var aw) && aw is "runway" or "taxiway")
            return aw == "runway" ? Category.Runway : Category.Taxiway;
        if (t.TryGetValue("waterway", out var ww) && ww is "river" or "stream" or "canal" or "drain" or "ditch")
            return Category.Waterway;
        if (t.TryGetValue("barrier", out _)) return Category.OtherLine;
        return null;
    }

    /// <summary>Returns the area category for closed ways / multipolygons, or null when nothing matches.</summary>
    public static Category? ClassifyArea(OsmElement e)
    {
        var t = e.Tags;
        if (e.Is("natural", "water") || e.Is("waterway", "riverbank") || e.Is("waterway", "dock")
            || e.Is("landuse", "reservoir") || e.Is("landuse", "basin"))
            return Category.Water;
        if (e.Is("landuse", "forest") || e.Is("natural", "wood")) return Category.Forest;
        if (t.ContainsKey("building")) return Category.Building;
        if (e.Is("highway", "pedestrian")) return Category.Pedestrian;

        if (t.TryGetValue("landuse", out var lu))
        {
            return lu switch
            {
                "residential" => Category.Residential,
                "commercial" => Category.Commercial,
                "retail" => Category.Retail,
                "industrial" or "quarry" or "landfill" => Category.Industrial,
                "farmland" or "farm" or "farmyard" or "meadow" or "orchard" or "allotments" => Category.Farmland,
                "grass" or "village_green" or "recreation_ground" or "cemetery" => Category.Park,
                _ => Category.OtherLanduse,
            };
        }
        if (t.ContainsKey("leisure")) return Category.Park;
        if (t.ContainsKey("office")) return Category.Office;
        if (t.ContainsKey("shop")) return Category.Retail;
        if (t.TryGetValue("natural", out var nat))
            return nat is "grassland" or "heath" or "scrub" ? Category.Park : Category.OtherLanduse;
        if (t.ContainsKey("amenity") || t.ContainsKey("aeroway") || t.ContainsKey("power") || e.Is("area", "yes"))
            return Category.OtherLanduse;
        return null;
    }

    private static bool IsClosed(OsmElement e) => e is OsmWay w && w.IsClosed;
}
