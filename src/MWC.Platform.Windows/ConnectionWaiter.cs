using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MWC.Platform.Windows;

internal enum ConnectionOutcome
{
    Connected,
    BadCredentials,
    NotInRange,
    Timeout,
    Cancelled,
    Failed
}

/// <summary>
/// 接続要求後、ACM connection_complete 通知を待機。
/// netshのExitCode依存ではなく実通知で判定する。
///
/// **2026-11-16 解決**: 購読先だった `NativeWifi.NetworkStateChanged` は
/// ManagedNativeWifi 3.0.2 に存在せず、本クラスはコンパイルできない状態だった。
/// 現在は <see cref="NetworkStateChangedEventHandlerBridge"/> 経由で実 API
/// (`NativeWifiPlayer.ConnectionChanged`)を購読する。対応付けの設計判断と
/// 根拠は Bridge 側の class doc に一本化してある。CLAUDE.md が必須事項として
/// 掲げる「接続成功は WlanNotification の connection_complete 受信 + 疎通確認
/// の 2 段」のうち、前段を担うのが本クラスである。
///
/// 既知の制限: 実 API は WLAN の理由コードを公開しないため、通知の
/// `Reason` は常に null になり、下記の BadCredentials / NotInRange 分類は
/// 発火しない(失敗は Failed になる)。理由コードの取得には生の
/// WlanNotification の P/Invoke が別途必要。
/// </summary>
internal sealed class ConnectionWaiter : IDisposable
{
    private readonly Guid _adapterId;
    private readonly ILogger _log;
    // RunContinuationsAsynchronously 必須: TrySetResult はネイティブ WLAN 通知の
    // コールバックスレッドから呼ばれる。これが無いと WaitAsync の await 以降
    // (疎通確認の HTTP プローブや waiter の Dispose) が通知スレッド上で同期実行され、
    // 以降の WLAN 通知配信を遅延/デッドロックさせうる。
    private readonly TaskCompletionSource<ConnectionOutcome> _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly EventHandler<NetworkStateChangedEventArgs> _handler;
    private bool _disposed;

    public ConnectionWaiter(Guid adapterId, ILogger log)
    {
        _adapterId = adapterId;
        _log = log;

        _handler = (sender, e) =>
        {
            if (e.InterfaceId != _adapterId) return;

            var s = e.State?.ToLowerInvariant() ?? "";
            _log.LogDebug("WLAN event: {State} adapter={Id}", s, e.InterfaceId);

            if (s == "connected")
            {
                _tcs.TrySetResult(ConnectionOutcome.Connected);
            }
            else if (s.Contains("disconnect") || s.Contains("fail"))
            {
                // 認証失敗判定: 直前イベントが authenticating だったか
                // 実プロダクトではWlanNotification未加工データ取得で
                // wlan_reason_code を読むのが理想。簡易版で代用。
                if (e.Reason?.Contains("auth", StringComparison.OrdinalIgnoreCase) == true ||
                    e.Reason?.Contains("key",  StringComparison.OrdinalIgnoreCase) == true)
                {
                    _tcs.TrySetResult(ConnectionOutcome.BadCredentials);
                }
                else if (IsNotInRangeReason(e.Reason))
                {
                    _tcs.TrySetResult(ConnectionOutcome.NotInRange);
                }
                else
                {
                    _tcs.TrySetResult(ConnectionOutcome.Failed);
                }
            }
        };

        NetworkStateChangedEventHandlerBridge.Subscribe(_handler, _log);
    }

    public async Task<ConnectionOutcome> WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        using var reg = cts.Token.Register(() =>
        {
            if (ct.IsCancellationRequested)
                _tcs.TrySetResult(ConnectionOutcome.Cancelled);
            else
                _tcs.TrySetResult(ConnectionOutcome.Timeout);
        });

        return await _tcs.Task;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        NetworkStateChangedEventHandlerBridge.Unsubscribe(_handler);
    }

    // Match WLAN reason code strings that indicate the BSS/network was not found.
    // Covers both enum-style names (e.g. "network_not_available") and fragments of
    // localized WlanReasonCodeToString output (e.g. "cannot be found").
    private static bool IsNotInRangeReason(string? reason) =>
        reason is not null &&
        (reason.Contains("not_available",   StringComparison.OrdinalIgnoreCase) ||
         reason.Contains("not_found",       StringComparison.OrdinalIgnoreCase) ||
         reason.Contains("no_match",        StringComparison.OrdinalIgnoreCase) ||
         reason.Contains("cannot be found", StringComparison.OrdinalIgnoreCase) ||
         reason.Contains("not available",   StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// 接続状態変化の統一表現。<see cref="NetworkStateChangedEventHandlerBridge"/>
/// が実 API (`NativeWifiPlayer.ConnectionChanged`) から詰めて渡す。
/// State は文字列のままにしてあるのは、購読側の判定が語彙ベース
/// ("connected" / "disconnect" を含むか等)で書かれているため。
/// </summary>
public sealed class NetworkStateChangedEventArgs : EventArgs
{
    public Guid InterfaceId { get; init; }
    public string State { get; init; } = "";
    public string? Reason { get; init; }
    public string? ConnectionMode { get; init; }
}
