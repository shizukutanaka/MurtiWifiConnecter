using System;
using System.Collections.Generic;
using ManagedNativeWifi;

namespace MWC.Platform.Windows;

// ManagedNativeWifi のバージョン差を吸収する型エイリアス
// 実際のバージョンに合わせて変更してください
using PhyType_ = ManagedNativeWifi.PhyType;
using ChannelBandwidth = ManagedNativeWifi.ChannelBandwidth;

/// <summary>
/// NativeWifi.NetworkStateChanged をラップして型安全に購読する。
/// ManagedNativeWifi のバージョンごとに実装が異なるため、ここで吸収。
/// </summary>
internal static class NetworkStateChangedEventHandlerBridge
{
    private static readonly List<Action<object?, NetworkStateChangedEventArgs>> _subs = new();

    static NetworkStateChangedEventHandlerBridge()
    {
        // ManagedNativeWifi の NetworkStateChanged を購読
        // 版差異がある場合はここを修正
        try
        {
            NativeWifi.NetworkStateChanged += OnNativeChanged;
        }
        catch { /* イベントが存在しない版では無視 */ }
    }

    private static void OnNativeChanged(object? sender, NetworkStateChangedEventArgs e)
    {
        lock (_subs)
            foreach (var sub in _subs) sub(sender, e);
    }

    public static void Subscribe(Action<object?, NetworkStateChangedEventArgs> handler)
    {
        lock (_subs) _subs.Add(handler);
    }

    public static void Unsubscribe(Action<object?, NetworkStateChangedEventArgs> handler)
    {
        lock (_subs) _subs.Remove(handler);
    }
}
