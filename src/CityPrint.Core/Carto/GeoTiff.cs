using System.Buffers.Binary;
using System.Text;

namespace CityPrint.Core.Carto;

/// <summary>
/// A single-band GeoTIFF raster as Carto writes it: uncompressed, strip-based, 16-bit integer or
/// 32-bit float samples, georeferenced with a tie point and pixel scale in a projected (UTM) CRS.
/// </summary>
public sealed class GeoTiff
{
    public int Width { get; private init; }
    public int Height { get; private init; }

    /// <summary>Row-major samples, top row first.</summary>
    public float[] Values { get; private init; } = [];

    /// <summary>Projected coordinates of the raster's outer top-left corner, and the size of one pixel.</summary>
    public double Left { get; private init; }
    public double Top { get; private init; }
    public double PixelWidth { get; private init; }
    public double PixelHeight { get; private init; }
    public double Right => Left + Width * PixelWidth;
    public double Bottom => Top - Height * PixelHeight;

    /// <summary>The projected CRS from the GeoKey directory (e.g. 32631 for UTM zone 31N), if present.</summary>
    public int? Epsg { get; private init; }
    public float? NoData { get; private init; }

    public bool IsNoData(float v) => float.IsNaN(v) || NoData is { } nd && v == nd;

    private const ushort TagWidth = 256, TagHeight = 257, TagBits = 258, TagCompression = 259,
        TagStripOffsets = 273, TagSamplesPerPixel = 277, TagStripByteCounts = 279, TagSampleFormat = 339,
        TagPixelScale = 33550, TagTiePoint = 33922, TagGeoKeys = 34735, TagNoData = 42113;

    private const ushort GeoKeyProjectedCrs = 3072;

    public static GeoTiff Load(string path) => Read(File.ReadAllBytes(path));

    public static GeoTiff Read(byte[] data)
    {
        bool little = data[0] == 'I' && data[1] == 'I';
        if (!little && !(data[0] == 'M' && data[1] == 'M')) throw new InvalidDataException("Not a TIFF file.");
        var tiff = new Reader(data, little);
        if (tiff.U16(2) != 42) throw new InvalidDataException("BigTIFF and other TIFF variants are not supported.");

        var tags = new Dictionary<ushort, (ushort Type, int Count, int Offset)>();
        int ifd = (int)tiff.U32(4);
        int entries = tiff.U16(ifd);
        for (int i = 0; i < entries; i++)
        {
            int e = ifd + 2 + i * 12;
            ushort type = tiff.U16(e + 2);
            int count = (int)tiff.U32(e + 4);
            // Values that fit in four bytes are stored in the entry itself.
            int size = TypeSize(type) * count;
            tags[tiff.U16(e)] = (type, count, size <= 4 ? e + 8 : (int)tiff.U32(e + 8));
        }

        long[] Numbers(ushort tag)
        {
            if (!tags.TryGetValue(tag, out var t)) return [];
            var r = new long[t.Count];
            for (int i = 0; i < t.Count; i++)
                r[i] = t.Type == 3 ? tiff.U16(t.Offset + i * 2) : tiff.U32(t.Offset + i * 4);
            return r;
        }
        double[] Doubles(ushort tag)
        {
            if (!tags.TryGetValue(tag, out var t)) return [];
            var r = new double[t.Count];
            for (int i = 0; i < t.Count; i++) r[i] = tiff.F64(t.Offset + i * 8);
            return r;
        }
        long One(ushort tag, long fallback) => Numbers(tag) is [var v, ..] ? v : fallback;

        int width = (int)One(TagWidth, 0), height = (int)One(TagHeight, 0);
        int bits = (int)One(TagBits, 8), format = (int)One(TagSampleFormat, 1);
        if (One(TagCompression, 1) != 1) throw new InvalidDataException("Compressed GeoTIFFs are not supported.");
        if (One(TagSamplesPerPixel, 1) != 1) throw new InvalidDataException("Only single-band GeoTIFFs are supported.");
        if (!tags.ContainsKey(TagStripOffsets)) throw new InvalidDataException("Tiled GeoTIFFs are not supported.");

        // Strips are stored in order, so their samples can be read as one sequence.
        var values = new float[width * height];
        int bytesPerSample = bits / 8, index = 0;
        var offsets = Numbers(TagStripOffsets);
        var counts = Numbers(TagStripByteCounts);
        for (int s = 0; s < offsets.Length && index < values.Length; s++)
        {
            int start = (int)offsets[s], end = start + (int)counts[s];
            for (int p = start; p + bytesPerSample <= end && index < values.Length; p += bytesPerSample)
                values[index++] = (bits, format) switch
                {
                    (32, 3) => tiff.F32(p),
                    (64, 3) => (float)tiff.F64(p),
                    (16, 2) => (short)tiff.U16(p),
                    (16, _) => tiff.U16(p),
                    (32, 2) => (int)tiff.U32(p),
                    (32, _) => tiff.U32(p),
                    (8, _) => data[p],
                    _ => throw new InvalidDataException($"Unsupported sample type ({bits}-bit, format {format})."),
                };
        }

        var scale = Doubles(TagPixelScale);
        var tie = Doubles(TagTiePoint);
        if (scale.Length < 2 || tie.Length < 6) throw new InvalidDataException("The GeoTIFF has no georeferencing.");

        int? epsg = null;
        var keys = Numbers(TagGeoKeys);
        for (int k = 4; k + 3 < keys.Length; k += 4)
            if (keys[k] == GeoKeyProjectedCrs && keys[k + 1] == 0) epsg = (int)keys[k + 3];

        float? noData = null;
        if (tags.TryGetValue(TagNoData, out var nd))
        {
            var text = Encoding.ASCII.GetString(data, nd.Offset, nd.Count).TrimEnd('\0', ' ');
            if (float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) noData = v;
        }

        return new GeoTiff
        {
            Width = width,
            Height = height,
            Values = values,
            PixelWidth = scale[0],
            PixelHeight = scale[1],
            Left = tie[3] - tie[0] * scale[0],
            Top = tie[4] + tie[1] * scale[1],
            Epsg = epsg,
            NoData = noData,
        };
    }

    private static int TypeSize(ushort type) => type switch
    {
        1 or 2 or 6 or 7 => 1,
        3 or 8 => 2,
        4 or 9 or 11 => 4,
        5 or 10 or 12 => 8,
        _ => 1,
    };

    private readonly struct Reader(byte[] data, bool little)
    {
        public ushort U16(int o) => little ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(o)) : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(o));
        public uint U32(int o) => little ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(o)) : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(o));
        public float F32(int o) => little ? BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(o)) : BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(o));
        public double F64(int o) => little ? BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(o)) : BinaryPrimitives.ReadDoubleBigEndian(data.AsSpan(o));
    }
}
