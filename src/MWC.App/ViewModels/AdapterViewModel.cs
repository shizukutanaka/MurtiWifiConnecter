using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MWC.Core.Abstractions;
using MWC.Core.Models;
using MWC.Core.Profile;
using MWC.Core.Services;

namespace MWC.App.ViewModels;

public sealed partial class AdapterViewModel : ObservableObject
{
    private readonly WifiAdapter           _adapter;
    private readonly IWifiService          _wifi;
    private readonly SignalHistoryService  _history;
    private readonly OuiLookupService      _oui;
    private readonly ILogger               _log;

    public Guid   Id   => _adapter.Id;
    public string Name => _adapter.Name;
    public string Description => _adapter.Description;

    /// <summary>1-9 のキーボードショートカット番号(Ctrl+1〜9)</summary>
    public int Index { get; set; } = 1;

    /// <summary>接続中ネットワークの信号強度(タブインジケータ用)</summary>
    public int CurrentSignal => Networks.FirstOrDefault(n => n.IsConnected)?.Signal ?? 0;

    /// <summary>接続状態の人間語ラベル</summary>
    public string ConnectionStatusLabel =>
        ConnectedSsid is null ? MWC.App.Resources.L.LabelNotConnected : $"→ {ConnectedSsid}";

    /// <summary>UI表示用 NetworkItemViewModel 一覧</summary>
    public ObservableCollection<NetworkItemViewModel> Networks { get; } = new();

    /// <summary>ChannelBandCanvas / ExportService 用の元 WifiNetwork 一覧</summary>
    public IReadOnlyList<WifiNetwork> SourceNetworks { get; private set; } =
        Array.Empty<WifiNetwork>();

    [ObservableProperty] private NetworkItemViewModel?    _selected;
    [ObservableProperty] private string?                  _connectedSsid;
    [ObservableProperty] private bool                     _isScanning;
    [ObservableProperty] private NetworkDetailViewModel   _detail = new();

    /// <summary>選択中 SSID の信号履歴サンプル (SignalHistoryCanvas用)</summary>
    public IReadOnlyList<MWC.Core.Services.SignalSample> SelectedHistory
        => _selected is null
            ? Array.Empty<MWC.Core.Services.SignalSample>()
            : _history.GetHistory(_selected.Ssid);

    partial void OnSelectedChanged(NetworkItemViewModel? v)
    {
        Detail.Load(v?.Source);
        OnPropertyChanged(nameof(SelectedHistory));
    }

    public AdapterPreferences Preferences { get; private set; }
    public AdapterPreferencesService PrefsService { get; }
    public string DisplayName => Preferences.CustomLabel ?? Name;
    public bool IsEnabled => Preferences.IsEnabled;
    public BandPreference PreferredBand => Preferences.PreferredBand;
    public IReadOnlyList<string> PinnedSsids => Preferences.PinnedSsids;

    private readonly ConnectionExecutor _executor;

    public AdapterViewModel(WifiAdapter a, IWifiService w,
        SignalHistoryService h, OuiLookupService oui, ILogger l,
        AdapterPreferencesService prefs, ConnectionExecutor executor)
    {
        _adapter  = a;
        _wifi     = w;
        _history  = h;
        _oui      = oui;
        _log      = l;
        PrefsService = prefs ?? throw new ArgumentNullException(nameof(prefs));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        Preferences = prefs.Get(a.Id);
        ConnectedSsid = a.ConnectedSsid;
    }

    public async Task RefreshAsync()
    {
        IsScanning = true;
        try
        {
            var nets = await _wifi.ScanAsync(_adapter.Id);

            // OUI解決 + 信号履歴記録
            _history.Record(nets);
            var enriched = nets
                .Select(n =>
                {
                    var vendor = n.BssEntries.Count > 0 ? _oui.Lookup(n.BssEntries[0].Bssid) : null;
                    return vendor is null ? n : n with { VendorName = vendor };
                })
                .ToList();

            // 子機の好みバンドフィルタを適用 (5GHz 専用ドングル等)
            SourceNetworks = ApplyBandFilter(enriched);

            // UI差分更新(スクロール位置維持)
            var byKey = enriched.ToDictionary(n => n.Ssid);
            for (int i = Networks.Count - 1; i >= 0; i--)
                if (!byKey.ContainsKey(Networks[i].Ssid)) Networks.RemoveAt(i);
            foreach (var n in enriched)
            {
                var ex = Networks.FirstOrDefault(x => x.Ssid == n.Ssid);
                if (ex is null) Networks.Add(new NetworkItemViewModel(n));
                else            ex.Update(n);
            }

            ConnectedSsid = enriched.FirstOrDefault(n => n.IsConnected)?.Ssid;
            OnPropertyChanged(nameof(CurrentSignal));
            OnPropertyChanged(nameof(ConnectionStatusLabel));

            // 選択中の詳細・履歴を最新化
            if (_selected is not null && byKey.TryGetValue(_selected.Ssid, out var upd))
                _selected.Update(upd);
            Detail.Load(_selected?.Source);
            OnPropertyChanged(nameof(SelectedHistory));
            OnPropertyChanged(nameof(SourceNetworks));
        }
        finally { IsScanning = false; }
    }

    /// <summary>このアダプターのバンド設定に応じてフィルタリング</summary>
    private IReadOnlyList<WifiNetwork> ApplyBandFilter(IReadOnlyList<WifiNetwork> all)
    {
        if (PreferredBand == BandPreference.Any) return all;
        var target = PreferredBand switch
        {
            BandPreference.Only2_4GHz => WifiBand.Band2_4GHz,
            BandPreference.Only5GHz   => WifiBand.Band5GHz,
            BandPreference.Only6GHz   => WifiBand.Band6GHz,
            _ => WifiBand.Unknown
        };
        return all.Where(n => n.Band == target).ToList();
    }

    /// <summary>表示名を変更(永続化)</summary>
    public void SetCustomLabel(string? label)
    {
        PrefsService.SetLabel(_adapter.Id, label);
        Preferences = PrefsService.Get(_adapter.Id);
        OnPropertyChanged(nameof(DisplayName));
    }

    /// <summary>子機を有効/無効に切替(永続化)</summary>
    public void ToggleEnabled()
    {
        PrefsService.SetEnabled(_adapter.Id, !Preferences.IsEnabled);
        Preferences = PrefsService.Get(_adapter.Id);
        OnPropertyChanged(nameof(IsEnabled));
    }

    /// <summary>このアダプター用のバンドフィルタを設定(永続化)</summary>
    public void SetPreferredBand(BandPreference band)
    {
        PrefsService.SetBandFilter(_adapter.Id, band);
        Preferences = PrefsService.Get(_adapter.Id);
        OnPropertyChanged(nameof(PreferredBand));
        _ = RefreshAsync().ContinueWith(
            t => _log.LogError(t.Exception!.GetBaseException(), "Band change refresh failed"),
            default, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    /// <summary>SSIDをこのアダプター用にピン留め(永続化)</summary>
    public void PinSsid(string ssid)
    {
        PrefsService.PinSsid(_adapter.Id, ssid);
        Preferences = PrefsService.Get(_adapter.Id);
        OnPropertyChanged(nameof(PinnedSsids));
    }

    [RelayCommand]
    public async Task ConnectAsync(string passphrase)
    {
        if (_selected is null) return;
        try
        {
            var res = await _executor.ConnectAsync(
                _adapter.Id, _selected.Ssid, _selected.Auth,
                passphrase, TimeSpan.FromSeconds(30));
            if (res.Success) await RefreshAsync();
            else _log.LogWarning("Connect failed: {f}", res.Failure);
        }
        catch (Exception ex) { _log.LogError(ex, "Connect"); }
    }

    [RelayCommand]
    public async Task DisconnectAsync()
    {
        await _executor.DisconnectAsync(_adapter.Id);
        await RefreshAsync();
    }

    /// <summary>
    /// 任意SSIDに接続(SystemTray・Sidebar の Quick Connect 用)。
    /// 既存プロファイルがある前提。新規接続は MainWindow 経由でパスフレーズダイアログ起動。
    /// </summary>
    public async Task<MWC.Core.Models.ConnectionResult> ConnectToSsidAsync(
        string ssid, System.TimeSpan? timeout = null,
        System.Threading.CancellationToken ct = default)
    {
        try
        {
            var res = await _executor.ConnectAsync(
                _adapter.Id, ssid, MWC.Core.Models.AuthMethod.WPA2PSK,
                "", timeout, ct);
            await RefreshAsync();
            return res;
        }
        catch (System.Exception ex)
        {
            _log.LogWarning(ex, "ConnectToSsid {ssid}", ssid);
            return MWC.Core.Models.ConnectionResult.Fail(MWC.Core.Models.ConnectionFailure.OsError);
        }
    }

    /// <summary>このアダプターが現在接続している接続を強制切断</summary>
    public bool IsAvailable => _adapter.State != MWC.Core.Models.AdapterState.NotReady;
}

// ════════════════════════════════════
//  NetworkItemViewModel
// ════════════════════════════════════
