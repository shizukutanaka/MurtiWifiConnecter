// tools/stubs/PlatformWindowsLocalTypes.Stub.cs — 型検査専用。
//
// WindowsWifiService.cs は同一プロジェクト内の ConnectionWaiter /
// NetworkStateChangedEventHandlerBridge / NetworkStateChangedEventArgs /
// ConnectionOutcome に依存しているが、それらは目下 `NativeWifi.NetworkStateChanged`
// という実在しないイベントを購読していて別の理由でコンパイルできない
// (既知・docs/COMPLETION-CHECKLIST.md §5 に文書化済み。実機 Windows での
// 設計判断が要るため意図的にまだ直していない)。WindowsWifiService.cs 自身に
// (それとは独立の)新たな欠陥が無いかを隔離して継続検査するため、これらの
// シグネチャだけを実ファイルからそのまま複製する(同一プロジェクトの自製型で
// あり、サードパーティ API の逆算ではないため循環しない)。
//
// ⚠ 実ファイル (ConnectionWaiter.cs / NetworkStateChangedEventHandlerBridge.cs) の
// 公開シグネチャを変えたら、このスタブも同時に更新すること。ずれると
// tools/typecheck-platform.sh の検査が無意味になる。
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MWC.Platform.Windows;

internal enum ConnectionOutcome { Connected, BadCredentials, NotInRange, Timeout, Cancelled, Failed }

internal sealed class ConnectionWaiter : IDisposable
{
    public ConnectionWaiter(Guid adapterId, ILogger log) { }
    public Task<ConnectionOutcome> WaitAsync(TimeSpan timeout, CancellationToken ct) => throw new NotSupportedException();
    public void Dispose() { }
}

public sealed class NetworkStateChangedEventArgs : EventArgs
{
    public Guid InterfaceId { get; init; }
    public string State { get; init; } = "";
    public string? Reason { get; init; }
    public string? ConnectionMode { get; init; }
}

internal static class NetworkStateChangedEventHandlerBridge
{
    public static void Subscribe(Action<object?, NetworkStateChangedEventArgs> handler, ILogger? log = null) { }
    public static void Unsubscribe(Action<object?, NetworkStateChangedEventArgs> handler) { }
}
