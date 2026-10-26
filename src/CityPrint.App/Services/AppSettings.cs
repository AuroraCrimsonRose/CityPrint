using System.Text.Json;
using System.Text.Json.Serialization;

namespace CityPrint.App.Services;

public enum AppThemeMode { Dark, Light, System }

public sealed class PosterSettings
{
    public string Paper { get; set; } = "A3";
    public bool Landscape { get; set; }
    public int Dpi { get; set; } = 200;
    public string Layout { get; set; } = "Gallery";
    public string MapTheme { get; set; } = "paper";
    public bool Labels { get; set; }
    public bool ShowScale { get; set; } = true;
}

/// <summary>User preferences, stored as JSON in %APPDATA%\CityPrint\settings.json (or the platform's equivalent).</summary>
public sealed class AppSettings
{
    public AppThemeMode AppTheme { get; set; } = AppThemeMode.Dark;
    public string MapTheme { get; set; } = "classic";
    public bool ShowLabels { get; set; } = true;
    public bool RealWidths { get; set; } = true;
    public bool ShowInferred { get; set; } = true;
    public bool ZoningVisible { get; set; }
    public bool DistrictsVisible { get; set; }
    public bool ShowMapBounds { get; set; } = true;
    public bool ShowTileGrid { get; set; }
    public bool AutoReload { get; set; } = true;
    public bool OpenLastFile { get; set; } = true;
    public List<string> RecentFiles { get; set; } = new();
    public PosterSettings Poster { get; set; } = new();

    public const int MaxRecent = 10;

    public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CityPrint");
    public static string FilePath { get; } = Path.Combine(Folder, "settings.json");

    /// <summary>Where settings lived before the app was renamed from CXOSM; read until the first save.</summary>
    private static string LegacyFilePath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CXOSM", "settings.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        try
        {
            string path = File.Exists(FilePath) ? FilePath : LegacyFilePath;
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new();
        }
        catch (Exception)
        {
            // A corrupt settings file shouldn't stop the app; fall back to defaults.
        }
        return new();
    }

    /// <summary>Set for scripted runs (screenshots) so they never touch the user's settings.</summary>
    public static bool ReadOnly { get; set; }

    public void Save()
    {
        if (ReadOnly) return;
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void AddRecent(string path)
    {
        path = Path.GetFullPath(path);
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > MaxRecent) RecentFiles.RemoveRange(MaxRecent, RecentFiles.Count - MaxRecent);
    }
}
