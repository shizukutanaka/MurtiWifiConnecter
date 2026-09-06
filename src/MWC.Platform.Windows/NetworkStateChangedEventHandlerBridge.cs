using System;
using System.Collections.Generic;
using ManagedNativeWifi;
using Microsoft.Extensions.Logging;

namespace MWC.Platform.Windows;

// ManagedNativeWifi のバージョン差を吸収する型エイリアス
// 実際のバージョンに合わせて変更してください
using PhyType_ = ManagedNativeWifi.PhyType;
using ChannelBandwidth = ManagedNativeWifi.ChannelBandwidth;   // ⚠ 2026-09: 下記参照。実在しない。

/// <summary>
/// NativeWifi.NetworkStateChanged をラップして型安全に購読する。
/// ManagedNativeWifi のバージョンごとに実装が異なるため、ここで吸収 …する**つもりだった**。
///
/// ⚠ **2026-09 実測: このファイルは現在のピン留めバージョンではコンパイルできない。**
/// `github.com/emoacht/ManagedNativeWifi`(NuGet に固定されている 3.0.2。
/// リポジトリ HEAD の `Source/ManagedNativeWifi/ManagedNativeWifi.csproj` が
/// `&lt;Version&gt;3.0.2&lt;/Version&gt;` であることを確認済み)を実際に取得して
/// 突き合わせたところ、以下がいずれも**実在しない**:
///
///   - `NativeWifi.NetworkStateChanged` — `NativeWifi` は static クラスで、
///     `public static event` を 1 つも公開していない(実ソース全体を grep して確認)。
///     状態変化通知は代わりに **`NativeWifiPlayer`**(構築して使う instance クラス)が
///     `NetworkRefreshed` / `ConnectionChanged` / `InterfaceChanged` / `ProfileChanged` /
///     `RadioStateChanged` / `SignalQualityChanged` / `AvailabilityChanged` という
///     **7 つに分かれた** instance イベントとして公開している
///     (`Source/ManagedNativeWifi/NativeWifiPlayer.cs`)。
///   - `ManagedNativeWifi.ChannelBandwidth` — この型は実ソースのどこにも存在しない
///     (大文字小文字を無視しても 0 件)。上の `using` 別名は本体で未使用のまま残っていた。
///
/// つまりこのクラスは書かれた時点から一度もコンパイルできていない
/// (`api.nuget.org` が塞がれておりこのセッションでは検証できなかったが、
/// GitHub からソースを取得して直接確認した — 推測ではない)。
///
/// 修正には単純な名前の付け替えでは済まない: 呼び出し元
/// (`ConnectionWaiter`)が期待する「1 つの状態変化イベント」という設計を、
/// 実 API の「7 種の instance イベント」にどう対応させるかという設計判断が要る。
/// 加えて `NativeWifiPlayer` は `IDisposable` の instance であり、現在の
/// static add/remove の使い方とはライフサイクルモデルが異なる。
/// **実機 Windows で検証できるセッションが設計・実装すべき**
/// (詳細は `docs/COMPLETION-CHECKLIST.md` の該当項目)。
/// </summary>
internal static class NetworkStateChangedEventHandlerBridge
{
    private static readonly List<Action<object?, NetworkStateChangedEventArgs>> _subs = new();
    private static bool _registered;

    private static void EnsureRegistered(ILogger? log)
    {
        if (_registered) return;
        try
        {
            NativeWifi.NetworkStateChanged += OnNativeChanged;
            _registered = true;
        }
        catch (Exception ex)
        {
            log?.LogDebug(ex, "NativeWifi.NetworkStateChanged unavailable in this ManagedNativeWifi version");
        }
    }

    private static void OnNativeChanged(object? sender, NetworkStateChangedEventArgs e)
    {
        lock (_subs)
            foreach (var sub in _subs) sub(sender, e);
    }

    public static void Subscribe(Action<object?, NetworkStateChangedEventArgs> handler, ILogger? log = null)
    {
        lock (_subs)
        {
            EnsureRegistered(log);
            _subs.Add(handler);
        }
    }

    public static void Unsubscribe(Action<object?, NetworkStateChangedEventArgs> handler)
    {
        lock (_subs) _subs.Remove(handler);
    }
}
