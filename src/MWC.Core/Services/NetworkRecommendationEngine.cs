using System;
using System.Collections.Generic;
using System.Linq;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// 統合ネットワーク推奨エンジン。
///
/// 4つの専門サービスのスコアを重み付き合算し、ユーザーに単一の推奨を提示する:
///   - SecurityAdvisoryService  (安全性: Dragonblood/MFP)
///   - RoamingAdvisoryService   (移動耐性: 802.11r/k/v)
///   - ChannelAdvisorService    (帯域/混雑: 6GHz/チャネル幅)
///   - SignalQualityPredictor   (信号予測: EMA トレンド)
///
/// 用途プロファイル (UsageProfile) により重みを変える:
///   - General:    バランス
///   - Realtime:   VoIP/ビデオ会議 — ローミングと信号安定性を重視
///   - Secure:     機密業務 — セキュリティを最重視
///   - Throughput: 大容量転送 — 帯域を重視
///
/// ゼロ外部依存。既存サービスを合成するのみ。
/// </summary>
public sealed class NetworkRecommendationEngine
{
    private readonly SecurityAdvisoryService _security;
    private readonly RoamingAdvisoryService  _roaming;
    private readonly ChannelAdvisorService   _channel;

    public NetworkRecommendationEngine(
        SecurityAdvisoryService? security = null,
        RoamingAdvisoryService?  roaming  = null,
        ChannelAdvisorService?   channel  = null)
    {
        _security = security ?? new SecurityAdvisoryService();
        _roaming  = roaming  ?? new RoamingAdvisoryService();
        _channel  = channel  ?? new ChannelAdvisorService();
    }

    /// <summary>
    /// 単一ネットワークの総合スコア (0-100) を計算する。
    /// </summary>
    public NetworkScore Score(WifiNetwork network, UsageProfile profile = UsageProfile.General)
    {
        // 各次元を 0-100 に正規化
        double securityScore = _security.ComputeScore(network);

        var roamingProfile = _roaming.Analyze(network);
        double roamingScore = roamingProfile.Tier switch
        {
            RoamingTier.Seamless => 100,
            RoamingTier.Fast     => 80,
            RoamingTier.Assisted => 55,
            _                    => 30
        };

        double channelScore = Math.Clamp(_channel.ScoreBandChoice(network), 0, 100);
        double signalScore  = Math.Clamp(network.SignalQuality, 0, 100);

        // 用途別の重み (合計 1.0)
        var w = GetWeights(profile);
        double total = w.Security * securityScore
                     + w.Roaming  * roamingScore
                     + w.Channel  * channelScore
                     + w.Signal   * signalScore;

        return new NetworkScore(
            Network:        network,
            Total:          Math.Round(Math.Clamp(total, 0, 100), 1),
            SecurityScore:  Math.Round(securityScore, 1),
            RoamingScore:   Math.Round(roamingScore, 1),
            ChannelScore:   Math.Round(channelScore, 1),
            SignalScore:    Math.Round(signalScore, 1),
            Profile:        profile);
    }

    /// <summary>
    /// 複数ネットワークを総合スコア順にランク付けする。
    /// </summary>
    public IReadOnlyList<NetworkScore> Rank(
        IEnumerable<WifiNetwork> networks, UsageProfile profile = UsageProfile.General)
    {
        return networks
            .Select(n => Score(n, profile))
            .OrderByDescending(s => s.Total)
            .ToList();
    }

    /// <summary>
    /// 最適なネットワークを1つ推奨する。
    /// </summary>
    public NetworkScore? Recommend(
        IEnumerable<WifiNetwork> networks, UsageProfile profile = UsageProfile.General)
    {
        return Rank(networks, profile).FirstOrDefault();
    }

    /// <summary>
    /// 推奨理由を人間語で説明する (説明可能性 / explainability)。
    /// 「なぜこの AP が推奨されたか」を各次元の寄与とともに示す。
    /// </summary>
    public RecommendationExplanation Explain(NetworkScore score)
    {
        var w = GetWeights(score.Profile);

        // Weighted contribution per dimension
        var contributions = new List<DimensionContribution>
        {
            new("Security",        score.SecurityScore, w.Security, score.SecurityScore * w.Security),
            new("Roaming",         score.RoamingScore,  w.Roaming,  score.RoamingScore  * w.Roaming),
            new("Band / Channel",  score.ChannelScore,  w.Channel,  score.ChannelScore  * w.Channel),
            new("Signal Strength", score.SignalScore,   w.Signal,   score.SignalScore   * w.Signal),
        };

        // Rank by contribution descending
        var ranked = contributions.OrderByDescending(c => c.WeightedContribution).ToList();
        var top    = ranked.First();

        // Usage profile description
        string profileDesc = score.Profile switch
        {
            UsageProfile.Realtime   => "Optimised for real-time communication (VoIP/video) — prioritises roaming and signal stability",
            UsageProfile.Secure     => "Optimised for confidential use — security is the primary factor",
            UsageProfile.Throughput => "Optimised for bulk transfer — band width is the primary factor",
            _                       => "Balanced across all dimensions"
        };

        // Summary sentence
        string summary = $"Overall score {score.Total:F0}/100 ({score.Grade}). " +
                         $"{profileDesc}. Top factor: \"{top.Dimension}\" (score {top.Score:F0}).";

        return new RecommendationExplanation(
            Summary:       summary,
            ProfileReason: profileDesc,
            Contributions: ranked,
            TopFactor:     top.Dimension);
    }


    // ── 用途別の重み ──────────────────────────────────────────────

    private static ScoreWeights GetWeights(UsageProfile profile) => profile switch
    {
        UsageProfile.Realtime   => new(Security: 0.20, Roaming: 0.35, Channel: 0.15, Signal: 0.30),
        UsageProfile.Secure     => new(Security: 0.55, Roaming: 0.10, Channel: 0.10, Signal: 0.25),
        UsageProfile.Throughput => new(Security: 0.15, Roaming: 0.10, Channel: 0.45, Signal: 0.30),
        _                       => new(Security: 0.30, Roaming: 0.20, Channel: 0.25, Signal: 0.25)
    };

    private readonly record struct ScoreWeights(
        double Security, double Roaming, double Channel, double Signal);
}

// ── データ型 ─────────────────────────────────────────────────────

/// <summary>用途プロファイル</summary>
public enum UsageProfile
{
    /// <summary>バランス型 (既定)</summary>
    General,
    /// <summary>リアルタイム通信 (VoIP/ビデオ会議)</summary>
    Realtime,
    /// <summary>機密業務 (セキュリティ重視)</summary>
    Secure,
    /// <summary>大容量転送 (帯域重視)</summary>
    Throughput
}

/// <summary>総合スコアの内訳</summary>
public sealed record NetworkScore(
    WifiNetwork  Network,
    double       Total,
    double       SecurityScore,
    double       RoamingScore,
    double       ChannelScore,
    double       SignalScore,
    UsageProfile Profile)
{
    /// <summary>推奨グレード</summary>
    public RecommendationGrade Grade => Total switch
    {
        >= 85 => RecommendationGrade.Excellent,
        >= 70 => RecommendationGrade.Good,
        >= 50 => RecommendationGrade.Fair,
        _     => RecommendationGrade.Poor
    };
}

/// <summary>推奨グレード</summary>
public enum RecommendationGrade
{
    Poor, Fair, Good, Excellent
}

/// <summary>推奨の説明 (explainability)</summary>
public sealed record RecommendationExplanation(
    string                            Summary,
    string                            ProfileReason,
    IReadOnlyList<DimensionContribution> Contributions,
    string                            TopFactor);

/// <summary>各次元のスコア寄与</summary>
public sealed record DimensionContribution(
    string Dimension,
    double Score,
    double Weight,
    double WeightedContribution);
