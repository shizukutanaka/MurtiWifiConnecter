using System;
using System.Linq;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  MeshNetworkDetector — メッシュ / マルチ AP 検出 (D6)
// ══════════════════════════════════════════════════════════════
public class MeshNetworkDetectorTests
{
    private readonly MeshNetworkDetector _svc = new();

    // ── 小ファクトリ ─────────────────────────────────────────────
    private static WifiNetwork Net(
        string ssid, WifiBand band, string bssid,
        bool ft = false, int signal = 70)
        => new()
        {
            Ssid = ssid, Band = band, SignalQuality = signal,
            FastTransition = ft,
            BssEntries = new[]
            {
                new BssInfo { Bssid = bssid, Channel = BandToChannel(band) }
            }
        };

    private static int BandToChannel(WifiBand b) => b switch
    {
        WifiBand.Band2_4GHz => 6,
        WifiBand.Band5GHz   => 36,
        WifiBand.Band6GHz   => 37,
        _ => 1
    };

    [Fact]
    public void DualBandSameSsid_DetectedAsMesh()
    {
        var nets = new[]
        {
            Net("HomeNet", WifiBand.Band2_4GHz, "aa:bb:cc:00:00:01"),
            Net("HomeNet", WifiBand.Band5GHz,   "aa:bb:cc:00:00:02"),
        };
        var groups = _svc.Detect(nets);
        groups.Should().ContainSingle();
        groups[0].Ssid.Should().Be("HomeNet");
        groups[0].NodeCount.Should().Be(2);
        groups[0].BandCoverage.Should().Contain(WifiBand.Band2_4GHz);
        groups[0].BandCoverage.Should().Contain(WifiBand.Band5GHz);
    }

    [Fact]
    public void TriBand_Has6GHz_And_IsTriBand()
    {
        var nets = new[]
        {
            Net("TriHome", WifiBand.Band2_4GHz, "aa:bb:cc:01:00:01"),
            Net("TriHome", WifiBand.Band5GHz,   "aa:bb:cc:01:00:02"),
            Net("TriHome", WifiBand.Band6GHz,   "aa:bb:cc:01:00:03"),
        };
        var g = _svc.Detect(nets)[0];
        g.Has6GHz.Should().BeTrue();
        g.IsTriBand.Should().BeTrue();
    }

    [Fact]
    public void SameSsidSameBand_NotDetectedAsMesh()
    {
        // 同一バンドの 2 AP は単なる同名 SSID (または隣室の別ルーター)
        var nets = new[]
        {
            Net("Office", WifiBand.Band5GHz, "aa:bb:cc:02:00:01"),
            Net("Office", WifiBand.Band5GHz, "aa:bb:cc:02:00:02"),
        };
        _svc.Detect(nets).Should().BeEmpty("同一バンドのみでは必須条件を満たさない");
    }

    [Fact]
    public void KnownEeroOui_HighConfidence()
    {
        var nets = new[]
        {
            Net("Eero", WifiBand.Band2_4GHz, "34:08:BC:00:01:01"),
            Net("Eero", WifiBand.Band5GHz,   "34:08:BC:00:01:02"),
        };
        var g = _svc.Detect(nets)[0];
        g.KnownMeshVendor.Should().BeTrue();
        g.Confidence.Should().Be(MeshConfidence.High);
    }

    [Fact]
    public void UnknownVendorDualBand_MediumOrLowConfidence()
    {
        var nets = new[]
        {
            Net("Generic", WifiBand.Band2_4GHz, "11:22:33:00:00:01"),
            Net("Generic", WifiBand.Band5GHz,   "11:22:33:00:00:02"),
        };
        var g = _svc.Detect(nets)[0];
        g.KnownMeshVendor.Should().BeFalse();
        g.Confidence.Should().BeOneOf(MeshConfidence.Low, MeshConfidence.Medium);
    }

    [Fact]
    public void FastTransitionAllMembers_FlagSet()
    {
        var nets = new[]
        {
            Net("FTNet", WifiBand.Band2_4GHz, "bb:cc:dd:00:00:01", ft: true),
            Net("FTNet", WifiBand.Band5GHz,   "bb:cc:dd:00:00:02", ft: true),
        };
        var g = _svc.Detect(nets)[0];
        g.HasFastTransition.Should().BeTrue();
    }

    [Fact]
    public void ConsistentMdid_AcrossNodes_RaisesConfidence()
    {
        // 同一 MDID を全 BSS が共有 → FT メッシュ網羅展開
        WifiNetwork WithMdid(WifiBand band, string bssid, ushort mdid) => new()
        {
            Ssid = "FtMesh", Band = band, SignalQuality = 70, FastTransition = true,
            BssEntries = new[]
            {
                new BssInfo { Bssid = bssid, Channel = BandToChannel(band), MobilityDomainId = mdid }
            }
        };

        var nets = new[]
        {
            WithMdid(WifiBand.Band2_4GHz, "11:22:33:00:00:01", 0xA1B2),
            WithMdid(WifiBand.Band5GHz,   "11:22:33:00:00:02", 0xA1B2),
        };
        var g = _svc.Detect(nets)[0];
        g.ConsistentMdid.Should().BeTrue();
        // multiBand(2) + consistentMdid(2) = 4 → Medium 以上
        g.Confidence.Should().BeOneOf(MeshConfidence.Medium, MeshConfidence.High);
    }

    [Fact]
    public void InconsistentMdid_NotFlaggedConsistent()
    {
        WifiNetwork WithMdid(WifiBand band, string bssid, ushort mdid) => new()
        {
            Ssid = "Mixed", Band = band, SignalQuality = 70, FastTransition = true,
            BssEntries = new[]
            {
                new BssInfo { Bssid = bssid, Channel = BandToChannel(band), MobilityDomainId = mdid }
            }
        };
        var nets = new[]
        {
            WithMdid(WifiBand.Band2_4GHz, "11:22:33:00:00:01", 0xAAAA),
            WithMdid(WifiBand.Band5GHz,   "11:22:33:00:00:02", 0xBBBB),
        };
        _svc.Detect(nets)[0].ConsistentMdid.Should().BeFalse("異なる MDID は一貫していない");
    }

    [Fact]
    public void DifferentSsids_NotGroupedTogether()
    {
        var nets = new[]
        {
            Net("NetA", WifiBand.Band2_4GHz, "cc:dd:ee:00:00:01"),
            Net("NetB", WifiBand.Band5GHz,   "cc:dd:ee:00:00:02"),
        };
        var groups = _svc.Detect(nets);
        groups.Should().BeEmpty("異なる SSID はグループ化しない");
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        _svc.Detect(Array.Empty<WifiNetwork>()).Should().BeEmpty();
    }

    [Fact]
    public void MultipleMeshNetworks_DetectedSeparately()
    {
        var nets = new[]
        {
            Net("HomeNet", WifiBand.Band2_4GHz, "34:08:BC:00:01:01"),
            Net("HomeNet", WifiBand.Band5GHz,   "34:08:BC:00:01:02"),
            Net("OfficeNet", WifiBand.Band2_4GHz, "9C:3D:CF:00:02:01"),
            Net("OfficeNet", WifiBand.Band5GHz,   "9C:3D:CF:00:02:02"),
        };
        var groups = _svc.Detect(nets);
        groups.Should().HaveCount(2);
        groups.Select(g => g.Ssid).Should().Contain(new[] { "HomeNet", "OfficeNet" });
    }
}
