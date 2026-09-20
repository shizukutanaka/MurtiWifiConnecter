using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MWC.App.Views;
using MWC.Core.Abstractions;
using MWC.Core.Models;
using MWC.Core.Services;

namespace MWC.App.ViewModels;

/// <summary>
/// すべての無線子機を1画面で俯瞰する ViewModel。
/// 各アダプターを横並び/縦並びで表示し、
/// アダプター毎にネットワーク選択・接続・優先順位設定ができる。
///
/// Apple "Mission Control" 的な俯瞰ビュー。
/// </summary>
public sealed partial class AllAdaptersOverviewViewModel : ObservableObject
{
    private readonly IWifiService               _wifi;
    private readonly AdapterPreferencesService  _prefs;
    private readonly NetworkHistoryService      _history;
    private readonly ConnectionExecutor         _executor;
    private readonly OuiLookupService           _oui;
    private readonly ILogger                    _log;

    public ObservableCollection<AdapterPanelViewModel> Panels { get; } = new();

    [ObservableProperty] private string _summaryStatus = MWC.App.Resources.L.Get("Status_Starting");

    public AllAdaptersOverviewViewModel(
        IWifiService wifi,
        AdapterPreferencesService prefs,
        NetworkHistoryService history,
        ConnectionExecutor executor,
        OuiLookupService oui,
        ILogger<AllAdaptersOverviewViewModel> log)
    {
        _wifi = wifi; _prefs = prefs; _history = history; _executor = executor;
        _oui = oui; _log = log;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            var adapters = await _wifi.GetAdaptersAsync();
            Panels.Clear();
            foreach (var a in adapters)
                Panels.Add(new AdapterPanelViewModel(a, _wifi, _prefs, _executor, _oui, _log));

            // 並列スキャン
            await Task.WhenAll(Panels.Select(p => p.RefreshAsync()));
            UpdateSummary();
        }
        catch (Exception ex) { _log.LogError(ex, "OverviewLoad"); }
    }

    [RelayCommand]
    public async Task ConnectAllPreferredAsync()
    {
        // 各子機を優先順位最上位ネットワークに接続。
        // 1 台の失敗が他を巻き込まないよう個別に隔離する — 本製品の中核価値
        // 「各アダプターを独立管理」は一括操作でも成立していなければならない。
        // 隔離しないと Task.WhenAll が最初の例外で中断し、UpdateSummary() に到達せず
        // 成功した子機の結果まで UI に反映されなくなる
        // (MainViewModel.SafeRefreshOne と同じ確立パターン。2026-07 品質パス)。
        await Task.WhenAll(Panels.Select(SafePanelOp));
        UpdateSummary();

        async Task SafePanelOp(AdapterPanelViewModel p)
        {
            try { await p.ConnectPreferredAsync(); }
            catch (Exception ex) { _log.LogWarning(ex, "ConnectAllPreferred: {n}", p.Name); }
        }
    }

    [RelayCommand]
    public async Task DisconnectAllAsync()
    {
        // ConnectAllPreferredAsync と同じ隔離方針(上記コメント参照)。
        await Task.WhenAll(Panels.Select(SafePanelOp));
        UpdateSummary();

        async Task SafePanelOp(AdapterPanelViewModel p)
        {
            try { await p.DisconnectAsync(); }
            catch (Exception ex) { _log.LogWarning(ex, "DisconnectAll: {n}", p.Name); }
        }
    }

    public void UpdateSummary()
    {
        int connected = Panels.Count(p => !string.IsNullOrEmpty(p.ConnectedSsid));
        SummaryStatus = MWC.App.Resources.L.StatusAdaptersConnected(connected, Panels.Count);
    }
}

/// <summary>1つの無線子機を表すパネル。</summary>
public sealed partial class AdapterPanelViewModel : ObservableObject
{
    private readonly WifiAdapter                _adapter;
    private readonly IWifiService               _wifi;
    private readonly AdapterPreferencesService  _prefs;
    private readonly ConnectionExecutor         _executor;
    private readonly OuiLookupService           _oui;
    private readonly ILogger                    _log;

    public Guid    Id          => _adapter.Id;
    public string  Name        => _adapter.Name;
    public string  Description => _adapter.Description;
    public string  NetworkListAutomationLabel => MWC.App.Resources.L.AllAdaptersNetworkListAutomation(Name);

    /// <summary>このアダプタで利用可能な全ネットワーク</summary>
    public ObservableCollection<NetworkItemViewModel> Networks { get; } = new();

    /// <summary>このアダプタの優先ネットワーク順序(順位付き)</summary>
    public ObservableCollection<PreferredNetworkRow> PreferredNetworks { get; } = new();

    [ObservableProperty] private NetworkItemViewModel? _selected;
    [ObservableProperty] private string?               _connectedSsid;
    [ObservableProperty] private int                   _connectedSignal;
    [ObservableProperty] private bool                  _isScanning;
    [ObservableProperty] private bool                  _isConnecting;
    [ObservableProperty] private string                _statusMessage = "";
    [ObservableProperty] private bool                  _autoReconnectEnabled;

    public IReadOnlyList<WifiNetwork> SourceNetworks { get; private set; } =
        Array.Empty<WifiNetwork>();

    public AdapterPanelViewModel(
        WifiAdapter a, IWifiService w,
        AdapterPreferencesService p, ConnectionExecutor ex,
        OuiLookupService o, ILogger l)
    {
        _adapter = a; _wifi = w; _prefs = p; _executor = ex; _oui = o; _log = l;
        AutoReconnectEnabled = p.IsAutoReconnectEnabled(a.Id);
        ReloadPreferredList();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsScanning = true;
        try
        {
            var rawNets = await _wifi.ScanAsync(_adapter.Id);
            // OWE Transition Mode: 同一 SSID の Open ビーコンは後方互換用のプレースホルダーであり
            // (RFC 8110)、OWE 対応クライアントは常に暗号化された OWE 側を使うべき。重複表示を防ぐ。
            var nets = new OweSelectionService().ApplyOwePreference(rawNets);
            SourceNetworks = nets;

            var byKey = nets.ToDictionary(n => n.Ssid);
            for (int i = Networks.Count - 1; i >= 0; i--)
                if (!byKey.ContainsKey(Networks[i].Ssid)) Networks.RemoveAt(i);

            foreach (var n in nets)
            {
                var enriched = n.BssEntries.Count > 0
                    ? n with { VendorName = _oui.Lookup(n.BssEntries[0].Bssid) ?? n.VendorName }
                    : n;

                var ex = Networks.FirstOrDefault(x => x.Ssid == enriched.Ssid);
                if (ex is null) Networks.Add(new NetworkItemViewModel(enriched));
                else            ex.Update(enriched);
            }

            var connected = nets.FirstOrDefault(n => n.IsConnected);
            ConnectedSsid    = connected?.Ssid;
            ConnectedSignal  = connected?.SignalQuality ?? 0;
            StatusMessage    = ConnectedSsid is null
                ? MWC.App.Resources.L.StatusNetworksFound(nets.Count)
                : MWC.App.Resources.L.Format("Status_ConnectedTo", ConnectedSsid, ConnectedSignal);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Panel refresh: {n}", _adapter.Name); }
        finally { IsScanning = false; }
    }

    /// <summary>優先順位最上位の圏内ネットワークに接続を試みる</summary>
    public async Task ConnectPreferredAsync()
    {
        if (IsConnecting) return;
        var best = _prefs.PickBestSsid(_adapter.Id, SourceNetworks.Select(n => n.Ssid));
        if (best is null)
        {
            StatusMessage = MWC.App.Resources.L.Get("Status_PriorityOutOfRange");
            return;
        }
        IsConnecting = true;
        try
        {
            StatusMessage = MWC.App.Resources.L.Get("Progress_Connecting");
            var net = SourceNetworks.FirstOrDefault(n => n.Ssid == best);
            if (net is null) { StatusMessage = MWC.App.Resources.L.Get("Status_PriorityOutOfRange"); return; }
            var res = await _executor.ConnectAsync(
                _adapter.Id, best, net.Auth, "", TimeSpan.FromSeconds(20));
            await RefreshAsync();
            StatusMessage = res.Success
                ? MWC.App.Resources.L.Format("Status_ConnectedTo_Short", best)
                : MWC.App.Resources.L.ErrorConnectionFailed(
                    MWC.App.Resources.L.ConnectionFailureLabel(
                        res.Failure ?? MWC.Core.Models.ConnectionFailure.Unknown));
            if (res.Success && res.BehindCaptivePortal)
                new CaptivePortalDialog(best)
                    { Owner = Application.Current?.MainWindow }
                    .ShowDialog();
        }
        catch (Exception ex)
        {
            // AsyncRelayCommand 経由の呼び出しは例外を ExecutionTask に格納するだけで
            // UI に伝播しないため、握りつぶさずログ記録 + ユーザー向け表示を行う
            // (AdapterViewModel.RefreshAsync と同じ 2026-07 品質パスの修正)。
            _log.LogError(ex, "ConnectPreferred failed for adapter {AdapterId}", _adapter.Id);
            StatusMessage = MWC.App.Resources.L.ErrorUnexpected(ex.Message);
        }
        finally { IsConnecting = false; }
    }

    [RelayCommand]
    public async Task DisconnectAsync()
    {
        await _executor.DisconnectAsync(_adapter.Id);
        await RefreshAsync();
    }

    [RelayCommand]
    public void AddPreferred()
    {
        if (Selected is null) return;
        _prefs.AddPreferred(_adapter.Id, Selected.Ssid);
        ReloadPreferredList();
    }

    [RelayCommand]
    public void RemovePreferred(string ssid)
    {
        _prefs.RemovePreferred(_adapter.Id, ssid);
        ReloadPreferredList();
    }

    [RelayCommand]
    public void MoveUpPreferred(string ssid)
    {
        _prefs.MoveUp(_adapter.Id, ssid);
        ReloadPreferredList();
    }

    partial void OnAutoReconnectEnabledChanged(bool value)
        => _prefs.SetAutoReconnect(_adapter.Id, value);

    private void ReloadPreferredList()
    {
        PreferredNetworks.Clear();
        var prefs = _prefs.GetPreferredNetworks(_adapter.Id);
        for (int i = 0; i < prefs.Count; i++)
            PreferredNetworks.Add(new PreferredNetworkRow(prefs[i], i + 1));
    }
}

/// <summary>優先ネットワーク1行を表現する</summary>
public sealed record PreferredNetworkRow(string Ssid, int Rank);
