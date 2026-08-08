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
/// Apple "Auto-Join Network" 相当の自動再接続サービス。
///
/// 動作:
///   1. アダプターが切断状態になったことを WlanNotification で検出
///   2. 接続履歴から最高スコアのネットワークを選択
///   3. プロファイルが保存済みなら自動接続を試みる
///   4. 失敗してもトースト通知のみ。UI をブロックしない
///
/// 設定: AdapterPreferencesService.IsAutoReconnectEnabled() で per-adapter 制御
/// </summary>
public sealed class AutoReconnectService : IAsyncDisposable, IDisposable
{
    private readonly IWifiService          _wifi;
    private readonly NetworkHistoryService     _history;
    private readonly NotificationService        _notify;
    private readonly AdapterPreferencesService  _adapterPrefs;
    private readonly ConnectionExecutor         _executor;
    private readonly ILogger<AutoReconnectService> _log;

    private readonly CancellationTokenSource _cts = new();
    private Task? _watchLoop;
    private bool _disposed;

    // ── 再接続の失敗追跡(バックオフ / 打ち切り用)──────────────────────
    // 自動再接続は無人動作のため、失敗しても誰も止めない。バックオフが無いと
    // 「切断 → 3 秒後に再試行 → 失敗 → また切断イベント」で事実上のタイトループになり、
    // 特に BadCredentials(パスワード変更後など)では永久に失敗し続ける。
    // 固定待機は再試行を同期させるだけで無効とされ、対策は指数バックオフ + ジッター、
    // および決定的失敗の非リトライ化 + 最大試行回数。
    // 既存の RetryPolicy(AWS Full Jitter 方式・IsRetriable 分類器)をそのまま再利用する。
    private readonly RetryPolicy _retry = new(
        baseDelay: TimeSpan.FromSeconds(2),
        maxDelay:  TimeSpan.FromMinutes(2),
        maxAttempts: 5);

    // (アダプターID, SSID) ごとの連続失敗回数。成功・別SSIDへの切替でリセットする。
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(Guid, string), int>
        _consecutiveFailures = new();

    public AutoReconnectService(
        IWifiService wifi,
        NetworkHistoryService history,
        NotificationService notify,
        AdapterPreferencesService adapterPrefs,
        ConnectionExecutor executor,
        ILogger<AutoReconnectService> log)
    {
        _wifi = wifi; _history = history; _notify = notify;
        _adapterPrefs = adapterPrefs;
        _executor = executor; _log = log;
    }

    public void Start()
    {
        _watchLoop = WatchAsync(_cts.Token);
    }

    private async Task WatchAsync(CancellationToken ct)
    {
        // 外側 try: await foreach 自体(列挙の継続)が例外を投げた場合の保険。
        // 以前は Task.Delay と await foreach ヘッダーがどの try にも入っておらず、
        // 特にシャットダウン時の Task.Delay の OperationCanceledException が
        // 無捕捉のまま Start() が保持する _watchLoop を fault 状態にしていた
        // (2026-07 品質パスで是正。DisposeAsync 側も参照)。
        try
        {
            await foreach (var ev in _wifi.SubscribeEventsAsync(ct).ConfigureAwait(false))
            {
                if (ev.Type != WifiEventType.Disconnected) continue;

                try
                {
                    await Task.Delay(3000, ct).ConfigureAwait(false);  // 意図的な切断と区別するため少し待つ

                    // 安い同期チェックを先に実行し、不要な非同期 API 呼び出しを防止する。
                    // ① ユーザーが DisconnectAsync で明示的に切断した場合はスキップ
                    if (_executor.WasRecentlyDisconnectedByUser(ev.AdapterId, TimeSpan.FromSeconds(15)))
                        continue;
                    // ② このアダプターで自動再接続が無効なら GetAdaptersAsync を呼ばずにスキップ
                    if (!_adapterPrefs.IsAutoReconnectEnabled(ev.AdapterId)) continue;

                    var adapters = await _wifi.GetAdaptersAsync(ct).ConfigureAwait(false);
                    var disconnected = adapters
                        .FirstOrDefault(a => a.Id == ev.AdapterId && a.ConnectedSsid is null);
                    if (disconnected is null) continue;  // 再接続済み or 別アダプター

                    var scan = await _wifi.ScanAsync(ev.AdapterId, ct).ConfigureAwait(false);

                    // ① まずアダプタ別優先ネットワークを試す
                    var preferred = _adapterPrefs.PickBestSsid(ev.AdapterId,
                        scan.Where(n => n.HasProfile).Select(n => n.Ssid));

                    MWC.Core.Models.WifiNetwork? candidate = null;
                    if (preferred is not null)
                        candidate = scan.FirstOrDefault(n => n.Ssid == preferred);

                    // ② 次に履歴(全アダプタ共通)から探す
                    if (candidate is null)
                    {
                        var recent = _history.GetRecentSsids(5);
                        candidate = recent
                            .Select(ssid => scan.FirstOrDefault(n => n.Ssid == ssid && n.HasProfile))
                            .FirstOrDefault(n => n is not null);
                    }

                    if (candidate is null) continue;

                    // このアダプター上で別の SSID に切り替わったら、他候補の失敗回数は無効。
                    // (同一キーのみ残すことで「電波が戻った別ネットワーク」の試行を妨げない)
                    ResetFailuresExcept(ev.AdapterId, candidate.Ssid);

                    var key      = (ev.AdapterId, candidate.Ssid);
                    var failures = _consecutiveFailures.GetValueOrDefault(key);

                    // 打ち切り: 一時的失敗でも上限に達したら諦める。
                    // 決定的失敗 (BadCredentials 等) は下で 1 回目に即時打ち切る。
                    if (failures >= _retry.MaxAttempts)
                    {
                        _log.LogInformation(
                            "AutoReconnect: giving up on {ssid} after {n} consecutive failures",
                            PiiMask.Ssid(candidate.Ssid), failures);
                        continue;
                    }

                    // 指数バックオフ + Full Jitter。初回 (failures == 0) は待たない。
                    if (failures > 0)
                    {
                        var delay = _retry.ComputeDelay(failures - 1);
                        _log.RetryBackoff(failures, (long)delay.TotalMilliseconds);
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                    }

                    _log.LogInformation("AutoReconnect: trying {ssid}", PiiMask.Ssid(candidate.Ssid));
                    var res = await _executor.ConnectAsync(
                        ev.AdapterId, candidate.Ssid, candidate.Auth,
                        "", TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);

                    if (res.Success)
                    {
                        _consecutiveFailures.TryRemove(key, out _);
                        _notify.NotifyConnected(candidate.Ssid, res.HasInternet, res.BehindCaptivePortal);
                    }
                    else
                    {
                        var failure = res.Failure ?? MWC.Core.Models.ConnectionFailure.Unknown;

                        // 決定的失敗 (認証情報誤り・権限不足・不正プロファイル等) は
                        // 何度試しても同じ結果になる。上限まで数えず即座に打ち切る。
                        _consecutiveFailures[key] = RetryPolicy.IsRetriable(failure)
                            ? failures + 1
                            : _retry.MaxAttempts;

                        if (!RetryPolicy.IsRetriable(failure))
                            _log.LogInformation(
                                "AutoReconnect: {ssid} failed with non-retriable {failure} — will not retry",
                                PiiMask.Ssid(candidate.Ssid), failure);

                        _notify.NotifyFailed(candidate.Ssid, failure);
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { _log.LogWarning(ex, "AutoReconnect error"); }
            }
        }
        catch (OperationCanceledException)
        {
            // シャットダウン時の正常終了 (DisposeAsync が _cts.Cancel() する経路)
        }
        catch (Exception ex)
        {
            // await foreach の列挙自体が失敗した場合 (SubscribeEventsAsync 側の異常)。
            // ここに来ると監視ループ全体が終了する — 個々のイベント処理失敗は
            // 内側の try/catch で継続済みなので、ここに到達するのは列挙不能な重大な状態。
            _log.LogError(ex, "AutoReconnect watch loop terminated unexpectedly");
        }
    }

    /// <summary>
    /// 指定アダプターについて、いま試そうとしている SSID 以外の失敗カウントを消す。
    /// 別ネットワークへ移動した場合に、以前の環境での失敗回数が新しい接続先の
    /// バックオフや打ち切り判定に影響しないようにする。
    /// </summary>
    private void ResetFailuresExcept(Guid adapterId, string keepSsid)
    {
        foreach (var k in _consecutiveFailures.Keys)
            if (k.Item1 == adapterId && k.Item2 != keepSsid)
                _consecutiveFailures.TryRemove(k, out _);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        // 監視ループの実完了を待ってから CTS を破棄する (固定 Delay ではなく確実に)
        if (_watchLoop is not null)
        {
            try { await _watchLoop.ConfigureAwait(false); }
            catch (Exception ex) { _log.LogDebug(ex, "AutoReconnect watch loop ended during dispose"); }
        }
        _cts.Dispose();
    }
}