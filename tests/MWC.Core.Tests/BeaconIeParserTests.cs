using System.Collections.Generic;
using FluentAssertions;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  BeaconIeParser — 1 パスで全 IE を集約解析
//  既存の個別パーサと同一結果を返すことを確認する
// ══════════════════════════════════════════════════════════════
public class BeaconIeParserTests
{
    // ── ヘルパ: 各 IE を組み立てる ──────────────────────────────
    private static byte[] Ssid(string s)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(s);
        var el = new List<byte> { 0, (byte)bytes.Length };
        el.AddRange(bytes);
        return el.ToArray();
    }

    private static byte[] NeighborReport(byte[] bssid6, uint info, byte opClass, byte ch, byte phy)
    {
        var body = new List<byte>(bssid6)
        {
            (byte)(info & 0xFF), (byte)((info >> 8) & 0xFF),
            (byte)((info >> 16) & 0xFF), (byte)((info >> 24) & 0xFF),
            opClass, ch, phy
        };
        var el = new List<byte> { 52, (byte)body.Count };
        el.AddRange(body);
        return el.ToArray();
    }

    private static byte[] MobilityDomain(ushort mdid, bool overDs)
        => new byte[] { 54, 3, (byte)(mdid & 0xFF), (byte)(mdid >> 8), (byte)(overDs ? 1 : 0) };

    private static byte[] BssLoadEl(ushort stations, byte util, ushort cap)
        => new byte[] { 11, 5, (byte)(stations & 0xFF), (byte)(stations >> 8), util,
                        (byte)(cap & 0xFF), (byte)(cap >> 8) };

    private static byte[] WmmParam()
        => new byte[]
        {
            221, 24, 0x00, 0x50, 0xF2, 0x02, 0x01, 0x01, 0x02, 0x00,
            0x03, 0xA4, 0, 0,       // BE
            0x27, 0xA4, 0, 0,       // BK
            0x42, 0x43, 188, 0,     // VI
            0x62, 0x32, 102, 0,     // VO
        };

    private static readonly byte[] Bssid1 = { 0x00, 0x11, 0x22, 0xAA, 0xBB, 0xCC };

    [Fact]
    public void AggregatesAllElementTypesInOnePass()
    {
        var stream = new List<byte>();
        stream.AddRange(Ssid("MyNet"));
        stream.AddRange(NeighborReport(Bssid1, 0u, 81, 6, 7));
        stream.AddRange(MobilityDomain(0x1A2B, overDs: true));
        stream.AddRange(BssLoadEl(15, 200, 0));
        stream.AddRange(new byte[] { 7, 6, (byte)'U', (byte)'S', (byte)'I', 1, 11, 30 }); // Country
        stream.AddRange(new byte[] { 35, 2, 20, 0 });                                     // TPC Report
        stream.AddRange(WmmParam());

        var s = BeaconIeParser.Parse(stream.ToArray());

        s.Neighbors.Should().ContainSingle();
        s.Neighbors[0].Bssid.Should().Be("00:11:22:aa:bb:cc");
        s.MobilityDomain.Should().NotBeNull();
        s.MobilityDomain!.Mdid.Should().Be(0x1A2B);
        s.MobilityDomain.OverDsCapable.Should().BeTrue();
        s.BssLoad.Should().NotBeNull();
        s.BssLoad!.StationCount.Should().Be(15);
        s.Wmm.Should().NotBeNull();
        s.WmmQosInfo.Should().Be(0x02);
        s.Country.Should().NotBeNull();
        s.Country!.CountryCode.Should().Be("US");
        s.Country.Environment.Should().Be(RegulatoryEnvironment.Indoor);
        s.Tpc.Should().NotBeNull();
        s.Tpc!.TransmitPowerDbm.Should().Be(20);

        s.SupportsFastTransition.Should().BeTrue();
        s.HasNeighborReport.Should().BeTrue();
        s.SupportsWmm.Should().BeTrue();
    }

    [Fact]
    public void MatchesIndividualParsers()
    {
        var stream = new List<byte>();
        stream.AddRange(NeighborReport(Bssid1, (1u << 10), 115, 36, 9));
        stream.AddRange(BssLoadEl(7, 128, 500));
        stream.AddRange(MobilityDomain(0xABCD, overDs: false));
        var data = stream.ToArray();

        var agg = BeaconIeParser.Parse(data);

        // 個別パーサと同一結果
        var indivNeighbors = NeighborReportParser.Parse(data);
        var indivBssLoad   = BssLoadParser.Parse(data);
        var indivMd        = MobilityDomainParser.Parse(data);

        agg.Neighbors.Should().BeEquivalentTo(indivNeighbors);
        agg.BssLoad.Should().Be(indivBssLoad);
        agg.MobilityDomain.Should().Be(indivMd);
        agg.Neighbors[0].SameMobilityDomain.Should().BeTrue();
    }

    [Fact]
    public void PresentElementIds_TracksAllSeenElements()
    {
        var stream = new List<byte>();
        stream.AddRange(Ssid("X"));            // ID 0
        stream.AddRange(BssLoadEl(1, 50, 0));  // ID 11
        var s = BeaconIeParser.Parse(stream.ToArray());

        s.PresentElementIds.Should().Contain(new byte[] { 0, 11 });
    }

    [Fact]
    public void EmptyOrTruncated_ReturnsEmptySummary()
    {
        var s = BeaconIeParser.Parse(System.Array.Empty<byte>());
        s.Neighbors.Should().BeEmpty();
        s.BssLoad.Should().BeNull();
        s.MobilityDomain.Should().BeNull();
        s.Wmm.Should().BeNull();
        s.SupportsFastTransition.Should().BeFalse();
        s.SupportsWmm.Should().BeFalse();

        // 切り詰め: 宣言長 > 実体 → 例外なしで空
        BeaconIeParser.Parse(new byte[] { 52, 20, 0x00 }).Neighbors.Should().BeEmpty();
    }

    [Fact]
    public void MultipleNeighbors_AllCollected()
    {
        var b2 = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x01 };
        var stream = new List<byte>();
        stream.AddRange(NeighborReport(Bssid1, 0u, 81, 1, 7));
        stream.AddRange(NeighborReport(b2, 0u, 121, 149, 9));
        var s = BeaconIeParser.Parse(stream.ToArray());

        s.Neighbors.Should().HaveCount(2);
        s.Neighbors[1].Channel.Should().Be(149);
    }

    [Fact]
    public void NonWmmVendorSpecific_DoesNotSetWmm()
    {
        // OUI 00:0C:E7 (not WMM)
        var stream = new List<byte> { 221, 7, 0x00, 0x0C, 0xE7, 0x02, 0x01, 0x01, 0x00 };
        var s = BeaconIeParser.Parse(stream.ToArray());
        s.Wmm.Should().BeNull();
        s.SupportsWmm.Should().BeFalse();
        s.PresentElementIds.Should().Contain((byte)221);
    }

    [Fact]
    public void ExtendedCapabilities_Bit19_SetsBssTransitionMgmt()
    {
        // EID 127, Length=3, body[0]=0x00, body[1]=0x00, body[2]=0x08 (bit 3 = bit 19 overall)
        var stream = new List<byte> { 127, 3, 0x00, 0x00, 0x08 };
        var s = BeaconIeParser.Parse(stream.ToArray());
        s.BssTransitionMgmt.Should().BeTrue("bit 19 of Extended Capabilities = BSS Transition (802.11v)");
    }

    [Fact]
    public void ExtendedCapabilities_Bit19_Clear_DoesNotSetBssTransitionMgmt()
    {
        // EID 127, Length=3, body[2]=0x00 (bit 3 not set)
        var stream = new List<byte> { 127, 3, 0x00, 0x00, 0x00 };
        var s = BeaconIeParser.Parse(stream.ToArray());
        s.BssTransitionMgmt.Should().BeFalse();
    }

    [Fact]
    public void ExtendedCapabilities_TooShort_DoesNotSetBssTransitionMgmt()
    {
        // EID 127, Length=2 (< 3), no body[2] to read
        var stream = new List<byte> { 127, 2, 0xFF, 0xFF };
        var s = BeaconIeParser.Parse(stream.ToArray());
        s.BssTransitionMgmt.Should().BeFalse("Short ExtCap IE must not set flag");
    }
}
