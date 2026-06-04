using System;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// リンクレート (スループット) 推定サービス。
///
/// RSSI / SNR から達成可能な MCS (Modulation and Coding Scheme) を推定し、
/// 理論リンクレート (PHY rate) を計算する。
///
/// 学術的背景:
///   無線リンクは SNR に応じて変調方式 (BPSK→QPSK→...→4096-QAM) を適応させる
///   (rate adaptation)。各 MCS には最低必要 SNR があり、IEEE 802.11 で規定される。
///   RSSI からノイズフロア (-95dBm 程度) を引いて SNR を推定し、
///   その SNR で使える最高 MCS を求めることでリンクレートを予測できる。
///
/// ゼロ外部依存。
/// </summary>
public sealed class LinkRateEstimator
{
    /// <summary>標準的な屋内ノイズフロア (dBm)</summary>
    public const int DefaultNoiseFloorDbm = -95;

    /// <summary>
    /// RSSI からノイズフロアを引いて SNR (dB) を推定する。
    /// </summary>
    public int EstimateSnr(int rssiDbm, int noiseFloorDbm = DefaultNoiseFloorDbm)
        => rssiDbm - noiseFloorDbm;

    /// <summary>
    /// SNR (dB) から達成可能な最高 MCS インデックスを推定する。
    /// IEEE 802.11ax/be の MCS 別最低必要 SNR に基づく近似。
    /// </summary>
    public int EstimateMaxMcs(int snrDb, bool supports4096Qam = true)
    {
        // MCS index → 最低必要 SNR (dB) の近似テーブル (802.11ax/be)
        // MCS 0 (BPSK 1/2) ... MCS 11 (1024-QAM 5/6) ... MCS 13 (4096-QAM 5/6)
        int mcs = snrDb switch
        {
            >= 43 => 13,  // 4096-QAM 5/6 (Wi-Fi 7)
            >= 40 => 12,  // 4096-QAM 3/4 (Wi-Fi 7)
            >= 37 => 11,  // 1024-QAM 5/6
            >= 35 => 10,  // 1024-QAM 3/4
            >= 32 => 9,   // 256-QAM 5/6
            >= 30 => 8,   // 256-QAM 3/4
            >= 27 => 7,   // 64-QAM 5/6
            >= 25 => 6,   // 64-QAM 3/4
            >= 22 => 5,   // 64-QAM 2/3
            >= 19 => 4,   // 16-QAM 3/4
            >= 16 => 3,   // 16-QAM 1/2
            >= 13 => 2,   // QPSK 3/4
            >= 10 => 1,   // QPSK 1/2
            >= 5  => 0,   // BPSK 1/2
            _     => -1   // 接続不能
        };

        // 4096-QAM 非対応なら MCS 11 で頭打ち
        if (!supports4096Qam && mcs > 11) mcs = 11;
        return mcs;
    }

    /// <summary>
    /// MCS / チャネル幅 / 空間ストリーム数から理論 PHY レート (Mbps) を計算する。
    /// </summary>
    public double EstimatePhyRateMbps(int mcs, int channelWidthMhz, int spatialStreams = 1)
    {
        if (mcs < 0) return 0;

        // MCS 0 / 20MHz / 1SS / GI=0.8µs を基準とした 1SS データレート (Mbps)
        // 802.11ax の HE-MCS テーブルに基づく近似
        double baseRate20 = mcs switch
        {
            0  => 8.6,    1  => 17.2,   2  => 25.8,   3  => 34.4,
            4  => 51.6,   5  => 68.8,   6  => 77.4,   7  => 86.0,
            8  => 103.2,  9  => 114.7,  10 => 129.0,  11 => 143.4,
            12 => 154.9,  13 => 172.1,  // 4096-QAM (Wi-Fi 7)
            _  => 0
        };

        // チャネル幅による倍率 (20MHz=1x, 40=2.08x, 80=4.33x, 160=8.67x, 320=17.3x)
        double widthMultiplier = channelWidthMhz switch
        {
            320 => 17.3, 160 => 8.67, 80 => 4.33, 40 => 2.08, _ => 1.0
        };

        return baseRate20 * widthMultiplier * spatialStreams;
    }

    /// <summary>
    /// ネットワークの RSSI から総合的なリンク品質を推定する。
    /// </summary>
    public LinkEstimate Estimate(
        int rssiDbm, int channelWidthMhz = 80, int spatialStreams = 2,
        bool supports4096Qam = true, int noiseFloorDbm = DefaultNoiseFloorDbm)
    {
        int snr = EstimateSnr(rssiDbm, noiseFloorDbm);
        int mcs = EstimateMaxMcs(snr, supports4096Qam);
        double phyRate = EstimatePhyRateMbps(mcs, channelWidthMhz, spatialStreams);

        // 実効スループットは PHY レートの約 60-70% (オーバーヘッド)
        double effectiveMbps = phyRate * 0.65;

        var quality = mcs switch
        {
            >= 11 => LinkQuality.Excellent,
            >= 7  => LinkQuality.Good,
            >= 3  => LinkQuality.Fair,
            >= 0  => LinkQuality.Poor,
            _     => LinkQuality.Unusable
        };

        return new LinkEstimate(
            SnrDb:           snr,
            MaxMcs:          mcs,
            PhyRateMbps:     Math.Round(phyRate, 1),
            EffectiveMbps:   Math.Round(effectiveMbps, 1),
            Quality:         quality);
    }
}

// ── データ型 ─────────────────────────────────────────────────────

/// <summary>リンク品質推定結果</summary>
public sealed record LinkEstimate(
    int         SnrDb,
    int         MaxMcs,
    double      PhyRateMbps,
    double      EffectiveMbps,
    LinkQuality Quality);

/// <summary>リンク品質</summary>
public enum LinkQuality
{
    Unusable, Poor, Fair, Good, Excellent
}
