using System;
using System.Collections.Generic;
using FluentAssertions;
using MWC.Core.Abstractions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  BeaconEnrichmentService — 生ビーコン IE で WifiNetwork を強化
// ══════════════════════════════════════════════════════════════
public class BeaconEnrichmentServiceTests
{
    private readonly BeaconEnrichmentService _svc = new();

    // Mobility Domain (ID 54) + Neighbor Report (ID 52) を含む IE 列を作る
    private static byte[] FtAndNeighborIes(ushort mdid)
    {
        var stream = new List<byte>();
        // Mobility Domain: mdid, over-DS=true
        stream.AddRange(new byte[] { 54, 3, (byte)(mdid & 0xFF), (byte)(mdid >> 8), 0x01 });
        // Neighbor Report (ID 52): BSSID + info + opClass/ch/phy
        stream.AddRange(new byte[]
        {
            52, 13, 0x00, 0x11, 0x22, 0xAA, 0xBB, 0xCC, 0, 0, 0, 0, 81, 6, 7
        });
        return stream.ToArray();
    }

    private static WifiNetwork Net(string ssid, string bssid) => new()
    {
        Ssid = ssid,
        BssEntries = new[] { new BssInfo { Bssid = bssid, Channel = 36 } }
    };

    [Fact]
    public void EnrichOne_AppliesParsedIeFlags()
    {
        var raw = new Dictionary<string, RawBeaconData>
        {
            ["aa:bb:cc:dd:ee:ff"] = new RawBeaconData(FtAndNeighborIes(0xABCD), 0, 100)
        };

        var net = _svc.EnrichOne(Net("Net", "AA:BB:CC:DD:EE:FF"), raw);

        net.FastTransition.Should().BeTrue("Mobility Domain 要素から FT を検出");
        net.NeighborReport.Should().BeTrue("Neighbor Report 要素を検出");
        net.BssEntries[0].MobilityDomainId.Should().Be(0xABCD);
    }

    [Fact]
    public void EnrichOne_BssidCaseInsensitiveMatch()
    {
        var raw = new Dictionary<string, RawBeaconData>
        {
            ["aa:bb:cc:dd:ee:ff"] = new RawBeaconData(FtAndNeighborIes(0x1111), 0, 100)
        };
        // ネットワーク側は大文字 BSSID
        var net = _svc.EnrichOne(Net("Net", "AA:BB:CC:DD:EE:FF"), raw);
        net.FastTransition.Should().BeTrue();
    }

    [Fact]
    public void EnrichOne_NoMatch_ReturnsOriginal()
    {
        var raw = new Dictionary<string, RawBeaconData>
        {
            ["11:22:33:44:55:66"] = new RawBeaconData(FtAndNeighborIes(0x2222), 0, 100)
        };
        var original = Net("Net", "AA:BB:CC:DD:EE:FF");
        var net = _svc.EnrichOne(original, raw);
        net.FastTransition.Should().BeFalse();
        net.Should().BeSameAs(original, "一致なしなら原型をそのまま返す");
    }

    [Fact]
    public void Enrich_EmptyRawBeacons_ReturnsInputUnchanged()
    {
        var nets = new[] { Net("A", "aa:bb:cc:00:00:01") };
        var result = _svc.Enrich(nets, new Dictionary<string, RawBeaconData>());
        result.Should().BeSameAs(nets);
    }

    [Fact]
    public void Enrich_MixedMatches_OnlyMatchedEnriched()
    {
        var nets = new[]
        {
            Net("Matched",   "aa:bb:cc:00:00:01"),
            Net("Unmatched", "ff:ff:ff:00:00:09"),
        };
        var raw = new Dictionary<string, RawBeaconData>
        {
            ["aa:bb:cc:00:00:01"] = new RawBeaconData(FtAndNeighborIes(0x3333), 0, 100)
        };

        var result = _svc.Enrich(nets, raw);
        result[0].FastTransition.Should().BeTrue();
        result[1].FastTransition.Should().BeFalse();
    }

    [Fact]
    public void NoBssEntries_ReturnsOriginal()
    {
        var net = new WifiNetwork { Ssid = "Hidden" };
        var raw = new Dictionary<string, RawBeaconData>
        {
            ["aa:bb:cc:00:00:01"] = new RawBeaconData(FtAndNeighborIes(1), 0, 100)
        };
        _svc.EnrichOne(net, raw).Should().BeSameAs(net);
    }

    [Fact]
    public void NullBeaconIeProvider_ReturnsEmpty()
    {
        NullBeaconIeProvider.Instance.GetRawBeacons(Guid.NewGuid()).Should().BeEmpty();
    }
}
