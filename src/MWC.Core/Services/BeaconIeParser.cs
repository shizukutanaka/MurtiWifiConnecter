using System;
using System.Collections.Generic;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// ビーコン / プローブ応答の情報要素 (IE) 列を **1 パス**で走査し、
/// MWC が関心を持つ全要素をまとめて抽出する集約パーサ。
///
/// 個別パーサ (NeighborReportParser, BssLoadParser, RnrParser,
/// MobilityDomainParser, WmmParser) はそれぞれ独立に全 IE を走査するため、
/// プラットフォーム層が同じ blob を 5 回舐めることになる。本クラスは
/// 走査を 1 回に統合し、各要素の本体スライスだけを対応デコーダへ渡す。
///
/// 802.11 IE の基本構造: [Element ID (1)] [Length (1)] [Body (Length)]
/// 拡張要素 (Element ID 255) は Body 先頭 1 バイトが Element ID Extension。
///
/// 不正・切り詰め入力でも例外を投げず、解釈できた範囲を返す (防衛的設計)。
/// </summary>
public static class BeaconIeParser
{
    /// <summary>IE 列を 1 パスで解析し、関心要素を集約した要約を返す。</summary>
    public static BeaconIeSummary Parse(ReadOnlySpan<byte> data)
    {
        List<NeighborApInfo>? neighbors = null;
        List<RnrNeighborAp>? rnr = null;
        BssLoad? bssLoad = null;
        MobilityDomainInfo? mobilityDomain = null;
        WmmParameters? wmm = null;
        byte? wmmQosInfo = null;
        CountryInfo? country = null;
        TpcReport? tpc = null;
        var presentIds = new List<byte>();
        bool bssTransitionMgmt = false;

        int i = 0;
        while (i + 2 <= data.Length)
        {
            byte id  = data[i];
            byte len = data[i + 1];
            int bodyStart = i + 2;
            if (bodyStart + len > data.Length) break;   // 切り詰め → 終了

            var body = data.Slice(bodyStart, len);
            presentIds.Add(id);

            switch (id)
            {
                case NeighborReportParser.NeighborReportElementId
                    when len >= NeighborReportParser.FixedFieldLength:
                    (neighbors ??= new()).Add(DecodeNeighbor(body));
                    break;

                case MobilityDomainParser.MdElementId
                    when len >= MobilityDomainParser.FixedBodyLength && mobilityDomain is null:
                    mobilityDomain = DecodeMobilityDomain(body);
                    break;

                case BssLoadParser.BssLoadElementId
                    when len >= BssLoadParser.FixedBodyLength && bssLoad is null:
                    bssLoad = DecodeBssLoad(body);
                    break;

                case RnrParser.RnrElementId:
                    DecodeRnr(body, rnr ??= new());
                    break;

                case CountryInfoParser.CountryElementId
                    when len >= CountryInfoParser.MinBodyLength && country is null:
                    country = CountryInfoParser.Parse(data.Slice(i, 2 + len));
                    break;

                case TpcReportParser.TpcReportElementId
                    when len >= TpcReportParser.BodyLength && tpc is null:
                    tpc = TpcReportParser.Parse(data.Slice(i, 2 + len));
                    break;

                // EID 127: Extended Capabilities (IEEE 802.11-2020 §9.4.2.27)
                // Bit 19 = BSS Transition (802.11v) = byte[2] bit 3
                case ExtendedCapabilitiesId when len >= 3:
                    bssTransitionMgmt = (body[2] & 0x08) != 0;
                    break;

                case VendorSpecificId:
                    DecodeVendorSpecific(body, ref wmm, ref wmmQosInfo);
                    break;
            }

            i = bodyStart + len;
        }

        return new BeaconIeSummary(
            Neighbors:          neighbors ?? EmptyNeighbors,
            RnrNeighbors:       rnr ?? EmptyRnr,
            BssLoad:            bssLoad,
            MobilityDomain:     mobilityDomain,
            Wmm:                wmm,
            WmmQosInfo:         wmmQosInfo,
            Country:            country,
            Tpc:                tpc,
            BssTransitionMgmt:  bssTransitionMgmt,
            PresentElementIds:  presentIds);
    }

    // ── 個別要素デコーダ (本体スライスのみを受け取る) ─────────────────
    private const byte ExtendedCapabilitiesId = 127;
    private const byte VendorSpecificId = 221;
    private static readonly byte[] WmmOui = { 0x00, 0x50, 0xF2 };
    private static readonly IReadOnlyList<NeighborApInfo> EmptyNeighbors = Array.Empty<NeighborApInfo>();
    private static readonly IReadOnlyList<RnrNeighborAp>  EmptyRnr       = Array.Empty<RnrNeighborAp>();

    private static NeighborApInfo DecodeNeighbor(ReadOnlySpan<byte> b)
    {
        uint info = (uint)(b[6] | (b[7] << 8) | (b[8] << 16) | (b[9] << 24));
        return new NeighborApInfo(
            Bssid:          FormatBssid(b),
            BssidInfo:      info,
            OperatingClass: b[10],
            Channel:        b[11],
            PhyType:        b[12]);
    }

    private static MobilityDomainInfo DecodeMobilityDomain(ReadOnlySpan<byte> b)
    {
        byte cap = b[2];
        return new MobilityDomainInfo(
            Mdid:                   (ushort)(b[0] | (b[1] << 8)),
            OverDsCapable:          (cap & 0x01) != 0,
            ResourceRequestCapable: (cap & 0x02) != 0);
    }

    private static BssLoad DecodeBssLoad(ReadOnlySpan<byte> b)
        => new(
            StationCount:               (ushort)(b[0] | (b[1] << 8)),
            ChannelUtilization:         b[2],
            AvailableAdmissionCapacity: (ushort)(b[3] | (b[4] << 8)));

    private static void DecodeRnr(ReadOnlySpan<byte> body, List<RnrNeighborAp> result)
    {
        int pos = 0;
        while (pos + 2 <= body.Length)
        {
            int info        = body[pos] | (body[pos + 1] << 8);
            int tbttCount   = (info & 0x000F) + 1;
            int tbttInfoLen = (info >> 9) & 0x7F;
            pos += 2;

            // tbttInfoLen == 0 は不正 (802.11 最小1バイト)。
            // 0 のまま進むと pos が動かず無限ループになるため停止する。
            if (tbttInfoLen == 0) break;

            for (int t = 0; t < tbttCount; t++)
            {
                if (pos + tbttInfoLen > body.Length) return;
                if (tbttInfoLen >= 3)
                {
                    byte opClass = body[pos + 1];
                    byte channel = body[pos + 2];
                    string? bssid = tbttInfoLen >= 9 ? FormatBssid(body.Slice(pos + 3, 6)) : null;
                    result.Add(new RnrNeighborAp(opClass, channel, bssid));
                }
                pos += tbttInfoLen;
            }
        }
    }

    private static void DecodeVendorSpecific(
        ReadOnlySpan<byte> b, ref WmmParameters? wmm, ref byte? wmmQosInfo)
    {
        // WMM: OUI 00:50:F2, Type 02
        if (b.Length < 7) return;
        if (b[0] != WmmOui[0] || b[1] != WmmOui[1] || b[2] != WmmOui[2]) return;
        if (b[3] != 0x02 || b[5] != 0x01) return;   // Type=WMM, Version=1

        byte subtype = b[4];
        if (subtype == 0x00 || subtype == 0x01)
            wmmQosInfo ??= b[6];

        // WMM Parameter (subtype 1): 4 AC params follow Reserved byte
        if (subtype == 0x01 && b.Length >= 24 && wmm is null)
        {
            var ac = new WmmAcParam[4];
            for (int k = 0; k < 4; k++)
            {
                int off = 8 + k * 4;
                byte aciAifsn = b[off];
                byte ecw      = b[off + 1];
                ushort txop   = (ushort)(b[off + 2] | (b[off + 3] << 8));
                ac[k] = new WmmAcParam(
                    Category:  (WmmAccessCategory)((aciAifsn >> 5) & 0x03),
                    Aifsn:     (byte)(aciAifsn & 0x0F),
                    AdmissionControlMandatory: (aciAifsn & 0x10) != 0,
                    EcwMin:    (byte)(ecw & 0x0F),
                    EcwMax:    (byte)((ecw >> 4) & 0x0F),
                    TxopLimit: txop);
            }
            wmm = new WmmParameters(b[6], ac);
        }
    }

    private static string FormatBssid(ReadOnlySpan<byte> b)
        => $"{b[0]:x2}:{b[1]:x2}:{b[2]:x2}:{b[3]:x2}:{b[4]:x2}:{b[5]:x2}";
}

/// <summary>ビーコン IE を 1 パス解析した集約結果。</summary>
public sealed record BeaconIeSummary(
    IReadOnlyList<NeighborApInfo> Neighbors,
    IReadOnlyList<RnrNeighborAp>  RnrNeighbors,
    BssLoad?                      BssLoad,
    MobilityDomainInfo?           MobilityDomain,
    WmmParameters?                Wmm,
    byte?                         WmmQosInfo,
    CountryInfo?                  Country,
    TpcReport?                    Tpc,
    bool                          BssTransitionMgmt,
    IReadOnlyList<byte>           PresentElementIds)
{
    /// <summary>802.11r Fast BSS Transition 対応 (Mobility Domain 要素あり)。</summary>
    public bool SupportsFastTransition => MobilityDomain is not null;

    /// <summary>802.11k Neighbor Report 情報を含む。</summary>
    public bool HasNeighborReport => Neighbors.Count > 0;

    /// <summary>WMM/QoS 対応 (WMM 要素あり)。</summary>
    public bool SupportsWmm => WmmQosInfo is not null;

    /// <summary>RNR で発見された 6GHz 近隣 AP があるか。</summary>
    public bool Has6GhzRnrNeighbor
    {
        get
        {
            foreach (var n in RnrNeighbors)
                if (n.Is6GHz) return true;
            return false;
        }
    }
}
