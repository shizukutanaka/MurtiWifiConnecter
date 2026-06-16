using System;
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
/// 設定: SettingsService.Current.AutoReconnect で on/off
/// </summary>
public sealed class AutoReconnectService : IAsyncDisposable, IDisposable
{
    private readonly IWifiService          _wifi;
    private readonly NetworkHistoryService     _history;
    private readonly NotificationService        _notify;
    private readonly SettingsService            _settings;
    private readonly AdapterPreferencesService  _adapterPrefs;
    private readonly ConnectionExecutor         _executor;
    private readonly ILogger<AutoReconnectService> _log;

    private readonly CancellationTokenSource _cts = new();
    private Task? _watchLoop;
    private bool _disposed;

    public AutoReconnectService(
        IWifiService wifi,
        NetworkHistoryService history,
        NotificationService notify,
        SettingsService settings,
        AdapterPreferencesService adapterPrefs,
        ConnectionExecutor executor,
        ILogger<AutoReconnectService> log)
    {
        _wifi = wifi; _history = history; _notify = notify;
        _settings = settings; _adapterPrefs = adapterPrefs;
        _executor = executor; _log = log;
    }

    public void Start()
    {
        _watchLoop = WatchAsync(_cts.Token);
    }

    private async Task WatchAsync(CancellationToken ct)
    {
        await foreach (var ev in _wifi.SubscribeEventsAsync(ct).ConfigureAwait(false))
        {
            if (ev.Type != WifiEventType.Disconnected) continue;

            await Task.Delay(3000, ct).ConfigureAwait(false);  // 意図的な切断と区別するため少し待つ

            try
            {
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

                _log.LogInformation("AutoReconnect: trying {ssid}", PiiMask.Ssid(candidate.Ssid));
                var res = await _executor.ConnectAsync(
                    ev.AdapterId, candidate.Ssid, candidate.Auth,
                    "", TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);

                if (res.Success)
                    _notify.NotifyConnected(candidate.Ssid, res.HasInternet, res.BehindCaptivePortal);
                else
                    _notify.NotifyFailed(candidate.Ssid, res.Failure ?? MWC.Core.Models.ConnectionFailure.Unknown);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { _log.LogWarning(ex, "AutoReconnect error"); }
        }
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
            catch { /* キャンセル/失敗は無視 */ }
        }
        _cts.Dispose();
    }
}