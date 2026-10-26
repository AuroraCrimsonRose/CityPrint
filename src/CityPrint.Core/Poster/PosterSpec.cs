using CityPrint.Core.Map;

namespace CityPrint.Core.Poster;

public enum PosterLayoutStyle { Gallery, FullBleed, Minimal }

public sealed record PaperSize(string Name, double WidthMm, double HeightMm)
{
    public override string ToString() => Name;

    public static IReadOnlyList<PaperSize> All { get; } =
    [
        new("A4", 210, 297),
        new("A3", 297, 420),
        new("A2", 420, 594),
        new("A1", 594, 841),
        new("Letter", 215.9, 279.4),
        new("Tabloid", 279.4, 431.8),
        new("18 × 24 in", 457.2, 609.6),
        new("24 × 36 in", 609.6, 914.4),
        new("Square 50 cm", 500, 500),
        // Screen sizes: at 96 DPI these come out at the named pixel size.
        new("Desktop 1920 × 1080", 508, 285.75),
        new("Desktop 3840 × 2160", 1016, 571.5),
        new("Phone 1170 × 2532", 309.56, 669.93),
    ];

    public static PaperSize ByName(string? name) => All.FirstOrDefault(p => p.Name == name) ?? All[1];
}

public readonly record struct Box(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
    public double CentreX => X + W / 2;
}

/// <summary>Everything that defines a poster. Page units are 1/96 inch so the screen preview,
/// PNG (scaled by DPI) and SVG all share one layout.</summary>
public sealed class PosterSpec
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Caption { get; set; } = "";
    public MapTheme Theme { get; set; } = MapThemes.Paper;
    public PaperSize Paper { get; set; } = PaperSize.All[1];
    public bool Landscape { get; set; }
    public PosterLayoutStyle Style { get; set; } = PosterLayoutStyle.Gallery;
    public bool Labels { get; set; }
    public double LabelScale { get; set; } = 1;
    public bool ScaleBar { get; set; } = true;
    public bool Legend { get; set; }
    public bool RealWidths { get; set; } = true;
    public int Dpi { get; set; } = 200;

    /// <summary>World centre of the map area, in game metres.</summary>
    public double CentreX { get; set; }
    public double CentreY { get; set; }

    /// <summary>World metres shown across the map area's width.</summary>
    public double SpanMetres { get; set; } = 6000;

    public const double UnitsPerMm = 96 / 25.4;

    public double PageWidth => (Landscape ? Math.Max(Paper.WidthMm, Paper.HeightMm) : Math.Min(Paper.WidthMm, Paper.HeightMm)) * UnitsPerMm;
    public double PageHeight => (Landscape ? Math.Min(Paper.WidthMm, Paper.HeightMm) : Math.Max(Paper.WidthMm, Paper.HeightMm)) * UnitsPerMm;

    public (int Width, int Height) PixelSize => ((int)Math.Round(PageWidth * Dpi / 96), (int)Math.Round(PageHeight * Dpi / 96));
}

/// <summary>Where each poster element goes, computed once for every renderer.</summary>
public sealed class PosterLayout
{
    public required Box Page { get; init; }
    public required Box Map { get; init; }

    /// <summary>Thin frame drawn around the map (Gallery only).</summary>
    public bool Frame { get; init; }

    /// <summary>A background-coloured fade over the bottom of the map that the text sits on.</summary>
    public Box? Fade { get; init; }

    public required double TitleSize { get; init; }
    public required double TitleY { get; init; }
    public required double SubtitleSize { get; init; }
    public required double SubtitleY { get; init; }
    public required double CaptionSize { get; init; }
    public required double CaptionY { get; init; }

    /// <summary>Text alignment anchor: 0 = left at TextX, 0.5 = centred on TextX.</summary>
    public required double TextAlign { get; init; }
    public required double TextX { get; init; }
    public required double LetterSpacing { get; init; }
    public required bool Uppercase { get; init; }

    public required Box LegendBox { get; init; }
    public required double Margin { get; init; }

    /// <summary>World-to-page scale for the map area.</summary>
    public double Scale(PosterSpec s) => Map.W / Math.Max(1, s.SpanMetres);

    public static PosterLayout Compute(PosterSpec s)
    {
        double w = s.PageWidth, h = s.PageHeight, shortSide = Math.Min(w, h);
        double m = shortSide * 0.06;
        var page = new Box(0, 0, w, h);

        switch (s.Style)
        {
            case PosterLayoutStyle.FullBleed:
            {
                double band = h * (s.Landscape ? 0.26 : 0.22);
                double title = Math.Min(w * 0.08, band * 0.34);
                // Stack upwards from the bottom margin: caption, subtitle, title (Y values are line tops).
                double captionY = h - m * 0.9 - title * 0.2 * 1.3;
                double subtitleY = captionY - title * 0.3 * 1.6;
                double titleY = subtitleY - title * 1.3;
                return new PosterLayout
                {
                    Page = page,
                    Map = page,
                    Fade = new Box(0, h - band * 1.4, w, band * 1.4),
                    TitleSize = title,
                    TitleY = titleY,
                    SubtitleSize = title * 0.3,
                    SubtitleY = subtitleY,
                    CaptionSize = title * 0.2,
                    CaptionY = captionY,
                    TextAlign = 0.5,
                    TextX = w / 2,
                    LetterSpacing = title * 0.22,
                    Uppercase = true,
                    LegendBox = new Box(m, m, w * 0.3, h * 0.4),
                    Margin = m,
                };
            }
            case PosterLayoutStyle.Minimal:
            {
                double bottom = m * 2.4;
                var map = new Box(m, m, w - 2 * m, h - m - bottom);
                // Title, subtitle and caption must fit the bottom margin: ~2.15 title heights in total.
                double title = Math.Min(bottom * 0.36, w * 0.05);
                double top = map.Bottom + bottom * 0.14;
                return new PosterLayout
                {
                    Page = page,
                    Map = map,
                    TitleSize = title,
                    TitleY = top,
                    SubtitleSize = title * 0.42,
                    SubtitleY = top + title * 1.2,
                    CaptionSize = title * 0.32,
                    CaptionY = top + title * 1.2 + title * 0.42 * 1.35,
                    TextAlign = 0,
                    TextX = m,
                    LetterSpacing = title * 0.04,
                    Uppercase = false,
                    LegendBox = new Box(map.X + m * 0.4, map.Y + m * 0.4, w * 0.28, map.H * 0.5),
                    Margin = m,
                };
            }
            default:
            {
                double band = h * (s.Landscape ? 0.24 : 0.19);
                var map = new Box(m, m, w - 2 * m, h - m - band);
                double title = Math.Min(w * 0.075, band * 0.36);
                double top = map.Bottom + band * 0.2;
                return new PosterLayout
                {
                    Page = page,
                    Map = map,
                    Frame = true,
                    TitleSize = title,
                    TitleY = top,
                    SubtitleSize = title * 0.3,
                    SubtitleY = top + title * 1.35,
                    CaptionSize = title * 0.22,
                    CaptionY = top + title * 1.35 + title * 0.5,
                    TextAlign = 0.5,
                    TextX = w / 2,
                    LetterSpacing = title * 0.25,
                    Uppercase = true,
                    LegendBox = new Box(map.X + m * 0.4, map.Y + m * 0.4, w * 0.28, map.H * 0.5),
                    Margin = m,
                };
            }
        }
    }
}

/// <summary>A transit line for poster legends.</summary>
public sealed record LegendEntry(string Label, uint Colour);

public static class PosterContent
{
    /// <summary>Distinct named transport lines with their colours, as drawn.</summary>
    public static List<LegendEntry> Legend(MapModel model, MapTheme theme)
    {
        var entries = new List<LegendEntry>();
        foreach (var g in model.Features.Where(f => f.Category == Category.TransitLine && f.Name != null).GroupBy(f => f.Name!))
        {
            var f = g.First();
            uint c = theme.ModeColours && f.Colour is { } col and not 0 ? col : theme.PaintOf(Category.TransitLine).Stroke;
            if (c == 0) continue;
            string label = f.Element.Tag("osm_export_route_ref") is { } r && r != g.Key ? $"{r}  {g.Key}" : g.Key;
            entries.Add(new LegendEntry(label, c));
        }
        foreach (var (cat, name) in new[] { (Category.Rail, "Rail"), (Category.Tram, "Tram"), (Category.Subway, "Subway"), (Category.Ferry, "Ferry") })
        {
            if (!model.CountByCategory.TryGetValue(cat, out int n) || n == 0) continue;
            uint c = theme.PaintOf(cat).Stroke;
            if (c != 0 && c != theme.Background) entries.Add(new LegendEntry(name, c));
        }
        return entries.OrderBy(e => e.Label, StringComparer.CurrentCultureIgnoreCase).Take(16).ToList();
    }

    /// <summary>"Middle_Shore.osm" → "Middle Shore".</summary>
    public static string CityName(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "My City";
        var name = Path.GetFileNameWithoutExtension(path).Replace('_', ' ').Replace('-', ' ').Trim();
        return name.Equals("export", StringComparison.OrdinalIgnoreCase) || name.Length == 0 ? "My City" : name;
    }

    public static string DefaultCaption(MapModel model)
    {
        double w = model.MaxX - model.MinX, h = model.MaxY - model.MinY;
        double roads = model.Features.Where(f => MapStyle.IsRoad(f.Category)).Sum(f => f.LengthMetres);
        return $"{MapStats.FormatLength(w)} × {MapStats.FormatLength(h)}  ·  {MapStats.FormatLength(roads)} of road".ToUpperInvariant();
    }
}
