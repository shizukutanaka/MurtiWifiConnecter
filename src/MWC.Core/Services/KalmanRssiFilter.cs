using System;

namespace MWC.Core.Services;

/// <summary>
/// 1次元カルマンフィルタによる RSSI 平滑化。
///
/// EMA (指数移動平均) は単純だが、測定ノイズとプロセスノイズを区別しない。
/// カルマンフィルタは両者を明示的にモデル化し、観測の信頼度に応じて
/// 動的にゲインを調整するため、急変への追従とノイズ除去を両立する。
///
/// 状態モデル (RSSI はゆっくり変化する定数と仮定):
///   予測:  x̂ₖ⁻ = x̂ₖ₋₁,  Pₖ⁻ = Pₖ₋₁ + Q
///   更新:  Kₖ = Pₖ⁻ / (Pₖ⁻ + R)
///          x̂ₖ = x̂ₖ⁻ + Kₖ(zₖ − x̂ₖ⁻)
///          Pₖ = (1 − Kₖ)Pₖ⁻
///
///   Q: プロセスノイズ (RSSI 自体の変動の大きさ)
///   R: 測定ノイズ (RSSI 測定のばらつき、典型的に数 dB)
///
/// ゼロ外部依存。stdlib のみ。
/// </summary>
public sealed class KalmanRssiFilter
{
    private readonly double _processNoise;      // Q
    private readonly double _measurementNoise;  // R

    private double  _estimate;        // x̂
    private double  _errorCovariance; // P
    private bool    _initialized;
    private int     _samples;

    /// <summary>
    /// 既定値は屋内 Wi-Fi RSSI に適した値。
    /// Q を小さく (RSSI は比較的安定)、R をやや大きく (測定ノイズあり)。
    /// </summary>
    public KalmanRssiFilter(double processNoise = 0.5, double measurementNoise = 4.0)
    {
        _processNoise     = processNoise;
        _measurementNoise = measurementNoise;
        _errorCovariance  = 1.0;
    }

    /// <summary>
    /// 新しい RSSI 測定値を取り込み、平滑化された推定値を返す。
    /// </summary>
    public double Update(double measurement)
    {
        if (!_initialized)
        {
            _estimate    = measurement;
            _initialized = true;
            _samples     = 1;
            return _estimate;
        }

        // 予測ステップ
        double predictedEstimate   = _estimate;
        double predictedCovariance = _errorCovariance + _processNoise;

        // 更新ステップ
        double kalmanGain = predictedCovariance / (predictedCovariance + _measurementNoise);
        _estimate         = predictedEstimate + kalmanGain * (measurement - predictedEstimate);
        _errorCovariance  = (1 - kalmanGain) * predictedCovariance;
        _samples++;

        return _estimate;
    }

    /// <summary>現在の平滑化推定値。観測がなければ null。</summary>
    public double? Current => _initialized ? _estimate : null;

    /// <summary>推定の不確かさ (誤差共分散)。小さいほど信頼できる。</summary>
    public double Uncertainty => _errorCovariance;

    /// <summary>取り込んだサンプル数。</summary>
    public int SampleCount => _samples;

    /// <summary>状態をリセットする。</summary>
    public void Reset()
    {
        _initialized     = false;
        _estimate        = 0;
        _errorCovariance = 1.0;
        _samples         = 0;
    }
}
