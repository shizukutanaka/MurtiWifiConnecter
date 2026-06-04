using System;

namespace MWC.Core.Services;

/// <summary>
/// 信号強度の非色覚依存表現サービス。
///
/// WCAG 2.1 達成基準 1.4.1 (色の使用): 色だけで情報を伝えてはならない。
/// 信号強度を色のみで示すと、色覚多様性 (約8%の男性) のユーザーが区別できない。
///
/// 本サービスは信号強度を以下の冗長な手がかりで符号化する:
///   - バーの本数 (0-4)
///   - 形状ラベル (記号)
///   - テキストラベル
///   - 色 (補助的)
///
/// これにより、色を識別できないユーザーもバーの本数・記号・テキストで判断できる。
/// </summary>
public static class SignalIconService
{
    /// <summary>
    /// 信号品質 (0-100) を非色覚依存の表現に変換する。
    /// </summary>
    public static SignalIndicator Describe(int signalQuality)
    {
        var q = Math.Clamp(signalQuality, 0, 100);

        return q switch
        {
            >= 80 => new SignalIndicator(
                Bars: 4, Level: SignalLevel.Excellent,
                Glyph: "▰▰▰▰", TextLabel: "非常に強い", AccentHex: "#3fb950"),
            >= 60 => new SignalIndicator(
                Bars: 3, Level: SignalLevel.Good,
                Glyph: "▰▰▰▱", TextLabel: "強い", AccentHex: "#3fb950"),
            >= 40 => new SignalIndicator(
                Bars: 2, Level: SignalLevel.Fair,
                Glyph: "▰▰▱▱", TextLabel: "普通", AccentHex: "#d29922"),
            >= 20 => new SignalIndicator(
                Bars: 1, Level: SignalLevel.Weak,
                Glyph: "▰▱▱▱", TextLabel: "弱い", AccentHex: "#d29922"),
            _ => new SignalIndicator(
                Bars: 0, Level: SignalLevel.VeryWeak,
                Glyph: "▱▱▱▱", TextLabel: "非常に弱い", AccentHex: "#f85149"),
        };
    }

    /// <summary>
    /// RSSI (dBm) を信号品質 (0-100) に変換する。
    /// -50dBm 以上を 100%、-100dBm 以下を 0% とする標準的な線形変換。
    /// </summary>
    public static int RssiToQuality(int rssiDbm)
    {
        if (rssiDbm >= -50) return 100;
        if (rssiDbm <= -100) return 0;
        return 2 * (rssiDbm + 100);
    }

    /// <summary>
    /// アクセシビリティ用の完全な説明文 (スクリーンリーダー向け)。
    /// 色に依存しない情報のみを含む。
    /// </summary>
    public static string AccessibleLabel(int signalQuality)
    {
        var ind = Describe(signalQuality);
        return $"信号強度 {ind.TextLabel} ({ind.Bars}/4 バー、{signalQuality}%)";
    }
}

/// <summary>信号強度インジケーター (冗長符号化)</summary>
public sealed record SignalIndicator(
    int         Bars,        // 0-4 本のバー
    SignalLevel Level,       // 列挙レベル
    string      Glyph,       // 記号表現 (▰▱)
    string      TextLabel,   // テキストラベル
    string      AccentHex);  // 補助的な色

/// <summary>信号レベル</summary>
public enum SignalLevel
{
    VeryWeak,
    Weak,
    Fair,
    Good,
    Excellent
}
