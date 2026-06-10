using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MWC.Core.Abstractions;
using MWC.Core.Models;
using MWC.Core.Services;

namespace MWC.App.Services;

/// <summary>
/// アダプターフェイルオーバーサービス。
///
/// 概要:
///   プライマリアダプターが切断された場合、<see cref="AdapterPreferences.FailoverAdapterId"/>
///   に設定されたバックアップアダプターへ自動的に切り替える。
///
/// 動作:
///   1. 30 秒ごとに全アダプターの接続状態を取得する。
///   2. EnableFailover=true かつ FailoverAdapterId が設定されているアダプターが
///      切断状態に変化した場合、バックアップアダプターの優先SSID に接続を試みる。
///   3. プライマリアダプターが復旧した場合、トースト通知のみ発行する
///      (バックアップからプライマリへの自動復帰は行わない — ユーザー操作による)。
///
/// 設計:
///   - IHostedService として DI コンテナに登録し、アプリ起動時に自動開始。
///   - fire-and-forget のタイマーコールバックで例外を飲み込まず LogWarning で記録。
/// </summary>
public sealed class AdapterFailoverService : IDisposable
{
    private readonly IWifiService                _wifi;
    private readonly AdapterPreferencesService   _prefs;
    private readonly ConnectionExecutor          _executor;
    private readonly NotificationService         _notify;
    private readonly ILogger<AdapterFailoverService> _log;

    private Timer?  _timer;

    // Adapter GUID → last known connected SSID (null = not connected)
    private readonly Dictionary<Guid, string?> _lastState = new();
    // Adapters currently in failover mode (primary lost connectivity)
    private readonly HashSet<Guid> _activeFailovers = new();

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    public AdapterFailoverService(
        IWifiService wifi,
        AdapterPreferencesService prefs,
        ConnectionExecutor executor,
        NotificationService notify,
        ILogger<AdapterFailoverService> log)
    {
        _wifi = wifi; _prefs = prefs; _executor = executor;
        _notify = notify; _log = log;
    }

    public void Start()
    {
        _timer = new Timer(
            _ => _ = CheckAsync(),
            null,
            TimeSpan.FromSeconds(15),   // initial delay — let app finish starting
            CheckInterval);
        _log.LogInformation("AdapterFailoverService started (interval={Interval}s)", CheckInterval.TotalSeconds);
    }

    public void Stop() => _timer?.Change(Timeout.Infinite, 0);

    public void Dispose() => _timer?.Dispose();

    private async Task CheckAsync()
    {
        try
        {
            var adapters = await _wifi.GetAdaptersAsync().ConfigureAwait(false);
            var adapterMap = adapters.ToDictionary(a => a.Id);

            foreach (var adapter in adapters)
            {
                var pref = _prefs.Get(adapter.Id);
                if (!pref.EnableFailover || pref.FailoverAdapterId is null)
                    continue;

                var currentSsid   = adapter.ConnectedSsid;
                var previousSsid  = _lastState.GetValueOrDefault(adapter.Id);
                _lastState[adapter.Id] = currentSsid;

                bool wasConnected  = previousSsid is not null;
                bool isConnected   = currentSsid is not null;

                // Primary went from connected → disconnected
                if (wasConnected && !isConnected && !_activeFailovers.Contains(adapter.Id))
                {
                    _log.LogInformation(
                        "Failover triggered: adapter {Id} ({Name}) lost connection",
                        adapter.Id, adapter.Name);
                    _activeFailovers.Add(adapter.Id);
                    await ActivateFailoverAsync(adapter, pref, adapterMap).ConfigureAwait(false);
                }

                // Primary came back (was in failover, now reconnected)
                if (isConnected && _activeFailovers.Contains(adapter.Id))
                {
                    _activeFailovers.Remove(adapter.Id);
                    _log.LogInformation(
                        "Failover resolved: primary adapter {Id} ({Name}) reconnected to {Ssid}",
                        adapter.Id, adapter.Name, currentSsid);
                    _notify.NotifyConnected(
                        MWC.App.Resources.L.NotifyFailoverRestored(adapter.Name),
                        hasInternet: true, captive: false);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AdapterFailoverService.CheckAsync failed");
        }
    }

    private async Task ActivateFailoverAsync(
        WifiAdapter primary,
        AdapterPreferences pref,
        Dictionary<Guid, WifiAdapter> adapterMap)
    {
        if (pref.FailoverAdapterId is not Guid failoverId) return;

        if (!adapterMap.TryGetValue(failoverId, out var failoverAdapter))
        {
            _log.LogWarning("Failover adapter {Id} not found", failoverId);
            return;
        }

        // Get the best SSID for the failover adapter from its preferences
        var failoverPrefs = _prefs.Get(failoverId);
        var targetSsid    = failoverPrefs.AutoConnectPriority.FirstOrDefault()
                         ?? failoverPrefs.PinnedSsids.FirstOrDefault();

        if (targetSsid is null)
        {
            _log.LogWarning(
                "Failover adapter {Id} has no preferred SSID configured", failoverId);
            return;
        }

        _log.LogInformation(
            "Activating failover: {Primary} → {Failover} SSID={Ssid}",
            primary.Name, failoverAdapter.Name, targetSsid);

        try
        {
            // Scan to find the target network and its auth method
            var visible = await _wifi.ScanAsync(failoverId).ConfigureAwait(false);
            var target  = visible.FirstOrDefault(n => n.Ssid == targetSsid);
            if (target is null)
            {
                _log.LogWarning("Failover target SSID {Ssid} not in range on adapter {Id}",
                    targetSsid, failoverId);
                return;
            }

            var result = await _executor.ConnectAsync(
                failoverId, targetSsid,
                auth: target.Auth,
                passphrase: "",
                timeout: TimeSpan.FromSeconds(20)).ConfigureAwait(false);

            if (result.Success)
            {
                _notify.NotifyConnected(
                    MWC.App.Resources.L.NotifyFailoverActivated(failoverAdapter.Name),
                    hasInternet: result.HasInternet, captive: result.BehindCaptivePortal);
                _log.LogInformation("Failover successful: connected to {Ssid} via {Adapter}",
                    targetSsid, failoverAdapter.Name);
            }
            else
            {
                _log.LogWarning("Failover connection failed: {Failure}", result.Failure);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Exception during failover connection attempt");
        }
    }
}
