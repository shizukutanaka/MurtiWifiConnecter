using System;

namespace MWC.Core.Services;

/// <summary>
/// Mobility Domain 要素 (Element ID 54, 802.11r) のパーサ。
///
/// 固定長 3 バイト本体:
///   bytes 0-1: MDID (Mobility Domain Identifier) — ローミンググループを識別する
///   byte  2  : FT Capability and Policy
///                bit 0 : FT over-DS (Distribution System) 対応
///                bit 1 : Resource Request Protocol 対応
///
/// FT over-the-air と over-the-DS の違い:
///   - over-the-air  : クライアントが直接 target AP と FT 4-way を実施 (より速い)
///   - over-the-DS   : 現 AP がブリッジ経由で target AP と FT を仲介
///
/// AP がビーコンに Mobility Domain IE を持つ = 802.11r Fast BSS Transition 対応。
/// </summary>
public static class MobilityDomainParser
{
    public const byte MdElementId      = 54;
    public const int  FixedBodyLength  = 3;

    /// <summary>
    /// 802.11 情報要素列から Mobility Domain 要素を解析する。
    /// 見つからない / 切り詰め → null。
    /// </summary>
    public static MobilityDomainInfo? Parse(ReadOnlySpan<byte> data)
    {
        int i = 0;
        while (i + 2 <= data.Length)
        {
            byte id  = data[i];
            byte len = data[i + 1];
            int bodyStart = i + 2;
            if (bodyStart + len > data.Length) break;

            if (id == MdElementId && len >= FixedBodyLength)
            {
                var b = data.Slice(bodyStart, len);
                byte cap = b[2];
                return new MobilityDomainInfo(
                    Mdid:           (ushort)(b[0] | (b[1] << 8)),
                    OverDsCapable:  (cap & 0x01) != 0,
                    ResourceRequestCapable: (cap & 0x02) != 0);
            }

            i = bodyStart + len;
        }
        return null;
    }
}

/// <summary>AP の 802.11r Mobility Domain 情報。</summary>
public sealed record MobilityDomainInfo(
    ushort Mdid,
    bool   OverDsCapable,
    bool   ResourceRequestCapable)
{
    /// <summary>
    /// MDID を 4 桁 16 進数で返す (例: "1A2B")。
    /// 同じ MDID を持つ AP 間で高速ローミングが可能。
    /// </summary>
    public string MdidHex => Mdid.ToString("X4");
}
