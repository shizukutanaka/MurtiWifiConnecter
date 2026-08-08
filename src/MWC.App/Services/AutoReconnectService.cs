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

    // ── Evil Twin 検査 ────────────────────────────────────────────────
    // 自動再接続は evil twin 攻撃の主要な侵入口である: 攻撃者が既知の SSID を持つ
    // 偽 AP を立てるだけで、ユーザーが何も操作しなくても端末が自動的に接続してしまう。
    // 特に危険なのがセキュリティ・ダウングレード (WPA2 だったはずの SSID が Open で出現)。
    // 手動接続なら NetworkDetailViewModel が画面で警告を出せるが、
    // 自動再接続は無人であり、誰も警告を見ない — 保護が最も必要なのはこちら側である。
    //
    // EvilTwinDetector は既存実装 (Core) をそのまま再利用する。
    // 学習データ (RecordTrusted) が無い間は検査 2〜4 が発火せず、
    // 検査 1 (同一 SSID に複数のセキュリティ設定が同時に見える) のみが働くため、
    // 初回利用時に正当な接続を誤ってブロックする危険は小さい。
    //
    // スレッド安全性: EvilTwinDetector は内部で素の Dictionary を使っており
    // スレッド安全ではない。このインスタンスは本サービス専用で、単一の監視ループ
    // (WatchLoop) からのみ Analyze/RecordTrusted を呼ぶため保護は不要。
    // 他所と共有してはならない。
    private readonly EvilTwinDetector _evilTwin = new();

    // ── 信頼ベースラインの永続化 ──────────────────────────────────────
    // 検査 2〜4 は過去の学習を前提とするため、学習がプロセスメモリ限りだと
    // アプリ再起動のたびに消え、直後は検査 1 しか発火しない = 理由が 1 件までしか
    // 積まれず HighRisk (2 件以上) に到達できない。つまり再起動直後は
    // この防御が事実上無効になる。不正 AP 検出は信頼済み SSID/BSSID の
    // ベースラインを事前に確立しておくことが前提の技術であり、永続化は必須。
    //
    // 保存先・書式は NetworkHistoryService / AdapterPreferencesService と同じ流儀
    // (LocalApplicationData/MWC 配下の JSON)。
    private static readonly string BaselinePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MWC", "trusted-aps.json");

    // 上限。NetworkHistoryService の MaxEntries=500 と同程度の水準に揃える。
    // 超過分は捨てる — ベースラインは「よく使う AP を守る」ためのもので、
    // 全履歴の保存が目的ではない。
    private const int MaxBaselineEntries = 500;

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
        // 監視開始前にベースラインを復元する。これが無いと再起動直後は
        // 検査 1 しか働かず HighRisk に到達できないため、Evil Twin 防御が
        // 事実上無効な状態で自動再接続が動いてしまう。
        LoadBaseline();
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

                    // Evil Twin 検査。HighRisk (独立した指標が 2 つ以上一致) の場合のみ
                    // 自動接続を中止する。Suspicious (指標 1 つ) で止めないのは、
                    // 正当な AP 増設・機器交換でも単一指標は発生しうるため — 無人動作で
                    // 正当な再接続を妨げる害と、攻撃を許す害の釣り合いを取る判断。
                    // ユーザーが手動接続する経路には従来どおり画面警告が出る。
                    var verdict = _evilTwin.Analyze(candidate, scan);
                    if (verdict.Risk == EvilTwinRisk.HighRisk)
                    {
                        _log.LogWarning(
                            "AutoReconnect: refusing {ssid} — evil twin suspected ({reasons})",
                            PiiMask.Ssid(candidate.Ssid), string.Join("; ", verdict.Reasons));
                        _notify.NotifyFailed(candidate.Ssid, MWC.Core.Models.ConnectionFailure.ProfileRejected);
                        continue;
                    }

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

                        // 成功した接続を「信頼できる基準」として学習させる。
                        // これにより次回以降、BSSID/ベンダー/セキュリティ方式の
                        // 差異を検出できるようになる (学習しなければ検査 2〜4 は永久に無効)。
                        var bssid = candidate.BssEntries.FirstOrDefault()?.Bssid;
                        if (!string.IsNullOrEmpty(bssid))
                        {
                            _evilTwin.RecordTrusted(candidate.Ssid, bssid, candidate.Auth);
                            // 学習内容をディスクへ残す。これが無いと再起動で消え、
                            // 検査 2〜4 が永久に立ち上がらない。
                            SaveBaseline();
                        }

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
    /// 信頼ベースラインをディスクから復元する。監視ループ開始前に 1 回だけ呼ぶ。
    /// 失敗しても致命的ではない (学習し直せる) ため、例外は握りつぶして続行する —
    /// ベースラインが無いことより、自動再接続自体が起動しないことの方が害が大きい。
    /// </summary>
    private void LoadBaseline()
    {
        try
        {
            if (!System.IO.File.Exists(BaselinePath)) return;

            var json = System.IO.File.ReadAllText(BaselinePath);
            var data = System.Text.Json.JsonSerializer
                .Deserialize<List<MWC.Core.Services.TrustedApBaseline>>(json);
            if (data is { Count: > 0 })
            {
                _evilTwin.ImportBaseline(data);
                _log.LogDebug("Evil-twin baseline restored: {n} networks", data.Count);
            }
        }
        catch (System.Text.Json.JsonException ex)
        {
            // 破損データは復旧不能。次回の保存で上書きされるので削除はしない。
            _log.LogWarning(ex, "Trusted-AP baseline is corrupt; starting with an empty baseline");
        }
        catch (System.IO.IOException ex)          { _log.LogWarning(ex, "Could not read trusted-AP baseline"); }
        catch (UnauthorizedAccessException ex)     { _log.LogWarning(ex, "Access denied reading trusted-AP baseline"); }
    }

    /// <summary>
    /// 信頼ベースラインをディスクへ保存する。接続成功で学習が増えた直後に呼ぶ。
    /// 保存失敗は次回の成功時に再試行されるため、例外は握りつぶす。
    /// </summary>
    private void SaveBaseline()
    {
        try
        {
            var snapshot = _evilTwin.ExportBaseline();
            if (snapshot.Count > MaxBaselineEntries)
                snapshot = snapshot.Take(MaxBaselineEntries).ToList();

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(BaselinePath)!);
            System.IO.File.WriteAllText(BaselinePath,
                System.Text.Json.JsonSerializer.Serialize(snapshot));
        }
        catch (System.IO.IOException ex)        { _log.LogDebug(ex, "Could not persist trusted-AP baseline"); }
        catch (UnauthorizedAccessException ex)   { _log.LogDebug(ex, "Access denied persisting trusted-AP baseline"); }
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