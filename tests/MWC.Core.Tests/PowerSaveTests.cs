using System;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  PowerSaveAdvisorService — TWT/rTWT 省電力分析
//  (arXiv 2402.15900, TASPER 2509.26245)
// ══════════════════════════════════════════════════════════════
public class PowerSaveAdvisorServiceTests
{
    private readonly PowerSaveAdvisorService _svc = new();

    private static WifiNetwork Net(bool twt = false, bool rtwt = false) =>
        new()
        {
            Ssid = "PowerNet", Band = WifiBand.Band6GHz, SignalQuality = 80,
            TargetWakeTime = twt, RestrictedTwt = rtwt
        };

    [Fact]
    public void Analyze_Rtwt_AdvancedTierMaxSaving()
    {
        var profile = _svc.Analyze(Net(twt: true, rtwt: true));

        profile.Tier.Should().Be(PowerSaveTier.Advanced);
        profile.SupportsRtwt.Should().BeTrue();
        profile.EstimatedSavingPercent.Should().Be(34, "TASPER の上限値");
        profile.Summary.Should().Contain("rTWT");
    }

    [Fact]
    public void Analyze_Twt_StandardTier()
    {
        var profile = _svc.Analyze(Net(twt: true));

        profile.Tier.Should().Be(PowerSaveTier.Standard);
        profile.SupportsTwt.Should().BeTrue();
        profile.SupportsRtwt.Should().BeFalse();
        profile.EstimatedSavingPercent.Should().Be(20);
    }

    [Fact]
    public void Analyze_NoTwt_LegacyTier()
    {
        var profile = _svc.Analyze(Net());

        profile.Tier.Should().Be(PowerSaveTier.Legacy);
        profile.SupportsTwt.Should().BeFalse();
        profile.EstimatedSavingPercent.Should().Be(0);
    }

    [Fact]
    public void RecommendedScanInterval_OnAc_ShortInterval()
    {
        _svc.RecommendedScanIntervalSeconds(Net(twt: true), onBattery: false)
            .Should().Be(15);
    }

    [Theory]
    [InlineData(true,  true,  30)]   // rTWT (Advanced)
    [InlineData(true,  false, 60)]   // TWT (Standard)
    [InlineData(false, false, 120)]  // Legacy
    public void RecommendedScanInterval_OnBattery_ScalesByTier(
        bool twt, bool rtwt, int expected)
    {
        _svc.RecommendedScanIntervalSeconds(Net(twt, rtwt), onBattery: true)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(10, true,  PowerMode.MaxSaving)]
    [InlineData(30, true,  PowerMode.Balanced)]
    [InlineData(80, true,  PowerMode.Performance)]
    [InlineData(10, false, PowerMode.Performance)]   // AC電源なら常にPerformance
    public void RecommendPowerMode_BatteryAndPower(int battery, bool onBattery, PowerMode expected)
    {
        _svc.RecommendPowerMode(battery, onBattery).Should().Be(expected);
    }

    [Fact]
    public void IsIotFriendly_TwtCapable_True()
    {
        _svc.IsIotFriendly(Net(twt: true)).Should().BeTrue();
        _svc.IsIotFriendly(Net(rtwt: true)).Should().BeTrue();
    }

    [Fact]
    public void IsIotFriendly_NoTwt_False()
    {
        _svc.IsIotFriendly(Net()).Should().BeFalse();
    }
}
