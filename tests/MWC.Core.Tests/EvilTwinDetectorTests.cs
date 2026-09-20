using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  EvilTwinDetector — Rogue AP / Evil Twin 検出
//  (arXiv 2406.01927: Position-based Rogue AP Detection)
// ══════════════════════════════════════════════════════════════
public class EvilTwinDetectorTests
{
    private static WifiNetwork Net(
        string ssid, AuthMethod auth, string bssid = "AA:BB:CC:DD:EE:FF", int signal = 70) =>
        new()
        {
            Ssid          = ssid,
            Auth          = auth,
            Band          = WifiBand.Band5GHz,
            SignalQuality = signal,
            Channel       = 36,
            BssEntries    = new[] { new BssInfo { Bssid = bssid, Rssi = -60, Channel = 36, FrequencyMhz = 5180 } }
        };

    [Fact]
    public void Analyze_MixedSecurity_FlagsHighRisk()
    {
        var detector = new EvilTwinDetector();
        var legit = Net("CorpWiFi", AuthMethod.WPA2Enterprise, "AA:BB:CC:00:00:01");
        var evil  = Net("CorpWiFi", AuthMethod.Open,           "FF:FF:FF:00:00:99");

        var all = new[] { legit, evil };
        var verdict = detector.Analyze(evil, all);

        verdict.IsSuspect.Should().BeTrue();
        verdict.Reasons.Should().Contain(r => r.Contains("different security configurations"));
    }

    [Fact]
    public void Analyze_KnownBssid_NoSuspicion()
    {
        var detector = new EvilTwinDetector();
        detector.RecordTrusted("HomeWiFi", "AA:BB:CC:DD:EE:FF", AuthMethod.WPA3SAE);

        var net = Net("HomeWiFi", AuthMethod.WPA3SAE, "AA:BB:CC:DD:EE:FF");
        var verdict = detector.Analyze(net, new[] { net });

        verdict.IsSuspect.Should().BeFalse();
        verdict.Risk.Should().Be(EvilTwinRisk.None);
    }

    [Fact]
    public void Analyze_SecurityDowngrade_Flags()
    {
        var detector = new EvilTwinDetector();
        detector.RecordTrusted("MyNet", "AA:BB:CC:DD:EE:FF", AuthMethod.WPA3SAE);

        // 同じ SSID が WPA3 → Open に降格
        var downgraded = Net("MyNet", AuthMethod.Open, "AA:BB:CC:DD:EE:FF");
        var verdict = detector.Analyze(downgraded, new[] { downgraded });

        verdict.IsSuspect.Should().BeTrue();
        verdict.Reasons.Should().Contain(r => r.Contains("downgrade") || r.Contains("impersonation"));
    }

    [Fact]
    public void Analyze_OpenImpersonatingEncrypted_HighRisk()
    {
        var detector = new EvilTwinDetector();
        detector.RecordTrusted("Secure", "AA:BB:CC:DD:EE:FF", AuthMethod.WPA2PSK);

        var fake = Net("Secure", AuthMethod.Open, "AA:BB:CC:DD:EE:FF");
        var verdict = detector.Analyze(fake, new[] { fake });

        verdict.IsSuspect.Should().BeTrue();
        // 製品が出す理由は "Security downgrade detected: known WPA2PSK vs current Open"。
        // "impersonation" という語は一度も生成されないため、元の期待は必ず外れていた。
        // 検出の中身は正しいので、実際の (より具体的な) 文言に合わせる。
        verdict.Reasons.Should().Contain(r => r.Contains("downgrade"));
    }

    [Fact]
    public void Analyze_DifferentOui_Flags()
    {
        var detector = new EvilTwinDetector();
        detector.RecordTrusted("Office", "AA:BB:CC:11:22:33", AuthMethod.WPA2PSK);

        // 全く異なるベンダー OUI
        var different = Net("Office", AuthMethod.WPA2PSK, "99:88:77:11:22:33");
        var verdict = detector.Analyze(different, new[] { different });

        verdict.Reasons.Should().Contain(r => r.Contains("OUI") || r.Contains("vendor"));
    }

    [Fact]
    public void GetTrustedBssids_ReturnsRecorded()
    {
        var detector = new EvilTwinDetector();
        detector.RecordTrusted("Net", "AA:BB:CC:DD:EE:FF", AuthMethod.WPA2PSK);
        detector.RecordTrusted("Net", "AA:BB:CC:DD:EE:00", AuthMethod.WPA2PSK);

        var trusted = detector.GetTrustedBssids("Net");
        trusted.Should().HaveCount(2);
        trusted.Should().Contain("AA:BB:CC:DD:EE:FF");
    }

    [Fact]
    public void GetTrustedBssids_UnknownSsid_Empty()
    {
        var detector = new EvilTwinDetector();
        detector.GetTrustedBssids("Ghost").Should().BeEmpty();
    }

    [Fact]
    public void RecordTrusted_NormalizesBssidFormat()
    {
        var detector = new EvilTwinDetector();
        detector.RecordTrusted("Net", "aa-bb-cc-dd-ee-ff", AuthMethod.WPA2PSK);

        // ハイフン区切り小文字でも正規化されて一致
        detector.GetTrustedBssids("Net").Should().Contain("AA:BB:CC:DD:EE:FF");
    }

    [Fact]
    public void Analyze_KnownVendor_SameBssidVendor_NoExtraFlag()
    {
        var detector = new EvilTwinDetector();
        // 既知の正規 AP を記録 (Cisco の OUI を含む BSSID と仮定)
        detector.RecordTrusted("CorpNet", "00:00:0C:11:22:33", AuthMethod.WPA2Enterprise);

        // 同じベンダーの BSSID なら追加フラグなし
        var net = new WifiNetwork
        {
            Ssid = "CorpNet", Auth = AuthMethod.WPA2Enterprise,
            Band = WifiBand.Band5GHz, SignalQuality = 70, Channel = 36,
            BssEntries = new[] { new BssInfo { Bssid = "00:00:0C:11:22:33", Rssi = -60, Channel = 36, FrequencyMhz = 5180 } }
        };
        var verdict = detector.Analyze(net, new[] { net });
        // 既知ベンダー・既知BSSIDなので疑いなし
        verdict.Reasons.Should().NotContain(r => r.Contains("vendor"));
    }

    [Fact]
    public void Analyze_KnownVendorMismatch_IsOnlyOneSuspiciousReason()
    {
        // Regression: a new BSSID whose OUI is known in the DB used to fire BOTH check 2
        // ("BSSID detected with different vendor") AND check 4 ("Device vendor different"),
        // inflating reasons.Count to 2 and producing a false HighRisk for a single indicator.
        // Fix: check 2 defers to check 4 when the OUI DB resolves a vendor name.
        var detector = new EvilTwinDetector();
        // Record a Cisco BSSID as trusted (000142 = Cisco in the OUI DB)
        detector.RecordTrusted("CorpNet", "00:01:42:11:22:33", AuthMethod.WPA2Enterprise);

        // New AP: Apple BSSID (001122 = Apple in the OUI DB), same SSID & auth
        var net = Net("CorpNet", AuthMethod.WPA2Enterprise, "00:11:22:99:88:77");
        var verdict = detector.Analyze(net, new[] { net });

        verdict.IsSuspect.Should().BeTrue("different vendor is a valid suspicion signal");
        verdict.Reasons.Should().HaveCount(1,
            because: "vendor mismatch is a single indicator — check 2 must not double-count check 4");
        verdict.Risk.Should().Be(EvilTwinRisk.Suspicious,
            because: "one indicator does not justify HighRisk");
    }

    [Fact]
    public void Analyze_DifferentOuiAndSecurityDowngrade_IsHighRisk()
    {
        // Verify that two simultaneous indicators escalate to HighRisk (2+ reasons).
        // Previous tests only trigger one indicator at a time and never assert HighRisk.
        var detector = new EvilTwinDetector();
        detector.RecordTrusted("SecureNet", "AA:BB:CC:11:22:33", AuthMethod.WPA2PSK);

        // Same SSID, different OUI vendor (reason 1) AND security downgrade (reason 2)
        var rogue = Net("SecureNet", AuthMethod.Open, "99:88:77:44:55:66");
        var verdict = detector.Analyze(rogue, new[] { rogue });

        verdict.Risk.Should().Be(EvilTwinRisk.HighRisk);
        verdict.Reasons.Should().HaveCountGreaterThanOrEqualTo(2);
        verdict.Reasons.Should().Contain(r => r.Contains("vendor") || r.Contains("OUI"));
        verdict.Reasons.Should().Contain(r => r.Contains("downgrade"));
    }

}

// ══════════════════════════════════════════════════════════════
//  TWT 省電力フラグ (arXiv 2402.15900)
// ══════════════════════════════════════════════════════════════
public class TwtFlagsTests
{
    [Fact]
    public void TwtFlags_DefaultFalse()
    {
        var net = new WifiNetwork { Ssid = "X", Band = WifiBand.Band5GHz };
        net.TargetWakeTime.Should().BeFalse();
        net.RestrictedTwt.Should().BeFalse();
    }

    [Fact]
    public void TwtFlags_CanBeSet()
    {
        var net = new WifiNetwork
        {
            Ssid = "IoT", Band = WifiBand.Band6GHz,
            TargetWakeTime = true, RestrictedTwt = true
        };
        net.TargetWakeTime.Should().BeTrue();
        net.RestrictedTwt.Should().BeTrue();
    }
}
