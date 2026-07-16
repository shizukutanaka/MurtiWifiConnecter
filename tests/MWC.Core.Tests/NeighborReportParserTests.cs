using System.Collections.Generic;
using FluentAssertions;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  NeighborReportParser — 802.11k Neighbor Report (Element ID 52)
//  バイトレベルのゴールデンテスト
// ══════════════════════════════════════════════════════════════
public class NeighborReportParserTests
{
    // 1 つの Neighbor Report 要素を組み立てる
    private static byte[] Element(byte[] bssid6, uint info, byte opClass, byte ch, byte phy,
                                  byte[]? subelements = null)
    {
        var body = new List<byte>();
        body.AddRange(bssid6);
        body.Add((byte)(info & 0xFF));
        body.Add((byte)((info >> 8) & 0xFF));
        body.Add((byte)((info >> 16) & 0xFF));
        body.Add((byte)((info >> 24) & 0xFF));
        body.Add(opClass); body.Add(ch); body.Add(phy);
        if (subelements is not null) body.AddRange(subelements);

        var el = new List<byte> { 52, (byte)body.Count };
        el.AddRange(body);
        return el.ToArray();
    }

    private static readonly byte[] Bssid1 = { 0x00, 0x11, 0x22, 0xAA, 0xBB, 0xCC };

    [Fact]
    public void ParsesSingleNeighbor_AllFields()
    {
        var bytes = Element(Bssid1, info: 0u, opClass: 81, ch: 6, phy: 7);
        var n = NeighborReportParser.Parse(bytes);

        n.Should().ContainSingle();
        n[0].Bssid.Should().Be("00:11:22:aa:bb:cc");
        n[0].OperatingClass.Should().Be(81);
        n[0].Channel.Should().Be(6);
        n[0].PhyType.Should().Be(7);
    }

    [Fact]
    public void DecodesBssidInfoBits()
    {
        // bit10 = Mobility Domain, bit11 = HT
        uint info = (1u << 10) | (1u << 11);
        var n = NeighborReportParser.Parse(Element(Bssid1, info, 115, 36, 9));
        n[0].SameMobilityDomain.Should().BeTrue();
        n[0].HighThroughput.Should().BeTrue();

        var n2 = NeighborReportParser.Parse(Element(Bssid1, 0u, 115, 36, 9));
        n2[0].SameMobilityDomain.Should().BeFalse();
    }

    [Fact]
    public void ParsesMultipleNeighbors()
    {
        var b2 = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x01 };
        var stream = new List<byte>();
        stream.AddRange(Element(Bssid1, 0u, 81, 1, 7));
        stream.AddRange(Element(b2, 0u, 121, 149, 9));
        var n = NeighborReportParser.Parse(stream.ToArray());

        n.Should().HaveCount(2);
        n[1].Bssid.Should().Be("de:ad:be:ef:00:01");
        n[1].Channel.Should().Be(149);
    }

    [Fact]
    public void IgnoresSubelements_ButKeepsFixedFields()
    {
        var sub = new byte[] { 0x06, 0x02, 0xAB, 0xCD }; // 任意のサブ要素
        var n = NeighborReportParser.Parse(Element(Bssid1, 0u, 81, 11, 7, sub));
        n.Should().ContainSingle();
        n[0].Channel.Should().Be(11);
    }

    [Fact]
    public void SkipsNon52Elements()
    {
        var stream = new List<byte>();
        stream.AddRange(new byte[] { 0x00, 0x03, 0x41, 0x42, 0x43 }); // SSID 要素 (ID 0)
        stream.AddRange(Element(Bssid1, 0u, 81, 6, 7));
        var n = NeighborReportParser.Parse(stream.ToArray());
        n.Should().ContainSingle();
        n[0].Bssid.Should().Be("00:11:22:aa:bb:cc");
    }

    [Fact]
    public void TruncatedOrEmpty_ReturnsParsedPrefixWithoutThrowing()
    {
        NeighborReportParser.Parse(System.Array.Empty<byte>()).Should().BeEmpty();
        // 長さが宣言より短い → 例外なしで空
        NeighborReportParser.Parse(new byte[] { 52, 20, 0x00, 0x11 }).Should().BeEmpty();
    }
}
