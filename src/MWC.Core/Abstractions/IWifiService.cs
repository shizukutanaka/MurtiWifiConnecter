using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MWC.Core.Models;

namespace MWC.Core.Abstractions;

/// <summary>
/// プラットフォーム非依存のWiFi操作IF。
/// Windows実装は MWC.Platform.Windows.WindowsWifiService。
/// テストは FakeWifiService。
/// </summary>
public interface IWifiService
{
    /// <summary>無線アダプター列挙</summary>
    Task<IReadOnlyList<WifiAdapter>> GetAdaptersAsync(CancellationToken ct = default);

    /// <summary>指定アダプターでスキャン実行→結果取得</summary>
    Task<IReadOnlyList<WifiNetwork>> ScanAsync(Guid adapterId, CancellationToken ct = default);

    /// <summary>プロファイル登録(冪等)</summary>
    Task<bool> RegisterProfileAsync(Guid adapterId, string profileXml, bool overwrite, CancellationToken ct = default);

    /// <summary>接続実行+完了待機+疎通確認</summary>
    Task<ConnectionResult> ConnectAsync(Guid adapterId, string profileName, string ssid,
        TimeSpan timeout, CancellationToken ct = default);

    /// <summary>切断</summary>
    Task<bool> DisconnectAsync(Guid adapterId, CancellationToken ct = default);

    /// <summary>プロファイル削除</summary>
    Task<bool> DeleteProfileAsync(Guid adapterId, string profileName, CancellationToken ct = default);

    /// <summary>登録済みプロファイル一覧</summary>
    Task<IReadOnlyList<string>> ListProfilesAsync(Guid adapterId, CancellationToken ct = default);

    /// <summary>状態変化通知購読</summary>
    IAsyncEnumerable<WifiEvent> SubscribeEventsAsync(CancellationToken ct = default);
}

public readonly record struct WifiEvent(
    Guid AdapterId,
    WifiEventType Type,
    string? Ssid,
    DateTimeOffset At);

public enum WifiEventType
{
    ScanComplete,
    Connecting,
    Connected,
    Disconnected,
    Failed
}
