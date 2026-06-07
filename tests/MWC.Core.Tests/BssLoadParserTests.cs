using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  BssLoadParser — BSS Load 要素 (Element ID 11) のバイトレベルテスト
// ══════════════════════════════════════════════════════════════
public class BssLoadParserTests
{
    // BSS Load 要素を組み立てる: ID=11, len=5, then fixed body
    private static byte[] Element(ushort stationCount, byte utilization, ushort admissionCapacity)
        => new byte[]
        {
            11, 5,
            (byte)(stationCount & 0xFF), (byte)(stationCount >> 8),
            utilization,
            (byte)(admissionCapacity & 0xFF), (byte)(admissionCapacity >> 8)
        };

    [Fact]
    public void ParsesAllFields()
    {
        var bytes = Element(stationCount: 42, utilization: 128, admissionCapacity: 1000);
        var r = BssLoadParser.Parse(bytes);

        r.Should().NotBeNull();
        r!.StationCount.Should().Be(42);
        r.ChannelUtilization.Should().Be(128);
        r.AvailableAdmissionCapacity.Should().Be(1000);
    }

    [Fact]
    public void UtilizationPercent_RoundsCorrectly()
    {
        // utilization=255 → 100%, utilization=0 → 0%, ~128 → ~50%
        BssLoadParser.Parse(Element(0, 255, 0))!.UtilizationPercent.Should().Be(100);
        BssLoadParser.Parse(Element(0, 0, 0))!.UtilizationPercent.Should().Be(0);

        var mid = BssLoadParser.Parse(Element(0, 128, 0))!;
        mid.UtilizationPercent.Should().BeInRange(49, 51);
    }

    [Fact]
    public void IsOverloaded_ThresholdAt75Percent()
    {
        BssLoadParser.Parse(Element(0, 192, 0))!.IsOverloaded.Should().BeTrue();
        BssLoadParser.Parse(Element(0, 191, 0))!.IsOverloaded.Should().BeFalse();
        BssLoadParser.Parse(Element(0, 255, 0))!.IsOverloaded.Should().BeTrue();
    }

    [Fact]
    public void SkipsNon11Elements_ThenFindsElement11()
    {
        var stream = new System.Collections.Generic.List<byte>();
        stream.AddRange(new byte[] { 0, 3, 0x41, 0x42, 0x43 }); // SSID (ID 0)
        stream.AddRange(Element(10, 50, 500));
        var r = BssLoadParser.Parse(stream.ToArray());

        r.Should().NotBeNull();
        r!.StationCount.Should().Be(10);
    }

    [Fact]
    public void TruncatedElement_ReturnsNull()
    {
        // Length declares 5 bytes but only 3 bytes of body present
        BssLoadParser.Parse(new byte[] { 11, 5, 0, 0, 128 }).Should().BeNull();
    }

    [Fact]
    public void EmptySpan_ReturnsNull()
    {
        BssLoadParser.Parse(System.Array.Empty<byte>()).Should().BeNull();
    }

    [Fact]
    public void ReturnsFirstElement_IgnoresSubsequent()
    {
        var stream = new System.Collections.Generic.List<byte>();
        stream.AddRange(Element(5, 60, 200));
        stream.AddRange(Element(99, 200, 0));
        var r = BssLoadParser.Parse(stream.ToArray());

        r!.StationCount.Should().Be(5);
    }
}
