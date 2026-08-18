using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  Passpoint (Hotspot 2.0) 検出の配線契約 — `mwc passpoint`。
//
//  `Hotspot20Service` は長らく孤立サービスだった。理由は
//  `WifiNetwork.IsPasspoint` が `BssInfo.HasInterworkingElement` を読むのに、
//  **どの層もそれを設定していなかった**こと (docs/FEATURE-AUDIT.md §1a)。
//  2026-07 に `BeaconIeParser` へ 802.11u Interworking 検出を実装し、
//  既存の IE パイプライン (WlanBssIeProvider → BeaconEnrichmentService →
//  BeaconIeApplier) を通って値が届くようになったため配線できた。
//
//  ここで固定するのは判定条件そのもの — Enterprise 認証と Interworking の
//  **両方**が要ることと、片方だけでは Passpoint と見なさないこと。
//  誤検知すると `mwc passpoint` が無関係な AP を「対応」と示してしまう。
// ══════════════════════════════════════════════════════════════
public class PasspointWiringTests
{
    private static WifiNetwork Net(AuthMethod auth, bool interworking)
        => new()
        {
            Ssid = "TestAP",
            Auth = auth,
            BssEntries = new List<BssInfo>
            {
                new() { Bssid = "AA:BB:CC:11:22:33", HasInterworkingElement = interworking },
            },
        };

    [Fact]
    public void EnterpriseWithInterworking_IsPasspoint()
    {
        Net(AuthMethod.WPA2Enterprise, interworking: true).IsPasspoint.Should().BeTrue();
    }

    [Fact]
    public void EnterpriseWithoutInterworking_IsNotPasspoint()
    {
        // 社内 Wi-Fi の多くは Enterprise だが Passpoint ではない。
        Net(AuthMethod.WPA2Enterprise, interworking: false).IsPasspoint.Should().BeFalse();
    }

    [Fact]
    public void InterworkingWithoutEnterprise_IsNotPasspoint()
    {
        // Interworking だけでは足りない。Passpoint は Enterprise 認証が前提。
        Net(AuthMethod.WPA2PSK, interworking: true).IsPasspoint.Should().BeFalse();
        Net(AuthMethod.Open,    interworking: true).IsPasspoint.Should().BeFalse();
    }

    [Theory]
    [InlineData(AuthMethod.WPA2Enterprise)]
    [InlineData(AuthMethod.WPA3Enterprise)]
    [InlineData(AuthMethod.WPA3Enterprise192)]
    public void AllEnterpriseVariants_QualifyWithInterworking(AuthMethod auth)
    {
        Net(auth, interworking: true).IsPasspoint.Should().BeTrue();
    }

    [Fact]
    public void NoBssEntries_IsNotPasspoint()
    {
        // BSS 情報が無ければ判定材料が無い。例外ではなく false になること。
        new WifiNetwork { Ssid = "X", Auth = AuthMethod.WPA2Enterprise }
            .IsPasspoint.Should().BeFalse();
    }

    // ── サービス側のフィルタ ──────────────────────────────────────

    [Fact]
    public void FilterPasspointNetworks_KeepsOnlyQualifyingNetworks()
    {
        var nets = new List<WifiNetwork>
        {
            Net(AuthMethod.WPA2Enterprise, true),          // 該当
            Net(AuthMethod.WPA2Enterprise, false),         // Enterprise だが Interworking 無し
            Net(AuthMethod.WPA2PSK, true),                 // Interworking だが PSK
            Net(AuthMethod.WPA3Enterprise, true),          // 該当
        };

        new Hotspot20Service().FilterPasspointNetworks(nets)
            .Should().HaveCount(2);
    }

    [Fact]
    public void FilterPasspointNetworks_OnEmptyScan_ReturnsEmpty_NotNull()
    {
        new Hotspot20Service().FilterPasspointNetworks(new List<WifiNetwork>())
            .Should().NotBeNull().And.BeEmpty();
    }

    // ── キャリアプリセット ────────────────────────────────────────

    [Fact]
    public void CarrierPresets_AreUsable()
    {
        // `mwc passpoint --carriers` が表示する内容。
        // 空だと機能が無意味になるため、最低限の健全性を固定する。
        var presets = Hotspot20Service.KnownCarriers;

        presets.Should().NotBeEmpty();
        presets.Should().OnlyContain(p =>
            !string.IsNullOrWhiteSpace(p.CarrierName) &&
            !string.IsNullOrWhiteSpace(p.Ssid) &&
            !string.IsNullOrWhiteSpace(p.Domain));
    }

    [Fact]
    public void CarrierProfile_BuildsFromPreset()
    {
        var preset = Hotspot20Service.KnownCarriers.First();
        var spec = new Hotspot20Service().BuildCarrierProfile(preset);

        spec.Ssid.Should().Be(preset.Ssid);
        spec.EapType.Should().Be(preset.EapType);
    }
}
