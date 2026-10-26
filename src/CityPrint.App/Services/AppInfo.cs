namespace CityPrint.App.Services;

/// <summary>Who made the app and where it lives.</summary>
public static class AppInfo
{
    public const string Name = "City Print";
    public const string Author = "Aurora Tejeda";
    public const string Credit = "Made with ♥ by " + Author;

    /// <summary>Source repository; the "Go to repository" button is disabled while this is empty.</summary>
    public const string RepoUrl = "https://github.com/AuroraCrimsonRose/CityPrint";

    public static bool HasRepo => RepoUrl.Length > 0;

    public static string Version => typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "";
}
