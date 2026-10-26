namespace CityPrint.Core.Map;

public enum GeometryKind { Point, Line, Area }

public enum Category
{
    // areas
    Water, Farmland, Forest, Park, Residential, Commercial, Retail, Industrial, Office, OtherLanduse, Pedestrian, Building,
    // lines
    Contour, Footway, Cycleway, Service, Street, Tertiary, Secondary, Primary, TrunkLink, Trunk, MotorwayLink, Motorway,
    Rail, Tram, Subway, LightRail, Platform, Power, Ferry, Runway, Taxiway, TransitLine, Waterway, OtherLine,
    // points
    TransitStop, Place, Poi, Tree, OtherPoint,
}

/// <param name="WidthMetres">Real-world width used when zoomed in; null means a fixed pixel width.</param>
/// <param name="MinPixels">Minimum drawn width, so features stay visible when zoomed out.</param>
public sealed record CategoryStyle(
    Category Category,
    string Name,
    string Group,
    GeometryKind Kind,
    uint Fill,
    uint Stroke = 0,
    uint Casing = 0,
    double? WidthMetres = null,
    double MinPixels = 1,
    double[]? Dash = null,
    int Order = 0);

/// <summary>
/// Palette follows the OSM Export mod's Classic Maperitive ruleset (OSMExport.mrules),
/// so the viewer looks like the mod's own PNG/PDF renders.
/// </summary>
public static class MapStyle
{
    public const uint Background = 0xFFF1EEE8;

    public static readonly IReadOnlyList<CategoryStyle> All =
    [
        new(Category.Water, "Water", "Nature", GeometryKind.Area, 0xFFB5D0D0, Order: 0),
        new(Category.Farmland, "Farmland", "Land use", GeometryKind.Area, 0xFFE9D8BE, Order: 1),
        new(Category.Forest, "Forest", "Nature", GeometryKind.Area, 0xFF8DC56C, Order: 2),
        new(Category.Park, "Parks & leisure", "Land use", GeometryKind.Area, 0xFFC0F6B0, Order: 3),
        new(Category.Residential, "Residential", "Land use", GeometryKind.Area, 0xFFDCDCDC, 0xFFC4C4C4, Order: 4),
        new(Category.Commercial, "Commercial", "Land use", GeometryKind.Area, 0xFFEFC8C8, 0xFFD9AFAF, Order: 5),
        new(Category.Retail, "Retail", "Land use", GeometryKind.Area, 0xFFF0D9D9, 0xFFF4A19E, Order: 6),
        new(Category.Industrial, "Industrial", "Land use", GeometryKind.Area, 0xFFDFD1D6, 0xFFC5B5BB, Order: 7),
        new(Category.Office, "Office", "Land use", GeometryKind.Area, 0xFF95ACDF, 0xFF7D93C4, Order: 8),
        new(Category.OtherLanduse, "Other land use", "Land use", GeometryKind.Area, 0xFFE4E0D8, 0xFFCCC8C0, Order: 9),
        new(Category.Pedestrian, "Pedestrian areas", "Land use", GeometryKind.Area, 0xFFEDEDED, 0xFFB1B1B1, Order: 10),
        new(Category.Building, "Buildings", "Land use", GeometryKind.Area, 0xFFBCA9A9, 0xFF8E7C7C, Order: 11),

        new(Category.Contour, "Contours", "Nature", GeometryKind.Line, 0, 0x60A08060, MinPixels: 0.75, Order: 20),
        new(Category.Waterway, "Waterways", "Nature", GeometryKind.Line, 0, 0xFFB5D0D0, WidthMetres: 8, MinPixels: 1.5, Order: 21),
        new(Category.Footway, "Footways & paths", "Roads", GeometryKind.Line, 0, 0xFFFA8072, WidthMetres: 3, MinPixels: 1, Dash: [2, 1.5], Order: 30),
        new(Category.Cycleway, "Cycleways", "Roads", GeometryKind.Line, 0, 0xFF4040FF, WidthMetres: 3, MinPixels: 1, Dash: [2, 1.5], Order: 31),
        new(Category.Service, "Service roads", "Roads", GeometryKind.Line, 0, 0xFFFFFFFF, 0xFFBBBBBB, WidthMetres: 6, MinPixels: 1, Order: 32),
        new(Category.Street, "Streets", "Roads", GeometryKind.Line, 0, 0xFFFFFFFF, 0xFF999999, WidthMetres: 3.5, MinPixels: 1.5, Order: 33),
        new(Category.Tertiary, "Tertiary", "Roads", GeometryKind.Line, 0, 0xFFFEFEB2, 0xFFC6C68A, WidthMetres: 3.5, MinPixels: 1.5, Order: 34),
        new(Category.Secondary, "Secondary", "Roads", GeometryKind.Line, 0, 0xFFFDD6A4, 0xFFCAA36F, WidthMetres: 3.5, MinPixels: 2, Order: 35),
        new(Category.Primary, "Primary", "Roads", GeometryKind.Line, 0, 0xFFECA2A3, 0xFFBD8182, WidthMetres: 3.5, MinPixels: 2, Order: 36),
        new(Category.TrunkLink, "Highway ramps", "Roads", GeometryKind.Line, 0, 0xFF96D296, 0xFF78A878, WidthMetres: 3.5, MinPixels: 1.5, Order: 37),
        new(Category.Trunk, "Highways", "Roads", GeometryKind.Line, 0, 0xFF96D296, 0xFF78A878, WidthMetres: 3.5, MinPixels: 2, Order: 38),
        new(Category.MotorwayLink, "Motorway ramps", "Roads", GeometryKind.Line, 0, 0xFF849BBD, 0xFF6A7C97, WidthMetres: 3.5, MinPixels: 1.5, Order: 39),
        new(Category.Motorway, "Motorways", "Roads", GeometryKind.Line, 0, 0xFF849BBD, 0xFF6A7C97, WidthMetres: 3.5, MinPixels: 2.5, Order: 40),

        new(Category.Platform, "Platforms", "Transit", GeometryKind.Line, 0, 0xFF808080, WidthMetres: 5, MinPixels: 2, Order: 50),
        new(Category.Subway, "Subway", "Transit", GeometryKind.Line, 0, 0xFF999999, WidthMetres: 4, MinPixels: 1.5, Dash: [3, 2], Order: 51),
        new(Category.Tram, "Tram", "Transit", GeometryKind.Line, 0, 0xFF444444, WidthMetres: 2, MinPixels: 1, Order: 52),
        new(Category.LightRail, "Light rail", "Transit", GeometryKind.Line, 0, 0xFF666666, WidthMetres: 3, MinPixels: 1.5, Order: 53),
        new(Category.Rail, "Rail", "Transit", GeometryKind.Line, 0, 0xFFFFFFFF, 0xFF808080, WidthMetres: 4, MinPixels: 2, Dash: [4, 4], Order: 54),
        new(Category.Runway, "Runways", "Transit", GeometryKind.Line, 0, 0xFFBBBBCC, WidthMetres: 45, MinPixels: 2, Order: 55),
        new(Category.Taxiway, "Taxiways", "Transit", GeometryKind.Line, 0, 0xFFBBBBCC, WidthMetres: 20, MinPixels: 1, Order: 56),
        new(Category.Ferry, "Ferry routes", "Transit", GeometryKind.Line, 0, 0xFF6666FF, MinPixels: 1.25, Dash: [8, 4], Order: 57),
        new(Category.TransitLine, "Transport lines", "Transit", GeometryKind.Line, 0, 0xFF00C0C0, 0xFF000000, MinPixels: 3, Order: 58),
        new(Category.Power, "Power lines", "Utilities", GeometryKind.Line, 0, 0xFF5C5C5C, MinPixels: 1, Order: 60),
        new(Category.OtherLine, "Other lines", "Other", GeometryKind.Line, 0, 0xFFA0A0A0, MinPixels: 1, Order: 61),

        new(Category.Tree, "Trees", "Nature", GeometryKind.Point, 0xFF6FA35A, Order: 70),
        new(Category.Poi, "Points of interest", "Places", GeometryKind.Point, 0xFF734A08, Order: 71),
        new(Category.TransitStop, "Stops & stations", "Transit", GeometryKind.Point, 0xFFFFFFFF, 0xFF000000, Order: 72),
        new(Category.Place, "Place names", "Places", GeometryKind.Point, 0xFF333333, Order: 73),
        new(Category.OtherPoint, "Other points", "Other", GeometryKind.Point, 0xFF909090, Order: 74),
    ];

    private static readonly Dictionary<Category, CategoryStyle> ByCategory = All.ToDictionary(s => s.Category);

    public static CategoryStyle Of(Category c) => ByCategory[c];

    public static bool IsRoad(Category c) => c is >= Category.Service and <= Category.Motorway;

    /// <summary>Colours the mod's ruleset uses for its non-standard transport-line ways.</summary>
    public static uint TransitColour(string? mode) => mode switch
    {
        "bus" => 0xFF00C8C8,
        "tram" => 0xFFE03030,
        "train" => 0xFFFF9900,
        "subway" => 0xFF8030A0,
        "ship" => 0xFF2040E0,
        "airplane" => 0xFF20A040,
        _ => 0xFF00C0C0,
    };
}
