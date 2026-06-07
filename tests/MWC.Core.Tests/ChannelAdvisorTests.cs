using System;
using System.Linq;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  ChannelAdvisorService — バンド/チャネル選択助言
//  (arXiv: 6GHz干渉 2307.00235, tri-band選択, OBSS負荷 2511.10143)
// ══════════════════════════════════════════════════════════════
public class ChannelAdvisorServiceTests
{
    private readonly ChannelAdvisorService _svc = new();

    private static WifiNetwork Net(WifiBand band, int signal, int channel = 36, int width = 80) =>
        new()
        {
            Ssid          = "MultiTband",
            Band          = band,
            SignalQuality = signal,
            Channel       = channel,
            ChannelWidth  = width
        };

    [Fact]
    public void RecommendBand_StrongSignalAllBands_Prefers6GHz()
    {
        var networks = new[]
        {
            Net(WifiBand.Band2_4GHz, 90, channel: 6),
            Net(WifiBand.Band5GHz,   90),
            Net(WifiBand.Band6GHz,   90),
        };

        var best = _svc.RecommendBand(networks);

        best.Should().NotBeNull();
        best!.Band.Should().Be(WifiBand.Band6GHz, "強信号なら最も空いている6GHzが最適");
    }

    [Fact]
    public void RecommendBand_Weak6GHzStrong5GHz_Prefers5GHz()
    {
        var networks = new[]
        {
            Net(WifiBand.Band6GHz, 20),   // 6GHz だが非常に弱い (壁越し)
            Net(WifiBand.Band5GHz, 85),   // 5GHz 強い
        };

        var best = _svc.RecommendBand(networks);

        best!.Band.Should().Be(WifiBand.Band5GHz,
            "6GHz は減衰が大きいため、弱信号では5GHzが実用的 (arXiv 2307.00235)");
    }

    [Fact]
    public void RecommendBand_EmptyList_ReturnsNull()
    {
        _svc.RecommendBand(Array.Empty<WifiNetwork>()).Should().BeNull();
    }

    [Theory]
    [InlineData(1,  true)]    // 非重複
    [InlineData(6,  true)]    // 非重複
    [InlineData(11, true)]    // 非重複
    [InlineData(3,  false)]   // 重複
    [InlineData(9,  false)]   // 重複
    public void IsNonOverlappingChannel_24GHz_DetectsCorrectly(int channel, bool expected)
    {
        var net = Net(WifiBand.Band2_4GHz, 70, channel: channel, width: 20);
        _svc.IsNonOverlappingChannel(net).Should().Be(expected);
    }

    [Fact]
    public void IsNonOverlappingChannel_5GHz_AlwaysTrue()
    {
        // 5GHz は基本的に非重複扱い
        _svc.IsNonOverlappingChannel(Net(WifiBand.Band5GHz, 70, channel: 36)).Should().BeTrue();
        _svc.IsNonOverlappingChannel(Net(WifiBand.Band5GHz, 70, channel: 149)).Should().BeTrue();
    }

    [Fact]
    public void AdviseChannelWidth_DenseEnvironment_Recommends20MHz()
    {
        var net = Net(WifiBand.Band5GHz, 80, width: 80);
        var advice = _svc.AdviseChannelWidth(net, nearbyApCount: 15);

        advice.Recommended.Should().Be(20, "高密度では20MHzが非重複チャネルを最大化");
        advice.IsOptimal.Should().BeFalse();
        advice.Reason.Should().Contain("高密度");
    }

    [Fact]
    public void AdviseChannelWidth_SparseEnvironment_Recommends80MHz()
    {
        var net = Net(WifiBand.Band5GHz, 80, width: 20);
        var advice = _svc.AdviseChannelWidth(net, nearbyApCount: 2);

        advice.Recommended.Should().Be(80, "低密度では広い幅で個別スループット最大化");
        advice.IsOptimal.Should().BeFalse();
    }

    [Fact]
    public void AdviseChannelWidth_AppropriateWidth_IsOptimal()
    {
        var net = Net(WifiBand.Band5GHz, 80, width: 20);
        var advice = _svc.AdviseChannelWidth(net, nearbyApCount: 15);  // 高密度で20MHz = 最適

        advice.IsOptimal.Should().BeTrue();
    }

    [Theory]
    [InlineData(1,  10)]    // 1台 → 10%
    [InlineData(5,  50)]    // 5台 → 50%
    [InlineData(10, 100)]   // 10台 → 100%
    [InlineData(15, 100)]   // 上限100%
    public void EstimateCongestion_ScalesWithApCount(int apCount, int expectedCongestion)
    {
        var networks = Enumerable.Range(0, apCount)
            .Select(i => Net(WifiBand.Band5GHz, 70, channel: 36))
            .ToList();

        _svc.EstimateCongestion(networks, channel: 36).Should().Be(expectedCongestion);
    }

    [Fact]
    public void EstimateCongestion_DifferentChannels_NotCounted()
    {
        var networks = new[]
        {
            Net(WifiBand.Band5GHz, 70, channel: 36),
            Net(WifiBand.Band5GHz, 70, channel: 40),
            Net(WifiBand.Band5GHz, 70, channel: 44),
        };

        // チャネル36 には1台のみ
        _svc.EstimateCongestion(networks, channel: 36).Should().Be(10);
    }

    [Fact]
    public void DescribeBandChoice_Weak6GHz_WarnsAboutWalls()
    {
        var desc = _svc.DescribeBandChoice(Net(WifiBand.Band6GHz, 25));
        desc.Should().Contain("6GHz");
        desc.Should().Contain("弱い");
    }

    [Fact]
    public void DescribeBandChoice_OverlappingChannel_WarnsInterference()
    {
        var desc = _svc.DescribeBandChoice(Net(WifiBand.Band2_4GHz, 70, channel: 3, width: 20));
        desc.Should().Contain("干渉");
    }

    [Fact]
    public void ScoreBandChoice_6GHzStrong_HigherThan24GHz()
    {
        var score6 = _svc.ScoreBandChoice(Net(WifiBand.Band6GHz, 85));
        var score24 = _svc.ScoreBandChoice(Net(WifiBand.Band2_4GHz, 85, channel: 6));

        score6.Should().BeGreaterThan(score24, "強信号では6GHzが2.4GHzより高スコア");
    }

    // ── AdviseCongestion (BSS Load ベース) ───────────────────────────────

    [Fact]
    public void AdviseCongestion_BssLoadPresent_UsesBssLoad()
    {
        var network = new WifiNetwork
        {
            Ssid = "Test",
            Band = WifiBand.Band5GHz,
            SignalQuality = 80,
            Channel = 36,
            BssEntries = new[]
            {
                new BssInfo
                {
                    Bssid = "aa:bb:cc:dd:ee:ff",
                    Rssi = -60, Channel = 36, FrequencyMhz = 5180,
                    BssLoad = new BssLoad(StationCount: 15, ChannelUtilization: 200,
                                         AvailableAdmissionCapacity: 0)
                }
            }
        };

        var advice = _svc.AdviseCongestion(network, Array.Empty<WifiNetwork>());

        advice.Source.Should().Be(CongestionSource.BssLoad);
        advice.StationCount.Should().Be(15);
        advice.IsOverloaded.Should().BeTrue();
        advice.UtilizationPercent.Should().BeGreaterThan(75);
    }

    [Fact]
    public void AdviseCongestion_NoBssLoad_FallsBackToApCount()
    {
        var network = Net(WifiBand.Band5GHz, 70, channel: 36);
        // 3 APs on same channel → 30% estimated
        var visible = new[]
        {
            Net(WifiBand.Band5GHz, 70, channel: 36),
            Net(WifiBand.Band5GHz, 70, channel: 36),
            Net(WifiBand.Band5GHz, 70, channel: 36),
        };

        var advice = _svc.AdviseCongestion(network, visible);

        advice.Source.Should().Be(CongestionSource.ApCount);
        advice.UtilizationPercent.Should().Be(30);
        advice.StationCount.Should().BeNull();
    }
}
