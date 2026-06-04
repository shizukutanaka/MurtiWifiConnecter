using System;
using System.Collections.Generic;
using System.Linq;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// バンド・チャネル選択助言サービス。
///
/// 学術的背景:
///   - Dogan-Tusha et al., "Evaluating The Interference Potential in 6 GHz"
///     (ACM WiNTECH 2023, arXiv 2307.00235): 6GHz 帯は 59 の新規 20MHz チャネルで輻輳を緩和
///   - "Optimal channel selection for tri-band Wi-Fi" (ScienceDirect 2024):
///     不適切なバンド管理が干渉を招く
///   - Band Steering best practices (Purple): 高密度環境では 20MHz 幅が
///     非重複チャネル数を最大化し、総容量で 40/80MHz に勝る
///   - "Learning-Based Channel Access" (arXiv 2511.10143): OBSS 負荷重複下で
///     プライマリチャネル選択が重要
///
/// クライアント視点での「どのバンド/AP を選ぶべきか」を助言する。
/// 本サービスはチャネル変更を行わず、接続先選択の助言のみ。
/// </summary>
public sealed class ChannelAdvisorService
{
    // 2.4GHz の非重複チャネル (1, 6, 11)
    private static readonly int[] NonOverlapping24 = { 1, 6, 11 };

    /// <summary>
    /// 同一 SSID の複数バンド AP から、最適なバンドを推奨する。
    /// バンドステアリング: 一般に 6GHz &gt; 5GHz &gt; 2.4GHz (空き具合と速度)。
    /// ただし信号強度が著しく弱い場合は下位バンドを優先。
    /// </summary>
    public WifiNetwork? RecommendBand(IEnumerable<WifiNetwork> sameSsidNetworks)
    {
        var list = sameSsidNetworks.ToList();
        if (list.Count == 0) return null;

        return list
            .OrderByDescending(n => ScoreBandChoice(n))
            .First();
    }

    /// <summary>
    /// バンド選択スコア。高いほど推奨。
    /// 信号が十分なら高バンドを優先、弱いなら到達性のある低バンドを優先。
    /// </summary>
    public double ScoreBandChoice(WifiNetwork network)
    {
        // 信号品質を 0-1 に正規化
        double signalFactor = Math.Clamp(network.SignalQuality / 100.0, 0, 1);

        // バンドの基礎スコア (空き具合・速度ポテンシャル)
        double bandBase = network.Band switch
        {
            WifiBand.Band6GHz   => 100,  // 最も空いている (59 新チャネル)
            WifiBand.Band5GHz   => 70,
            WifiBand.Band2_4GHz => 40,   // 混雑しやすい
            _                   => 20
        };

        // 高バンドは減衰が大きいため、信号が弱いと実用性が下がる
        // 6GHz は壁の透過損失が大きい (arXiv 2307.00235 の BEL 測定)
        double reachabilityPenalty = network.Band switch
        {
            WifiBand.Band6GHz   => (1 - signalFactor) * 50,  // 弱信号で大きく減点
            WifiBand.Band5GHz   => (1 - signalFactor) * 30,
            WifiBand.Band2_4GHz => (1 - signalFactor) * 10,  // 到達性が高い
            _                   => 0
        };

        return bandBase * signalFactor - reachabilityPenalty + network.SignalQuality * 0.3;
    }

    /// <summary>
    /// 2.4GHz チャネルが非重複 (1/6/11) かどうかを判定する。
    /// 重複チャネルは隣接 AP と干渉しやすい。
    /// </summary>
    public bool IsNonOverlappingChannel(WifiNetwork network)
    {
        if (network.Band != WifiBand.Band2_4GHz) return true;  // 5/6GHz は基本非重複
        return NonOverlapping24.Contains(network.Channel);
    }

    /// <summary>
    /// チャネル幅に関する助言。
    /// 高密度環境では狭い幅 (20MHz) が非重複チャネル数を最大化する。
    /// </summary>
    public ChannelWidthAdvice AdviseChannelWidth(WifiNetwork network, int nearbyApCount)
    {
        bool dense = nearbyApCount >= 10;

        if (dense && network.ChannelWidth >= 80)
        {
            return new ChannelWidthAdvice(
                Recommended: 20,
                Reason: "高密度環境 (近隣AP多数)。20MHz 幅の方が非重複チャネルが増え、" +
                        "co-channel 干渉が減り総容量が向上する。",
                IsOptimal: false);
        }

        if (!dense && network.ChannelWidth <= 20 && network.Band != WifiBand.Band2_4GHz)
        {
            return new ChannelWidthAdvice(
                Recommended: 80,
                Reason: "低密度環境。80/160MHz 幅で個別スループットを最大化できる。",
                IsOptimal: false);
        }

        return new ChannelWidthAdvice(
            Recommended: network.ChannelWidth,
            Reason: "現在のチャネル幅は環境に適している。",
            IsOptimal: true);
    }

    /// <summary>
    /// 同一 SSID の AP 群から推定される「チャネル混雑度」を返す (0-100)。
    /// 同一チャネルに複数 BSS が密集しているほど高い (OBSS 負荷)。
    /// </summary>
    public int EstimateCongestion(IEnumerable<WifiNetwork> allVisibleNetworks, int channel)
    {
        var onSameChannel = allVisibleNetworks.Count(n => n.Channel == channel);
        // 同一チャネル AP 数 → 混雑度 (経験的に 5台で50%, 10台で100%)
        return Math.Clamp(onSameChannel * 10, 0, 100);
    }

    /// <summary>
    /// 人間語のバンド助言。
    /// </summary>
    public string DescribeBandChoice(WifiNetwork network)
    {
        return network.Band switch
        {
            WifiBand.Band6GHz when network.SignalQuality >= 50 =>
                "6GHz 帯。最も空いており高速。近距離で最適。",
            WifiBand.Band6GHz =>
                "6GHz 帯だが信号が弱い。壁越しでは 5GHz の方が安定する場合がある。",
            WifiBand.Band5GHz =>
                "5GHz 帯。速度と到達性のバランスが良い。",
            WifiBand.Band2_4GHz when !IsNonOverlappingChannel(network) =>
                $"2.4GHz 帯 (Ch{network.Channel})。重複チャネルのため干渉しやすい。",
            WifiBand.Band2_4GHz =>
                "2.4GHz 帯。到達性は高いが混雑しやすい。",
            _ => "不明なバンド。"
        };
    }
}

// ── データ型 ─────────────────────────────────────────────────────

/// <summary>チャネル幅の助言</summary>
public sealed record ChannelWidthAdvice(
    int    Recommended,
    string Reason,
    bool   IsOptimal);
