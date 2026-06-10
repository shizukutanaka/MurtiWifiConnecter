using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MWC.App.Services;
using MWC.Core.Models;
using MWC.Core.Services;

namespace MWC.App.ViewModels;

/// <summary>
/// Apple HIG "Progressive Disclosure" + Spotlight検索:
///   - シンプルモード: SSID + 信号 + セキュリティバッジ + 接続ボタン
///   - 詳細モード: PHY / ベンダー / チャンネル / BSSID も表示
///   - 検索: タイプするだけでフィルタ
/// </summary>
public sealed partial class NetworkFilterViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly AdapterPreferencesService _adapterPrefs;
    private IReadOnlyList<NetworkItemViewModel> _source = Array.Empty<NetworkItemViewModel>();
    private Guid? _currentAdapterId;
    private bool _loadingPreset;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool   _isExpertMode;
    [ObservableProperty] private bool   _showSecuredOnly;
    [ObservableProperty] private bool   _showFavoritesFirst = true;

    public ObservableCollection<NetworkItemViewModel> Filtered { get; } = new();

    public NetworkFilterViewModel(SettingsService settings, AdapterPreferencesService adapterPrefs)
    {
        _settings     = settings;
        _adapterPrefs = adapterPrefs;
        IsExpertMode  = settings.Current.DisplayMode == DisplayMode.Expert;
    }

    /// <summary>アダプター切替時に呼び出し。アダプター固有のフィルタ設定を復元する。</summary>
    public void SetAdapter(Guid? adapterId)
    {
        _currentAdapterId = adapterId;
        if (!adapterId.HasValue) return;
        var prefs = _adapterPrefs.Get(adapterId.Value);
        _loadingPreset = true;
        ShowSecuredOnly    = prefs.ShowSecuredOnly;
        ShowFavoritesFirst = prefs.ShowFavoritesFirst;
        _loadingPreset = false;
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value)     => ApplyFilter();
    partial void OnShowSecuredOnlyChanged(bool value)  { SaveFilterPreset(); ApplyFilter(); }
    partial void OnShowFavoritesFirstChanged(bool value) { SaveFilterPreset(); ApplyFilter(); }

    private void SaveFilterPreset()
    {
        if (_loadingPreset || !_currentAdapterId.HasValue) return;
        _adapterPrefs.SetFilterPreset(_currentAdapterId.Value, ShowSecuredOnly, ShowFavoritesFirst);
    }

    public void SetSource(IReadOnlyList<NetworkItemViewModel> networks)
    {
        _source = networks;
        ApplyFilter();
    }

    [RelayCommand]
    public void ClearSearch() => SearchText = "";

    [RelayCommand]
    public void ToggleExpertMode()
    {
        IsExpertMode = !IsExpertMode;
        _settings.Save(_settings.Current with
        {
            DisplayMode = IsExpertMode ? DisplayMode.Expert : DisplayMode.Simple
        });
    }

    private void ApplyFilter()
    {
        var q = SearchText.Trim();

        var pinned  = _settings.Current.PinnedNetworks;
        var hidden  = _settings.Current.HiddenNetworks;

        var result = _source
            .Where(n => !hidden.Contains(n.Ssid))
            .Where(n => string.IsNullOrEmpty(q)
                        || n.Ssid.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || n.VendorLabel.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Where(n => !ShowSecuredOnly || n.Auth is not (AuthMethod.Open or AuthMethod.WEP))
            .OrderByDescending(n => n.IsConnected)
            .ThenByDescending(n => ShowFavoritesFirst && pinned.Contains(n.Ssid))
            .ThenByDescending(n => n.Signal)
            .ToList();

        // 差分更新
        for (int i = Filtered.Count - 1; i >= 0; i--)
            if (!result.Any(r => r.Ssid == Filtered[i].Ssid)) Filtered.RemoveAt(i);
        for (int i = 0; i < result.Count; i++)
        {
            var item = result[i];
            var existing = Filtered.FirstOrDefault(f => f.Ssid == item.Ssid);
            if (existing is null)
                Filtered.Insert(Math.Min(i, Filtered.Count), item);
        }
    }
}
