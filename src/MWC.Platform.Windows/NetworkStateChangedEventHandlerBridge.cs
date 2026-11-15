using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using ManagedNativeWifi;
using Microsoft.Extensions.Logging;

namespace MWC.Platform.Windows;

/// <summary>
/// 接続状態の変化通知を、呼び出し元が期待する「1 つの状態変化イベント」
/// (<see cref="NetworkStateChangedEventArgs"/>)に統一して購読させるための橋渡し。
///
/// ── 経緯(2026-09 実測) ───────────────────────────────
/// 当初このクラスは `NativeWifi.NetworkStateChanged` という static イベントを
/// 購読する前提で書かれていたが、そのようなイベントは ManagedNativeWifi 3.0.2
/// (ピン留めバージョン。NuGet 配布物 DLL と GitHub 実ソースの両方で確認)に
/// **存在しない**。`NativeWifi` は static クラスでイベントを 1 つも公開しておらず、
/// 状態変化通知は `NativeWifiPlayer`(構築して使う `IDisposable` の instance
/// クラス)が 7 つの instance イベントとして公開している:
/// `NetworkRefreshed` / `ConnectionChanged` / `InterfaceChanged` /
/// `ProfileChanged` / `RadioStateChanged` / `SignalQualityChanged` /
/// `AvailabilityChanged`。
///
/// ── 設計判断(2026-11-16) ────────────────────────────
/// 1. **転送するのは `ConnectionChanged` のみ。**
///    自製型 <see cref="NetworkStateChangedEventArgs"/> の
///    State/Reason/ConnectionMode という契約が表すのは「接続状態の遷移」であり、
///    7 イベントの中でその意味を持つのは `ConnectionChanged` だけである。
///    残り 6 つ(スキャン完了、アダプター着脱、プロファイル変更、無線 ON/OFF、
///    シグナル品質、到達可能性)は別の切り口の通知で、接続状態の語彙に
///    無理に押し込むと購読側(`ConnectionWaiter` の成否判定や
///    `WindowsWifiService` のイベント種別対応)が誤読する。
///    必要になったら、この型とは別の表面として公開すべきもの。
/// 2. **State への対応付け**は購読側が既に前提にしている語彙に合わせる:
///      Completed    → "Connected"   (接続完了。実 API に "Connected" は無い)
///      Started      → "Associating" (遷移中。実 API に接続試行中の細分は無い)
///      Failed       → "Failed"
///      Disconnecting→ "Disconnecting"
///      Disconnected → "Disconnected"
///      Unknown      → "Unknown"
/// 3. **Reason は常に null。**
///    `ConnectionChangedEventArgs.Data`(`ConnectionNotificationData`)が公開する
///    のは ConnectionMode / ProfileName / Ssid / BssType / IsSecurityEnabled
///    のみで、WLAN の理由コード(wlan_reason_code)は含まれない。
///    よって `ConnectionWaiter` の Reason ベースの失敗分類
///    (BadCredentials / NotInRange)は発火せず、失敗は Failed に落ちる。
///    これは実 API の限界であり、理由コードが要るなら生の WlanNotification を
///    別途 P/Invoke で受ける必要がある(現時点では未実装)。
/// 4. **NativeWifiPlayer のライフサイクル**: 購読者が 1 人以上いる間だけ
///    保持し、最後の購読解除で Dispose する。static なファサードの裏に
///    instance を隠すことで、呼び出し元 2 か所(`ConnectionWaiter` /
///    `WindowsWifiService.SubscribeEventsAsync`)の購読モデルは変えず、
///    誰も聞いていない間に WLAN 通知登録をぶら下げ続けることもない。
///    Player の生成に失敗した場合は例外を伝播させず「イベントが来ない」
///    状態に劣化し、次の Subscribe で再試行する(従来の static 購読時の
///    フォールバックと同じ考え方)。
/// </summary>
internal static class NetworkStateChangedEventHandlerBridge
{
    private static readonly List<EventHandler<NetworkStateChangedEventArgs>> _subs = new();
    private static NativeWifiPlayer? _player;

    public static void Subscribe(EventHandler<NetworkStateChangedEventArgs> handler, ILogger? log = null)
    {
        lock (_subs)
        {
            if (_subs.Count == 0)
                EnsurePlayer(log);
            _subs.Add(handler);
        }
    }

    public static void Unsubscribe(EventHandler<NetworkStateChangedEventArgs> handler)
    {
        lock (_subs)
        {
            _subs.Remove(handler);
            if (_subs.Count == 0)
                ReleasePlayer();
        }
    }

    // Player 生成は劣化境界: 失敗しても購読登録自体は成立させ、
    // 「通知が来ない」状態として扱う。WLAN サービス停止中などに
    // 接続処理全体を例外で落とす方が利用者にとって悪い。
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Degradation boundary: if WLAN notification registration is unavailable, subscribers simply receive no events; creation is retried on the next Subscribe.")]
    private static void EnsurePlayer(ILogger? log)
    {
        if (_player is not null) return;
        try
        {
            var player = new NativeWifiPlayer();
            player.ConnectionChanged += OnConnectionChanged;
            _player = player;
        }
        catch (Exception ex)
        {
            log?.LogDebug(ex, "NativeWifiPlayer unavailable; connection-state events will not be delivered");
        }
    }

    private static void ReleasePlayer()
    {
        if (_player is null) return;
        _player.ConnectionChanged -= OnConnectionChanged;
        _player.Dispose();
        _player = null;
    }

    private static void OnConnectionChanged(object? sender, ConnectionChangedEventArgs e)
    {
        var args = new NetworkStateChangedEventArgs
        {
            InterfaceId    = e.InterfaceId,
            State          = MapState(e.ChangedState),
            // ConnectionNotificationData は理由コードを公開しない (class doc 参照)
            Reason         = null,
            ConnectionMode = e.Data?.ConnectionMode.ToString(),
        };

        // 購読解除中の変更と競合しないようスナップショットを取り、
        // ロックの外で呼ぶ (ハンドラ内から Unsubscribe されても止まらない)。
        EventHandler<NetworkStateChangedEventArgs>[] snapshot;
        lock (_subs)
            snapshot = _subs.ToArray();
        foreach (var sub in snapshot)
            sub(sender, args);
    }

    private static string MapState(ConnectionChangedState state) => state switch
    {
        ConnectionChangedState.Completed     => "Connected",
        ConnectionChangedState.Started       => "Associating",
        ConnectionChangedState.Failed        => "Failed",
        ConnectionChangedState.Disconnecting => "Disconnecting",
        ConnectionChangedState.Disconnected  => "Disconnected",
        _                                    => "Unknown",
    };
}
