using System;

namespace MWC.Core.Services;

/// <summary>
/// TPC Report 要素 (Element ID 35, 802.11h) のパーサ。
///
/// AP の送信電力制御情報を解析する。固定長 2 バイト本体:
///   byte 0: Transmit Power (dBm, 符号付き)
///   byte 1: Link Margin (dB, 符号付き) — 受信側が報告する余裕。ビーコンでは通常 0。
///
/// 送信電力が分かると RSSI からの距離推定精度が上がり、
/// また AP 間の出力差 (カバレッジ設計の良し悪し) を比較できる。
/// 切り詰め・不正入力でも例外を投げない。
/// </summary>
public static class TpcReportParser
{
    public const byte TpcReportElementId = 35;
    public const int  BodyLength         = 2;

    public static TpcReport? Parse(ReadOnlySpan<byte> data)
    {
        int i = 0;
        while (i + 2 <= data.Length)
        {
            byte id  = data[i];
            byte len = data[i + 1];
            int bodyStart = i + 2;
            if (bodyStart + len > data.Length) break;

            if (id == TpcReportElementId && len >= BodyLength)
            {
                var b = data.Slice(bodyStart, len);
                return new TpcReport(
                    TransmitPowerDbm: unchecked((sbyte)b[0]),
                    LinkMarginDb:     unchecked((sbyte)b[1]));
            }

            i = bodyStart + len;
        }
        return null;
    }
}

/// <summary>TPC Report の送信電力情報。</summary>
public sealed record TpcReport(
    sbyte TransmitPowerDbm,
    sbyte LinkMarginDb);
