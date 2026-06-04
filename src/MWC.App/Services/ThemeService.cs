using System;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using MWC.App.Services;

namespace MWC.App.Services;

/// <summary>
/// ダーク/ライト/システム テーマ切替。
/// ResourceDictionary を差し替えることで全 Window に即時反映。
/// </summary>
public sealed class ThemeService
{
    private const string DarkUri        = "/MWC.App;component/Themes/Dark.xaml";
    private const string LightUri       = "/MWC.App;component/Themes/Light.xaml";
    private const string FluentUri      = "/MWC.App;component/Themes/Fluent.xaml";
    private const string SolarizedUri   = "/MWC.App;component/Themes/Solarized.xaml";
    private const string NordUri        = "/MWC.App;component/Themes/Nord.xaml";
    private const string CatppuccinUri  = "/MWC.App;component/Themes/Catppuccin.xaml";
    private const string RegPath  =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly SettingsService       _settings;
    private readonly ILogger<ThemeService> _log;
    private          AppTheme              _current;

    public event Action<AppTheme>? ThemeChanged;

    public ThemeService(SettingsService settings, ILogger<ThemeService> log)
    {
        _settings = settings;
        _log      = log;
        _current  = settings.Current.Theme;
    }

    public void Apply(AppTheme theme)
    {
        _current = theme;
        bool dark = theme switch
        {
            AppTheme.Dark       => true,
            AppTheme.Light      => false,
            AppTheme.Solarized  => true,
            AppTheme.Nord       => true,
            AppTheme.Catppuccin => true,
            AppTheme.Fluent     => IsWindowsDarkMode(),
            AppTheme.System     => IsWindowsDarkMode(),
            _               => true
        };

        var uri = _current switch
        {
            AppTheme.Fluent     => FluentUri,
            AppTheme.Solarized  => SolarizedUri,
            AppTheme.Nord       => NordUri,
            AppTheme.Catppuccin => CatppuccinUri,
            _                   => dark ? DarkUri : LightUri,
        };
        var newDict = new ResourceDictionary
            { Source = new Uri(uri, UriKind.Relative) };

        var merged = Application.Current.Resources.MergedDictionaries;
        for (int i = merged.Count - 1; i >= 0; i--)
        {
            var src = merged[i].Source?.ToString() ?? "";
            if (src.Contains("/Themes/Dark") || src.Contains("/Themes/Light"))
                merged.RemoveAt(i);
        }
        merged.Add(newDict);

        ThemeChanged?.Invoke(_current);
        _log.LogDebug("Theme: {theme} dark={dark}", theme, dark);
    }

    /// <summary>Windows テーマ変更イベント購読開始</summary>
    public void StartSystemWatcher()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General && _current == AppTheme.System)
            Application.Current?.Dispatcher.Invoke(() => Apply(AppTheme.System));
    }

    private static bool IsWindowsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegPath);
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return true; }
    }
}
