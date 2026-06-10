using System;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// RSSI から AP までの概算距離を推定する (NetSpot / WiFi Analyzer の "Distance" 相当)。
///
/// 対数距離パスロスモデル:
///   PathLoss(dB) = TxPower(dBm) − RSSI(dBm)
///   PathLoss     = 10·n·log10(d) + 20·log10(f_MHz) − 27.55
/// これを距離 d (メートル) について解く:
///   d = 10^((PathLoss − 20·log10(f_MHz) + 27.55) / (10·n))
///
/// 定数 27.55 は自由空間モデル (FSPL: 20log10(d_m)+20log10(f_MHz)−27.55) 由来。
/// n=2 で自由空間、屋内は 2.5–4.0。高い周波数 (6GHz) は同距離でも減衰が大きく、
/// 周波数項により自動的に距離が短く出る。
///
/// ⚠ 距離推定は本質的に不確実 (マルチパス/遮蔽/AP 送信出力差)。
/// 結果は ±幅と信頼度付きで返し、絶対値ではなく相対比較・目安として使う。
/// </summary>
public sealed class RssiDistanceEstimator
{
    /// <summary>環境別パスロス指数 n の既定値。</summary>
    public const double FreeSpaceExponent = 2.0;
    public const double IndoorLineOfSight = 2.5;
    public const double IndoorObstructed  = 3.5;

    /// <summary>AP の想定実効送信出力 (EIRP, dBm)。家庭用 AP の概算。</summary>
    public const double DefaultTxPowerDbm = 20.0;

    private readonly double _pathLossExponent;
    private readonly double _txPowerDbm;

    public RssiDistanceEstimator(
        double pathLossExponent = IndoorLineOfSight,
        double txPowerDbm = DefaultTxPowerDbm)
    {
        if (pathLossExponent <= 0) throw new ArgumentOutOfRangeException(nameof(pathLossExponent));
        _pathLossExponent = pathLossExponent;
        _txPowerDbm = txPowerDbm;
    }

    /// <summary>
    /// RSSI (dBm) と周波数 (MHz) から距離を推定する。
    /// </summary>
    public DistanceEstimate Estimate(int rssiDbm, int frequencyMhz)
    {
        if (frequencyMhz <= 0)
            return new DistanceEstimate(0, 0, 0, DistanceConfidence.Unknown);

        // RSSI が送信出力以上 → 1m 未満 (至近)
        if (rssiDbm >= _txPowerDbm)
            return new DistanceEstimate(0.5, 0.0, 1.0, DistanceConfidence.Low);

        double pathLoss = _txPowerDbm - rssiDbm;
        double d = DistanceFor(pathLoss, frequencyMhz, _pathLossExponent);

        // 不確実性: パスロス ±6dB 相当を距離幅に換算 (マルチパス/遮蔽の目安)
        double dMin = DistanceFor(pathLoss - 6, frequencyMhz, _pathLossExponent);
        double dMax = DistanceFor(pathLoss + 6, frequencyMhz, _pathLossExponent);

        return new DistanceEstimate(
            Meters:    Math.Round(d, 1),
            MinMeters: Math.Round(dMin, 1),
            MaxMeters: Math.Round(dMax, 1),
            Confidence: ConfidenceFor(rssiDbm));
    }

    /// <summary>
    /// <see cref="WifiNetwork"/> の RSSI と周波数から推定する。
    /// FrequencyMhz が未設定ならバンドから代表周波数を補う。
    /// </summary>
    public DistanceEstimate Estimate(WifiNetwork network)
    {
        int rssi = network.Rssi ?? QualityToRssi(network.SignalQuality);
        int freq = network.FrequencyMhz ?? RepresentativeFreq(network.Band);
        return Estimate(rssi, freq);
    }

    private static double DistanceFor(double pathLoss, int freqMhz, double n)
    {
        double exponent = (pathLoss - 20.0 * Math.Log10(freqMhz) + 27.55) / (10.0 * n);
        return Math.Max(0.1, Math.Pow(10, exponent));
    }

    private static DistanceConfidence ConfidenceFor(int rssiDbm) => rssiDbm switch
    {
        >= -50 => DistanceConfidence.High,    // 強信号 = 近距離 = 推定が比較的安定
        >= -70 => DistanceConfidence.Medium,
        _      => DistanceConfidence.Low       // 弱信号はマルチパスで大きくぶれる
    };

    /// <summary>信号品質 (0-100%) を概算 RSSI に変換 (-100..-30 dBm)。</summary>
    private static int QualityToRssi(int quality)
        => (int)Math.Round(-100 + Math.Clamp(quality, 0, 100) * 0.7);

    /// <summary>バンドの代表周波数 (MHz)。</summary>
    private static int RepresentativeFreq(WifiBand band) => band switch
    {
        WifiBand.Band2_4GHz => 2442,  // ch 7 付近
        WifiBand.Band5GHz   => 5500,  // ch 100 付近
        WifiBand.Band6GHz   => 6500,  // 帯域中央付近
        _                   => 2442
    };
}

// ── データ型 ─────────────────────────────────────────────────────

/// <summary>距離推定結果 (メートル、不確実性幅つき)。</summary>
public readonly record struct DistanceEstimate(
    double             Meters,
    double             MinMeters,
    double             MaxMeters,
    DistanceConfidence Confidence)
{
    /// <summary>表示用ラベル (例: "約 4.2 m (2–7 m)")。</summary>
    public string Label => Confidence == DistanceConfidence.Unknown
        ? "Unknown"
        : $"~{Meters:0.#} m ({MinMeters:0.#}–{MaxMeters:0.#} m)";
}

/// <summary>距離推定の信頼度。</summary>
public enum DistanceConfidence { Unknown, Low, Medium, High }
