using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MWC.Core.Abstractions;
using MWC.Core.Models;
using MWC.Core.Profile;
using MWC.Core.Services;
using MWC.App.Services;

namespace MWC.App.ViewModels;

// ════════════════════════════════════
//  MainViewModel
// ════════════════════════════════════
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IWifiService        _wifi;
    private readonly ILogger<MainViewModel> _log;
    private readonly SignalHistoryService _history;
    private readonly OuiLookupService    _oui;
    // DispatcherTimer を使う (System.Timers.Timer ではない): Tick は UI スレッドで発火し、
    // await 後の継続も UI スレッドに戻るため、RefreshAsync が束縛済み ObservableCollection
    // (SelectedAdapter.Networks) を安全に変更できる。ThreadPool タイマーだと
    // SynchronizationContext が無く、コレクション変更が Dispatcher 外で起き
    // NotSupportedException を投げて自動スキャンが無言で失敗していた。
    private readonly System.Windows.Threading.DispatcherTimer _timer;

    public ObservableCollection<AdapterViewModel> Adapters { get; } = new();

    [ObservableProperty] private AdapterViewModel? _selectedAdapter;
    [ObservableProperty] private string            _statusMessage = MWC.App.Resources.L.Get("Status_Starting");
    [ObservableProperty] private bool              _isBusy;
    [ObservableProperty] private bool              _isScanning;

    partial void OnSelectedAdapterChanged(AdapterViewModel? v)
    {
        Filter.SetAdapter(v?.Id);
        // fire-and-forget だが SafeRunAsync で例外を捕捉・ログ化し、未観測例外でのクラッシュを防ぐ
        if (v is not null)
            _ = AsyncEventHelper.SafeRunAsync(_log, "AdapterSelected", () => v.RefreshAsync());
    }

    public NetworkFilterViewModel Filter { get; }

    public AdapterPreferencesService AdapterPreferences { get; }

    private readonly ConnectionExecutor _executor;

    public MainViewModel(
        IWifiService wifi, ILogger<MainViewModel> log,
        SignalHistoryService history, OuiLookupService oui,
        NetworkFilterViewModel filter,
        AdapterPreferencesService adapterPrefs,
        ConnectionExecutor executor)
    {
        _wifi = wifi; _log = log; _history = history; _oui = oui;
        Filter = filter;
        AdapterPreferences = adapterPrefs;
        _executor = executor;
        _timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        // Tick は UI スレッドで発火。SafeRunAsync で例外を捕捉し未観測例外を防ぐ。
        _timer.Tick += (_, _) => _ = AsyncEventHelper.SafeRunAsync(_log, "AutoScan", SafeRefresh);
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var ads = await _wifi.GetAdaptersAsync();
            Adapters.Clear();
            int idx = 1;
            foreach (var a in ads)
            {
                var avm = new AdapterViewModel(a, _wifi, _history, _oui, _log,
                    AdapterPreferences, _executor) { Index = idx++ };
                Adapters.Add(avm);
            }
            SelectedAdapter ??= Adapters.FirstOrDefault();
            // 全アダプター並列スキャン (各子機独立)。SafeRefreshOne で各 Task を
            // try/catch ラップし、1 つの子機の失敗が他を巻き込まず全件ログされるようにする。
            await Task.WhenAll(Adapters.Select(SafeRefreshOne));
            if (SelectedAdapter is not null)
                Filter.SetSource(SelectedAdapter.Networks.ToList());
            _timer.Start();
            StatusMessage = MWC.App.Resources.L.StatusAdapterCount(ads.Count);
        }
        catch (Exception ex) { _log.LogError(ex, "Load"); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task RefreshAsync() => await SafeRefresh();

    /// <summary>全アダプターを並列でスキャン(マルチアダプター運用、Apple "Concurrent")</summary>
    [RelayCommand]
    public async Task RefreshAllAsync()
    {
        IsScanning = true;
        try
        {
            // 各子機を独立に try/catch ラップ (自動スキャン経路 L176 と同じ SafeRefreshOne)。
            await Task.WhenAll(Adapters.Select(SafeRefreshOne));
            if (SelectedAdapter is not null)
                Filter.SetSource(SelectedAdapter.Networks.ToList());
            int connected = Adapters.Count(a => a.ConnectedSsid is not null);
            StatusMessage = MWC.App.Resources.L.StatusAdaptersConnected(connected, Adapters.Count);
        }
        catch (Exception ex) { _log.LogWarning(ex, "RefreshAll"); }
        finally { IsScanning = false; }
    }

    /// <summary>キーボードショートカット(Ctrl+1〜9)でアダプター切替</summary>
    public void SelectAdapterByIndex(int idx)
    {
        var target = Adapters.FirstOrDefault(a => a.Index == idx);
        if (target is not null) SelectedAdapter = target;
    }

    /// <summary>同SSIDが他アダプターで接続中か確認(重複防止)</summary>
    public AdapterViewModel? FindAdapterConnectedTo(string ssid)
        => Adapters.FirstOrDefault(a =>
            a != SelectedAdapter && a.ConnectedSsid == ssid);

    [RelayCommand]
    public void Export(string fmt)
    {
        var nets = SelectedAdapter?.SourceNetworks;
        if (nets is null || nets.Count == 0) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName   = $"mwc-scan-{DateTime.Now:yyyyMMdd-HHmmss}",
            DefaultExt = fmt == "json" ? "json" : fmt == "txt" ? "txt" : "csv",
            Filter     = fmt switch
            {
                "json" => MWC.App.Resources.L.Get("Export_FilterJson"),
                "txt"  => MWC.App.Resources.L.Get("Export_FilterTxt"),
                _      => MWC.App.Resources.L.Get("Export_FilterCsv")
            }
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            switch (fmt)
            {
                case "json": ExportService.ToJson(nets, dlg.FileName); break;
                case "txt":  ExportService.ToText(nets, dlg.FileName); break;
                default:     ExportService.ToCsv (nets, dlg.FileName); break;
            }
            StatusMessage = MWC.App.Resources.L.StatusExported(Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex) { _log.LogError(ex, "Export"); }
    }

    /// <summary>
    /// 全アダプター並列スキャン。
    /// Apple流: 「アダプター単位の独立性」を保証する設計。
    /// 各 AdapterViewModel.IsScanning でアダプター毎の進捗を表示。
    /// </summary>
    private async Task SafeRefresh()
    {
        if (Adapters.Count == 0) return;
        IsScanning = true;
        try
        {
            // 全アダプターを真の並列でスキャン
            // (各 RefreshAsync は独立して IsScanning を管理する)
            await Task.WhenAll(Adapters.Select(a => SafeRefreshOne(a)));
            // フィルター更新は SelectedAdapter 基準
            if (SelectedAdapter is not null)
                Filter.SetSource(SelectedAdapter.Networks.ToList());
        }
        finally { IsScanning = false; }
    }

    private async Task SafeRefreshOne(AdapterViewModel a)
    {
        try { await a.RefreshAsync(); }
        catch (Exception ex) { _log.LogWarning(ex, "refresh adapter {name}", a.Name); }
    }

    /// <summary>設定変更をランタイムに即適用(スキャン間隔等)</summary>
    public void ApplySettings(AppSettings settings)
    {
        var intervalSeconds = settings.AutoScanIntervalSeconds;
        if (intervalSeconds > 0)
        {
            _timer.Interval = TimeSpan.FromSeconds(intervalSeconds);
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    public void Dispose()
    {
        // DispatcherTimer は IDisposable ではない。Stop() で Tick を止める。
        _timer.Stop();
    }
}

// ════════════════════════════════════
//  AdapterViewModel
// ════════════════════════════════════
