using System;
using System.Threading;
using System.Threading.Tasks;
using ManagedNativeWifi;
using Microsoft.Extensions.Logging;

namespace MWC.Platform.Windows;

internal enum ConnectionOutcome
{
    Connected,
    BadCredentials,
    Timeout,
    Cancelled,
    Failed
}

/// <summary>
/// 接続要求後、ACM connection_complete 通知を待機。
/// netshのExitCode依存ではなく実通知で判定。
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
            _log.LogDebug("WLAN event: {state} adapter={id}", s, e.InterfaceId);

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
                else
                {
                    _tcs.TrySetResult(ConnectionOutcome.Failed);
                }
            }
        };

        NativeWifi.NetworkStateChanged += _handler;
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
        NativeWifi.NetworkStateChanged -= _handler;
    }
}

/// <summary>
/// ManagedNativeWifi が StateChanged イベントを公開しない版に対応する
/// シム。実装は ManagedNativeWifi のバージョンに合わせて差替。
/// </summary>
public sealed class NetworkStateChangedEventArgs : EventArgs
{
    public Guid InterfaceId { get; init; }
    public string State { get; init; } = "";
    public string? Reason { get; init; }
    public string? ConnectionMode { get; init; }
}
