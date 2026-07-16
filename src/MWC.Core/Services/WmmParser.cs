using System;
using System.Collections.Generic;

namespace MWC.Core.Services;

/// <summary>
/// WMM (Wi-Fi Multimedia) / WME Parameter 要素のパーサ。
///
/// WMM は Vendor Specific 要素 (Element ID 221) として送信される:
///   OUI:      00 50 F2
///   Type:     02 (WMM)
///   Subtype:  01 (WMM Parameter)  or  00 (WMM Info)
///   Version:  01
///   QoS Info: 1 byte
///   Reserved: 1 byte  (Parameter のみ)
///   AC Params:4 × 4 bytes (Parameter のみ) — BE/BK/VI/VO 順
///
/// 各 AC エントリ (4 バイト):
///   byte 0: ACI/AIFSN
///     bits 0-3: AIFSN (Arbitration Inter-Frame Space Number)
///     bit  4  : ACM   (Admission Control Mandatory)
///     bits 5-6: ACI   (00=BE, 01=BK, 10=VI, 11=VO)
///   byte 1: ECWmin (bits 0-3) / ECWmax (bits 4-7)
///   byte 2-3: TXOP Limit (リトルエンディアン, 単位 32 μs)
///
/// 参考: Wi-Fi Alliance WMM Specification v1.2.0; IEEE 802.11-2020 §9.4.2.30
/// </summary>
public static class WmmParser
{
    private const byte VendorSpecificId = 221;
    private static ReadOnlySpan<byte> WmmOui => [0x00, 0x50, 0xF2];
    private const byte WmmType      = 0x02;
    private const byte WmmSubtypeParam = 0x01;
    private const byte WmmSubtypeInfo  = 0x00;
    private const byte WmmVersion   = 0x01;

    // WMM Parameter 本体の最小長 (OUI3 + Type1 + Subtype1 + Ver1 + QoS1 + Rsvd1 + 4×AC4 = 24)
    private const int MinParamBodyLen = 24;
    // WMM Info 最小長 (OUI3 + Type1 + Subtype1 + Ver1 + QoS1 = 7)
    private const int MinInfoBodyLen  = 7;

    /// <summary>
    /// 802.11 情報要素列から WMM Parameter 要素を解析する。
    /// WMM Parameter が見つからない場合は null (WMM Info のみでも null)。
    /// </summary>
    public static WmmParameters? ParseParameters(ReadOnlySpan<byte> data)
    {
        int i = 0;
        while (i + 2 <= data.Length)
        {
            byte id  = data[i];
            byte len = data[i + 1];
            int bodyStart = i + 2;
            if (bodyStart + len > data.Length) break;

            if (id == VendorSpecificId && len >= MinParamBodyLen)
            {
                var b = data.Slice(bodyStart, len);
                if (IsWmmParam(b))
                    return ParseAcParams(b);
            }

            i = bodyStart + len;
        }
        return null;
    }

    /// <summary>
    /// 情報要素列から WMM QoS Info バイトを取得する (Info 要素からでも可)。
    /// AP の EDCA パラメータ更新カウンタ (bits 0-3) を含む。
    /// </summary>
    public static byte? ParseQosInfo(ReadOnlySpan<byte> data)
    {
        int i = 0;
        while (i + 2 <= data.Length)
        {
            byte id  = data[i];
            byte len = data[i + 1];
            int bodyStart = i + 2;
            if (bodyStart + len > data.Length) break;

            if (id == VendorSpecificId && len >= MinInfoBodyLen)
            {
                var b = data.Slice(bodyStart, len);
                if (IsWmmOui(b) && b[3] == WmmType &&
                    (b[4] == WmmSubtypeParam || b[4] == WmmSubtypeInfo) &&
                    b[5] == WmmVersion)
                    return b[6]; // QoS Info
            }

            i = bodyStart + len;
        }
        return null;
    }

    private static bool IsWmmOui(ReadOnlySpan<byte> b)
        => b.Length >= 3 && b[0] == WmmOui[0] && b[1] == WmmOui[1] && b[2] == WmmOui[2];

    private static bool IsWmmParam(ReadOnlySpan<byte> b)
        => IsWmmOui(b) && b.Length > 5 && b[3] == WmmType
           && b[4] == WmmSubtypeParam && b[5] == WmmVersion;

    private static WmmParameters ParseAcParams(ReadOnlySpan<byte> b)
    {
        // byte 6: QoS Info, byte 7: Reserved, bytes 8-23: 4 AC params (4B each)
        byte qosInfo = b[6];
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
        return new WmmParameters(qosInfo, ac);
    }
}

// ── データ型 ─────────────────────────────────────────────────────

/// <summary>AP がビーコンで示す WMM EDCA パラメータ。</summary>
public sealed record WmmParameters(byte QosInfo, WmmAcParam[] AcParams)
{
    /// <summary>EDCA パラメータ更新カウンタ (bits 0-3)。変化で再取得が必要。</summary>
    public int ParameterSetCount => QosInfo & 0x0F;

    /// <summary>U-APSD アドバタイズ (bit 7)。</summary>
    public bool UapsdEnabled => (QosInfo & 0x80) != 0;

    /// <summary>指定カテゴリのパラメータを返す。</summary>
    public WmmAcParam? GetAc(WmmAccessCategory cat)
    {
        foreach (var p in AcParams)
            if (p.Category == cat) return p;
        return null;
    }
}

/// <summary>1 つの Access Category (AC) の EDCA パラメータ。</summary>
public sealed record WmmAcParam(
    WmmAccessCategory Category,
    byte  Aifsn,
    bool  AdmissionControlMandatory,
    byte  EcwMin,
    byte  EcwMax,
    ushort TxopLimit)
{
    /// <summary>CWmin = 2^ECWmin - 1 (競合ウィンドウ下限)。</summary>
    public int CwMin => (1 << EcwMin) - 1;

    /// <summary>CWmax = 2^ECWmax - 1 (競合ウィンドウ上限)。</summary>
    public int CwMax => (1 << EcwMax) - 1;

    /// <summary>TXOP Limit を μs で返す (単位は 32 μs)。</summary>
    public int TxopLimitMicroseconds => TxopLimit * 32;
}

/// <summary>WMM Access Category (優先度順: VO > VI > BE > BK)。</summary>
public enum WmmAccessCategory
{
    /// <summary>Best Effort — デフォルトトラフィック (ACI=0)</summary>
    BestEffort = 0,
    /// <summary>Background — バルク転送・低優先度 (ACI=1)</summary>
    Background = 1,
    /// <summary>Video — ビデオストリーム (ACI=2)</summary>
    Video = 2,
    /// <summary>Voice — VoIP/音声 (最高優先度) (ACI=3)</summary>
    Voice = 3
}
