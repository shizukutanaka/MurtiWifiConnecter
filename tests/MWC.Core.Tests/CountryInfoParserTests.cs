using System.Collections.Generic;
using FluentAssertions;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  CountryInfoParser — Country 要素 (Element ID 7, 802.11d)
// ══════════════════════════════════════════════════════════════
public class CountryInfoParserTests
{
    // Country 要素を組み立てる: code(2) + env(1) + triplets(3n)
    private static byte[] Element(string code, char env, params (byte first, byte count, sbyte power)[] triplets)
    {
        var body = new List<byte> { (byte)code[0], (byte)code[1], (byte)env };
        foreach (var (f, c, p) in triplets)
        {
            body.Add(f); body.Add(c); body.Add(unchecked((byte)p));
        }
        // 802.11 要素は偶数長が望ましいが必須ではない
        var el = new List<byte> { 7, (byte)body.Count };
        el.AddRange(body);
        return el.ToArray();
    }

    [Fact]
    public void ParsesCountryCode_AndIndoorEnvironment()
    {
        var r = CountryInfoParser.Parse(Element("US", 'I', (1, 11, 30)));
        r.Should().NotBeNull();
        r!.CountryCode.Should().Be("US");
        r.Environment.Should().Be(RegulatoryEnvironment.Indoor);
    }

    [Fact]
    public void ParsesTriplet_MaxTxPower()
    {
        var r = CountryInfoParser.Parse(Element("JP", ' ', (1, 13, 20), (36, 4, 23)));
        r!.Triplets.Should().HaveCount(2);
        r.Triplets[0].FirstChannel.Should().Be(1);
        r.Triplets[0].ChannelCount.Should().Be(13);
        r.MaxTxPowerDbm.Should().Be(23, "全三つ組での最大値");
    }

    [Fact]
    public void OutdoorEnvironment_Detected()
    {
        CountryInfoParser.Parse(Element("DE", 'O', (1, 13, 20)))!
            .Environment.Should().Be(RegulatoryEnvironment.Outdoor);
    }

    [Fact]
    public void NegativeTxPower_DecodedSigned()
    {
        var r = CountryInfoParser.Parse(Element("US", 'I', (1, 11, -3)));
        r!.Triplets[0].MaxTxPowerDbm.Should().Be(-3);
    }

    [Fact]
    public void SkipsNon7Elements()
    {
        var stream = new List<byte> { 0, 3, 0x41, 0x42, 0x43 }; // SSID
        stream.AddRange(Element("FR", ' ', (1, 13, 20)));
        CountryInfoParser.Parse(stream.ToArray())!.CountryCode.Should().Be("FR");
    }

    [Fact]
    public void TruncatedOrEmpty_ReturnsNull()
    {
        CountryInfoParser.Parse(System.Array.Empty<byte>()).Should().BeNull();
        CountryInfoParser.Parse(new byte[] { 7, 10, 0x55 }).Should().BeNull();
    }
}
