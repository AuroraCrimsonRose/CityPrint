namespace CityPrint.Core.Map;

/// <summary>Per-category colours for a theme. A zero colour means "don't draw that part".</summary>
/// <param name="WidthScale">Multiplies the category's drawn width.</param>
/// <param name="Dashed">Overrides whether the category's dash pattern is used.</param>
public sealed record Paint(uint Fill, uint Stroke, uint Casing, double WidthScale = 1, bool? Dashed = null);

/// <summary>A complete colour scheme for the map renderer and the poster/SVG exporters.</summary>
public sealed class MapTheme
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public bool IsDark { get; init; }

    public uint Background { get; init; } = MapStyle.Background;
    public uint LabelText { get; init; } = 0xFF222222;
    public uint LabelHalo { get; init; } = 0xDCFFFFFF;
    public uint PlaceText { get; init; } = 0xFF333333;
    public uint StopText { get; init; } = 0xFF1A3A6A;
    public uint PoiText { get; init; } = 0xFF5A3A08;
    public uint StopFill { get; init; } = 0xFFFFFFFF;
    public uint StopStroke { get; init; } = 0xFF000000;
    public uint Frame { get; init; } = 0x90606060;
    public uint Grid { get; init; } = 0x40000000;

    /// <summary>Thin outlines around land-use lots when zoomed in.</summary>
    public bool AreaOutlines { get; init; } = true;

    /// <summary>Use the mod's per-mode colours (bus cyan, tram red…) for transport lines.</summary>
    public bool ModeColours { get; init; } = true;

    /// <summary>Multiplies stop marker size; transit-focused themes enlarge them.</summary>
    public double StopScale { get; init; } = 1;

    public IReadOnlyDictionary<Category, Paint> Overrides { get; init; } = new Dictionary<Category, Paint>();

    public Paint PaintOf(Category c)
    {
        if (Overrides.TryGetValue(c, out var p)) return p;
        var s = MapStyle.Of(c);
        return new Paint(s.Fill, s.Stroke, s.Casing);
    }

    public override string ToString() => Name;
}

public static class MapThemes
{
    /// <summary>The OSM Export mod's Classic Maperitive ruleset.</summary>
    public static readonly MapTheme Classic = new()
    {
        Id = "classic",
        Name = "Classic",
        Description = "The OSM Export mod's Maperitive style",
    };

    public static readonly MapTheme Midnight = new()
    {
        Id = "midnight",
        Name = "Midnight",
        Description = "Dark map with amber motorways",
        IsDark = true,
        Background = 0xFF171A20,
        LabelText = 0xFFD7DCE3,
        LabelHalo = 0xE0171A20,
        PlaceText = 0xFFEDEFF2,
        StopText = 0xFF9CC4F0,
        PoiText = 0xFFC9A46A,
        StopFill = 0xFF171A20,
        StopStroke = 0xFFE6E8EC,
        Frame = 0x905A606B,
        Grid = 0x20FFFFFF,
        Overrides = new Dictionary<Category, Paint>
        {
            [Category.Water] = new(0xFF0E2433, 0, 0),
            [Category.Farmland] = new(0xFF232B20, 0, 0),
            [Category.Forest] = new(0xFF17301F, 0, 0),
            [Category.Park] = new(0xFF1B3324, 0, 0),
            [Category.Residential] = new(0xFF252932, 0xFF2E333D, 0),
            [Category.Commercial] = new(0xFF33252C, 0xFF3E2C35, 0),
            [Category.Retail] = new(0xFF382A2E, 0xFF45323A, 0),
            [Category.Industrial] = new(0xFF2C2A33, 0xFF36333F, 0),
            [Category.Office] = new(0xFF222D44, 0xFF2B3852, 0),
            [Category.OtherLanduse] = new(0xFF24262B, 0xFF2C2F35, 0),
            [Category.Pedestrian] = new(0xFF2A2D33, 0xFF33373E, 0),
            [Category.Building] = new(0xFF34373F, 0xFF40444D, 0),
            [Category.Contour] = new(0, 0x30FFFFFF, 0),
            [Category.Waterway] = new(0, 0xFF1B3A4E, 0),
            [Category.Footway] = new(0, 0xFF7A625C, 0),
            [Category.Cycleway] = new(0, 0xFF4A6AC0, 0),
            [Category.Service] = new(0, 0xFF3A3F48, 0xFF22252B),
            [Category.Street] = new(0, 0xFF4A505B, 0xFF22252B),
            [Category.Tertiary] = new(0, 0xFF5E5A44, 0xFF2A2820),
            [Category.Secondary] = new(0, 0xFF8A6A3A, 0xFF3A2E1C),
            [Category.Primary] = new(0, 0xFF9A5A4A, 0xFF40261F),
            [Category.TrunkLink] = new(0, 0xFF3F7F6A, 0xFF1E3B32),
            [Category.Trunk] = new(0, 0xFF3F7F6A, 0xFF1E3B32),
            [Category.MotorwayLink] = new(0, 0xFFD08A2E, 0xFF5A3C14),
            [Category.Motorway] = new(0, 0xFFE09A35, 0xFF5A3C14),
            [Category.Platform] = new(0, 0xFF555B66, 0),
            [Category.Subway] = new(0, 0xFF7C6AA8, 0),
            [Category.Tram] = new(0, 0xFFB0B6C0, 0),
            [Category.LightRail] = new(0, 0xFF9AA0AA, 0),
            [Category.Rail] = new(0, 0xFF171A20, 0xFF8A93A3),
            [Category.Runway] = new(0, 0xFF3A3F48, 0),
            [Category.Taxiway] = new(0, 0xFF33373F, 0),
            [Category.Ferry] = new(0, 0xFF3F7FBF, 0),
            [Category.Power] = new(0, 0xFF4A4F58, 0),
            [Category.OtherLine] = new(0, 0xFF5A606B, 0),
            [Category.Tree] = new(0xFF2E5A3A, 0, 0),
            [Category.Poi] = new(0xFFC9A46A, 0, 0),
            [Category.OtherPoint] = new(0xFF6A707B, 0, 0),
        },
    };

    public static readonly MapTheme Blueprint = new()
    {
        Id = "blueprint",
        Name = "Blueprint",
        Description = "Outlines on drafting blue",
        IsDark = true,
        Background = 0xFF0F3A66,
        LabelText = 0xFFE8F2FC,
        LabelHalo = 0xE00F3A66,
        PlaceText = 0xFFFFFFFF,
        StopText = 0xFFE8F2FC,
        PoiText = 0xFFBFD7F0,
        StopFill = 0xFF0F3A66,
        StopStroke = 0xFFFFFFFF,
        Frame = 0xC0BFD7F0,
        Grid = 0x30FFFFFF,
        ModeColours = false,
        Overrides = BlueprintPaints(),
    };

    public static readonly MapTheme Paper = new()
    {
        Id = "paper",
        Name = "Paper",
        Description = "Minimal ink-on-cream poster style",
        Background = 0xFFF4EFE6,
        LabelText = 0xFF2A2A2A,
        LabelHalo = 0xE0F4EFE6,
        PlaceText = 0xFF2A2A2A,
        StopText = 0xFF2A2A2A,
        PoiText = 0xFF5A5A5A,
        StopFill = 0xFFF4EFE6,
        StopStroke = 0xFF2A2A2A,
        Frame = 0x802A2A2A,
        Grid = 0x202A2A2A,
        AreaOutlines = false,
        ModeColours = false,
        Overrides = PosterPaints(ink: 0xFF2A2A2A, water: 0xFF9DB9C4, park: 0xFFDCE3CC, building: 0xFFE2D9CB, accent: 0xFF2A2A2A, rail: 0xFF5A5A5A, ferry: 0xFF7A9AAA),
    };

    public static readonly MapTheme Noir = new()
    {
        Id = "noir",
        Name = "Noir",
        Description = "Light on black poster style with gold motorways",
        IsDark = true,
        Background = 0xFF0D0D0F,
        LabelText = 0xFFE8E6E1,
        LabelHalo = 0xE00D0D0F,
        PlaceText = 0xFFF2C14E,
        StopText = 0xFFE8E6E1,
        PoiText = 0xFFA8A6A1,
        StopFill = 0xFF0D0D0F,
        StopStroke = 0xFFE8E6E1,
        Frame = 0x80E8E6E1,
        Grid = 0x18FFFFFF,
        AreaOutlines = false,
        ModeColours = false,
        Overrides = PosterPaints(ink: 0xFFE8E6E1, water: 0xFF1D2430, park: 0xFF141A14, building: 0xFF1A1A1D, accent: 0xFFF2C14E, rail: 0xFF8A8A8A, ferry: 0xFF3A4A5E),
    };

    public static readonly MapTheme Transit = new()
    {
        Id = "transit",
        Name = "Transit",
        Description = "Muted base map with rail, tram, ferries and stops highlighted",
        Background = 0xFFF2F2F0,
        LabelText = 0xFF3A3A3A,
        PlaceText = 0xFF555555,
        StopText = 0xFF111111,
        StopFill = 0xFFFFFFFF,
        StopStroke = 0xFF222222,
        AreaOutlines = false,
        StopScale = 1.5,
        Overrides = new Dictionary<Category, Paint>
        {
            [Category.Water] = new(0xFFC9D9E0, 0, 0),
            [Category.Farmland] = new(0xFFE9E9E6, 0, 0),
            [Category.Forest] = new(0xFFDDE5D8, 0, 0),
            [Category.Park] = new(0xFFDDE5D8, 0, 0),
            [Category.Residential] = new(0xFFE6E6E3, 0, 0),
            [Category.Commercial] = new(0xFFE6E6E3, 0, 0),
            [Category.Retail] = new(0xFFE6E6E3, 0, 0),
            [Category.Industrial] = new(0xFFE6E6E3, 0, 0),
            [Category.Office] = new(0xFFE6E6E3, 0, 0),
            [Category.OtherLanduse] = new(0xFFE6E6E3, 0, 0),
            [Category.Pedestrian] = new(0xFFEDEDEA, 0, 0),
            [Category.Building] = new(0xFFDDDDDA, 0, 0),
            [Category.Contour] = new(0, 0, 0),
            [Category.Footway] = new(0, 0, 0),
            [Category.Cycleway] = new(0, 0, 0),
            [Category.Service] = new(0, 0xFFFFFFFF, 0xFFD8D8D4),
            [Category.Street] = new(0, 0xFFFFFFFF, 0xFFCFCFCB),
            [Category.Tertiary] = new(0, 0xFFFFFFFF, 0xFFCFCFCB),
            [Category.Secondary] = new(0, 0xFFFFFFFF, 0xFFC4C4C0),
            [Category.Primary] = new(0, 0xFFFFFFFF, 0xFFC4C4C0),
            [Category.TrunkLink] = new(0, 0xFFE4E4E0, 0xFFBFBFBA),
            [Category.Trunk] = new(0, 0xFFE4E4E0, 0xFFBFBFBA),
            [Category.MotorwayLink] = new(0, 0xFFD9D9D5, 0xFFBFBFBA),
            [Category.Motorway] = new(0, 0xFFD9D9D5, 0xFFBFBFBA),
            [Category.Platform] = new(0, 0xFF9A9A9A, 0),
            [Category.Rail] = new(0, 0xFFD23F31, 0, 1.6, Dashed: false),
            [Category.Tram] = new(0, 0xFFE8732C, 0, 2.2),
            [Category.Subway] = new(0, 0xFF7B3FA0, 0, 1.6, Dashed: false),
            [Category.LightRail] = new(0, 0xFF2E86C1, 0, 1.6),
            [Category.Ferry] = new(0, 0xFF1F6FB2, 0, 1.8),
            [Category.TransitLine] = new(0, 0xFF00A0A0, 0xFF000000, 1.4),
            [Category.Power] = new(0, 0, 0),
            [Category.Runway] = new(0, 0xFFD0D0D8, 0),
            [Category.Taxiway] = new(0, 0xFFD8D8DE, 0),
            [Category.Poi] = new(0xFFB0B0B0, 0, 0),
            [Category.Tree] = new(0, 0, 0),
        },
    };

    public static IReadOnlyList<MapTheme> All { get; } = [Classic, Midnight, Blueprint, Paper, Noir, Transit];

    public static MapTheme ById(string? id) => All.FirstOrDefault(t => t.Id == id) ?? Classic;

    private static Dictionary<Category, Paint> BlueprintPaints()
    {
        const uint line = 0xFFCFE3F7, faint = 0x806FA0D0, bright = 0xFFFFFFFF;
        var d = new Dictionary<Category, Paint>();
        foreach (var s in MapStyle.All)
        {
            d[s.Category] = s.Kind switch
            {
                GeometryKind.Area => new(0, faint, 0),
                GeometryKind.Point => new(line, 0, 0),
                _ => new(0, line, 0, 1, Dashed: s.Dash != null ? true : null),
            };
        }
        d[Category.Water] = new(0xFF0B2F55, 0xA06FA0D0, 0);
        d[Category.Building] = new(0x209CC2E8, 0xFF9CC2E8, 0);
        d[Category.Motorway] = new(0, bright, 0, 1.1);
        d[Category.MotorwayLink] = new(0, bright, 0);
        d[Category.Trunk] = new(0, bright, 0);
        d[Category.Rail] = new(0, bright, 0, 0.6, Dashed: true);
        d[Category.Power] = new(0, 0x806FA0D0, 0);
        d[Category.Contour] = new(0, 0x406FA0D0, 0);
        return d;
    }

    /// <summary>Single-ink road network for posters: roads differ only by width.</summary>
    private static Dictionary<Category, Paint> PosterPaints(uint ink, uint water, uint park, uint building, uint accent, uint rail, uint ferry)
    {
        var d = new Dictionary<Category, Paint>();
        foreach (var s in MapStyle.All)
        {
            d[s.Category] = s.Kind switch
            {
                GeometryKind.Area => new(0, 0, 0),
                GeometryKind.Point => new(0, 0, 0),
                _ => MapStyle.IsRoad(s.Category) ? new(0, ink, 0, 0.8) : new(0, 0, 0),
            };
        }
        d[Category.Water] = new(water, 0, 0);
        d[Category.Park] = new(park, 0, 0);
        d[Category.Forest] = new(park, 0, 0);
        d[Category.Building] = new(building, 0, 0);
        d[Category.Footway] = new(0, ink, 0, 0.6, Dashed: false);
        d[Category.Motorway] = new(0, accent, 0, 0.8);
        d[Category.MotorwayLink] = new(0, accent, 0, 0.8);
        d[Category.Rail] = new(0, rail, 0, 0.5, Dashed: true);
        d[Category.Tram] = new(0, rail, 0, 0.8);
        d[Category.Subway] = new(0, rail, 0, 0.6);
        d[Category.Ferry] = new(0, ferry, 0, 1, Dashed: true);
        d[Category.TransitLine] = new(0, rail, 0);
        d[Category.TransitStop] = new(0xFFFFFFFF, ink, 0);
        d[Category.Place] = new(ink, 0, 0);
        return d;
    }
}
