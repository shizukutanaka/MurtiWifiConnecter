using FluentAssertions;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  MobilityDomainParser — 802.11r Mobility Domain (Element ID 54)
//  バイトレベルのゴールデンテスト
// ══════════════════════════════════════════════════════════════
public class MobilityDomainParserTests
{
    // Mobility Domain 要素を組み立てる
    private static byte[] Element(ushort mdid, bool overDs, bool rrp)
    {
        byte cap = (byte)((overDs ? 0x01 : 0) | (rrp ? 0x02 : 0));
        return new byte[]
        {
            54, 3,
            (byte)(mdid & 0xFF), (byte)(mdid >> 8),
            cap
        };
    }

    [Fact]
    public void ParsesMdid_OverDs()
    {
        var r = MobilityDomainParser.Parse(Element(0x1A2B, overDs: true, rrp: false));

        r.Should().NotBeNull();
        r!.Mdid.Should().Be(0x1A2B);
        r.MdidHex.Should().Be("1A2B");
        r.OverDsCapable.Should().BeTrue();
        r.ResourceRequestCapable.Should().BeFalse();
    }

    [Fact]
    public void ParsesBothCapabilityBits()
    {
        var r = MobilityDomainParser.Parse(Element(0x0001, overDs: true, rrp: true));
        r!.OverDsCapable.Should().BeTrue();
        r.ResourceRequestCapable.Should().BeTrue();
    }

    [Fact]
    public void NoCapabilities_BothFalse()
    {
        var r = MobilityDomainParser.Parse(Element(0xFFFF, overDs: false, rrp: false));
        r!.OverDsCapable.Should().BeFalse();
        r.ResourceRequestCapable.Should().BeFalse();
    }

    [Fact]
    public void SkipsNon54Elements_FindsMobilityDomain()
    {
        var stream = new System.Collections.Generic.List<byte>();
        stream.AddRange(new byte[] { 52, 13, 0x00, 0x11, 0x22, 0xAA, 0xBB, 0xCC, 0,0,0,0, 81,6,7 }); // Neighbor Report
        stream.AddRange(Element(0xABCD, overDs: false, rrp: false));
        var r = MobilityDomainParser.Parse(stream.ToArray());

        r.Should().NotBeNull();
        r!.Mdid.Should().Be(0xABCD);
    }

    [Fact]
    public void TruncatedElement_ReturnsNull()
    {
        MobilityDomainParser.Parse(new byte[] { 54, 3, 0x01 }).Should().BeNull();
    }

    [Fact]
    public void EmptySpan_ReturnsNull()
    {
        MobilityDomainParser.Parse(System.Array.Empty<byte>()).Should().BeNull();
    }
}
