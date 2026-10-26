namespace CityPrint.Core.Osm;

/// <summary>
/// The OSM Export mod maps game metres to degrees with a flat 111,000 m/deg scale
/// around (0,0): a 14,336 m map spans roughly ±0.0646°. Inverting that gives
/// game-space metres directly, so no spherical projection is needed.
/// </summary>
public static class Projection
{
    public const double MetresPerDegree = 111_000.0;

    /// <summary>Side length of a standard CS2 map in metres.</summary>
    public const double MapSize = 14_336.0;

    /// <summary>CS2 maps are a 23×23 grid of purchasable tiles (529; the vanilla game unlocks 441).</summary>
    public const int TileCount = 23;

    public static double ToX(double lon) => lon * MetresPerDegree;
    public static double ToY(double lat) => lat * MetresPerDegree;
    public static double ToLon(double x) => x / MetresPerDegree;
    public static double ToLat(double y) => y / MetresPerDegree;
}
