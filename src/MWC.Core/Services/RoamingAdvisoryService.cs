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

    /// <summary>フラッピング判定: window 内のローミング回数がこれ以上で過剰</summary>
    public const int FlappingThreshold = 4;
    /// <summary>スティッキー判定: これ以下の RSSI で居座るとスティッキー候補</summary>
    public const int StickyRssiDbm = -75;

    /// <summary>
    /// ローミングの安定性を判定する(スティッキークライアント / フラッピング検出)。
    /// 単一スナップショットの能力判定 (Analyze) とは別に、直近のローミング履歴と
    /// 現在の信号強度から挙動を診断する。純粋関数(履歴は呼び出し側が供給)。
    /// </summary>
    /// <param name="roams">直近のローミングイベント(新旧順不同)</param>
    /// <param name="currentRssiDbm">現在の接続 RSSI (dBm, 負値)</param>
    /// <param name="now">基準時刻</param>
    /// <param name="window">評価ウィンドウ(既定 60 秒)</param>
    public RoamingStability AnalyzeStability(
        IReadOnlyList<RoamEvent> roams, int currentRssiDbm, DateTimeOffset now,
        TimeSpan? window = null)
    {
        var w = window ?? TimeSpan.FromSeconds(60);
        var cutoff = now - w;
        int roamCount = roams.Count(r => r.At >= cutoff && r.At <= now);

        if (roamCount >= FlappingThreshold)
            return new RoamingStability(RoamingStabilityState.Flapping, roamCount,
                $"短時間に {roamCount} 回ローミングしている(フラッピング)。" +
                "ローミング閾値が攻撃的すぎる可能性。AP の出力/配置の見直しを推奨。");

        if (roamCount == 0 && currentRssiDbm <= StickyRssiDbm)
            return new RoamingStability(RoamingStabilityState.Sticky, 0,
                $"弱い信号 ({currentRssiDbm}dBm) のまま同じ AP に居座っている可能性" +
                "(スティッキークライアント)。手動再接続やより近い AP への移動を推奨。");

        return new RoamingStability(RoamingStabilityState.Stable, roamCount,
            "ローミングは安定している。");
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

/// <summary>ローミングイベント(どの BSSID へいつ遷移したか)。</summary>
public sealed record RoamEvent(string Bssid, DateTimeOffset At);

/// <summary>ローミング安定性の状態。</summary>
public enum RoamingStabilityState
{
    /// <summary>安定</summary>
    Stable,
    /// <summary>弱信号で居座り(スティッキー)</summary>
    Sticky,
    /// <summary>過剰な再ローミング(フラッピング)</summary>
    Flapping
}

/// <summary>ローミング安定性の診断結果。</summary>
public sealed record RoamingStability(RoamingStabilityState State, int RoamCount, string Advice)
{
    public bool NeedsAttention => State != RoamingStabilityState.Stable;
}
