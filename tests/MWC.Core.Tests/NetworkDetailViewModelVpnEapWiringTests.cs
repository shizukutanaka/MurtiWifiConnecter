using System;
using FluentAssertions;
using MWC.App.ViewModels;
using MWC.Core.Models;
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
}
