using System;
using System.Collections.Generic;

namespace MWC.Core.Services;

/// <summary>
/// Reduced Neighbor Report (RNR) 要素 (Element ID 201) のパーサ。
///
/// 802.11ax / Wi-Fi 6E で導入。2.4/5GHz ビーコンに 6GHz AP の情報を埋め込み、
/// クライアントが 6GHz 帯へのスキャンなしに近隣 AP を発見できるようにする。
/// 802.11be (Wi-Fi 7) では TBTT Information に MLD Parameters サブフィールドが
/// 追加され、同一 AP MLD の他リンクを識別できる。
///
/// 要素構造 (本体は 1 つ以上の Neighbor AP Information フィールドの連鎖):
///   [TBTT Information Header (2B)] [Operating Class (1B)] [Channel Number (1B)]
///   [TBTT Information Set ...]
///   TBTT Information Header (little-endian u16):
///     bits 0-1  : TBTT Information Field Type
///     bit  2    : Filtered Neighbor AP
///     bit  3    : Reserved
///     bits 4-7  : TBTT Information Count - 1
///     bits 8-15 : TBTT Information Length (各 TBTT エントリの長さ, バイト)
///   Operating Class / Channel Number はこのフィールドに 1 回だけ置かれる
///   (各 TBTT エントリの中ではない)。
///   各 TBTT Information エントリ (TBTT Information Length バイト):
///     byte 0      : Neighbor AP TBTT Offset (単位: 0.5 TU)
///     以降のサブフィールドは Length 値で有無が決まる (出現順は固定):
///       BSSID (6B)        : Length ∈ {7,8,9,11,12,13} または ≥16
///       Short-SSID (4B)   : Length ∈ {5,6,11,12,13} または ≥16
///       BSS Parameters(1B): Length ∈ {2,6,8,9,12,13} または ≥16
///       20MHz PSD (1B)    : Length ∈ {9,13} または ≥16
///       MLD Parameters(3B): Length ≥ 16
///       Reserved          : Length &gt; 16 の残り
///   (長さ 0,3,4,10,14,15 は Reserved — 内容は読まずにスキップ)
///   MLD Parameters (3B, little-endian):
///     bits 0-7  : MLD ID (報告 AP が属する AP MLD の識別子)
///     bits 8-11 : Link ID
///     bits 12-19: BSS Parameters Change Count
///     bit  20   : All Updates Included
///     bit  21   : Disabled Link Indication
///     bits 22-23: Reserved
///
/// 上記ビット配置は Wireshark の dissector (epan/dissectors/packet-ieee80211.c
/// の dissect_neighbor_ap_info / hf_ieee80211_rnr_mld_* マスク)と突き合わせて
/// 確認済み — 802.11-2020 §9.4.2.170 / 802.11be の公式レイアウトと一致する。
/// (2026-09-19 修正: 従来は count を bits 0-3、length を bits 9-15 と読み、
/// かつ Operating Class/Channel を各エントリ内と誤認していたため、実ビーコンは
/// 全て誤読されていた。テストフィクスチャも同じ誤配置で書かれていたため
/// 双方が通ってしまっていた。)
///
/// 切り詰め・不正入力でも例外を投げない防衛的設計。
/// </summary>
public static class RnrParser
{
    public const byte RnrElementId    = 201;
    private const int NeighborInfoLen = 2;   // TBTT Information Header 長

    /// <summary>
    /// 802.11 情報要素列から RNR 要素をすべて解析し、
    /// 参照される近隣 AP の概要を返す。
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

    internal static void ParseRnrBody(ReadOnlySpan<byte> body, List<RnrNeighborAp> result)
    {
        int pos = 0;
        while (pos + NeighborInfoLen + 2 <= body.Length)
        {
            // TBTT Information Header (2 bytes, little-endian)
            int info        = body[pos] | (body[pos + 1] << 8);
            int tbttCount   = ((info >> 4) & 0x0F) + 1;   // bits 7-4, stored as count-1
            int tbttInfoLen = (info >> 8) & 0xFF;          // bits 15-8

            pos += NeighborInfoLen;
            // Operating Class / Channel Number は TBTT Information Header の直後に
            // フィールド単位で一度だけ置かれる。
            byte opClass = body[pos];
            byte channel = body[pos + 1];
            pos += 2;

            // tbttInfoLen == 0 は不正 (802.11 最小 1 バイト)。
            // 0 のまま進むと後続の Neighbor AP Info 解析がずれるため停止する。
            if (tbttInfoLen == 0) break;

            for (int t = 0; t < tbttCount; t++)
            {
                if (pos + tbttInfoLen > body.Length)
                {
                    // 切り詰められたエントリは破棄するが、後続セットがあれば継続。
                    pos += (tbttCount - t) * tbttInfoLen;
                    break;
                }

                ParseTbttEntry(body.Slice(pos, tbttInfoLen), opClass, channel, result);
                pos += tbttInfoLen;
            }
        }
    }

    private static void ParseTbttEntry(
        ReadOnlySpan<byte> entry, byte opClass, byte channel, List<RnrNeighborAp> result)
    {
        int len = entry.Length;
        int p = 1; // byte 0 = Neighbor AP TBTT Offset

        // 各サブフィールドの有無は Length 値で決まる (出現順は固定)。
        // Length > 16 は 16 と同じサブフィールド構成 + Reserved テール。
        int el = Math.Min(len, 16);
        bool hasBssid     = el is 7 or 8 or 9 or 11 or 12 or 13 or 16;
        bool hasShortSsid = el is 5 or 6 or 11 or 12 or 13 or 16;
        bool hasBssParams = el is 2 or 6 or 8 or 9 or 12 or 13 or 16;
        bool hasPsd       = el is 9 or 13 or 16;
        bool hasMldParams = len >= 16;

        // Reserved 長 (3,4,10,14,15) では全サブフィールドが偽なので
        // 実質 TBTT Offset のみのエントリとして扱う。

        string? bssid = null;
        byte?   mldId = null;
        byte?   linkId = null;

        if (hasBssid)
        {
            if (p + 6 > len) return;
            bssid = FormatBssid(entry.Slice(p, 6));
            p += 6;
        }
        if (hasShortSsid)
        {
            if (p + 4 > len) return;
            p += 4; // Short-SSID (SSID の CRC32)。MLO リンク特定には不要。
        }
        if (hasBssParams)
        {
            if (p + 1 > len) return;
            p += 1; // BSS Parameters — OCT Recommended 等のフラグバイト
        }
        if (hasPsd)
        {
            if (p + 1 > len) return;
            p += 1; // 20 MHz PSD
        }
        if (hasMldParams)
        {
            if (p + 3 > len) return;
            int mld = entry[p] | (entry[p + 1] << 8) | (entry[p + 2] << 16);
            mldId   = (byte)(mld & 0xFF);        // bits 0-7:  MLD ID
            linkId  = (byte)((mld >> 8) & 0x0F); // bits 8-11: Link ID
        }

        result.Add(new RnrNeighborAp(
            OperatingClass: opClass,
            Channel:        channel,
            Bssid:          bssid,
            MldId:          mldId,
            MldLinkId:      linkId));
    }

    private static string FormatBssid(ReadOnlySpan<byte> b)
        => $"{b[0]:x2}:{b[1]:x2}:{b[2]:x2}:{b[3]:x2}:{b[4]:x2}:{b[5]:x2}";
}

/// <summary>RNR 要素が示す近隣 AP の情報 (Operating Class ベース)。</summary>
public sealed record RnrNeighborAp(
    byte    OperatingClass,
    byte    Channel,
    string? Bssid,
    byte?   MldId = null,
    byte?   MldLinkId = null)
{
    /// <summary>6GHz 帯域の AP かどうか (Operating Class 131–135)。</summary>
    public bool Is6GHz => OperatingClass is >= 131 and <= 135;

    /// <summary>
    /// MLD Parameters を広告しているか。true ならこの近隣 AP は何らかの
    /// AP MLD のリンクである。ビーコン発信 AP 自身の MLD との同一性は
    /// <see cref="MldId"/> を ML 要素の AP MLD ID と照合して判定する。
    /// </summary>
    public bool IsMloAffiliated => MldId.HasValue;
}
