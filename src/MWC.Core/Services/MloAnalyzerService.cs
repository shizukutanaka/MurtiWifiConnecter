using System;
using System.Collections.Generic;
using System.Linq;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// Wi-Fi 7 MLO (Multi-Link Operation) 分析サービス。
///
/// MLO は複数バンド (2.4/5/6GHz) のリンクを同時に使い、以下を実現する:
///   - スループット集約: 複数リンクの帯域を合算
///   - 低レイテンシ: 最も空いているリンクを選択 (STR)
///   - 信頼性: 1リンクが劣化しても他リンクで継続
///
/// 動作モード:
///   - STR (Simultaneous Transmit Receive): 全リンク同時送受信 (最高性能)
///   - EMLSR (Enhanced Multi-Link Single Radio): 1無線を切替 (低コスト機器)
///
/// 本サービスは MLO 構成を分析し、ユーザーに利点を提示する。
/// </summary>
public sealed class MloAnalyzerService
{
    private readonly LinkRateEstimator _rateEstimator = new();

    /// <summary>
    /// ネットワークの MLO 構成を分析する。
    /// </summary>
    public MloAnalysis Analyze(WifiNetwork network)
    {
        // 「この AP は MLO を広告しているか」と「リンクごとの詳細を持っているか」は
        // **別の問い**である。以前は両方を 1 つの条件に潰して `IsMlo: false` を返していたため、
        // ビーコンから MLO を検出できている Wi-Fi 7 AP に対して**事実と異なる答え**を返し、
        // `BeaconIeApplier` が設定した `WifiNetwork.IsMlo` は誰にも届いていなかった
        // (`IsMlo` の消費者はここだけで、ここが握り潰していた)。
        if (!network.IsMlo)
            return new MloAnalysis(
                IsMlo:            false,
                LinkCount:        0,
                Bands:            Array.Empty<WifiBand>(),
                IsCrossBand:      false,
                AggregatedMbps:   0,
                BestLinkRssi:     network.SignalQuality > 0 ? -60 : 0,
                ReliabilityTier:  MloReliability.SingleLink,
                Summary:          "MLO not supported (single link).");

        // MLO は広告されているが、リンク詳細を供給するプラットフォーム層がまだ無い。
        // 「MLO 対応」までは事実として言える。リンク数や集約速度は**言わない** —
        // 0 を返して呼び出し側に "0-link MLO, 0Mbps" と表示させると、
        // 測っていない値を測ったかのように見せることになる (LinkCount == 0 が目印)。
        if (network.MloLinks.Count == 0)
            return new MloAnalysis(
                IsMlo:            true,
                LinkCount:        0,
                Bands:            Array.Empty<WifiBand>(),
                IsCrossBand:      false,
                AggregatedMbps:   0,
                BestLinkRssi:     0,
                ReliabilityTier:  MloReliability.SingleLink,
                Summary:          "Wi-Fi 7 MLO advertised by this AP. Per-link detail is not " +
                                  "available on this platform, so link count and aggregate " +
                                  "throughput are not reported.");

        var links = network.MloLinks;
        var bands = links.Select(l => l.Band).Distinct().ToList();
        bool crossBand = bands.Count >= 2;

        // 集約スループット (各リンクの推定実効レートを合算)
        double aggregated = links.Sum(l =>
            _rateEstimator.Estimate(l.Rssi, l.ChannelWidth, spatialStreams: 2).EffectiveMbps);

        int bestRssi = links.Max(l => l.Rssi);

        // 信頼性階層
        var reliability = links.Count switch
        {
            >= 3 => MloReliability.TripleLink,
            2    => MloReliability.DualLink,
            _    => MloReliability.SingleLink
        };

        string summary = crossBand
            ? $"{links.Count}-link MLO ({string.Join("+", bands.Select(BandLabel))}). " +
              $"Aggregated approx. {aggregated:F0}Mbps. Continues on other bands if one link degrades."
            : $"{links.Count}-link MLO (same band). Aggregated approx. {aggregated:F0}Mbps.";

        return new MloAnalysis(
            IsMlo:           true,
            LinkCount:       links.Count,
            Bands:           bands,
            IsCrossBand:     crossBand,
            AggregatedMbps:  Math.Round(aggregated, 1),
            BestLinkRssi:    bestRssi,
            ReliabilityTier: reliability,
            Summary:         summary);
    }

    /// <summary>
    /// MLO のレイテンシ削減効果を推定する。
    /// 複数リンクから最も空いているものを選べるため、単一リンクより低レイテンシ。
    /// </summary>
    public double EstimateLatencyReductionPercent(WifiNetwork network)
    {
        if (!network.IsMlo || network.MloLinks.Count < 2) return 0;

        // リンク数が増えるほど「最良リンク選択」の効果が高まる (逓減)
        // 2リンク ≈ 30%, 3リンク ≈ 45% (経験的近似)
        return network.MloLinks.Count switch
        {
            2 => 30.0,
            >= 3 => 45.0,
            _ => 0.0
        };
    }

    /// <summary>
    /// 最も品質の良いリンクを返す (STR で優先送信されるリンク)。
    /// </summary>
    public MloLink? BestLink(WifiNetwork network)
        => network.MloLinks.Count == 0
            ? null
            : network.MloLinks.OrderByDescending(l => l.Rssi).First();

    // MLO が不利になりうる閾値
    private const int WeakRssiDbm    = -78;  // これ以下は弱リンク
    private const int AsymmetricGapDb = 25;  // リンク間 RSSI 差がこれ以上で非対称

    /// <summary>
    /// MLO アノマリーを検出する (arXiv 2210.07695: Performance, Anomalies, and Solutions)。
    /// MLO は条件次第で単一(最良)リンクより遅延・スループットが悪化しうる:
    ///   - リンク非対称が大きいと弱リンクがヘッドオブライン遅延を招く
    ///   - 全リンクが弱い場合は集約の利点が乏しい
    ///   - 同一バンドのみの MLO は障害時の冗長効果が小さい
    /// </summary>
    public MloAnomaly DetectAnomaly(WifiNetwork network)
    {
        if (!network.IsMlo || network.MloLinks.Count < 2)
            return new MloAnomaly(MloAnomalyKind.None, null);

        var links = network.MloLinks;
        int best  = links.Max(l => l.Rssi);   // RSSI は負値、best は 0 に近い
        int worst = links.Min(l => l.Rssi);
        int gap   = best - worst;

        if (best <= WeakRssiDbm)
            return new MloAnomaly(MloAnomalyKind.AllLinksWeak,
                "All links are weak. MLO aggregation offers limited benefit; consider pinning to the best link or improving AP placement.");

        if (gap >= AsymmetricGapDb)
            return new MloAnomaly(MloAnomalyKind.AsymmetricLinks,
                $"Large RSSI gap between links (approx. {gap}dB). For latency-sensitive traffic, MLO may perform worse than the best single link " +
                "(arXiv 2210.07695). Consider pinning to the stronger link.");

        if (links.Select(l => l.Band).Distinct().Count() < 2)
            return new MloAnomaly(MloAnomalyKind.SameBandRedundancy,
                "Single-band MLO only. Redundancy on link failure is limited. A cross-band configuration is recommended.");

        return new MloAnomaly(MloAnomalyKind.None, null);
    }

    private static string BandLabel(WifiBand b) => b switch
    {
        WifiBand.Band2_4GHz => "2.4G",
        WifiBand.Band5GHz   => "5G",
        WifiBand.Band6GHz   => "6G",
        _                   => "?"
    };
}

// ── データ型 ─────────────────────────────────────────────────────

/// <summary>MLO 分析結果</summary>
public sealed record MloAnalysis(
    bool                  IsMlo,
    int                   LinkCount,
    IReadOnlyList<WifiBand> Bands,
    bool                  IsCrossBand,
    double                AggregatedMbps,
    int                   BestLinkRssi,
    MloReliability        ReliabilityTier,
    string                Summary);

/// <summary>MLO 信頼性階層</summary>
public enum MloReliability
{
    SingleLink, DualLink, TripleLink
}

/// <summary>MLO アノマリー検出結果 (arXiv 2210.07695)。</summary>
public sealed record MloAnomaly(MloAnomalyKind Kind, string? Advice)
{
    public bool HasAnomaly => Kind != MloAnomalyKind.None;
}

/// <summary>MLO が不利になりうる種別。</summary>
public enum MloAnomalyKind
{
    None,
    /// <summary>リンク間 RSSI 差が大きい(弱リンクが遅延を悪化)</summary>
    AsymmetricLinks,
    /// <summary>全リンクが弱い(集約利点が乏しい)</summary>
    AllLinksWeak,
    /// <summary>同一バンドのみ(冗長効果が小さい)</summary>
    SameBandRedundancy
}
