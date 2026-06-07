using System;
using System.Collections.Generic;
using System.Linq;

namespace MWC.Core.Models;

public sealed record WifiNetwork
{
    public required string Ssid { get; init; }
    public IReadOnlyList<BssInfo> BssEntries { get; init; } = Array.Empty<BssInfo>();
    public int SignalQuality { get; init; }
    public int? Rssi { get; init; }
    public AuthMethod Auth { get; init; }
    public CipherType Cipher { get; init; }
    public WifiBand Band { get; init; }
    public int Channel { get; init; }
    public int ChannelWidth { get; init; }
    public PhyType Phy { get; init; }
    public bool IsConnected { get; init; }
    public bool IsHidden { get; init; }
    public bool HasProfile { get; init; }
    public string? ProfileName { get; init; }
    public int? MaxLinkSpeedMbps { get; init; }
    public string? VendorName { get; init; }
    public int? FrequencyMhz { get; init; }

    // ── Wi-Fi 7 MLO ──────────────────────────────────
    /// <summary>MLO 使用中かどうか</summary>
    // ── 省電力 (arXiv 2402.15900, 2411.17424: TWT / rTWT) ──
    /// <summary>802.11ax Target Wake Time 対応 — IoT/バッテリー機器の省電力</summary>
    public bool TargetWakeTime { get; init; }

    /// <summary>Wi-Fi 7 restricted TWT 対応 — リアルタイムトラフィックの低遅延スケジューリング</summary>
    public bool RestrictedTwt { get; init; }

    // ── 高速ローミング (arXiv: Machań & Wozniak, IEEE 802.11r/k/v) ──
    /// <summary>802.11r Fast BSS Transition 対応 — 再認証を 13ms 程度まで短縮</summary>
    public bool FastTransition { get; init; }

    /// <summary>802.11k Neighbor Report 対応 — AP候補リストでスキャンを排除</summary>
    public bool NeighborReport { get; init; }

    /// <summary>802.11v BSS Transition Management 対応 — ネットワーク主導ローミング誘導</summary>
    public bool BssTransitionMgmt { get; init; }

    // ── セキュリティ堅牢性 (arXiv: Dragonblood, wifi-deauthentication WiSec2022) ──
    /// <summary>Protected Management Frames (802.11w/MFP) 対応 — deauth攻撃を防ぐ</summary>
    public PmfStatus Pmf { get; init; } = PmfStatus.Unknown;

    /// <summary>WPA3 transition mode (WPA2/WPA3 混在) — Dragonblood ダウングレード攻撃に脆弱</summary>
    public bool IsWpa3TransitionMode { get; init; }

    /// <summary>WPS (Wi-Fi Protected Setup) 有効 — 外部レジストラ PIN 方式は総当たり/Pixie-Dust に脆弱</summary>
    public bool WpsEnabled { get; init; }

    public bool IsMlo { get; init; }
    /// <summary>MLO リンク一覧(Wi-Fi 7 のみ有効)</summary>
    public IReadOnlyList<MloLink> MloLinks { get; init; } = Array.Empty<MloLink>();
    /// <summary>MLO 集約速度上限 (Mbps) — IsMlo=true 時のみ有効</summary>
    // ── Hotspot 2.0 / Passpoint ──────────────────────────────────────
    /// <summary>Passpoint/Hotspot2.0 AP かどうか</summary>
    /// <summary>
    /// セキュリティ堅牢性スコア。
    /// arXiv 研究知見:
    ///   - WPA3 transition mode は Dragonblood ダウングレード攻撃に脆弱 (Vanhoef & Ronen 2020)
    ///   - MFP Inactive は deauth/disassoc 攻撃に脆弱 (WiSec 2022)
    /// </summary>
    public SecurityHardening Hardening
    {
        get
        {
            // WPA3-SAE + MFP Required = 最も堅牢
            if (Auth is AuthMethod.WPA3SAE or AuthMethod.WPA3Enterprise or AuthMethod.WPA3Enterprise192
                && Pmf == PmfStatus.Required && !IsWpa3TransitionMode)
                return SecurityHardening.Hardened;

            // WPA3 transition mode = ダウングレード攻撃リスク
            if (IsWpa3TransitionMode)
                return SecurityHardening.TransitionModeRisk;

            // MFP なし = deauth 攻撃リスク
            if (Pmf == PmfStatus.Disabled &&
                Auth is AuthMethod.WPA2PSK or AuthMethod.WPA2Enterprise)
                return SecurityHardening.NoMfpRisk;

            return SecurityHardening.Standard;
        }
    }

    public bool IsPasspoint => Auth is AuthMethod.WPA2Enterprise or AuthMethod.WPA3Enterprise
        && BssEntries.Any(b => b.HasInterworkingElement);

    public int? MloAggregatedSpeedMbps => IsMlo && MloLinks.Count > 0
        ? MloLinks.EstimatedAggregatedSpeedMbps()
        : null;
}

public sealed record BssInfo
{
    public required string Bssid { get; init; }
    public int Rssi { get; init; }
    public int Channel { get; init; }
    public int FrequencyMhz { get; init; }
    public PhyType Phy { get; init; }
    public int ChannelWidth { get; init; }
    /// <summary>Interworking (802.11u) IE を含む = Passpoint/Hotspot2.0 対応</summary>
    public bool HasInterworkingElement { get; init; }
    /// <summary>Protected Management Frames (802.11w) 状態</summary>
    public PmfStatus Pmf { get; init; } = PmfStatus.Unknown;
    /// <summary>BSS Load (Element ID 11) — チャネル混雑情報 (null = 要素なし)</summary>
    public BssLoad? BssLoad { get; init; }
}

/// <summary>Protected Management Frames (802.11w) 状態</summary>
public enum PmfStatus
{
    /// <summary>不明 (スキャンで判定できない)</summary>
    Unknown,
    /// <summary>未対応 — deauth/disassoc 攻撃に脆弱</summary>
    Disabled,
    /// <summary>対応 (オプション) — クライアント次第で有効</summary>
    Capable,
    /// <summary>必須 — 全クライアントで MFP 強制 (WPA3 既定)</summary>
    Required
}

/// <summary>セキュリティ堅牢性レベル (arXiv 研究知見に基づく)</summary>
public enum SecurityHardening
{
    /// <summary>WPA3-SAE + MFP必須 — Dragonblood/deauth 両方に耐性</summary>
    Hardened,
    /// <summary>標準的な保護</summary>
    Standard,
    /// <summary>WPA3 transition mode — ダウングレード攻撃リスク (Dragonblood)</summary>
    TransitionModeRisk,
    /// <summary>MFP無効 — deauth/disassoc 攻撃リスク (WiSec 2022)</summary>
    NoMfpRisk
}

public enum PhyType
{
    Unknown, Dot11b, Dot11a, Dot11g, Dot11n, Dot11ac, Dot11ax, Dot11be, Dot11bn
}

public static class PhyTypeExtensions
{
    public static string ToGenerationLabel(this PhyType phy) => phy switch
    {
        PhyType.Dot11b  => "Wi-Fi 1 (802.11b)",
        PhyType.Dot11a  => "Wi-Fi 2 (802.11a)",
        PhyType.Dot11g  => "Wi-Fi 3 (802.11g)",
        PhyType.Dot11n  => "Wi-Fi 4 (802.11n)",
        PhyType.Dot11ac => "Wi-Fi 5 (802.11ac)",
        PhyType.Dot11ax => "Wi-Fi 6/6E (802.11ax)",
        PhyType.Dot11be => "Wi-Fi 7 (802.11be)",
        PhyType.Dot11bn => "Wi-Fi 8 (802.11bn — Preview)",
        _ => "Unknown"
    };
    public static string ToShortLabel(this PhyType phy) => phy switch
    {
        PhyType.Dot11b  => "Wi-Fi 1",
        PhyType.Dot11a  => "Wi-Fi 2",
        PhyType.Dot11g  => "Wi-Fi 3",
        PhyType.Dot11n  => "Wi-Fi 4",
        PhyType.Dot11ac => "Wi-Fi 5",
        PhyType.Dot11ax => "Wi-Fi 6/6E",
        PhyType.Dot11be => "Wi-Fi 7",
        PhyType.Dot11bn => "Wi-Fi 8",
        _ => "?"
    };
}

public enum WifiBand { Unknown, Band2_4GHz, Band5GHz, Band6GHz }

public sealed record WifiAdapter
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public AdapterState State { get; init; }
    public string? ConnectedSsid { get; init; }
}

public enum AdapterState
{
    NotReady, Connected, AdHocNetworkFormed,
    Disconnecting, Disconnected, Associating, Discovering, Authenticating
}

// ══ Wi-Fi 7 (802.11be) MLO 拡張 ═══════════════════════════════════════

/// <summary>
/// MLO (Multi-Link Operation) リンク情報。
/// Wi-Fi 7 では複数バンドを同時使用できる。
/// </summary>
public sealed record MloLink
{
    /// <summary>リンク ID (0-15)</summary>
    public int LinkId { get; init; }

    /// <summary>使用バンド</summary>
    public WifiBand Band { get; init; }

    /// <summary>使用チャネル</summary>
    public int Channel { get; init; }

    /// <summary>周波数 (MHz)</summary>
    public int FrequencyMhz { get; init; }

    /// <summary>このリンクの RSSI (dBm)</summary>
    public int Rssi { get; init; }

    /// <summary>チャネル幅 (MHz)</summary>
    public int ChannelWidth { get; init; }
}

/// <summary>Wi-Fi 7 アダプター能力</summary>
public sealed record WiFi7Capability
{
    /// <summary>MLO 対応</summary>
    public bool SupportsMlo { get; init; }

    /// <summary>16K A-MPDU 対応</summary>
    public bool Supports16KAmpdu { get; init; }

    /// <summary>Multi-RU 対応(同時複数バンド送受信)</summary>
    public bool SupportsMultiRu { get; init; }

    /// <summary>最大 MLO リンク数</summary>
    public int MaxMloLinks { get; init; }

    /// <summary>最大 MCS インデックス (0-13)</summary>
    public int MaxMcsIndex { get; init; }
}

public static class MloExtensions
{
    /// <summary>MLO リンクの集約スループット上限を推定 (Mbps)</summary>
    public static int EstimatedAggregatedSpeedMbps(
        this IReadOnlyList<MloLink> links, int mcsIndex = 13)
    {
        // 帯域幅→空間ストリーム1本のMCS13理論値 (近似)
        static int BwToMbps(int chanWidthMhz) => chanWidthMhz switch
        {
            320 => 11529,
            160 => 5765,
            80  => 2882,
            40  => 1441,
            _   => 720
        };
        // 簡易推定: 全リンク合算(実際はオーバーヘッド等で低下)
        return links.Sum(l => BwToMbps(l.ChannelWidth));
    }
}

// ══ Wi-Fi 7 EHT (Extremely High Throughput) 拡張 ═══════════════════════
// IEEE 802.11be-2025 (公開: 2025年7月22日)

/// <summary>
/// Wi-Fi 7 / EHT の高度な物理層機能。
/// Preamble Puncturing, 4096-QAM, rTWT (restricted Target Wake Time) をモデル化。
/// </summary>
public sealed record EhtCapability
{
    /// <summary>Preamble Puncturing 対応 — チャネルの一部に干渉があっても残帯域を活用</summary>
    public bool SupportsPreamblePuncturing { get; init; }

    /// <summary>4096-QAM 対応 (MCS 13) — Wi-Fi 6 の 1024-QAM から約20%スループット向上</summary>
    public bool Supports4096Qam { get; init; }

    /// <summary>最大 MCS インデックス: Wi-Fi 6 = 11, Wi-Fi 7 = 13 (4096-QAM)</summary>
    public int MaxMcsIndex { get; init; }

    /// <summary>
    /// rTWT (Restricted Target Wake Time) 対応。
    /// IoT デバイスの省電力スケジューリング — Wi-Fi 7 の新機能。
    /// </summary>
    public bool SupportsRtwt { get; init; }

    /// <summary>Stream Classification Service (SCS) 対応 — QoS 優先度設定</summary>
    public bool SupportsScs { get; init; }

    /// <summary>推定理論最大スループット計算 (Gbps)</summary>
    public double EstimatedPeakGbps(int channelWidthMhz, int spatialStreams = 1)
    {
        // 4096-QAM + 320MHz + 空間ストリーム × 理論値
        double baseRate = channelWidthMhz switch
        {
            320 => 11.529,  // 1SS, 4096-QAM, 320MHz
            160 => 5.765,
            80  => 2.882,
            40  => 1.441,
            _   => 0.720
        };
        double mcsMultiplier = Supports4096Qam ? 1.0 : 0.83;  // 1024-QAM 比
        return baseRate * mcsMultiplier * spatialStreams;
    }
}

/// <summary>
/// Wi-Fi 8 (IEEE 802.11bn) 先行モデル — 2026年以降の認証取得見込み。
/// Multi-AP coordination と AI ベースリソース配分が中心機能。
/// </summary>
public sealed record WiFi8Capability
{
    /// <summary>Multi-AP Coordination 対応 — 複数 AP が協調して干渉を最小化</summary>
    public bool SupportsMultiApCoordination { get; init; }

    /// <summary>Coordinated Spatial Reuse (CSR) — AP間で空間資源を共用</summary>
    public bool SupportsCoordinatedSpatialReuse { get; init; }

    /// <summary>Coordinated OFDMA (Co-OFDMA) — 周波数資源の協調割当</summary>
    public bool SupportsCoordinatedOfdma { get; init; }

    /// <summary>Super High Throughput (SHT) — 최大 100 Gbps 理論値 (複数AP合算)</summary>
    public bool SupportsUltraHighThroughput { get; init; }
}

// ══ BSS Load (802.11e/ax) ════════════════════════════════════════════════

/// <summary>AP ビーコンの BSS Load 要素 (Element ID 11) から得たチャネル負荷スナップショット。</summary>
public sealed record BssLoad(
    ushort StationCount,
    byte   ChannelUtilization,
    ushort AvailableAdmissionCapacity)
{
    /// <summary>チャネル占有率 0.0–1.0 (255 → 100%)。</summary>
    public double UtilizationFraction => ChannelUtilization / 255.0;

    /// <summary>占有率を 0–100% の整数で返す (表示用)。</summary>
    public int UtilizationPercent => (int)Math.Round(UtilizationFraction * 100.0);

    /// <summary>チャネルが過負荷かどうか (占有率 75% 超)。</summary>
    public bool IsOverloaded => ChannelUtilization > 191; // 191/255 ≈ 75%
}
