using CityPrint.Core.Osm;

namespace CityPrint.Core.Map;

/// <summary>
/// Colours that tell buildings and points of interest apart by what they are. Themes with
/// <see cref="MapTheme.TypeColours"/> off draw them all alike.
/// </summary>
public static class TypeColours
{
    public const uint Residential = 0xFFE8A06A;
    public const uint Commercial = 0xFF5B9BE0;
    public const uint Office = 0xFF9C7FD8;
    public const uint Industrial = 0xFFD9B44A;
    public const uint Civic = 0xFFE06A7A;

    public static uint? Building(OsmElement e) => e.Tag("building") switch
    {
        "residential" or "house" or "apartments" or "detached" or "terrace" => Residential,
        "commercial" or "retail" or "supermarket" => Commercial,
        "office" => Office,
        "industrial" or "warehouse" or "factory" => Industrial,
        "public" or "civic" or "government" or "hospital" or "school" or "university" or "fire_station" => Civic,
        _ => null,
    };

    /// <summary>Marker colour and icon (a name from the app's Lucide icon set) for a point of interest.</summary>
    public static (uint Colour, string Icon) Poi(OsmElement e)
    {
        const uint police = 0xFF2B5FD9, health = 0xFFE03A3A, fire = 0xFFF07020, education = 0xFFE0B020,
            civic = 0xFF8A6D3B, transport = 0xFF1A3A6A, power = 0xFF9B59B6, water = 0xFF2AA9C9,
            nature = 0xFF3DA94A, grey = 0xFF707070;
        switch (e.Tag("amenity"))
        {
            case "police" or "prison": return (police, "Shield");
            case "hospital" or "clinic" or "doctors" or "pharmacy": return (health, "Plus");
            case "fire_station": return (fire, "Flame");
            case "school" or "college" or "university" or "kindergarten" or "library" or "research_institute": return (education, "GraduationCap");
            case "townhall" or "courthouse": return (civic, "Landmark");
            case "post_office" or "post_box": return (civic, "Mail");
            case "grave_yard" or "crematorium" or "place_of_worship": return (grey, "Landmark");
            case "waste_transfer_station" or "recycling": return (0xFF7A7A40, "Trash");
            case "bus_station": return (transport, "Bus");
            case "ferry_terminal": return (transport, "Ship");
            case "taxi" or "parking" or "bicycle_parking" or "fuel" or "vehicle_depot": return (transport, "CarFront");
            case "bank" or "bar" or "restaurant" or "cafe" or "fast_food" or "pub": return (Commercial, "Store");
        }
        if (e.Has("shop")) return (Commercial, "Store");
        if (e.Has("office")) return (Office, "Building");
        if (e.Has("power")) return (power, "Zap");
        switch (e.Tag("man_made"))
        {
            case "wastewater_plant": return (water, "WavesHorizontal");
            case "water_works" or "water_tower" or "pumping_station": return (water, "Droplet");
            case "works" or "warehouse": return (Industrial, "Factory");
        }
        if (e.Has("leisure")) return (nature, "Trees");
        if (e.Has("tourism")) return (0xFFD94FA0, "MapPinned");
        if (e.Has("aeroway") || e.Tag("railway") is "station") return (transport, "MapPin");
        return (grey, "MapPin");
    }

    /// <summary>Mixes <paramref name="tint"/> into <paramref name="baseColour"/>, keeping the base's alpha.</summary>
    public static uint Blend(uint baseColour, uint tint, double amount)
    {
        uint Mix(int shift) => (uint)Math.Round(((baseColour >> shift) & 0xFF) * (1 - amount) + ((tint >> shift) & 0xFF) * amount) << shift;
        return (baseColour & 0xFF000000) | Mix(16) | Mix(8) | Mix(0);
    }
}
