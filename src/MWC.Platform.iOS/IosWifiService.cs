using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MWC.Core.Abstractions;
using MWC.Core.Models;

namespace MWC.Platform.iOS;

/// <summary>
/// iOS NEHotspotConfiguration 経由の IWifiService 実装。
///
/// .NET MAUI / Xamarin.iOS で動作。iOS 11+ が必要。
///
/// iOS の Wi-Fi API 制約:
///   - スキャン機能: CNCopySupportedInterfaces + CNCopyCurrentNetworkInfo のみ
///     (現在接続中の SSID のみ取得可、スキャン結果一覧は取得不可)
///   - 接続: NEHotspotConfiguration (iOS 11+)
///     ユーザーの明示的な確認ダイアログが表示される
///   - Enterprise: NEHotspotConfigurationManager.apply() でプロファイル適用
///
/// 必要なエンタイトルメント:
///   - com.apple.developer.networking.wifi-info (現在の SSID 取得)
///   - com.apple.developer.networking.HotspotConfiguration (接続)
///   - com.apple.developer.networking.multipath (Multipath TCP)
///
/// App Store 審査:
///   - HotspotConfiguration エンタイトルメントは Apple の審査が必要
///   - 「Location Services を有効にしたアプリ」としてプロビジョニングが必要
/// </summary>
public sealed class IosWifiService : IWifiService
{
    public async Task<IReadOnlyList<WifiAdapter>> GetAdaptersAsync(CancellationToken ct = default)
    {
        // iOS では Wi-Fi アダプター情報は直接取得できない
        // CNCopySupportedInterfaces() で en0 などが返る
        return await Task.FromResult<IReadOnlyList<WifiAdapter>>(new[]
        {
            new WifiAdapter
            {
                Id          = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                Name        = "en0",
                Description = "iOS Wi-Fi",
                IsEnabled   = true,
            }
        }).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WifiNetwork>> ScanAsync(
        Guid adapterId, CancellationToken ct = default)
    {
        // iOS ではスキャン API が公開されていないため
        // CNCopyCurrentNetworkInfo で現在の接続 SSID のみ返す
        //
        // 実装例:
        // var info = CaptiveNetwork.TryCopyCurrentNetworkInfo("en0", out var dict);
        // var ssid = dict?[CaptiveNetwork.NetworkInfoKeySSID]?.ToString();

        return await Task.FromResult<IReadOnlyList<WifiNetwork>>(
            Array.Empty<WifiNetwork>()).ConfigureAwait(false);
    }

    public Task<bool> RegisterProfileAsync(
        Guid adapterId, string profileXml, bool overwrite, CancellationToken ct = default)
    {
        // iOS では NEHotspotConfigurationManager.shared.apply(config) を使用
        // Enterprise 向け: NEHotspotEAPSettings で EAP 設定
        // 未実装スタブ — 登録は行われないため false。
        return Task.FromResult(false);
    }

    public async Task<ConnectionResult> ConnectAsync(
        Guid adapterId, string ssid, string profileName,
        TimeSpan timeout, CancellationToken ct = default)
    {
        // NEHotspotConfiguration(ssid: passphrase:) → apply()
        // ユーザーが確認ダイアログで許可した場合のみ接続
        //
        // 実装例:
        // var config = new NEHotspotConfiguration(ssid, passphrase, isWEP: false);
        // config.JoinOnce = false;
        // await NEHotspotConfigurationManager.SharedManager.ApplyConfigurationAsync(config);
        //
        // エラーコード: NEHotspotConfigurationError.AlreadyAssociated(自動接続中)
        //              NEHotspotConfigurationError.UserDenied(ユーザーが拒否)

        return await Task.FromResult(ConnectionResult.Fail(ConnectionFailure.OsError))
            .ConfigureAwait(false);
    }

    public async Task<bool> DisconnectAsync(Guid adapterId, CancellationToken ct = default)
    {
        // NEHotspotConfigurationManager.removeConfiguration(forSSID:)
        // ただし接続中の SSID を直接切断する API は iOS 16 まで存在しない
        return await Task.FromResult(false).ConfigureAwait(false);
    }
}
