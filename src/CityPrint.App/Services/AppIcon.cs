using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace CityPrint.App.Services;

/// <summary>The app icon (amber tile with the Lucide map glyph), drawn at runtime so it needs no image assets.</summary>
public static class AppIcon
{
    public static RenderTargetBitmap Render(int size)
    {
        var bmp = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using var ctx = bmp.CreateDrawingContext();
        double k = size / 24.0;
        var tile = new Rect(0.5 * k, 0.5 * k, 23 * k, 23 * k);
        ctx.DrawRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#F6B456"), 0), new GradientStop(Color.Parse("#E08A2A"), 1) },
        }, null, tile, 5.5 * k, 5.5 * k);

        if (Application.Current?.FindResource("Icon.Map") is Geometry glyph)
        {
            // Lucide's 24-unit glyph, shrunk to sit inside the tile.
            using (ctx.PushTransform(Matrix.CreateScale(0.66, 0.66) * Matrix.CreateTranslation(4.1, 4.1) * Matrix.CreateScale(k, k)))
                ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Color.Parse("#1A1206")), 2.4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), glyph);
        }
        return bmp;
    }

    public static WindowIcon Create() => new(Render(64));

    /// <summary>Writes a multi-size .ico with PNG-compressed entries (supported since Windows Vista).</summary>
    public static void SaveIco(string path)
    {
        int[] sizes = [16, 24, 32, 48, 64, 128, 256];
        var pngs = sizes.Select(Png).ToList();

        using var w = new BinaryWriter(File.Create(path));
        w.Write((short)0);
        w.Write((short)1);
        w.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((short)1);
            w.Write((short)32);
            w.Write(pngs[i].Length);
            w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var p in pngs) w.Write(p);
    }

    /// <summary>Writes a macOS .icns with PNG entries. The format is big-endian: "icns", total length,
    /// then (type, entry length including its 8-byte header, data) for each size.</summary>
    public static void SaveIcns(string path)
    {
        (string Type, int Size)[] entries = [("ic11", 32), ("ic12", 64), ("ic07", 128), ("ic08", 256), ("ic09", 512), ("ic10", 1024)];
        var pngs = entries.Select(e => Png(e.Size)).ToList();

        using var w = new BinaryWriter(File.Create(path));
        w.Write("icns"u8);
        WriteBigEndian(w, 8 + pngs.Sum(p => 8 + p.Length));
        for (int i = 0; i < entries.Length; i++)
        {
            w.Write(System.Text.Encoding.ASCII.GetBytes(entries[i].Type));
            WriteBigEndian(w, 8 + pngs[i].Length);
            w.Write(pngs[i]);
        }
    }

    private static byte[] Png(int size)
    {
        using var bmp = Render(size);
        using var ms = new MemoryStream();
        bmp.Save(ms, PngBitmapEncoderOptions.Default);
        return ms.ToArray();
    }

    private static void WriteBigEndian(BinaryWriter w, int value)
    {
        Span<byte> b = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(b, value);
        w.Write(b);
    }
}
