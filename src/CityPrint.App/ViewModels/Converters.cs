using Avalonia.Data.Converters;
using Avalonia.Media;
using CityPrint.Core.Map;

namespace CityPrint.App.ViewModels;

public static class Converters
{
    /// <summary>Dims layers that have no features in the loaded file.</summary>
    public static readonly IValueConverter CountToOpacity =
        new FuncValueConverter<int, double>(c => c > 0 ? 1.0 : 0.4);

    public static readonly IValueConverter Upper =
        new FuncValueConverter<string, string>(s => s?.ToUpperInvariant() ?? "");

    public static readonly IValueConverter ArgbToBrush =
        new FuncValueConverter<uint, IBrush>(c => new SolidColorBrush(Color.FromUInt32(c)));

    /// <summary>A theme's signature colour (its motorway stroke) for the theme picker chip.</summary>
    public static readonly IValueConverter ThemeAccent =
        new FuncValueConverter<MapTheme, IBrush>(t =>
        {
            if (t is null) return Brushes.Transparent;
            uint c = t.PaintOf(Category.Motorway).Stroke;
            if (c == 0 || c == t.Background) c = t.PaintOf(Category.Rail).Stroke;
            return new SolidColorBrush(Color.FromUInt32(c));
        });
}

public static class SettingsInfo
{
    public static string Path => CityPrint.App.Services.AppSettings.FilePath;
}
