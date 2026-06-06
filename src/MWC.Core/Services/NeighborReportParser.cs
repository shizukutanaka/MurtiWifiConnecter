using System;
using System.Collections.Generic;

namespace MWC.Core.Services;

/// <summary>
/// 802.11k Neighbor Report 要素 (Element ID 52) のパーサ。
///
/// Neighbor Report Response フレーム本体には、近隣 AP ごとに 1 つの
/// Neighbor Report 要素が並ぶ。各要素の固定部 (13 バイト) は:
///   - BSSID (6)
///   - BSSID Information (4, リトルエンディアン)
///   - Operating Class (1)
///   - Channel Number (1)
///   - PHY Type (1)
///   - 以降は任意のサブ要素 (本パーサはスキップ)
///
/// プラットフォーム層が取得した生バイト列を構造化する純粋関数。
/// 不正・切り詰めバイト列でも例外を投げず、解釈できた範囲を返す。
/// </summary>
public static class NeighborReportParser
{
    /// <summary>Neighbor Report 要素の Element ID</summary>
    public const byte NeighborReportElementId = 52;

    /// <summary>固定部の長さ (BSSID6 + Info4 + OpClass1 + Channel1 + PHY1)</summary>
    public const int FixedFieldLength = 13;

    /// <summary>
    /// 連結された 802.11 情報要素列から Neighbor Report 要素を抽出して解析する。
    /// </summary>
    public static IReadOnlyList<NeighborApInfo> Parse(ReadOnlySpan<byte> data)
    {
        var list = new List<NeighborApInfo>();
        int i = 0;
        while (i + 2 <= data.Length)
        {
            byte elemId = data[i];
            byte len    = data[i + 1];
            int bodyStart = i + 2;
            if (bodyStart + len > data.Length) break;   // 切り詰め → 終了

            if (elemId == NeighborReportElementId && len >= FixedFieldLength)
            {
                var b = data.Slice(bodyStart, len);
                uint info = (uint)(b[6] | (b[7] << 8) | (b[8] << 16) | (b[9] << 24));
                list.Add(new NeighborApInfo(
                    Bssid:          FormatBssid(b),
                    BssidInfo:      info,
                    OperatingClass: b[10],
                    Channel:        b[11],
                    PhyType:        b[12]));
            }

            i = bodyStart + len;   // 次の要素へ (非 52 要素も長さ分スキップ)
        }
        return list;
    }

    private static string FormatBssid(ReadOnlySpan<byte> b)
        => $"{b[0]:x2}:{b[1]:x2}:{b[2]:x2}:{b[3]:x2}:{b[4]:x2}:{b[5]:x2}";
}

/// <summary>Neighbor Report が示す近隣 AP の情報。</summary>
public sealed record NeighborApInfo(
    string Bssid,
    uint   BssidInfo,
    byte   OperatingClass,
    byte   Channel,
    byte   PhyType)
{
    /// <summary>同一 Mobility Domain — 802.11r 高速遷移が可能 (BSSID Info bit 10)。</summary>
    public bool SameMobilityDomain => (BssidInfo & (1u << 10)) != 0;

    /// <summary>High Throughput (802.11n) 対応 (BSSID Info bit 11)。</summary>
    public bool HighThroughput => (BssidInfo & (1u << 11)) != 0;
}
