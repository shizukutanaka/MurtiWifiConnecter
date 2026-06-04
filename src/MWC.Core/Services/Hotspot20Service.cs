using System;
using System.Collections.Generic;
using System.Linq;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// Hotspot 2.0 (Passpoint) サービス。
///
/// Hotspot 2.0 / Passpoint は Wi-Fi Alliance の自動接続規格。
///   - ANQP (Access Network Query Protocol) で AP 情報を事前取得
///   - EAP-SIM/AKA/TLS で自動認証(パスワード入力不要)
///   - ローミング対応(携帯キャリアと Wi-Fi の自動切替)
///
/// 本サービスが提供する機能:
///   1. Passpoint プロファイル仕様の生成
///   2. スキャン結果からの Passpoint AP 識別
///   3. キャリア別設定プリセット (au/SoftBank/docomo 等)
/// </summary>
public sealed class Hotspot20Service
{
    // ── Public API ───────────────────────────────────────────────────

    /// <summary>
    /// スキャン結果から Passpoint 対応 AP を識別して返す。
    /// Passpoint AP は RSN IE に特定 OUI (00:50:F2:04) を含む。
    /// </summary>
    /// <summary>スキャン結果から Passpoint (Hotspot 2.0) 対応 AP のみを返す。</summary>
    public IReadOnlyList<WifiNetwork> FilterPasspointNetworks(
        IReadOnlyList<WifiNetwork> networks)
        => networks.Where(n => n.IsPasspoint).ToList();

    /// <summary>
    /// プリセット設定からキャリア Passpoint プロファイルを生成。
    /// </summary>
    public WifiProfileSpec BuildCarrierProfile(CarrierPasspointPreset preset)
    {
        return new WifiProfileSpec
        {
            Ssid              = preset.Ssid,
            Auth              = preset.UseEapSim ? AuthMethod.WPA2Enterprise : AuthMethod.WPA3Enterprise,
            EapType           = preset.EapType,
            Domain            = preset.Domain,
            ServerNames       = preset.RadiusServers.ToArray(),
        };
    }

    /// <summary>
    /// カスタム Passpoint プロファイルを生成(ホームプロバイダー + ローミングパートナー)。
    /// </summary>
    public Hotspot20Profile BuildProfile(
        string homeOi,
        string domain,
        EapType eapType,
        IEnumerable<string>? roamingOis = null,
        IEnumerable<string>? friendlyNames = null)
    {
        return new Hotspot20Profile(
            HomeOI:       homeOi,
            Domain:       domain,
            EapType:      eapType,
            RoamingOIs:   roamingOis?.ToList() ?? new(),
            FriendlyNames: friendlyNames?.ToList() ?? new());
    }

    /// <summary>
    /// 既知の Passpoint キャリアプリセット一覧。
    /// </summary>
    public static IReadOnlyList<CarrierPasspointPreset> KnownCarriers { get; } = new List<CarrierPasspointPreset>
    {
        // 日本
        new("au Wi-Fi",        "0001de.mno.au.kddi.com",
            "au_wifi",    EapType.EAP_AKA,  true,  new[]{"wifi-auth.au.kddi.com"}),
        new("SoftBank Wi-Fi",  "001ae6.mno.softbank.ne.jp",
            "0000d0wifi", EapType.EAP_AKA,  true,  new[]{"wifi-auth.softbank.ne.jp"}),
        new("docomo Wi-Fi",    "mno.mnc010.mcc440.3gppnetwork.org",
            "0001d0wifi", EapType.EAP_AKA,  true,  new[]{"wifi-auth.nttdocomo.com"}),
        // 海外
        new("AT&T Wi-Fi",      "wlan.mnc410.mcc310.3gppnetwork.org",
            "attwifi",    EapType.EAP_AKA,  true,  new[]{"wifi.attsecurity.com"}),
        new("T-Mobile Wi-Fi",  "wlan.mnc260.mcc310.3gppnetwork.org",
            "tmobile",    EapType.EAP_AKA,  true,  new[]{"wifi.t-mobile.com"}),
        new("Boingo Hotspot",  "boingo.com",
            "Boingo_Passpoint", EapType.PEAP_MSCHAPv2, false, new[]{"eap.boingo.com"}),
    };
}

// ── データ型 ───────────────────────────────────────────────────────────

/// <summary>Hotspot 2.0 プロファイル</summary>
public sealed record Hotspot20Profile(
    string                OrganizationIdentifier = "",
    string                HomeOI                 = "",
    string                Domain                 = "",
    EapType               EapType                = EapType.PEAP_MSCHAPv2,
    IReadOnlyList<string> RoamingOIs             = null!,
    IReadOnlyList<string> FriendlyNames          = null!
)
{
    public IReadOnlyList<string> RoamingOIs  { get; init; } = RoamingOIs  ?? Array.Empty<string>();
    public IReadOnlyList<string> FriendlyNames { get; init; } = FriendlyNames ?? Array.Empty<string>();
}

/// <summary>既知キャリア Passpoint プリセット</summary>
public sealed record CarrierPasspointPreset(
    string                CarrierName,
    string                Domain,
    string                Ssid,
    EapType               EapType,
    bool                  UseEapSim,
    IReadOnlyList<string> RadiusServers);

/// <summary>WifiNetwork への Passpoint 拡張</summary>
public static class WifiNetworkPasspointExtensions
{
    /// <summary>Passpoint AP かどうか(Interworking bit により判定)</summary>
    public static bool IsPasspoint(this WifiNetwork network)
        => network.Auth is AuthMethod.WPA2Enterprise or AuthMethod.WPA3Enterprise
           && network.BssEntries.Any(b => b.HasInterworkingElement);
}
