using System;
using System.Linq;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  LinkRateEstimator — RSSI/SNR → MCS → スループット推定
// ══════════════════════════════════════════════════════════════
public class LinkRateEstimatorTests
{
    private readonly LinkRateEstimator _svc = new();

    [Theory]
    [InlineData(-50, -95, 45)]   // 強信号 → 高SNR
    [InlineData(-70, -95, 25)]
    [InlineData(-90, -95, 5)]    // 弱信号 → 低SNR
    public void EstimateSnr_RssiMinusNoiseFloor(int rssi, int noise, int expectedSnr)
    {
        _svc.EstimateSnr(rssi, noise).Should().Be(expectedSnr);
    }

    [Theory]
    [InlineData(45, 13)]   // 高SNR → 4096-QAM (MCS13)
    [InlineData(37, 11)]   // 1024-QAM
    [InlineData(27, 7)]    // 64-QAM
    [InlineData(13, 2)]    // QPSK
    [InlineData(5,  0)]    // BPSK
    [InlineData(2, -1)]    // 接続不能
    public void EstimateMaxMcs_SnrToMcs(int snr, int expectedMcs)
    {
        _svc.EstimateMaxMcs(snr).Should().Be(expectedMcs);
    }

    [Fact]
    public void EstimateMaxMcs_No4096Qam_CapsAt11()
    {
        // 4096-QAM 非対応なら高SNRでもMCS11止まり
        _svc.EstimateMaxMcs(45, supports4096Qam: false).Should().Be(11);
    }

    [Fact]
    public void EstimatePhyRate_HigherMcs_HigherRate()
    {
        var low  = _svc.EstimatePhyRateMbps(0, 80, 2);
        var high = _svc.EstimatePhyRateMbps(11, 80, 2);
        high.Should().BeGreaterThan(low);
    }

    [Fact]
    public void EstimatePhyRate_WiderChannel_HigherRate()
    {
        var narrow = _svc.EstimatePhyRateMbps(9, 20, 1);
        var wide   = _svc.EstimatePhyRateMbps(9, 160, 1);
        wide.Should().BeGreaterThan(narrow * 5, "160MHz は20MHzの約8.67倍");
    }

    [Fact]
    public void EstimatePhyRate_MoreStreams_ScalesLinearly()
    {
        var ss1 = _svc.EstimatePhyRateMbps(9, 80, 1);
        var ss2 = _svc.EstimatePhyRateMbps(9, 80, 2);
        ss2.Should().BeApproximately(ss1 * 2, 0.1);
    }

    [Fact]
    public void EstimatePhyRate_NegativeMcs_ReturnsZero()
    {
        _svc.EstimatePhyRateMbps(-1, 80, 2).Should().Be(0);
    }

    [Fact]
    public void Estimate_StrongSignal_ExcellentQuality()
    {
        var est = _svc.Estimate(rssiDbm: -45, channelWidthMhz: 160, spatialStreams: 2);

        est.Quality.Should().Be(LinkQuality.Excellent);
        est.MaxMcs.Should().BeGreaterOrEqualTo(11);
        est.EffectiveMbps.Should().BeLessThan(est.PhyRateMbps, "実効スループットはPHYレートの約65%");
        est.EffectiveMbps.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Estimate_WeakSignal_PoorOrUnusable()
    {
        var est = _svc.Estimate(rssiDbm: -92, channelWidthMhz: 20, spatialStreams: 1);
        est.Quality.Should().BeOneOf(LinkQuality.Poor, LinkQuality.Unusable, LinkQuality.Fair);
        est.SnrDb.Should().BeLessThan(15);
    }

    [Fact]
    public void Estimate_EffectiveLessThanPhy()
    {
        var est = _svc.Estimate(-55, 80, 2);
        est.EffectiveMbps.Should().BeLessThan(est.PhyRateMbps);
    }
}

// ══════════════════════════════════════════════════════════════
//  MloAnalyzerService — Wi-Fi 7 Multi-Link Operation
// ══════════════════════════════════════════════════════════════
public class MloAnalyzerServiceTests
{
    private readonly MloAnalyzerService _svc = new();

    private static WifiNetwork MloNet(params (WifiBand band, int rssi, int width)[] links) =>
        new()
        {
            Ssid    = "MLO-Net",
            Band    = links[0].band,
            SignalQuality = 80,
            IsMlo   = true,
            MloLinks = links.Select((l, i) => new MloLink
            {
                LinkId = i, Band = l.band, Rssi = l.rssi,
                ChannelWidth = l.width, Channel = 36, FrequencyMhz = 5180
            }).ToList()
        };

    [Fact]
    public void Analyze_NonMlo_ReturnsSingleLink()
    {
        var net = new WifiNetwork { Ssid = "X", Band = WifiBand.Band5GHz, SignalQuality = 70 };
        var analysis = _svc.Analyze(net);

        analysis.IsMlo.Should().BeFalse();
        analysis.ReliabilityTier.Should().Be(MloReliability.SingleLink);
    }

    [Fact]
    public void Analyze_DualBandMlo_DetectsCrossBand()
    {
        var net = MloNet(
            (WifiBand.Band5GHz, -55, 160),
            (WifiBand.Band6GHz, -60, 320));

        var analysis = _svc.Analyze(net);

        analysis.IsMlo.Should().BeTrue();
        analysis.LinkCount.Should().Be(2);
        analysis.IsCrossBand.Should().BeTrue();
        analysis.Bands.Should().Contain(WifiBand.Band5GHz);
        analysis.Bands.Should().Contain(WifiBand.Band6GHz);
        analysis.ReliabilityTier.Should().Be(MloReliability.DualLink);
        analysis.AggregatedMbps.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Analyze_TripleLink_HighestReliability()
    {
        var net = MloNet(
            (WifiBand.Band2_4GHz, -65, 40),
            (WifiBand.Band5GHz,   -55, 160),
            (WifiBand.Band6GHz,   -58, 320));

        var analysis = _svc.Analyze(net);

        analysis.ReliabilityTier.Should().Be(MloReliability.TripleLink);
        analysis.LinkCount.Should().Be(3);
        analysis.Summary.Should().Contain("継続");
    }

    [Fact]
    public void Analyze_AggregatedThroughput_SumsLinks()
    {
        var net = MloNet(
            (WifiBand.Band5GHz, -50, 160),
            (WifiBand.Band6GHz, -50, 320));

        var analysis = _svc.Analyze(net);
        // 2リンクの集約は単一リンクより大きい
        analysis.AggregatedMbps.Should().BeGreaterThan(1000);
    }

    [Fact]
    public void EstimateLatencyReduction_DualLink_About30Percent()
    {
        var net = MloNet(
            (WifiBand.Band5GHz, -55, 160),
            (WifiBand.Band6GHz, -60, 320));

        _svc.EstimateLatencyReductionPercent(net).Should().Be(30.0);
    }

    [Fact]
    public void EstimateLatencyReduction_TripleLink_About45Percent()
    {
        var net = MloNet(
            (WifiBand.Band2_4GHz, -65, 40),
            (WifiBand.Band5GHz,   -55, 160),
            (WifiBand.Band6GHz,   -58, 320));

        _svc.EstimateLatencyReductionPercent(net).Should().Be(45.0);
    }

    [Fact]
    public void EstimateLatencyReduction_NonMlo_Zero()
    {
        var net = new WifiNetwork { Ssid = "X", Band = WifiBand.Band5GHz, SignalQuality = 70 };
        _svc.EstimateLatencyReductionPercent(net).Should().Be(0);
    }

    [Fact]
    public void BestLink_ReturnsStrongestRssi()
    {
        var net = MloNet(
            (WifiBand.Band5GHz, -70, 160),
            (WifiBand.Band6GHz, -50, 320));   // これが最強

        var best = _svc.BestLink(net);

        best.Should().NotBeNull();
        best!.Rssi.Should().Be(-50);
        best.Band.Should().Be(WifiBand.Band6GHz);
    }

    [Fact]
    public void BestLink_NoLinks_ReturnsNull()
    {
        var net = new WifiNetwork { Ssid = "X", Band = WifiBand.Band5GHz, SignalQuality = 70 };
        _svc.BestLink(net).Should().BeNull();
    }
}
