using FluentAssertions;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  SixGhzChannelHelper — 6GHz PSC / チャネル変換
// ══════════════════════════════════════════════════════════════
public class SixGhzChannelHelperTests
{
    [Fact]
    public void AllChannels_Has59Channels()
    {
        // IEEE 802.11ax: 6GHz = ch 1–233 (20MHz 間隔) = 59 チャネル
        SixGhzChannelHelper.AllChannels.Should().HaveCount(59);
        SixGhzChannelHelper.AllChannels[0].Should().Be(1);
        SixGhzChannelHelper.AllChannels[^1].Should().Be(233);
    }

    [Fact]
    public void PreferredScanningChannels_StartsAt5_Step32()
    {
        var psc = SixGhzChannelHelper.PreferredScanningChannels;
        psc[0].Should().Be(5);
        psc[1].Should().Be(37);
        psc[2].Should().Be(69);
        // All ≤ 233
        psc.Should().AllSatisfy(ch => ch.Should().BeLessOrEqualTo(233));
    }

    [Theory]
    [InlineData(5,   true)]
    [InlineData(37,  true)]
    [InlineData(69,  true)]
    [InlineData(229, true)]   // 5 + 7*32 = 229
    [InlineData(1,   false)]
    [InlineData(9,   false)]
    [InlineData(100, false)]
    public void IsPreferredScanningChannel_Correct(int channel, bool expected)
    {
        SixGhzChannelHelper.IsPreferredScanningChannel(channel).Should().Be(expected);
    }

    [Theory]
    [InlineData(1,   5955)]   // 5950 + 1×5
    [InlineData(5,   5975)]   // 5950 + 5×5
    [InlineData(37,  6135)]   // PSC ch 37
    [InlineData(233, 7115)]   // last ch
    public void ChannelToFreqMhz_KnownValues(int channel, int expectedMhz)
    {
        SixGhzChannelHelper.ChannelToFreqMhz(channel).Should().Be(expectedMhz);
    }

    [Theory]
    [InlineData(5955,  1)]
    [InlineData(5975,  5)]
    [InlineData(6135, 37)]
    [InlineData(7115, 233)]
    [InlineData(2400, -1)]   // 2.4GHz → invalid
    [InlineData(5180, -1)]   // 5GHz → invalid
    public void FreqMhzToChannel_RoundTrip(int freqMhz, int expectedChannel)
    {
        SixGhzChannelHelper.FreqMhzToChannel(freqMhz).Should().Be(expectedChannel);
    }

    [Fact]
    public void PscIsSubsetOfAllChannels()
    {
        var all = new System.Collections.Generic.HashSet<int>(SixGhzChannelHelper.AllChannels);
        foreach (var ch in SixGhzChannelHelper.PreferredScanningChannels)
            all.Should().Contain(ch, $"PSC ch {ch} must be in the full 6GHz channel list");
    }
}
