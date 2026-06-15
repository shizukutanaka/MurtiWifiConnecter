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

    /// <summary>
    /// 指定アダプターでスキャン実行→結果取得。
    ///
    /// 契約: 結果は <b>SSID 単位で一意</b>であること。同一 SSID が複数バンド
    /// (2.4/5/6GHz) や複数 AP (メッシュ) で観測される場合、それらの BSS は
    /// 1 つの <see cref="WifiNetwork"/> の <see cref="WifiNetwork.BssEntries"/> に
    /// 集約し、代表値 (band/channel/signal 等) は最強シグナルの BSS を採用する。
    /// 隠し (空 SSID) ネットワークは結果に含めない。
    ///
    /// この一意性は <see cref="WifiNetwork.Ssid"/> をキーに扱う全消費側
    /// (SignalHistoryService のリングバッファ、NetworkFilterViewModel の重複排除、
    /// AdapterViewModel の差分更新 ToDictionary 等) が前提とする。BSS 単位の
    /// 行を返すと重複キーで例外/履歴破損を招く。
    /// </summary>
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
