using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MWC.Core.Abstractions;
using MWC.Core.Models;
using MWC.Core.Profile;

namespace MWC.Core.Services;

/// <summary>
/// IWifiService.ConnectAsync の単一エントリポイント。
/// プロジェクト内に散在していた接続フローを統一。
///
/// 責務:
///   - WifiProfileSpec → XML 変換
///   - プロファイル登録 (オーバーライト)
///     ただし PSK 系でパスフレーズが空の場合は既存保存プロファイルを再利用するためスキップ
///   - 実接続 + タイムアウト
///   - History 自動記録
///   - 構造化ログ / OTel
///
/// 呼出元:
///   - AdapterViewModel.ConnectAsync / ConnectToSsidAsync
///   - AdapterConnectExtension.ConnectWithAppleFlowAsync
///   - AutoReconnectService.WatchAsync
///   - AdapterFailoverService.ConnectAsync
///   - AllAdaptersOverviewViewModel.AdapterPanelViewModel.ConnectPreferredAsync
///   - MultiAdapterCommand.ConnectOneAsync (CLI)
///   - MainWindow.UpdateTray (トレイメニュー接続)
/// </summary>
public sealed class ConnectionExecutor
{
    private readonly IWifiService               _wifi;
    private readonly NetworkHistoryService      _history;
    private readonly ILogger<ConnectionExecutor> _log;
    // アダプターごとの排他ロック(並列接続によるドライバー不整合を防止)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim>
        _perAdapterLocks = new();
    // ユーザーが意図的に切断したアダプターと時刻を記録
    // AutoReconnect / Failover が誤って再接続しないよう使う
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, DateTimeOffset>
        _userDisconnects = new();

    public ConnectionExecutor(
        IWifiService wifi,
        NetworkHistoryService history,
        ILogger<ConnectionExecutor> log)
    {
        _wifi = wifi; _history = history; _log = log;
    }

    /// <summary>
    /// 接続実行(プロファイル登録 → ConnectAsync → 履歴記録の一連フロー)。
    /// NonBroadcast などのすべての接続パラメータを <see cref="WifiProfileSpec"/> で渡す。
    /// </summary>
    public async Task<ConnectionResult> ConnectAsync(
        Guid adapterId,
        WifiProfileSpec spec,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var _lock = _perAdapterLocks.GetOrAdd(adapterId, _ => new SemaphoreSlim(1, 1));
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
        var to = timeout ?? TimeSpan.FromSeconds(25);
        MwcActivity.ConnectAttempts.Add(1,
            new System.Collections.Generic.KeyValuePair<string,object?>("wifi.auth", spec.Auth.ToString()));

        using var activity = MwcActivity.StartConnectActivity(spec.Ssid, spec.Auth.ToString());
        var sw = Stopwatch.StartNew();

        try
        {
            // 1. プロファイル登録
            // パスフレーズが空でPSK系認証の場合、既存保存プロファイルを再利用するためスキップ。
            // (AutoReconnect / Failover パスが passphrase="" で呼ぶケースに対応)
            bool needsPassphrase = spec.Auth is AuthMethod.WPAPSK or AuthMethod.WPA2PSK
                                   or AuthMethod.WPA3SAE or AuthMethod.WPA3Transition or AuthMethod.WEP;
            bool shouldRegister  = !needsPassphrase || !string.IsNullOrEmpty(spec.Passphrase);
            if (shouldRegister)
            {
                var xml = ProfileXmlBuilder.Build(spec);
                if (!await _wifi.RegisterProfileAsync(adapterId, xml, overwrite: true, ct).ConfigureAwait(false))
                {
                    _history.RecordConnection(spec.Ssid, false);
                    return ConnectionResult.Fail(ConnectionFailure.OsError);
                }
            }

            // 2. 接続実行
            _log.ConnectAttempt(adapterId, MwcLog.HashSsid(spec.Ssid), spec.Auth);
            var result = await _wifi.ConnectAsync(adapterId, spec.Ssid, spec.Ssid, to, ct).ConfigureAwait(false);

            // 3. 履歴記録
            _history.RecordConnection(spec.Ssid, result.Success);

            sw.Stop();
            MwcActivity.ConnectDurationMs.Record(sw.Elapsed.TotalMilliseconds);

            if (result.Success)
            {
                MwcActivity.ConnectSuccesses.Add(1);
                activity?.SetStatus(ActivityStatusCode.Ok);
                _log.ConnectSucceeded(adapterId, MwcLog.HashSsid(spec.Ssid), sw.ElapsedMilliseconds);
            }
            else
            {
                MwcActivity.ConnectFailures.Add(1,
                    new System.Collections.Generic.KeyValuePair<string,object?>("failure", result.Failure?.ToString()));
                activity?.SetStatus(ActivityStatusCode.Error, result.Failure?.ToString() ?? "unknown");
                _log.ConnectFailed(adapterId, MwcLog.HashSsid(spec.Ssid), result.Failure ?? ConnectionFailure.Unknown, 0);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            _history.RecordConnection(spec.Ssid, false);
            throw;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ConnectAsync exception: {ssid}", PiiMask.Ssid(spec.Ssid));
            _history.RecordConnection(spec.Ssid, false);
            return ConnectionResult.Fail(ConnectionFailure.OsError);
        }
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// 便利オーバーロード: SSID/Auth/Passphrase で接続。NonBroadcast 等が不要な場合に使用。
    /// </summary>
    public Task<ConnectionResult> ConnectAsync(
        Guid adapterId,
        string ssid,
        AuthMethod auth,
        string passphrase = "",
        TimeSpan? timeout = null,
        CancellationToken ct = default)
        => ConnectAsync(adapterId,
            new WifiProfileSpec { Ssid = ssid, Auth = auth, Passphrase = passphrase },
            timeout, ct);

    /// <summary>
    /// 切断 + 履歴更新。ユーザー起因の切断として記録し、自動再接続を抑制する。
    /// </summary>
    public async Task<bool> DisconnectAsync(Guid adapterId, CancellationToken ct = default)
    {
        _userDisconnects[adapterId] = DateTimeOffset.UtcNow;
        try
        {
            return await _wifi.DisconnectAsync(adapterId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Disconnect failed");
            return false;
        }
    }

    /// <summary>
    /// 指定アダプターがユーザー操作により切断されてから <paramref name="within"/> 以内か確認。
    /// true なら AutoReconnect / Failover はスキップすべき。
    /// </summary>
    public bool WasRecentlyDisconnectedByUser(Guid adapterId, TimeSpan within)
        => _userDisconnects.TryGetValue(adapterId, out var t)
           && DateTimeOffset.UtcNow - t <= within;
}
