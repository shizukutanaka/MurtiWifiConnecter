using System;
using System.Collections.Generic;

namespace MWC.Core.Services;

/// <summary>
/// Reduced Neighbor Report (RNR) 要素 (Element ID 201) のパーサ。
///
/// 802.11ax / Wi-Fi 6E で導入。2.4/5GHz ビーコンに 6GHz AP の情報を埋め込み、
/// クライアントが 6GHz 帯へのスキャンなしに近隣 AP を発見できるようにする。
///
/// 要素構造 (本体は 1 つ以上の TBTT Information Set の連鎖):
///   [Neighbor AP Info field (2B)] [TBTT Information Set ...]
///   Neighbor AP Info:
///     bits 0-3  : TBTT Info Count - 1 (実際の TBTT 数)
///     bits 4    : TBTT Info Type (0=Short, 1=Extended)
///     bits 5    : Filtered Neighbor AP
///     bits 6-8  : Reserved
///     bits 9-15 : TBTT Info Length (各 TBTT エントリの長さ, バイト)
///   TBTT Info Set: TBTT Count エントリが連続 (各 TBTTInfoLength バイト)
///   各 TBTT エントリの先頭 1B: TBTT Offset (単位: 0.5 TU)
///   残り: Operating Class, Channel Number, BSSID(6), ... (Length 依存)
///
/// 本パーサは Operating Class/Channel/BSSID の抽出に特化
/// (Short format TBTT = 7B 以上を期待)。
/// 切り詰め・不正入力でも例外を投げない防衛的設計。
/// </summary>
public static class RnrParser
{
    public const byte RnrElementId    = 201;
    private const int NeighborInfoLen = 2;   // Neighbor AP Info フィールド長
    private const int MinTbttLen      = 7;   // Offset(1) + OpClass(1) + Channel(1) + BSSID(6) - 2 = minimum for BSSID

    /// <summary>
    /// 802.11 情報要素列から RNR 要素をすべて解析し、
    /// 参照される 6GHz (および他帯域) AP の概要を返す。
    /// </summary>
    public static IReadOnlyList<RnrNeighborAp> Parse(ReadOnlySpan<byte> data)
    {
        var result = new List<RnrNeighborAp>();
        int i = 0;
        while (i + 2 <= data.Length)
        {
            byte id  = data[i];
            byte len = data[i + 1];
            int bodyStart = i + 2;
            if (bodyStart + len > data.Length) break;

            if (id == RnrElementId)
                ParseRnrBody(data.Slice(bodyStart, len), result);

            i = bodyStart + len;
        }
        return result;
    }

    private static void ParseRnrBody(ReadOnlySpan<byte> body, List<RnrNeighborAp> result)
    {
        int pos = 0;
        while (pos + NeighborInfoLen <= body.Length)
        {
            // Neighbor AP Info field (2 bytes, little-endian)
            int info         = body[pos] | (body[pos + 1] << 8);
            int tbttCount    = (info & 0x000F) + 1;          // bits 3-0, stored as count-1
            int tbttInfoLen  = (info >> 9) & 0x7F;           // bits 15-9

            pos += NeighborInfoLen;

            // TBTT Info Set: tbttCount エントリ × tbttInfoLen バイト
            for (int t = 0; t < tbttCount; t++)
            {
                if (pos + tbttInfoLen > body.Length) return;

                // Operating Class と Channel は Offset の後の 2 バイト
                // ただし Short format では:
                //   byte 0: TBTT Offset
                //   byte 1: Operating Class
                //   byte 2: Channel Number
                //   bytes 3-8: BSSID (optional, tbttInfoLen >= 9)
                if (tbttInfoLen >= 3)
                {
                    byte opClass = body[pos + 1];
                    byte channel = body[pos + 2];
                    string? bssid = null;
                    if (tbttInfoLen >= 9)
                        bssid = FormatBssid(body.Slice(pos + 3, 6));

                    result.Add(new RnrNeighborAp(
                        OperatingClass: opClass,
                        Channel:        channel,
                        Bssid:          bssid));
                }

                pos += tbttInfoLen;
            }
        }
    }

    private static string FormatBssid(ReadOnlySpan<byte> b)
        => $"{b[0]:x2}:{b[1]:x2}:{b[2]:x2}:{b[3]:x2}:{b[4]:x2}:{b[5]:x2}";
}

/// <summary>RNR 要素が示す近隣 AP の情報 (Operating Class ベース)。</summary>
public sealed record RnrNeighborAp(
    byte    OperatingClass,
    byte    Channel,
    string? Bssid)
{
    /// <summary>6GHz 帯域の AP かどうか (Operating Class 131–135)。</summary>
    public bool Is6GHz => OperatingClass is >= 131 and <= 135;
}
