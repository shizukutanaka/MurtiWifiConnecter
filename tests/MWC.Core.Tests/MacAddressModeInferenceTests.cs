using System;
using System.Collections.Generic;
using FluentAssertions;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  MacAddressModeInference — MAC アドレスからランダム化を判定する
//
//  この機能はかつて「OS の設定値なので Core には切り出せない」と記録されていた。
//  それは問いの取り違えで、必要なのは設定ではなく**効果**であり、効果は
//  IEEE 802 アドレスの Locally Administered ビットに現れる。
//  以下のテストはその判定規則をバイト列で固定する。
// ══════════════════════════════════════════════════════════════
public class MacAddressModeInferenceTests
{
    // ── 単一アドレスからの判定 ──────────────────────────────────

    [Theory]
    [InlineData(0x02)]   // LAA のみ
    [InlineData(0x06)]   // LAA + 別ビット
    [InlineData(0x0A)]
    [InlineData(0x0E)]
    [InlineData(0xDA)]   // 実際に Windows/Android が生成しがちな形
    public void LocallyAdministeredAddress_IsRandomised(byte firstOctet)
    {
        var mac = new byte[] { firstOctet, 0x11, 0x22, 0x33, 0x44, 0x55 };

        var r = MacAddressModeInference.FromAddress(mac);

        r.IsRandomized.Should().BeTrue();
        r.Mode.Should().Be(MacAddressMode.Randomized,
            "a single address cannot tell per-network from daily rotation");
        r.Evidence.Should().Be(MacModeEvidence.LocallyAdministeredBitSet);
    }

    [Fact]
    public void UniversallyAdministeredAddress_IsHardware()
    {
        // LAA ビットが立っていない = IEEE 割当の焼き込みアドレス
        var mac = new byte[] { 0x3C, 0x22, 0xFB, 0x01, 0x02, 0x03 };

        var r = MacAddressModeInference.FromAddress(mac);

        r.Mode.Should().Be(MacAddressMode.Hardware);
        r.IsRandomized.Should().BeFalse();
    }

    [Fact]
    public void KnownVendorOui_StrengthensTheHardwareVerdict()
    {
        // 00:11:22 は内蔵 OUI DB に Apple として存在する
        var mac = new byte[] { 0x00, 0x11, 0x22, 0xAA, 0xBB, 0xCC };

        var r = MacAddressModeInference.FromAddress(mac, new OuiLookupService());

        r.Mode.Should().Be(MacAddressMode.Hardware);
        r.Evidence.Should().Be(MacModeEvidence.UniversallyAdministeredWithKnownVendor);
    }

    [Fact]
    public void UnknownOui_DoesNotChangeTheVerdict()
    {
        // 内蔵 DB は IEEE 全体の抜粋にすぎない。引けないことを randomized の
        // 根拠にしてはならない — LAA ビットだけが結論を決める。
        var mac = new byte[] { 0x3C, 0x9D, 0xEF, 0x01, 0x02, 0x03 };

        var r = MacAddressModeInference.FromAddress(mac, new OuiLookupService());

        r.Mode.Should().Be(MacAddressMode.Hardware);
        r.Evidence.Should().Be(MacModeEvidence.UniversallyAdministered);
    }

    [Fact]
    public void GroupAddress_IsRejected()
    {
        // bit0 = Group/Multicast。端末アドレスではない。
        var mac = new byte[] { 0x01, 0x00, 0x5E, 0x00, 0x00, 0xFB };

        var r = MacAddressModeInference.FromAddress(mac);

        r.Mode.Should().Be(MacAddressMode.Unknown);
        r.Evidence.Should().Be(MacModeEvidence.NotAUnicastAddress);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(7)]
    public void WrongLength_IsRejected(int length)
    {
        var r = MacAddressModeInference.FromAddress(new byte[length]);

        r.Mode.Should().Be(MacAddressMode.Unknown);
        r.Evidence.Should().Be(MacModeEvidence.MalformedAddress);
    }

    // ── 観測履歴からの判定 ──────────────────────────────────────

    [Fact]
    public void SameSsidDifferentAddressesAcrossDays_IsDailyRotation()
    {
        var day1 = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);
        var obs = new List<MacObservation>
        {
            new("HomeNet", new byte[] { 0x02, 0x11, 0x22, 0x33, 0x44, 0x55 }, day1),
            new("HomeNet", new byte[] { 0x02, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE }, day1.AddDays(1)),
        };

        var r = MacAddressModeInference.FromHistory(obs);

        r.Mode.Should().Be(MacAddressMode.RandomDaily);
        r.Evidence.Should().Be(MacModeEvidence.AddressChangedWithinSameSsidAcrossDays);
    }

    [Fact]
    public void DifferentAddressPerSsid_IsPerNetworkRandomisation()
    {
        var t = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);
        var obs = new List<MacObservation>
        {
            new("HomeNet", new byte[] { 0x02, 0x11, 0x22, 0x33, 0x44, 0x55 }, t),
            new("CafeNet", new byte[] { 0x02, 0x99, 0x88, 0x77, 0x66, 0x55 }, t.AddHours(3)),
        };

        var r = MacAddressModeInference.FromHistory(obs);

        r.Mode.Should().Be(MacAddressMode.RandomPerNetwork);
        r.Evidence.Should().Be(MacModeEvidence.AddressDiffersPerSsid);
    }

    [Fact]
    public void SameSsidSameAddress_StaysUndetermined()
    {
        // ランダム化されていることは分かるが、種類を判定する材料が無い。
        // ここで RandomPerNetwork と決めつけないことが重要。
        var t = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);
        var mac = new byte[] { 0x02, 0x11, 0x22, 0x33, 0x44, 0x55 };
        var obs = new List<MacObservation>
        {
            new("HomeNet", mac, t),
            new("HomeNet", mac, t.AddDays(2)),
        };

        var r = MacAddressModeInference.FromHistory(obs);

        r.Mode.Should().Be(MacAddressMode.Randomized);
        r.IsRandomized.Should().BeTrue();
    }

    [Fact]
    public void AllHardwareObservations_ReportHardware()
    {
        var t = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);
        var mac = new byte[] { 0x3C, 0x22, 0xFB, 0x01, 0x02, 0x03 };
        var obs = new List<MacObservation>
        {
            new("HomeNet", mac, t),
            new("CafeNet", mac, t.AddDays(1)),
        };

        MacAddressModeInference.FromHistory(obs).Mode.Should().Be(MacAddressMode.Hardware);
    }

    [Fact]
    public void NoObservations_IsUnknown()
    {
        var r = MacAddressModeInference.FromHistory(Array.Empty<MacObservation>());

        r.Mode.Should().Be(MacAddressMode.Unknown);
        r.Evidence.Should().Be(MacModeEvidence.NoObservations);
    }

    // ── 文字列パース ────────────────────────────────────────────

    [Theory]
    [InlineData("AA:BB:CC:DD:EE:FF")]
    [InlineData("aa-bb-cc-dd-ee-ff")]
    [InlineData("AABB.CCDD.EEFF")]
    [InlineData("AABBCCDDEEFF")]
    [InlineData("  AA:BB:CC:DD:EE:FF  ")]
    public void TryParse_AcceptsTheUsualSeparators(string text)
    {
        MacAddressModeInference.TryParse(text, out var mac).Should().BeTrue();
        mac.Should().Equal(0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AA:BB:CC")]
    [InlineData("AA:BB:CC:DD:EE:FF:00")]
    [InlineData("ZZ:BB:CC:DD:EE:FF")]
    public void TryParse_RejectsMalformedInput(string? text)
    {
        MacAddressModeInference.TryParse(text, out _).Should().BeFalse();
    }
}
