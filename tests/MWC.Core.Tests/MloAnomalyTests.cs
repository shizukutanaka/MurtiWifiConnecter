using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  MloAnalyzerService.DetectAnomaly — MLO が単一リンクに劣る条件
//  (arXiv 2210.07695: MLO Performance, Anomalies, and Solutions)
// ══════════════════════════════════════════════════════════════
public class MloAnomalyTests
{
    private readonly MloAnalyzerService _svc = new();

    private static WifiNetwork Mlo(params MloLink[] links) =>
        new() { Ssid = "MLO-AP", IsMlo = true, MloLinks = links };

    [Fact]
    public void NonMlo_NoAnomaly()
    {
        var n = new WifiNetwork { Ssid = "x", IsMlo = false };
        _svc.DetectAnomaly(n).HasAnomaly.Should().BeFalse();
    }

    [Fact]
    public void HealthyCrossBand_NoAnomaly()
    {
        var n = Mlo(
            new MloLink { Band = WifiBand.Band5GHz, ChannelWidth = 160, Rssi = -50 },
            new MloLink { Band = WifiBand.Band6GHz, ChannelWidth = 320, Rssi = -55 });
        _svc.DetectAnomaly(n).Kind.Should().Be(MloAnomalyKind.None);
    }

    [Fact]
    public void AsymmetricLinks_FlaggedWithAdvice()
    {
        var n = Mlo(
            new MloLink { Band = WifiBand.Band5GHz, ChannelWidth = 160, Rssi = -45 },
            new MloLink { Band = WifiBand.Band6GHz, ChannelWidth = 320, Rssi = -82 }); // 37dB 差だが best は強い
        var a = _svc.DetectAnomaly(n);
        a.Kind.Should().Be(MloAnomalyKind.AsymmetricLinks);
        a.Advice.Should().Contain("2210.07695");
    }

    [Fact]
    public void AllLinksWeak_Flagged()
    {
        var n = Mlo(
            new MloLink { Band = WifiBand.Band5GHz, ChannelWidth = 80, Rssi = -82 },
            new MloLink { Band = WifiBand.Band6GHz, ChannelWidth = 80, Rssi = -85 });
        _svc.DetectAnomaly(n).Kind.Should().Be(MloAnomalyKind.AllLinksWeak);
    }

    [Fact]
    public void SameBandOnly_FlaggedAsLimitedRedundancy()
    {
        var n = Mlo(
            new MloLink { Band = WifiBand.Band5GHz, ChannelWidth = 160, Rssi = -50 },
            new MloLink { Band = WifiBand.Band5GHz, ChannelWidth = 80,  Rssi = -55 });
        _svc.DetectAnomaly(n).Kind.Should().Be(MloAnomalyKind.SameBandRedundancy);
    }
}
