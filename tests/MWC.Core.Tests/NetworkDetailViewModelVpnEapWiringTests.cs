using System;
using FluentAssertions;
using MWC.App.ViewModels;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  NetworkDetailViewModel — VPN advice / EAP stats GUI wiring
//  (docs/FEATURE-AUDIT.md §2a follow-up: VpnAdvisoryService and
//  EapAuthStatsService were previously CLI-only; wired into the GUI
//  detail panel the same way SecurityAdvisoryService already was.)
//
//  Both services are held as `private static readonly` fields on the
//  ViewModel (matching every other *AdvisoryService field already on this
//  class), constructed once at type-load time from the real
//  %LocalAppData%/MWC/*.json files. This means a record written to
//  EapAuthStatsService *after* the ViewModel's static field was
//  constructed is not visible within the same test process — so this
//  test class only covers the no-prior-data path for EAP stats
//  (HasEapStats stays false), and the VPN advice paths that don't depend
//  on pre-seeded state (unknown network, open network).
// ══════════════════════════════════════════════════════════════
public class NetworkDetailViewModelVpnEapWiringTests
{
    private static WifiNetwork Net(string ssid, AuthMethod auth) =>
        new() { Ssid = ssid, Auth = auth, Band = WifiBand.Band5GHz, SignalQuality = 70 };

    [Fact]
    public void Load_UnknownEncryptedNetwork_ShowsRecommendedVpnAdvice()
    {
        var vm = new NetworkDetailViewModel();
        var ssid = "VpnWiring_Unknown_" + Guid.NewGuid().ToString("N")[..8];

        vm.Load(Net(ssid, AuthMethod.WPA3SAE));

        vm.VpnAdviceLabel.Should().NotBeNullOrEmpty();
        // Never-before-seen SSID: VpnAdvisoryService.Analyze returns Recommended
        // regardless of encryption strength (isKnownTrustedNetwork=false path).
        vm.VpnAdviceLabel.Should().Be(MWC.App.Resources.L.Get("Detail_VpnAdvice_Recommended"));
    }

    [Fact]
    public void Load_OpenNetwork_ShowsStronglyRecommendedVpnAdvice()
    {
        var vm = new NetworkDetailViewModel();
        var ssid = "VpnWiring_Open_" + Guid.NewGuid().ToString("N")[..8];

        vm.Load(Net(ssid, AuthMethod.Open));

        vm.VpnAdviceLabel.Should().Be(MWC.App.Resources.L.Get("Detail_VpnAdvice_StronglyRecommended"));
    }

    [Fact]
    public void Load_NullNetwork_ClearsVpnAndEapLabels()
    {
        var vm = new NetworkDetailViewModel();
        vm.Load(Net("VpnWiring_ToBeCleared", AuthMethod.WPA2PSK));
        vm.VpnAdviceLabel.Should().NotBeNullOrEmpty();

        vm.Load(null);

        vm.VpnAdviceLabel.Should().BeEmpty();
        vm.EapStatsLabel.Should().BeEmpty();
        vm.HasEapStats.Should().BeFalse();
    }

    [Fact]
    public void Load_NetworkWithNoPriorEapAttempts_HasEapStatsIsFalse()
    {
        var vm = new NetworkDetailViewModel();
        // A fresh, never-connected-to SSID has no recorded EapAuthStatsService entry.
        var ssid = "VpnWiring_NoEap_" + Guid.NewGuid().ToString("N")[..8];

        vm.Load(Net(ssid, AuthMethod.WPA2Enterprise));

        vm.HasEapStats.Should().BeFalse();
        vm.EapStatsLabel.Should().BeEmpty();
    }

    // ── RegulatoryDomainService wiring ──────────────────────────────
    // RegulatoryDomainService.DetectCurrentRegion() reads RegionInfo.CurrentRegion
    // (the OS/CI environment's locale), which this test suite cannot control. So
    // rather than hard-coding an expected region, these tests either (a) check
    // behavior that holds regardless of region (non-6GHz networks never show
    // regulatory info), or (b) compute the expected answer via the same
    // RegulatoryDomainService call the ViewModel makes, and assert consistency.

    [Theory]
    [InlineData(WifiBand.Band2_4GHz)]
    [InlineData(WifiBand.Band5GHz)]
    public void Load_NonSixGhzNetwork_HasNoRegulatoryInfo(WifiBand band)
    {
        var vm = new NetworkDetailViewModel();
        var ssid = "RegWiring_NonSixGhz_" + Guid.NewGuid().ToString("N")[..8];
        var net = new WifiNetwork { Ssid = ssid, Auth = AuthMethod.WPA2PSK, Band = band, Channel = 36 };

        vm.Load(net);

        vm.HasRegulatoryInfo.Should().BeFalse();
        vm.RegulatoryLabel.Should().BeEmpty();
    }

    [Fact]
    public void Load_SixGhzNetwork_ShowsRegulatoryInfoConsistentWithService()
    {
        var vm = new NetworkDetailViewModel();
        var ssid = "RegWiring_SixGhz_" + Guid.NewGuid().ToString("N")[..8];
        const int channel = 37; // a PSC channel (see RegulatoryDomainService.PscChannels)
        var net = new WifiNetwork { Ssid = ssid, Auth = AuthMethod.WPA3SAE, Band = WifiBand.Band6GHz, Channel = channel };

        vm.Load(net);

        vm.HasRegulatoryInfo.Should().BeTrue();
        vm.RegulatoryLabel.Should().NotBeNullOrEmpty();

        var regulatory = new RegulatoryDomainService();
        var region = regulatory.DetectCurrentRegion();
        bool expectedLegal = regulatory.IsChannelLegal(channel, region.CountryCode);

        if (expectedLegal)
            vm.RegulatoryLabel.Should().Contain(region.CountryName)
                .And.Contain("Legal", because: "channel 37 is legal in the detected region per RegulatoryDomainService");
        else
            vm.RegulatoryLabel.Should().Contain(region.CountryName)
                .And.Contain("Not permitted");
    }

    [Fact]
    public void Load_NullNetwork_ClearsRegulatoryInfo()
    {
        var vm = new NetworkDetailViewModel();
        vm.Load(new WifiNetwork { Ssid = "RegWiring_ToClear", Auth = AuthMethod.WPA3SAE, Band = WifiBand.Band6GHz, Channel = 37 });
        vm.HasRegulatoryInfo.Should().BeTrue();

        vm.Load(null);

        vm.HasRegulatoryInfo.Should().BeFalse();
        vm.RegulatoryLabel.Should().BeEmpty();
    }
}
