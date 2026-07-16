using System;
using System.Collections.Generic;
using System.Linq;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// 干渉分析サービス。
///
/// 学術的背景:
///   Cross-Technology Interference (CTI) 研究 (arXiv 2503.05429 系):
///   2.4GHz 帯は Wi-Fi / Bluetooth / Zigbee / 電子レンジ等が共存し、
///   相互干渉でスループットが低下する。5GHz/6GHz への移行や
///   非重複チャネル選択で緩和できる。
///
/// 本サービスはクライアント視点で観測可能な干渉指標を提供する:
///   - 同一/隣接チャネルの Wi-Fi 密度 (co-channel / adjacent-channel)
///   - 2.4GHz の Bluetooth 共存リスク
///   - チャネル混雑に基づく推奨アクション
/// </summary>
public sealed class InterferenceAnalyzer
{
    /// <summary>
    /// 対象ネットワークの干渉状況を分析する。
    /// </summary>
    public InterferenceReport Analyze(
        WifiNetwork target, IReadOnlyList<WifiNetwork> allVisible)
    {
        var factors = new List<InterferenceFactor>();
        int score = 100;  // 100=干渉なし、0=深刻

        // 1. Co-channel 干渉 (同一チャネルの他 AP)
        var coChannel = allVisible.Count(n =>
            n.Channel == target.Channel &&
            !ReferenceEquals(n, target) &&
            n.Band == target.Band);
        if (coChannel > 0)
        {
            int penalty = Math.Min(coChannel * 12, 50);
            score -= penalty;
            factors.Add(new InterferenceFactor(InterferenceFactorKind.CoChannel, coChannel, target.Channel));
        }

        // 2. Adjacent-channel 干渉 (2.4GHz のみ — 5/6GHz は基本非重複)
        if (target.Band == WifiBand.Band2_4GHz)
        {
            var adjacent = allVisible.Count(n =>
                n.Band == WifiBand.Band2_4GHz &&
                !ReferenceEquals(n, target) &&
                Math.Abs(n.Channel - target.Channel) is > 0 and < 5);
            if (adjacent > 0)
            {
                int penalty = Math.Min(adjacent * 8, 30);
                score -= penalty;
                factors.Add(new InterferenceFactor(InterferenceFactorKind.AdjacentChannel, adjacent, target.Channel));
            }

            // 3. Bluetooth 共存リスク (2.4GHz は BT と同居)
            score -= 10;
            factors.Add(new InterferenceFactor(InterferenceFactorKind.BluetoothCoexistence, 0, 0));
        }

        score = Math.Clamp(score, 0, 100);

        var level = score switch
        {
            >= 80 => InterferenceLevel.Low,
            >= 50 => InterferenceLevel.Moderate,
            >= 25 => InterferenceLevel.High,
            _     => InterferenceLevel.Severe
        };

        var recommendation = DetermineRecommendation(target.Band, level);

        return new InterferenceReport(
            Score:          score,
            Level:          level,
            Factors:        factors,
            Recommendation: recommendation);
    }

    /// <summary>
    /// 2.4GHz の Bluetooth 共存スコア (0-100、高いほど良好)。
    /// BT はチャネル 1/6/11 以外でより干渉しやすい (FHSS が全帯域を使うため
    /// 厳密には全チャネル影響するが、混雑チャネルほど顕著)。
    /// </summary>
    public int BluetoothCoexistenceScore(WifiNetwork network, int nearby24Count)
    {
        if (network.Band != WifiBand.Band2_4GHz)
            return 100;  // 5/6GHz は BT と非干渉

        int score = 70;  // 2.4GHz の基礎スコア
        // 非重複チャネル (1/6/11) はやや有利
        if (network.Channel is 1 or 6 or 11) score += 15;
        // 周辺 AP が多いほど BT との競合も激化
        score -= Math.Min(nearby24Count * 3, 40);

        return Math.Clamp(score, 0, 100);
    }

    private static InterferenceRecommendationKind DetermineRecommendation(
        WifiBand band, InterferenceLevel level)
    {
        if (level == InterferenceLevel.Low) return InterferenceRecommendationKind.None;
        return band == WifiBand.Band2_4GHz
            ? InterferenceRecommendationKind.SwitchBand
            : InterferenceRecommendationKind.SwitchChannel;
    }
}

// ── データ型 ─────────────────────────────────────────────────────

/// <summary>干渉レポート</summary>
public sealed record InterferenceReport(
    int                              Score,
    InterferenceLevel                Level,
    IReadOnlyList<InterferenceFactor> Factors,
    InterferenceRecommendationKind   Recommendation);

/// <summary>干渉因子の種別</summary>
public enum InterferenceFactorKind
{
    CoChannel,
    AdjacentChannel,
    BluetoothCoexistence
}

/// <summary>個別干渉因子 (種別+数量+チャネル)</summary>
public sealed record InterferenceFactor(
    InterferenceFactorKind Kind,
    int Count,
    int Channel);

/// <summary>干渉レベル</summary>
public enum InterferenceLevel
{
    Low, Moderate, High, Severe
}

/// <summary>干渉低減のための推奨アクション種別</summary>
public enum InterferenceRecommendationKind
{
    None,
    SwitchBand,
    SwitchChannel
}
