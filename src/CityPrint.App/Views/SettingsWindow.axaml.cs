using Avalonia.Controls;
using Avalonia.Interactivity;
using CityPrint.App.Services;
using CityPrint.App.ViewModels;

namespace CityPrint.App.Views;

public partial class SettingsWindow : Window
{
    private AppState State => AppState.Current;

    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = AppState.Current;
        AppNameText.Text = $"{AppInfo.Name} {AppInfo.Version}";
        SyncFromSettings();
    }

    /// <summary>Controls that aren't bound to AppState read the settings directly.</summary>
    private void SyncFromSettings()
    {
        var mode = State.Settings.AppTheme;
        DarkRadio.IsChecked = mode == AppThemeMode.Dark;
        LightRadio.IsChecked = mode == AppThemeMode.Light;
        SystemRadio.IsChecked = mode == AppThemeMode.System;
        OpenLast.IsChecked = State.Settings.OpenLastFile;
    }

    private void OnAppTheme(object? sender, RoutedEventArgs e)
    {
        var mode = LightRadio.IsChecked == true ? AppThemeMode.Light : SystemRadio.IsChecked == true ? AppThemeMode.System : AppThemeMode.Dark;
        Appearance.Set(mode);
    }

    private void OnOpenLast(object? sender, RoutedEventArgs e)
    {
        State.Settings.OpenLastFile = OpenLast.IsChecked == true;
        State.Settings.Save();
    }

    private void OnReset(object? sender, RoutedEventArgs e)
    {
        State.ResetSettings();
        SyncFromSettings();
    }

    private void OnOpenRepo(object? sender, RoutedEventArgs e)
    {
        if (AppInfo.HasRepo) Shell.OpenUrl(AppInfo.RepoUrl);
    }

    private void OnClearRecent(object? sender, RoutedEventArgs e) => State.ClearRecent();
    private void OnOpenFolder(object? sender, RoutedEventArgs e) => Shell.OpenFolder(AppSettings.Folder);
    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
