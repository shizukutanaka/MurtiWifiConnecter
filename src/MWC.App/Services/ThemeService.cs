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
public sealed class ThemeService : IDisposable
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
            if (merged[i].Source?.ToString().Contains("/Themes/") == true)
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
        // このハンドラは OS の SystemEvents 専用スレッドで発火する。同期 Invoke で
        // UI スレッドの応答を待つと、(1) 共有 OS スレッドを不要に塞ぎ他アプリの
        // SystemEvents ハンドラを待たせ、(2) アプリ終了処理中に UI スレッドが
        // ブロックしていると相互待ちでデッドロックしうる。テーマ適用は戻り値不要の
        // fire-and-forget なので BeginInvoke でキューに積み、即座にスレッドを返す。
        if (e.Category == UserPreferenceCategory.General && _current == AppTheme.System)
            Application.Current?.Dispatcher.BeginInvoke(() => Apply(AppTheme.System));
    }

    public void Dispose()
        => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

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
