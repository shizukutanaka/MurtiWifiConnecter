using System;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// BSS Load 要素 (Element ID 11, 802.11e/ax) のパーサ。
///
/// AP がビーコン / プローブ応答に含める BSS 負荷情報を解析する。
/// 固定長 5 バイト本体:
///   - Station Count          (2, リトルエンディアン): 関連付けステーション数
///   - Channel Utilization    (1): 0–255 で表した占有率 (255 ≈ 100%)
///   - Available Admission Capacity (2, リトルエンディアン): 利用可能帯域 (単位: 32 μs/s)
///
/// 切り詰め・不正入力でも例外を投げず null を返す (防衛的設計)。
/// </summary>
public static class BssLoadParser
{
    public const byte BssLoadElementId = 11;
    public const int  FixedBodyLength  = 5;

    /// <summary>
    /// 802.11 情報要素列から最初の BSS Load 要素を取り出して解析する。
    /// 見つからない / 切り詰め → null。
    /// </summary>
    public static BssLoad? Parse(ReadOnlySpan<byte> data)
    {
        int i = 0;
        while (i + 2 <= data.Length)
        {
            byte id  = data[i];
            byte len = data[i + 1];
            int bodyStart = i + 2;
            if (bodyStart + len > data.Length) break;

            if (id == BssLoadElementId && len >= FixedBodyLength)
            {
                var b = data.Slice(bodyStart, len);
                return new BssLoad(
                    StationCount:               (ushort)(b[0] | (b[1] << 8)),
                    ChannelUtilization:         b[2],
                    AvailableAdmissionCapacity: (ushort)(b[3] | (b[4] << 8)));
            }

            i = bodyStart + len;
        }
        return null;
    }
}
