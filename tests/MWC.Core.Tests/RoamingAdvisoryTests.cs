using System;
using System.Linq;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  RoamingAdvisoryService — 802.11r/k/v 高速ローミング診断
//  (arXiv: Machań & Wozniak, Telecommunication Systems)
// ══════════════════════════════════════════════════════════════
public class RoamingAdvisoryServiceTests
{
    private readonly RoamingAdvisoryService _svc = new();

    private static WifiNetwork Net(
        bool ft = false, bool k = false, bool v = false,
        AuthMethod auth = AuthMethod.WPA2PSK, int signal = 80) =>
        new()
        {
            Ssid              = "RoamNet",
            Auth              = auth,
            FastTransition    = ft,
            NeighborReport    = k,
            BssTransitionMgmt = v,
            Band              = WifiBand.Band5GHz,
            SignalQuality     = signal
        };

    [Fact]
    public void Analyze_AllThreeStandards_IsSeamless()
    {
        var profile = _svc.Analyze(Net(ft: true, k: true, v: true));

        profile.Tier.Should().Be(RoamingTier.Seamless);
        profile.SupportedStandards.Should().BeEquivalentTo(new[] { "802.11r", "802.11k", "802.11v" });
        profile.EstimatedHandoverMs.Should().Be(RoamingAdvisoryService.OptimalFtMs);
        profile.EstimatedHandoverMs.Should().Be(13, "論文の最良ケースは 13ms");
        profile.VoipReady.Should().BeTrue();
    }

    [Fact]
    public void Analyze_OnlyFastTransition_IsFast()
    {
        var profile = _svc.Analyze(Net(ft: true));

        profile.Tier.Should().Be(RoamingTier.Fast);
        profile.EstimatedHandoverMs.Should().Be(50);
        profile.VoipReady.Should().BeTrue("50ms 以下は VoIP 可能");
    }

    [Fact]
    public void Analyze_OnlyKAndV_IsAssisted()
    {
        var profile = _svc.Analyze(Net(k: true, v: true));

        profile.Tier.Should().Be(RoamingTier.Assisted);
        profile.SupportedStandards.Should().Contain("802.11k");
        profile.SupportedStandards.Should().Contain("802.11v");
        profile.SupportedStandards.Should().NotContain("802.11r");
    }

    [Fact]
    public void Analyze_NoRoamingSupport_IsStandard()
    {
        var profile = _svc.Analyze(Net());

        profile.Tier.Should().Be(RoamingTier.Standard);
        profile.SupportedStandards.Should().BeEmpty();
        profile.EstimatedHandoverMs.Should().Be(RoamingAdvisoryService.LegacyHandoverMs);
        profile.VoipReady.Should().BeFalse("250ms の遷移は通話が途切れる");
    }

    [Fact]
    public void Analyze_EnterpriseWithFt_IsEnterpriseOptimized()
    {
        var profile = _svc.Analyze(Net(ft: true, auth: AuthMethod.WPA2Enterprise));

        profile.IsEnterpriseOptimized.Should().BeTrue(
            "802.11r は企業認証 (802.1X) を高速化する設計");
    }

    [Fact]
    public void Analyze_PskWithFt_NotEnterpriseOptimized()
    {
        var profile = _svc.Analyze(Net(ft: true, auth: AuthMethod.WPA2PSK));
        profile.IsEnterpriseOptimized.Should().BeFalse();
    }

    [Theory]
    [InlineData(true,  true,  true,  true)]   // Seamless → VoIP OK
    [InlineData(true,  false, false, true)]   // Fast → VoIP OK
    [InlineData(false, true,  true,  false)]  // Assisted → VoIP NG (120ms)
    [InlineData(false, false, false, false)]  // Standard → VoIP NG
    public void IsRealtimeCapable_MatchesExpectation(bool ft, bool k, bool v, bool expected)
    {
        _svc.IsRealtimeCapable(Net(ft, k, v)).Should().Be(expected);
    }

    [Fact]
    public void RecommendForMobility_PrefersSeamless()
    {
        var networks = new[]
        {
            Net(ft: false, k: false, v: false, signal: 95),         // 標準だが強い
            Net(ft: true,  k: true,  v: true,  signal: 70),         // シームレスだが弱い
            Net(ft: true,  k: false, v: false, signal: 85),         // Fast
        };

        var best = _svc.RecommendForMobility(networks, "RoamNet");

        best.Should().NotBeNull();
        // モビリティ重視ではローミング階層を信号より優先
        _svc.Analyze(best!).Tier.Should().Be(RoamingTier.Seamless);
    }

    [Fact]
    public void DescribeRoaming_Seamless_MentionsVoip()
    {
        var desc = _svc.DescribeRoaming(Net(ft: true, k: true, v: true));
        desc.Should().Contain("シームレス");
        desc.Should().Contain("VoIP");
        desc.Should().Contain("13ms");
    }

    [Fact]
    public void DescribeRoaming_Standard_MentionsInterruption()
    {
        var desc = _svc.DescribeRoaming(Net());
        desc.Should().Contain("標準");
        desc.Should().Contain("中断");
    }

    [Fact]
    public void HandoverDelay_Constants_AreOrdered()
    {
        // 遅延の大小関係: 最適FT < FT < レガシー
        RoamingAdvisoryService.OptimalFtMs.Should().BeLessThan(RoamingAdvisoryService.FastTransitionMs);
        RoamingAdvisoryService.FastTransitionMs.Should().BeLessThan(RoamingAdvisoryService.LegacyHandoverMs);
    }
}

// ══════════════════════════════════════════════════════════════
//  WifiNetwork ローミングフラグ
// ══════════════════════════════════════════════════════════════
public class WifiNetworkRoamingFlagsTests
{
    [Fact]
    public void RoamingFlags_DefaultFalse()
    {
        var net = new WifiNetwork { Ssid = "X", Band = WifiBand.Band5GHz };
        net.FastTransition.Should().BeFalse();
        net.NeighborReport.Should().BeFalse();
        net.BssTransitionMgmt.Should().BeFalse();
    }

    [Fact]
    public void RoamingFlags_CanBeSet()
    {
        var net = new WifiNetwork
        {
            Ssid = "Enterprise",
            Band = WifiBand.Band5GHz,
            FastTransition = true,
            NeighborReport = true,
            BssTransitionMgmt = true
        };
        net.FastTransition.Should().BeTrue();
        net.NeighborReport.Should().BeTrue();
        net.BssTransitionMgmt.Should().BeTrue();
    }
}
