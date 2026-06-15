using System;
using System.Collections.Generic;
using System.Linq;

namespace MWC.Core.Services;

/// <summary>
/// Wi-Fi 信号品質予測サービス。
///
/// 学術的背景:
///   Formis, Scanzio, Cena, Valenzano,
///   "Linear Combination of Exponential Moving Averages for Wireless Channel Prediction"
///   (IEEE INDIN 2023, arXiv 2509.18933 で発展)
///
/// 複数の時定数を持つ指数移動平均 (EMA) を線形結合することで、
/// 短期変動と長期トレンドの両方を捉え、次の RSSI を予測する。
/// 重いディープラーニングモデルに匹敵する精度を、計算コストほぼゼロで実現する
/// (チャネル非依存モデルでも競合性能、というのが原論文の主要知見)。
///
/// ゼロ外部依存 — stdlib のみ。
/// </summary>
public sealed class SignalQualityPredictor
{
    // 異なる時定数の EMA — 短期(機敏) / 中期 / 長期(安定)
    private readonly double _alphaFast;   // 短期 EMA 係数
    private readonly double _alphaMid;    // 中期
    private readonly double _alphaSlow;   // 長期

    // 線形結合の重み (合計 1.0)
    private readonly double _wFast;
    private readonly double _wMid;
    private readonly double _wSlow;

    private double? _emaFast;
    private double? _emaMid;
    private double? _emaSlow;
    private int     _samples;

    /// <summary>
    /// 既定パラメータは原論文の推奨値に近い値を採用。
    /// alpha が大きいほど直近サンプルへの反応が速い。
    /// </summary>
    public SignalQualityPredictor(
        double alphaFast = 0.6, double alphaMid = 0.3, double alphaSlow = 0.1,
        double wFast = 0.5, double wMid = 0.3, double wSlow = 0.2)
    {
        // EMA 係数 alpha は (0,1] 範囲。範囲外だと平滑化が発散/破綻する。
        static void CheckAlpha(double a, string name)
        {
            if (a is <= 0 or > 1)
                throw new ArgumentOutOfRangeException(name, "EMA coefficient alpha must be in (0, 1].");
        }
        CheckAlpha(alphaFast, nameof(alphaFast));
        CheckAlpha(alphaMid,  nameof(alphaMid));
        CheckAlpha(alphaSlow, nameof(alphaSlow));

        // 重みは非負かつ合計>0。合計0だと正規化で 0/0 = NaN が全予測に伝播する。
        if (wFast < 0 || wMid < 0 || wSlow < 0)
            throw new ArgumentOutOfRangeException(nameof(wFast), "Linear-combination weights must be >= 0.");
        var sum = wFast + wMid + wSlow;
        if (sum <= 0)
            throw new ArgumentOutOfRangeException(nameof(wFast),
                "At least one linear-combination weight must be > 0.");

        _alphaFast = alphaFast; _alphaMid = alphaMid; _alphaSlow = alphaSlow;
        _wFast = wFast / sum; _wMid = wMid / sum; _wSlow = wSlow / sum;
    }

    /// <summary>観測した RSSI サンプルを取り込む。</summary>
    public void Observe(double rssi)
    {
        _emaFast = _emaFast is null ? rssi : _alphaFast * rssi + (1 - _alphaFast) * _emaFast.Value;
        _emaMid  = _emaMid  is null ? rssi : _alphaMid  * rssi + (1 - _alphaMid)  * _emaMid.Value;
        _emaSlow = _emaSlow is null ? rssi : _alphaSlow * rssi + (1 - _alphaSlow) * _emaSlow.Value;
        _samples++;
    }

    /// <summary>
    /// 次の RSSI を予測する。観測がなければ null。
    /// EMA の線形結合で短期反応と長期安定性を両立する。
    /// </summary>
    public double? Predict()
    {
        if (_emaFast is null) return null;
        return _wFast * _emaFast.Value
             + _wMid  * _emaMid!.Value
             + _wSlow * _emaSlow!.Value;
    }

    /// <summary>
    /// 信号の安定性を評価する。
    /// 短期 EMA と長期 EMA の乖離が大きいほど不安定 (変動が激しい)。
    /// </summary>
    public SignalTrend EvaluateTrend()
    {
        if (_emaFast is null || _samples < 3) return SignalTrend.Unknown;

        var diff = _emaFast.Value - _emaSlow!.Value;

        // 短期が長期を上回る = 改善傾向
        if (diff > 3.0)  return SignalTrend.Improving;
        if (diff < -3.0) return SignalTrend.Degrading;
        return SignalTrend.Stable;
    }

    /// <summary>
    /// 一連の RSSI サンプルから次の値をバッチ予測する (ヘルパー)。
    /// </summary>
    public static double? PredictFromHistory(IEnumerable<int> rssiHistory)
    {
        var predictor = new SignalQualityPredictor();
        foreach (var r in rssiHistory) predictor.Observe(r);
        return predictor.Predict();
    }

    /// <summary>取り込んだサンプル数。</summary>
    public int SampleCount => _samples;

    /// <summary>状態をリセットする。</summary>
    public void Reset()
    {
        _emaFast = _emaMid = _emaSlow = null;
        _samples = 0;
    }
}

/// <summary>信号トレンド</summary>
public enum SignalTrend
{
    /// <summary>サンプル不足で判定不可</summary>
    Unknown,
    /// <summary>改善傾向</summary>
    Improving,
    /// <summary>安定</summary>
    Stable,
    /// <summary>悪化傾向</summary>
    Degrading
}
