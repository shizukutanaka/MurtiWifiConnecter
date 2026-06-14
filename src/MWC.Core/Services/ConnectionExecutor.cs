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
/// プロジェクト内 4箇所に散在していた接続フローを統一。
///
/// 責務:
///   - WifiProfileSpec → XML 変換
///   - プロファイル登録 (オーバーライト)
///   - 実接続 + タイムアウト
///   - History 自動記録
///   - 構造化ログ
///
/// 4箇所の呼出元:
///   - MainViewModel.AdapterViewModel.ConnectAsync
///   - AdapterConnectExtension.ConnectWithAppleFlowAsync (内部呼出)
///   - AutoReconnectService.WatchAsync
///   - AllAdaptersOverviewViewModel.ConnectBestAsync
/// </summary>
public sealed class ConnectionExecutor
{
    private readonly IWifiService               _wifi;
    private readonly NetworkHistoryService      _history;
    private readonly ILogger<ConnectionExecutor> _log;
    // アダプターごとの排他ロック(並列接続によるドライバー不整合を防止)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim>
        _perAdapterLocks = new();

    public ConnectionExecutor(
        IWifiService wifi,
        NetworkHistoryService history,
        ILogger<ConnectionExecutor> log)
    {
        _wifi = wifi; _history = history; _log = log;
    }

    /// <summary>
    /// 接続実行(プロファイル登録 → ConnectAsync → 履歴記録の一連フロー)。
    /// </summary>
    public async Task<ConnectionResult> ConnectAsync(
        Guid adapterId,
        string ssid,
        AuthMethod auth,
        string passphrase = "",
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var _lock = _perAdapterLocks.GetOrAdd(adapterId, _ => new SemaphoreSlim(1, 1));
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
        var to = timeout ?? TimeSpan.FromSeconds(25);
        MwcActivity.ConnectAttempts.Add(1,
            new System.Collections.Generic.KeyValuePair<string,object?>("wifi.auth", auth.ToString()));

        using var activity = MwcActivity.StartConnectActivity(ssid, auth.ToString());
        var sw = Stopwatch.StartNew();

        try
        {
            // 1. プロファイル登録
            var spec = new WifiProfileSpec { Ssid = ssid, Auth = auth, Passphrase = passphrase };
            var xml  = ProfileXmlBuilder.Build(spec);
            if (!await _wifi.RegisterProfileAsync(adapterId, xml, overwrite: true, ct).ConfigureAwait(false))
            {
                _history.RecordConnection(ssid, false);
                return ConnectionResult.Fail(ConnectionFailure.OsError);
            }

            // 2. 接続実行
            _log.LogInformation("Connecting to {ssid} on adapter {id}", PiiMask.Ssid(ssid), adapterId);
            var result = await _wifi.ConnectAsync(adapterId, ssid, ssid, to, ct).ConfigureAwait(false);

            // 3. 履歴記録
            _history.RecordConnection(ssid, result.Success);

            sw.Stop();
            MwcActivity.ConnectDurationMs.Record(sw.Elapsed.TotalMilliseconds);

            if (result.Success)
            {
                MwcActivity.ConnectSuccesses.Add(1);
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
            else
            {
                MwcActivity.ConnectFailures.Add(1,
                    new System.Collections.Generic.KeyValuePair<string,object?>("failure", result.Failure?.ToString()));
                activity?.SetStatus(ActivityStatusCode.Error, result.Failure?.ToString() ?? "unknown");
            }

            _log.LogInformation("Connection {res}: {ssid} ({ms:F1}ms)",
                result.Success ? "success" : $"failed ({result.Failure})", PiiMask.Ssid(ssid), sw.Elapsed.TotalMilliseconds);

            return result;
        }
        catch (OperationCanceledException)
        {
            _history.RecordConnection(ssid, false);
            throw;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ConnectAsync exception: {ssid}", PiiMask.Ssid(ssid));
            _history.RecordConnection(ssid, false);
            return ConnectionResult.Fail(ConnectionFailure.OsError);
        }
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// 切断 + 履歴更新。
    /// </summary>
    public async Task<bool> DisconnectAsync(Guid adapterId, CancellationToken ct = default)
    {
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
}
