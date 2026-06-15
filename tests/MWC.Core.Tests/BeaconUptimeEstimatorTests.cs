using System;
using FluentAssertions;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  BeaconUptimeEstimator — TSF タイムスタンプ → AP 稼働時間
// ══════════════════════════════════════════════════════════════
public class BeaconUptimeEstimatorTests
{
    [Fact]
    public void FromTsf_ConvertsMicrosecondsToTimeSpan()
    {
        // 1 時間 = 3,600,000,000 µs
        var up = BeaconUptimeEstimator.FromTsf(3_600_000_000UL);
        up.Should().NotBeNull();
        up!.Value.TotalHours.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void FromTsf_Zero_ReturnsNull()
    {
        BeaconUptimeEstimator.FromTsf(0).Should().BeNull();
    }

    [Fact]
    public void FromTsf_ImplausiblyLarge_ReturnsNull()
    {
        // 100 年分 µs → 上限 (10年) 超でクランプ → null
        BeaconUptimeEstimator.FromTsf(ulong.MaxValue).Should().BeNull();
    }

    [Fact]
    public void FromBeaconTimestamp_LittleEndian()
    {
        // 1,000,000 µs = 1 秒 → LE bytes
        ulong tsf = 1_000_000;
        var bytes = new byte[8];
        for (int i = 0; i < 8; i++) bytes[i] = (byte)(tsf >> (8 * i));

        var up = BeaconUptimeEstimator.FromBeaconTimestamp(bytes);
        up!.Value.TotalSeconds.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void FromBeaconTimestamp_TooShort_ReturnsNull()
    {
        BeaconUptimeEstimator.FromBeaconTimestamp(new byte[] { 1, 2, 3 }).Should().BeNull();
    }

    [Fact]
    public void IsRecentlyRebooted_Under5Minutes()
    {
        BeaconUptimeEstimator.IsRecentlyRebooted(TimeSpan.FromMinutes(2)).Should().BeTrue();
        BeaconUptimeEstimator.IsRecentlyRebooted(TimeSpan.FromHours(2)).Should().BeFalse();
        BeaconUptimeEstimator.IsRecentlyRebooted(null).Should().BeFalse();
    }

    [Theory]
    [InlineData(2.0, 0, 0, "2日")]
    [InlineData(0, 3, 0, "3時間")]
    [InlineData(0, 0, 15, "15分")]
    public void ToLabel_HumanReadable(double days, int hours, int minutes, string expectedFragment)
    {
        var t = TimeSpan.FromDays(days) + TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);
        BeaconUptimeEstimator.ToLabel(t).Should().Contain(expectedFragment);
    }

    [Fact]
    public void ToLabel_Null_Unknown()
    {
        BeaconUptimeEstimator.ToLabel(null).Should().Be("Unknown");
    }
}
