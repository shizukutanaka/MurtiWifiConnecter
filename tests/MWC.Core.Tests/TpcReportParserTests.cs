using FluentAssertions;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  TpcReportParser — TPC Report 要素 (Element ID 35, 802.11h)
// ══════════════════════════════════════════════════════════════
public class TpcReportParserTests
{
    private static byte[] Element(sbyte power, sbyte margin)
        => new byte[] { 35, 2, unchecked((byte)power), unchecked((byte)margin) };

    [Fact]
    public void ParsesTransmitPower_AndLinkMargin()
    {
        var r = TpcReportParser.Parse(Element(power: 20, margin: 5));
        r.Should().NotBeNull();
        r!.TransmitPowerDbm.Should().Be(20);
        r.LinkMarginDb.Should().Be(5);
    }

    [Fact]
    public void NegativeValues_DecodedSigned()
    {
        var r = TpcReportParser.Parse(Element(power: -10, margin: -2));
        r!.TransmitPowerDbm.Should().Be(-10);
        r.LinkMarginDb.Should().Be(-2);
    }

    [Fact]
    public void SkipsOtherElements()
    {
        var stream = new System.Collections.Generic.List<byte> { 11, 5, 0, 0, 128, 0, 0 }; // BSS Load
        stream.AddRange(Element(15, 0));
        TpcReportParser.Parse(stream.ToArray())!.TransmitPowerDbm.Should().Be(15);
    }

    [Fact]
    public void TruncatedOrEmpty_ReturnsNull()
    {
        TpcReportParser.Parse(System.Array.Empty<byte>()).Should().BeNull();
        TpcReportParser.Parse(new byte[] { 35, 2, 0x14 }).Should().BeNull();
    }
}
