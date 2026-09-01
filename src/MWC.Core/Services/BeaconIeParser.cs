using System;
using System.Collections.Generic;
using System.Linq;          // IReadOnlyList<byte>.Contains (Enumerable.Contains) に必要。
                            // 無いと MemoryExtensions.Contains(ReadOnlySpan<byte>,byte) しか
                            // 見えず CS1929 でビルドが落ちる。
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
        var presentExtIds = new List<byte>();
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

            // 拡張要素 (Element ID 255) は Body 先頭 1 バイトが Element ID Extension。
            // 本文が空の壊れた要素で範囲外参照しないよう長さを確認する。
            if (id == ExtendedElementId && len >= 1)
                presentExtIds.Add(body[0]);

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
                    RnrParser.ParseRnrBody(body, rnr ??= new());
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
            PresentElementIds:  presentIds,
            PresentExtensionIds: presentExtIds);
    }

    // ── 個別要素デコーダ (本体スライスのみを受け取る) ─────────────────
    private const byte ExtendedCapabilitiesId = 127;
    /// <summary>拡張要素のコンテナ ID。実体は Body 先頭 1 バイトの Element ID Extension で決まる。</summary>
    private const byte ExtendedElementId = 255;
    private const byte VendorSpecificId = 221;
    // WMM の OUI/Type/Subtype 定数は WmmParser が持つ。ここに複製を残すと
    // 「どちらが正か」が再び分からなくなるため置かない。
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

    // WMM の復号は WmmParser に一本化してある。以前はこのメソッドが AC パラメータの
    // 展開を丸ごと自前で持っており、WmmParser.ParseAcParams と 1 バイト単位で同一の
    // コードが 2 箇所に存在した。WmmParserTests が検証していたのは WmmParser 側で、
    // 製品が実行していたのはこちら側 — テストが「動いていない方の実装」を保証していた。
    // 本体レベルの入口 (ParseParameterBody / ParseQosInfoBody) に委譲することで、
    // 1 パス走査を保ったまま実装を 1 つにする。
    private static void DecodeVendorSpecific(
        ReadOnlySpan<byte> b, ref WmmParameters? wmm, ref byte? wmmQosInfo)
    {
        wmmQosInfo ??= WmmParser.ParseQosInfoBody(b);
        wmm        ??= WmmParser.ParseParameterBody(b);
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
    IReadOnlyList<byte>           PresentElementIds,
    // 既定値を持たせて後方互換にする。既存の呼び出し側 (テストを含む) は
    // 拡張要素を扱わないため、追加のたびに全構築箇所を書き換える必要はない。
    IReadOnlyList<byte>?          PresentExtensionIds = null)
{
    /// <summary>802.11r Fast BSS Transition 対応 (Mobility Domain 要素あり)。</summary>
    public bool SupportsFastTransition => MobilityDomain is not null;

    /// <summary>
    /// 802.11u Interworking 要素 (Element ID 107) を含む = Passpoint / Hotspot 2.0 の候補。
    ///
    /// Interworking 要素の存在は「この AP がネットワーク選択のための情報提供に対応している」
    /// ことを示し、Passpoint 対応 AP は必ずこれを広告する。Hotspot 2.0 の完全な判定には
    /// さらに Vendor Specific 要素 (WFA OUI) の確認が要るが、Interworking の有無は
    /// 第一段のふるい分けとして有効で、`Hotspot20Service` が必要とするのはこの信号である。
    ///
    /// 専用フィールドを増やさず <see cref="PresentElementIds"/> から導出しているのは、
    /// パーサーが既に全要素 ID を記録しており、本要素については「あるか無いか」しか
    /// 使わないため — 使わない本文を保持する理由がない。
    /// </summary>
    public bool HasInterworking => PresentElementIds.Contains(InterworkingElementId);

    /// <summary>802.11u Interworking 要素の Element ID。</summary>
    public const byte InterworkingElementId = 107;

    /// <summary>
    /// 802.11be Multi-Link 要素 (拡張要素、Element ID Extension 107) を広告しているか
    /// = この AP は Wi-Fi 7 の MLO (Multi-Link Operation) に対応している。
    ///
    /// これは AP が**広告する能力**であり、実際に張られたリンクの本数や
    /// リンクごとの RSSI とは別物である。後者は接続中のランタイム API
    /// (`ManagedNativeWifi.GetRealtimeConnectionQuality`) からしか得られず、
    /// `WifiNetwork.MloLinks` を埋めるにはそちらが要る (docs/FEATURE-AUDIT.md §1d)。
    /// ビーコンから分かるのは「対応しているか否か」までで、
    /// スキャン一覧で Wi-Fi 7 AP を見分けるにはそれで足りる。
    ///
    /// Interworking (ID 107) と数値が同じだが**名前空間が異なる** —
    /// あちらは通常の Element ID、こちらは拡張要素の Element ID Extension。
    /// 混同しないよう別プロパティ・別リストで扱う。
    /// </summary>
    public bool HasMultiLink =>
        PresentExtensionIds is not null && PresentExtensionIds.Contains(MultiLinkExtensionId);

    /// <summary>802.11be Multi-Link 要素の Element ID Extension。</summary>
    public const byte MultiLinkExtensionId = 107;

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
