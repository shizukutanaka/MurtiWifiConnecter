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
        ConnectedSsid is null ? MWC.App.Resources.L.LabelNotConnected
        : _connectedSince.HasValue
            ? $"→ {ConnectedSsid}  ({FormatDuration(DateTimeOffset.UtcNow - _connectedSince.Value)})"
            : $"→ {ConnectedSsid}";

    private static string FormatDuration(TimeSpan e) =>
        e.TotalHours >= 1 ? $"{(int)e.TotalHours}h {e.Minutes:D2}m" : $"{(int)e.TotalMinutes}m";

    /// <summary>ツールバー接続状態テキスト (resx 経由でローカライズ済み)</summary>
    public string ToolbarStatusText =>
        ConnectedSsid is null
            ? MWC.App.Resources.L.LabelNotConnected
            : MWC.App.Resources.L.LabelConnected(ConnectedSsid);

    /// <summary>信号履歴タブのタイトル (resx 経由でローカライズ済み)</summary>
    public string SignalHistoryTitle =>
        _selected is null
            ? MWC.App.Resources.L.MainSelectHistoryHint
            : MWC.App.Resources.L.MainSignalHistoryTitle(_selected.Ssid);

    /// <summary>UI表示用 NetworkItemViewModel 一覧</summary>
    public ObservableCollection<NetworkItemViewModel> Networks { get; } = new();

    /// <summary>ChannelBandCanvas / ExportService 用の元 WifiNetwork 一覧</summary>
    public IReadOnlyList<WifiNetwork> SourceNetworks { get; private set; } =
        Array.Empty<WifiNetwork>();

    [ObservableProperty] private NetworkItemViewModel?    _selected;
    [ObservableProperty] private string?                  _connectedSsid;
    [ObservableProperty] private bool                     _isScanning;
    [ObservableProperty] private NetworkDetailViewModel   _detail = new();

    private DateTimeOffset? _connectedSince;
    private string?         _prevConnectedSsid;

    /// <summary>選択中 SSID の信号履歴サンプル (SignalHistoryCanvas用)</summary>
    public IReadOnlyList<MWC.Core.Services.SignalSample> SelectedHistory
        => _selected is null
            ? Array.Empty<MWC.Core.Services.SignalSample>()
            : _history.GetHistory(_selected.Ssid);

    partial void OnSelectedChanged(NetworkItemViewModel? v)
    {
        var rssiHistory = v is null ? null
            : _history.GetHistory(v.Ssid)
                       .Where(s => s.Rssi.HasValue)
                       .Select(s => s.Rssi!.Value)
                       .ToList();
        Detail.Load(v?.Source, SourceNetworks, rssiHistory: rssiHistory);
        OnPropertyChanged(nameof(SelectedHistory));
        OnPropertyChanged(nameof(SignalHistoryTitle));
    }

    partial void OnConnectedSsidChanged(string? value)
    {
        OnPropertyChanged(nameof(ConnectionStatusLabel));
        OnPropertyChanged(nameof(ToolbarStatusText));
    }

    private static string ComputeTrendArrow(IReadOnlyList<MWC.Core.Services.SignalSample> history)
    {
        if (history.Count < 3) return "";
        int delta = history[0].Quality - history[Math.Min(2, history.Count - 1)].Quality;
        return delta > 5 ? "↑" : delta < -5 ? "↓" : "";
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

            // UI差分更新はバンドフィルタ後の SourceNetworks を基準にする
            // (enriched を使うと band-filter の効果がUIに反映されない)
            var byKey = SourceNetworks.ToDictionary(n => n.Ssid);
            for (int i = Networks.Count - 1; i >= 0; i--)
                if (!byKey.ContainsKey(Networks[i].Ssid)) Networks.RemoveAt(i);
            foreach (var n in SourceNetworks)
            {
                var ex = Networks.FirstOrDefault(x => x.Ssid == n.Ssid);
                if (ex is null) Networks.Add(new NetworkItemViewModel(n));
                else            ex.Update(n);
            }

            // チャンネル混雑度・信号トレンドを計算して各ネットワークに設定
            // (congestion context uses enriched — all bands — for accurate co-channel counting)
            var channelAdvisor = new ChannelAdvisorService();
            foreach (var netVm in Networks)
            {
                var advisory = channelAdvisor.AdviseCongestion(netVm.Source, enriched);
                netVm.CongestionPercent   = advisory.UtilizationPercent;
                netVm.IsChannelOverloaded = advisory.IsOverloaded;
                netVm.SignalTrendLabel    = ComputeTrendArrow(_history.GetHistory(netVm.Ssid));
            }

            var connectedNet     = enriched.FirstOrDefault(n => n.IsConnected);
            var newConnectedSsid = connectedNet?.Ssid;
            if (newConnectedSsid != _prevConnectedSsid)
            {
                _connectedSince    = newConnectedSsid is null ? null : DateTimeOffset.UtcNow;
                _prevConnectedSsid = newConnectedSsid;
                if (connectedNet is not null)
                    NetworkDetailViewModel.RecordTrustedConnection(connectedNet);
            }
            ConnectedSsid = newConnectedSsid;
            OnPropertyChanged(nameof(ConnectionStatusLabel));
            OnPropertyChanged(nameof(CurrentSignal));

            // 選択中の詳細・履歴を最新化
            if (_selected is not null && byKey.TryGetValue(_selected.Ssid, out var upd))
                _selected.Update(upd);
            var duration = _connectedSince.HasValue
                ? DateTimeOffset.UtcNow - _connectedSince.Value
                : (TimeSpan?)null;
            var selHistory = _selected is null ? null
                : _history.GetHistory(_selected.Ssid)
                           .Where(s => s.Rssi.HasValue)
                           .Select(s => s.Rssi!.Value)
                           .ToList();
            Detail.Load(_selected?.Source, SourceNetworks, duration, selHistory);
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
