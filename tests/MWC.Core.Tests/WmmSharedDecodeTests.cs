using FluentAssertions;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  WMM デコードが 1 実装であることを固定するテスト。
//
//  なぜ要るか:
//    WMM の AC パラメータ展開は以前 2 箇所にあった —
//    `WmmParser.ParseAcParams` と `BeaconIeParser.DecodeVendorSpecific` の中身。
//    バイト単位で同一のコードだったが、**製品が実行していたのは BeaconIeParser 側**で、
//    `WmmParserTests` が検証していたのは WmmParser 側だった。
//    つまりテストは「動いていない方の実装」を保証しており、実際に走る経路は無検査だった。
//    片方だけを直せば両者は静かに食い違う。
//
//    現在は BeaconIeParser が WmmParser の本体レベル入口へ委譲するため実装は 1 つ。
//    このテストはその不変条件 —「2 つの入口が同じ答えを返す」— を固定する。
//    再び複製が持ち込まれたら、ここが落ちる。
// ══════════════════════════════════════════════════════════════
public class WmmSharedDecodeTests
{
    // WMM Parameter 要素 (Element ID 221 / Length 24 / OUI 00:50:F2 / Type 2 / Subtype 1)
    private static byte[] ParamElement() =>
    [
        221, 24,
        0x00, 0x50, 0xF2,   // OUI
        0x02,               // OUI Type = WMM
        0x01,               // Subtype = Parameter
        0x01,               // Version
        0x02, 0x00,         // QoS Info, Reserved
        0x03, 0xA4, 0x00, 0x00,   // AC_BE  AIFSN=3
        0x27, 0xA4, 0x00, 0x00,   // AC_BK  AIFSN=7
        0x42, 0x43, 0xBC, 0x00,   // AC_VI  TXOP=188
        0x62, 0x32, 0x66, 0x00,   // AC_VO  TXOP=102
    ];

    // WMM Info 要素 (Subtype 0)。Parameter は含まないが QoS Info は取れる。
    private static byte[] InfoElement() =>
        [221, 7, 0x00, 0x50, 0xF2, 0x02, 0x00, 0x01, 0x03];

    [Fact]
    public void BothEntryPoints_DecodeParametersIdentically()
    {
        var element = ParamElement();

        var viaScanner   = WmmParser.ParseParameters(element);   // IE 列を自分で走査する入口
        var viaOnePass   = BeaconIeParser.Parse(element).Wmm;    // 製品が実際に通る 1 パス入口

        viaScanner.Should().NotBeNull("the element is a well-formed WMM Parameter IE");
        viaOnePass.Should().NotBeNull("the one-pass parser must find the same element");
        viaOnePass.Should().BeEquivalentTo(viaScanner);
    }

    [Fact]
    public void BothEntryPoints_ReadTheSameQosInfo()
    {
        var element = InfoElement();

        byte? viaScanner = WmmParser.ParseQosInfo(element);
        byte? viaOnePass = BeaconIeParser.Parse(element).WmmQosInfo;

        viaScanner.Should().Be((byte)0x03);
        viaOnePass.Should().Be(viaScanner);
    }

    [Fact]
    public void OnePassParser_LeavesWmmNull_WhenOnlyInfoElementPresent()
    {
        // Info 要素 (Subtype 0) は AC パラメータを持たない。QoS Info だけが取れる。
        var summary = BeaconIeParser.Parse(InfoElement());

        summary.Wmm.Should().BeNull();
        summary.WmmQosInfo.Should().Be(0x03);
    }

    [Fact]
    public void ParameterBody_RejectsTruncatedElement()
    {
        // 本体が 24 バイト未満 = AC パラメータが揃っていない。null を返し、例外は投げない。
        var body = ParamElement()[2..^4];

        WmmParser.ParseParameterBody(body).Should().BeNull();
    }

    [Fact]
    public void ParameterBody_RejectsForeignOui()
    {
        var body = ParamElement()[2..];
        body[0] = 0x00; body[1] = 0x0C; body[2] = 0xE7;   // 別ベンダーの OUI

        WmmParser.ParseParameterBody(body).Should().BeNull();
        WmmParser.ParseQosInfoBody(body).Should().BeNull();
    }
}
