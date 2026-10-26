using System.Diagnostics;
using Avalonia;
using Avalonia.Styling;
using CityPrint.App.ViewModels;

namespace CityPrint.App.Services;

/// <summary>Applies and persists the app's light/dark mode.</summary>
public static class Appearance
{
    public static event Action? Changed;

    public static void Apply(AppThemeMode mode)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = mode switch
            {
                AppThemeMode.Light => ThemeVariant.Light,
                AppThemeMode.System => ThemeVariant.Default,
                _ => ThemeVariant.Dark,
            };
        Changed?.Invoke();
    }

    public static void Set(AppThemeMode mode)
    {
        AppState.Current.Settings.AppTheme = mode;
        AppState.Current.Settings.Save();
        Apply(mode);
    }

    public static bool IsDark => Application.Current?.ActualThemeVariant != ThemeVariant.Light;
}

public static class Shell
{
    public static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });

    public static void OpenFolder(string path)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }
}
