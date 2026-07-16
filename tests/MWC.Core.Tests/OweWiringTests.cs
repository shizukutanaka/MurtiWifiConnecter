using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MWC.App.ViewModels;
using MWC.Core.Models;
using MWC.Core.Services;
using MWC.Core.Tests.Fakes;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  OweSelectionService GUI wiring
//  (docs/FEATURE-AUDIT.md §4 priority-2 follow-up: previously a fully
//  tested but orphaned Core service with zero call sites in App/CLI.
//  Wired into AdapterViewModel.RefreshAsync and
//  AllAdaptersOverviewViewModel.AdapterPanelViewModel.RefreshAsync so
//  the OWE Transition Mode Open-beacon placeholder — RFC 8110 — no
//  longer shows up as a separate, confusing duplicate entry.)
// ══════════════════════════════════════════════════════════════
public class OweWiringTests
{
    private static WifiNetwork Net(string ssid, AuthMethod auth, int signal = 70) => new()
    {
        Ssid = ssid, Auth = auth, SignalQuality = signal, Band = WifiBand.Band5GHz,
    };

    [Fact]
    public async Task AdapterViewModel_RefreshAsync_MergesOweTransitionPair()
    {
        var wifi = new FakeWifiService();
        wifi.FakeNetworks.Clear();
        wifi.FakeNetworks.Add(Net("FreeWifi", AuthMethod.Open, 80));
        wifi.FakeNetworks.Add(Net("FreeWifi", AuthMethod.OWE, 78));
        wifi.FakeNetworks.Add(Net("Home", AuthMethod.WPA2PSK, 90));

        // 独自の Guid を使う(共有 FakeAdapters[0].Id を再利用すると、他テストが
        // AdapterPreferencesService の永続化ファイルへ書き込んだ帯域フィルタ設定の
        // 影響を受けかねないため)。
        var adapter = new WifiAdapter { Id = Guid.NewGuid(), Name = "TestNic", Description = "Test" };
        var history = new SignalHistoryService();
        var oui     = new OuiLookupService();
        var prefs   = new AdapterPreferencesService();
        var netHist = new NetworkHistoryService(NullLogger<NetworkHistoryService>.Instance);
        var executor = new ConnectionExecutor(wifi, netHist, NullLogger<ConnectionExecutor>.Instance);

        var vm = new AdapterViewModel(adapter, wifi, history, oui,
            NullLogger.Instance, prefs, executor);

        await vm.RefreshAsync();

        vm.SourceNetworks.Should().HaveCount(2,
            because: "the Open beacon of the FreeWifi OWE-transition pair should be merged away");
        vm.SourceNetworks.Any(n => n.Ssid == "FreeWifi" && n.Auth == AuthMethod.Open)
            .Should().BeFalse();
        vm.SourceNetworks.Any(n => n.Ssid == "FreeWifi" && n.Auth == AuthMethod.OWE)
            .Should().BeTrue();
        vm.SourceNetworks.Any(n => n.Ssid == "Home").Should().BeTrue();

        // The UI-facing collection should reflect the same merge (no duplicate SSID entries).
        vm.Networks.Select(n => n.Ssid).Should().OnlyHaveUniqueItems();
        vm.Networks.Should().HaveCount(2);
    }

    [Fact]
    public async Task AllAdaptersOverview_AdapterPanel_RefreshAsync_MergesOweTransitionPair()
    {
        var wifi = new FakeWifiService();
        wifi.FakeNetworks.Clear();
        wifi.FakeNetworks.Add(Net("CafeNet", AuthMethod.Open, 75));
        wifi.FakeNetworks.Add(Net("CafeNet", AuthMethod.OWE, 74));

        var adapter = new WifiAdapter { Id = Guid.NewGuid(), Name = "TestNic2", Description = "Test" };
        var prefs   = new AdapterPreferencesService();
        var oui     = new OuiLookupService();
        var netHist = new NetworkHistoryService(NullLogger<NetworkHistoryService>.Instance);
        var executor = new ConnectionExecutor(wifi, netHist, NullLogger<ConnectionExecutor>.Instance);

        var panel = new AdapterPanelViewModel(adapter, wifi, prefs, executor, oui, NullLogger.Instance);

        await panel.RefreshAsync();

        panel.SourceNetworks.Should().HaveCount(1);
        panel.SourceNetworks[0].Auth.Should().Be(AuthMethod.OWE);
        panel.Networks.Should().HaveCount(1,
            because: "the Open/OWE Transition Mode pair must not appear as two separate rows");
    }
}
