using System;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  BeaconIeApplier — BeaconIeSummary → WifiNetwork フラグ反映
// ══════════════════════════════════════════════════════════════
public class BeaconIeApplierTests
{
    private static BeaconIeSummary Summary(
        bool ft = false, bool nr = false, BssLoad? bssLoad = null)
    {
        var neighbors = nr
            ? (System.Collections.Generic.IReadOnlyList<NeighborApInfo>)
              new[] { new NeighborApInfo("aa:bb:cc:dd:ee:ff", 0u, 81, 6, 7) }
            : Array.Empty<NeighborApInfo>();
        var md = ft ? new MobilityDomainInfo(0x1234, false, false) : null;

        return new BeaconIeSummary(
            Neighbors: neighbors,
            RnrNeighbors: Array.Empty<RnrNeighborAp>(),
            BssLoad: bssLoad,
            MobilityDomain: md,
            Wmm: null,
            WmmQosInfo: null,
            PresentElementIds: Array.Empty<byte>());
    }

    private static WifiNetwork BaseNet(params BssInfo[] entries)
        => new() { Ssid = "Test", BssEntries = entries };

    [Fact]
    public void AppliesFastTransition_And_NeighborReport()
    {
        var net = BaseNet().WithBeaconIe(Summary(ft: true, nr: true));
        net.FastTransition.Should().BeTrue();
        net.NeighborReport.Should().BeTrue();
    }

    [Fact]
    public void DoesNotClearExistingTrueFlags()
    {
        var net = (BaseNet() with { FastTransition = true, NeighborReport = true })
                  .WithBeaconIe(Summary(ft: false, nr: false));
        net.FastTransition.Should().BeTrue("既存 true は IE 要約で打ち消さない");
        net.NeighborReport.Should().BeTrue();
    }

    [Fact]
    public void BackfillsBssLoadOnFirstEntry()
    {
        var bss = new BssInfo { Bssid = "aa:bb:cc:dd:ee:ff", Channel = 36 };
        var load = new BssLoad(10, 128, 0);
        var net = BaseNet(bss).WithBeaconIe(Summary(bssLoad: load));

        net.BssEntries[0].BssLoad.Should().Be(load);
    }

    [Fact]
    public void DoesNotOverwriteExistingBssLoad()
    {
        var existing = new BssLoad(5, 50, 0);
        var bss = new BssInfo { Bssid = "x", BssLoad = existing };
        var net = BaseNet(bss).WithBeaconIe(Summary(bssLoad: new BssLoad(99, 200, 0)));

        net.BssEntries[0].BssLoad.Should().Be(existing, "既存 BssLoad を上書きしない");
    }

    [Fact]
    public void NoBssEntries_NoThrow()
    {
        var net = BaseNet().WithBeaconIe(Summary(bssLoad: new BssLoad(1, 1, 0)));
        net.BssEntries.Should().BeEmpty();
    }

    [Fact]
    public void OriginalInstanceUnchanged()
    {
        var original = BaseNet();
        original.WithBeaconIe(Summary(ft: true));
        original.FastTransition.Should().BeFalse("元インスタンスは不変");
    }
}
