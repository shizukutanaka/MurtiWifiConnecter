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
    private readonly System.Timers.Timer _timer;

    public ObservableCollection<AdapterViewModel> Adapters { get; } = new();

    [ObservableProperty] private AdapterViewModel? _selectedAdapter;
    [ObservableProperty] private string            _statusMessage = MWC.App.Resources.L.Get("Status_Starting");
    [ObservableProperty] private bool              _isBusy;
    [ObservableProperty] private bool              _isScanning;

    partial void OnSelectedAdapterChanged(AdapterViewModel? v)
    {
        if (v is not null) _ = v.RefreshAsync();
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
        _timer = new System.Timers.Timer(15_000) { AutoReset = true };
        _timer.Elapsed += async (_, _) => await SafeRefresh();
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
            // 全アダプター並列スキャン (各子機独立)
            await Task.WhenAll(Adapters.Select(a => a.RefreshAsync()));
            if (SelectedAdapter is not null)
                Filter.SetSource(SelectedAdapter.Networks.ToList());
            _timer.Start();
            StatusMessage = $"{ads.Count} アダプター";
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
            await Task.WhenAll(Adapters.Select(a => a.RefreshAsync()));
            if (SelectedAdapter is not null)
                Filter.SetSource(SelectedAdapter.Networks.ToList());
            int connected = Adapters.Count(a => a.ConnectedSsid is not null);
            StatusMessage = $"{connected} / {Adapters.Count} アダプターが接続中";
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
                "json" => "JSON (*.json)|*.json",
                "txt"  => "Text (*.txt)|*.txt",
                _      => "CSV (*.csv)|*.csv"
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
            StatusMessage = $"Export → {Path.GetFileName(dlg.FileName)}";
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
        var intervalMs = settings.AutoScanIntervalSeconds * 1000;
        if (intervalMs > 0)
        {
            _timer.Interval = intervalMs;
            _timer.Enabled  = true;
        }
        else
        {
            _timer.Enabled = false;
        }
    }

    public void Dispose()
    {
        _timer.Stop(); _timer.Dispose();
    }
}

// ════════════════════════════════════
//  AdapterViewModel
// ════════════════════════════════════
