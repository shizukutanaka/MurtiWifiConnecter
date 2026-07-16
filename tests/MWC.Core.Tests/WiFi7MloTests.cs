using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

public class EhtCapabilityMloIntegrationTests
{
    [Fact]
    public void EhtCapability_4096Qam_320MHz_IsHighestThroughput()
    {
        var cap = new EhtCapability
        {
            Supports4096Qam            = true,
            SupportsPreamblePuncturing = true,
            SupportsRtwt               = true,
            SupportsScs                = true,
            MaxMcsIndex                = 13
        };

        var peak = cap.EstimatedPeakGbps(320, spatialStreams: 4);

        // 4SS × 11.529 Gbps × 1.0 (4096-QAM) = 46.116 Gbps (Wi-Fi 7 規格最大)
        peak.Should().BeGreaterThan(40.0, "4SS Wi-Fi 7 @ 320MHz must exceed 40 Gbps");
        cap.Supports4096Qam.Should().BeTrue();
        cap.MaxMcsIndex.Should().Be(13);
        cap.SupportsPreamblePuncturing.Should().BeTrue();
        cap.SupportsRtwt.Should().BeTrue("rTWT はIEEE 802.11be-2025の必須機能");
    }

    [Fact]
    public void EhtCapability_NoQam4096_LowerThroughput()
    {
        var wifi6Cap = new EhtCapability { Supports4096Qam = false, MaxMcsIndex = 11 };
        var wifi7Cap = new EhtCapability { Supports4096Qam = true,  MaxMcsIndex = 13 };

        var peak6 = wifi6Cap.EstimatedPeakGbps(160);
        var peak7 = wifi7Cap.EstimatedPeakGbps(160);

        peak7.Should().BeGreaterThan(peak6);
        (peak7 / peak6).Should().BeApproximately(1.20, 0.05,
            "4096-QAM は1024-QAM より約20%スループット向上 (IEEE 802.11be 規格値)");
    }

    [Fact]
    public void PhyType_WiFi8_HasLabel()
    {
        PhyType.Dot11bn.ToGenerationLabel().Should().Contain("Wi-Fi 8");
        PhyType.Dot11bn.ToShortLabel().Should().Be("Wi-Fi 8");
        // Wi-Fi 7 より後の世代
        ((int)PhyType.Dot11bn).Should().BeGreaterThan((int)PhyType.Dot11be);
    }

    [Fact]
    public void WiFi8Capability_MultiApCoordination()
    {
        var cap = new WiFi8Capability
        {
            SupportsMultiApCoordination    = true,
            SupportsCoordinatedSpatialReuse= true,
            SupportsCoordinatedOfdma       = true,
            SupportsUltraHighThroughput    = false
        };

        cap.SupportsMultiApCoordination.Should().BeTrue();
        cap.SupportsCoordinatedOfdma.Should().BeTrue();
        cap.SupportsUltraHighThroughput.Should().BeFalse("実デバイスでは未対応");
    }
}

public class FrozenDictionaryRegulatoryTests
{
    private readonly RegulatoryDomainService _svc = new();

    [Fact]
    public void GetRegion_HotPath_IsConsistentAcrossMultipleCalls()
    {
        // FrozenDictionary はスレッドセーフで決定論的
        var r1 = _svc.GetRegion("US");
        var r2 = _svc.GetRegion("US");
        var r3 = _svc.GetRegion("US");

        r1.Should().Be(r2);
        r2.Should().Be(r3);
        r1.CountryCode.Should().Be("US");
        r1.Mode.Should().Be(Band6GHzMode.FullBand);
    }

    [Fact]
    public void GetRegion_CaseInsensitive_Works()
    {
        // FrozenDictionary(StringComparer.OrdinalIgnoreCase) の検証
        _svc.GetRegion("us").Mode.Should().Be(Band6GHzMode.FullBand);
        _svc.GetRegion("Us").Mode.Should().Be(Band6GHzMode.FullBand);
        _svc.GetRegion("uS").Mode.Should().Be(Band6GHzMode.FullBand);
    }

    [Fact]
    public void AllRegions_ContainsExpectedCountries()
    {
        var regions = _svc.AllRegions;
        regions.Should().NotBeEmpty();
        regions.Count.Should().BeGreaterThan(10);
        regions.Should().Contain(r => r.CountryCode == "JP");
        regions.Should().Contain(r => r.CountryCode == "US");
        regions.Should().Contain(r => r.CountryCode == "CN");
    }
}
