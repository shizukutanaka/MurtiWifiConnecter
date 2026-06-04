using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MWC.App.Services;

namespace MWC.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _svc;

    // DisplayMode — RadioButton に IsSimple/IsExpert で双方向バインド
    [ObservableProperty] private bool   _isSimpleMode    = true;
    [ObservableProperty] private bool   _isExpertMode    = false;

    // ThemeIndex — ComboBox SelectedIndex に直接バインド
    [ObservableProperty] private int    _themeIndex      = 0;  // 0=Dark,1=Light,2=System

    [ObservableProperty] private string _language        = "ja";
    [ObservableProperty] private int    _autoScanInterval = 15;
    [ObservableProperty] private bool   _scanOnStartup   = true;
    [ObservableProperty] private bool   _showNotifications = true;

    // RadioButton 相互排他
    partial void OnIsSimpleModeChanged(bool v)  { if (v) _isExpertMode = false; OnPropertyChanged(nameof(IsExpertMode)); }
    partial void OnIsExpertModeChanged(bool v)  { if (v) _isSimpleMode = false; OnPropertyChanged(nameof(IsSimpleMode)); }

    // 公開プロパティ (SettingsService 向け変換)
    public AppTheme    Theme       => _themeIndex switch { 1 => AppTheme.Light, 2 => AppTheme.System, _ => AppTheme.Dark };
    public DisplayMode DisplayMode => _isExpertMode ? DisplayMode.Expert : DisplayMode.Simple;

    public IReadOnlyList<(string Code, string Label)> Languages { get; } = new[]
    {
        ("ja","日本語"), ("en","English"), ("zh-Hans","中文(简体)"),
        ("ko","한국어"), ("ar","العربية"), ("es","Español"),
        ("fr","Français"), ("de","Deutsch"), ("ru","Русский"), ("pt-BR","Português")
    };

    public IReadOnlyList<(int Secs, string Label)> ScanIntervals { get; } = new[]
    {
        (0,"手動のみ"),(10,"10秒"),(15,"15秒"),(30,"30秒"),(60,"1分"),(300,"5分")
    };

    public SettingsViewModel(SettingsService svc)
    {
        _svc = svc;
        Load();
    }

    private void Load()
    {
        var s = _svc.Current;
        _isSimpleMode     = s.DisplayMode == DisplayMode.Simple;
        _isExpertMode     = s.DisplayMode == DisplayMode.Expert;
        _themeIndex       = s.Theme switch { AppTheme.Light => 1, AppTheme.System => 2, _ => 0 };
        _language         = s.Language;
        _autoScanInterval = s.AutoScanIntervalSeconds;
        _scanOnStartup    = s.ScanOnStartup;
        _showNotifications = s.ShowConnectionNotifications;
        OnPropertyChanged(nameof(IsSimpleMode));
        OnPropertyChanged(nameof(IsExpertMode));
        OnPropertyChanged(nameof(ThemeIndex));
        OnPropertyChanged(nameof(Language));
    }

    [RelayCommand]
    public void Save()
    {
        _svc.Save(_svc.Current with
        {
            DisplayMode                 = DisplayMode,
            Theme                       = Theme,
            Language                    = Language,
            AutoScanIntervalSeconds     = AutoScanInterval,
            ScanOnStartup               = ScanOnStartup,
            ShowConnectionNotifications = ShowNotifications,
            HasCompletedFirstRun        = true,
        });
    }

    [RelayCommand]
    public void Reset()
    {
        _svc.Save(new AppSettings { HasCompletedFirstRun = true });
        Load();
    }
}
