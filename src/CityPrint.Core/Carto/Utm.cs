namespace CityPrint.Core.Carto;

/// <summary>
/// Forward Universal Transverse Mercator on WGS84, using Krüger's series (accurate to well under a
/// millimetre within a zone). Carto writes GeoJSON in longitude/latitude; projecting back to UTM
/// recovers the flat metres the game works in.
/// </summary>
public static class Utm
{
    private const double A = 6378137.0;
    private const double F = 1 / 298.257223563;
    private const double K0 = 0.9996;
    private const double FalseEasting = 500_000;

    private static readonly double N = F / (2 - F);
    private static readonly double Rect = A / (1 + N) * (1 + N * N / 4 + N * N * N * N / 64);
    private static readonly double[] Alpha =
    [
        N / 2 - 2 * N * N / 3 + 5 * N * N * N / 16,
        13 * N * N / 48 - 3 * N * N * N / 5,
        61 * N * N * N / 240,
    ];
    private static readonly double E = Math.Sqrt(F * (2 - F));

    public static int ZoneOf(double lon) => Math.Clamp((int)Math.Floor((lon + 180) / 6) + 1, 1, 60);

    /// <summary>UTM zone and hemisphere for an EPSG code (32601–32660 north, 32701–32760 south).</summary>
    public static (int Zone, bool North)? FromEpsg(int epsg) => epsg switch
    {
        >= 32601 and <= 32660 => (epsg - 32600, true),
        >= 32701 and <= 32760 => (epsg - 32700, false),
        _ => null,
    };

    public static (double Easting, double Northing) Forward(double lon, double lat, int zone, bool north)
    {
        double lon0 = (zone * 6 - 183) * Math.PI / 180;
        double phi = lat * Math.PI / 180, lam = lon * Math.PI / 180 - lon0;

        // Conformal latitude, then the transverse Mercator on the sphere, then Krüger's correction.
        double t = Math.Sinh(Atanh(Math.Sin(phi)) - E * Atanh(E * Math.Sin(phi)));
        double xi = Math.Atan2(t, Math.Cos(lam));
        double eta = Atanh(Math.Sin(lam) / Math.Sqrt(1 + t * t));

        double x = eta, y = xi;
        for (int j = 1; j <= 3; j++)
        {
            x += Alpha[j - 1] * Math.Cos(2 * j * xi) * Math.Sinh(2 * j * eta);
            y += Alpha[j - 1] * Math.Sin(2 * j * xi) * Math.Cosh(2 * j * eta);
        }
        return (FalseEasting + K0 * Rect * x, (north ? 0 : 10_000_000) + K0 * Rect * y);
    }

    private static double Atanh(double v) => 0.5 * Math.Log((1 + v) / (1 - v));
}
