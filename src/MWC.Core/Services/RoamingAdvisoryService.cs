using System;
using System.Collections.Generic;
using System.Linq;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// 高速ローミング診断サービス。
///
/// 学術的背景:
///   Machań &amp; Wozniak, "On the fast BSS transition algorithms in the
///   IEEE 802.11r local area wireless networks" (Telecommunication Systems)
///
/// 高速ローミングの3標準:
///   - **802.11r** (Fast BSS Transition): 再認証ハンドシェイクを簡略化。
///     通常 200-300ms かかる遷移を 50ms 未満、最良で 13ms まで短縮
///   - **802.11k** (Neighbor Report): AP がクライアントに候補AP一覧を提供。
///     全チャネルスキャン (最も時間のかかるフェーズ) を排除
///   - **802.11v** (BSS Transition Management): ネットワークが
///     クライアントに最適なAPへの遷移を誘導
///
/// これらは WPA2/WPA3-Enterprise で最も効果的 (複雑な 802.1X 認証を高速化する設計)。
/// VoIP / ビデオ会議など途切れを許容できないサービスで特に重要。
///
/// 本サービスは診断と推奨のみ。実際の遷移は OS/ドライバーが実行する。
/// </summary>
public sealed class RoamingAdvisoryService
{
    /// <summary>遷移遅延の目安 (ミリ秒)</summary>
    public const int LegacyHandoverMs    = 250;  // 標準的な再認証
    public const int FastTransitionMs    = 50;   // 802.11r
    public const int OptimalFtMs         = 13;   // FT + 最適化 (論文の最良ケース)

    /// <summary>
    /// ネットワークのローミング能力を診断する。
    /// </summary>
    public RoamingProfile Analyze(WifiNetwork network)
    {
        var supported = new List<string>();
        if (network.FastTransition)    supported.Add("802.11r");
        if (network.NeighborReport)    supported.Add("802.11k");
        if (network.BssTransitionMgmt) supported.Add("802.11v");

        var tier = (network.FastTransition, network.NeighborReport, network.BssTransitionMgmt) switch
        {
            (true, true, true)  => RoamingTier.Seamless,    // 全対応 = 最良
            (true, _, _)        => RoamingTier.Fast,         // 11r あり
            (false, true, true) => RoamingTier.Assisted,     // 11k+11v (スキャン補助)
            _                   => RoamingTier.Standard       // 標準
        };

        var estimatedMs = tier switch
        {
            RoamingTier.Seamless => OptimalFtMs,
            RoamingTier.Fast     => FastTransitionMs,
            RoamingTier.Assisted => 120,
            _                    => LegacyHandoverMs
        };

        // 企業認証では FT の効果が最大
        bool enterprise = network.Auth is AuthMethod.WPA2Enterprise
                                       or AuthMethod.WPA3Enterprise
                                       or AuthMethod.WPA3Enterprise192;

        return new RoamingProfile(
            Tier:               tier,
            SupportedStandards: supported,
            EstimatedHandoverMs: estimatedMs,
            IsEnterpriseOptimized: enterprise && network.FastTransition,
            VoipReady:          estimatedMs <= 50);
    }

    /// <summary>
    /// VoIP / ビデオ会議に適したネットワークかどうか。
    /// 50ms 以下の遷移遅延が途切れない通話の目安。
    /// </summary>
    public bool IsRealtimeCapable(WifiNetwork network)
        => Analyze(network).VoipReady;

    /// <summary>
    /// 複数の同一 SSID AP から、最もローミングに優れたものを推奨する。
    /// モバイル利用 (歩き回る) シーンで有用。
    /// </summary>
    public WifiNetwork? RecommendForMobility(IEnumerable<WifiNetwork> networks, string ssid)
    {
        return networks
            .Where(n => string.Equals(n.Ssid, ssid, StringComparison.Ordinal))
            .OrderBy(n => (int)Analyze(n).Tier)            // Seamless(0) を最優先
            .ThenByDescending(n => n.SignalQuality)
            .FirstOrDefault();
    }

    /// <summary>
    /// 人間語のローミングアドバイスを生成する。
    /// </summary>
    public string DescribeRoaming(WifiNetwork network)
    {
        var profile = Analyze(network);
        return profile.Tier switch
        {
            RoamingTier.Seamless =>
                $"シームレスローミング対応 ({string.Join("/", profile.SupportedStandards)})。" +
                $"遷移遅延 約{profile.EstimatedHandoverMs}ms — VoIP/ビデオ通話でも途切れない。",
            RoamingTier.Fast =>
                $"高速ローミング対応 (802.11r)。遷移遅延 約{profile.EstimatedHandoverMs}ms。",
            RoamingTier.Assisted =>
                $"スキャン補助あり (802.11k/v)。AP候補リストでローミング判断が速い。",
            _ =>
                $"標準ローミング。遷移時に約{profile.EstimatedHandoverMs}ms の中断が発生しうる。"
        };
    }
}

// ── データ型 ─────────────────────────────────────────────────────

/// <summary>ローミングプロファイル</summary>
public sealed record RoamingProfile(
    RoamingTier         Tier,
    IReadOnlyList<string> SupportedStandards,
    int                 EstimatedHandoverMs,
    bool                IsEnterpriseOptimized,
    bool                VoipReady);

/// <summary>ローミング能力の階層 (値が小さいほど優秀)</summary>
public enum RoamingTier
{
    /// <summary>802.11r+k+v 全対応 — シームレス</summary>
    Seamless = 0,
    /// <summary>802.11r あり — 高速</summary>
    Fast = 1,
    /// <summary>802.11k/v によるスキャン補助</summary>
    Assisted = 2,
    /// <summary>標準ローミング</summary>
    Standard = 3
}
