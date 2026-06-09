using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MWC.Core.Abstractions;
using MWC.Core.Models;

namespace MWC.Platform.Android;

/// <summary>
/// Android WifiManager 経由の IWifiService 実装。
///
/// .NET MAUI で動作。Android API 29+ (Android 10+) が必要。
///
/// 必要な権限 (AndroidManifest.xml):
///   - ACCESS_WIFI_STATE
///   - CHANGE_WIFI_STATE
///   - ACCESS_FINE_LOCATION (Android 9+ でスキャン必須)
///   - CHANGE_NETWORK_STATE
///   - ACCESS_NETWORK_STATE
///   - NEARBY_WIFI_DEVICES (Android 13+)
///
/// 注意事項:
///   - Android 9+ で WEP 接続は非推奨
///   - Android 10+ でパスワードなし Open 接続に制限あり
///   - Android 12+ で SSID 一覧取得に NEARBY_WIFI_DEVICES 権限が必要
///   - Android 13+ で権限モデルが再設計された (WifiNetworkSpecifier)
/// </summary>
public sealed class AndroidWifiService : IWifiService
{
    // MAUI では Platform.CurrentActivity から WifiManager を取得する
    // private WifiManager? _wifiManager =>
    //     Platform.CurrentActivity?.GetSystemService(Context.WifiService) as WifiManager;

    public async Task<IReadOnlyList<WifiAdapter>> GetAdaptersAsync(CancellationToken ct = default)
    {
        // Android では基本的に Wi-Fi アダプターは1つ
        // API 29+ の WifiManager.getConnectionInfo() で現在の接続情報を取得
        return await Task.FromResult<IReadOnlyList<WifiAdapter>>(new[]
        {
            new WifiAdapter
            {
                Id          = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                Name        = "wlan0",
                Description = "Android Wi-Fi",
                IsEnabled   = true,  // WifiManager.isWifiEnabled()
            }
        }).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WifiNetwork>> ScanAsync(
        Guid adapterId, CancellationToken ct = default)
    {
        // Android 9+: WifiManager.startScan() は非推奨
        // 推奨: WifiManager.getScanResults() で最後のスキャン結果を取得
        // ScanResult を WifiNetwork に変換

        // 実装例 (MAUI コンテキスト):
        // var wm = _wifiManager ?? throw new InvalidOperationException("WifiManager unavailable");
        // var results = wm.ScanResults;
        // return results.Select(r => new WifiNetwork { ... }).ToList();

        return await Task.FromResult<IReadOnlyList<WifiNetwork>>(
            Array.Empty<WifiNetwork>()).ConfigureAwait(false);
    }

    public Task<bool> RegisterProfileAsync(
        Guid adapterId, string profileXml, bool overwrite, CancellationToken ct = default)
    {
        // Android 10+: WifiNetworkSuggestion API を使用
        // Android 29+: WifiManager.addNetworkSuggestions()
        // 未実装スタブ — 登録は行われないため false。
        return Task.FromResult(false);
    }

    public async Task<ConnectionResult> ConnectAsync(
        Guid adapterId, string ssid, string profileName,
        TimeSpan timeout, CancellationToken ct = default)
    {
        // Android 10+ (API 29): WifiNetworkSpecifier を使った接続
        // WifiManager.requestNetwork(NetworkRequest, ConnectivityManager.NetworkCallback)

        // Android 11+ (API 30): Internet接続のある Wi-Fi には WifiNetworkSuggestion が必要
        return await Task.FromResult(ConnectionResult.Fail(ConnectionFailure.OsError))
            .ConfigureAwait(false);
    }

    public async Task<bool> DisconnectAsync(Guid adapterId, CancellationToken ct = default)
    {
        // Android 10+: ConnectivityManager.bindProcessToNetwork(null) でバインド解除
        // Android 10-: WifiManager.disconnect()
        return await Task.FromResult(true).ConfigureAwait(false);
    }
}
