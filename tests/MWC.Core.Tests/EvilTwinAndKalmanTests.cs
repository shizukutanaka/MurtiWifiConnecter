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
        verdict.Reasons.Should().Contain(r => r.Contains("impersonation"));
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
//  KalmanRssiFilter — カルマンフィルタ RSSI 平滑化
// ══════════════════════════════════════════════════════════════
public class KalmanRssiFilterTests
{
    [Fact]
    public void Update_FirstSample_ReturnsItself()
    {
        var kf = new KalmanRssiFilter();
        kf.Update(-55).Should().Be(-55);
        kf.SampleCount.Should().Be(1);
    }

    [Fact]
    public void Update_StableSignal_ConvergesAndReducesUncertainty()
    {
        var kf = new KalmanRssiFilter();
        for (int i = 0; i < 20; i++) kf.Update(-50);

        kf.Current.Should().NotBeNull();
        kf.Current!.Value.Should().BeApproximately(-50, 1.0);
        // 安定した観測で不確かさが下がる
        kf.Uncertainty.Should().BeLessThan(1.0);
    }

    [Fact]
    public void Update_NoisySignal_SmoothsOutNoise()
    {
        var kf = new KalmanRssiFilter();
        // -50 中心にノイズが乗った信号
        var noisy = new[] { -50, -45, -55, -48, -52, -47, -53, -50, -49, -51 };
        double last = 0;
        foreach (var z in noisy) last = kf.Update(z);

        // 平滑化値はノイズの範囲内で中心付近に収まる
        last.Should().BeInRange(-53, -47);
    }

    [Fact]
    public void Update_StepChange_TracksNewLevel()
    {
        var kf = new KalmanRssiFilter();
        for (int i = 0; i < 15; i++) kf.Update(-70);
        // 信号が急に強くなる
        for (int i = 0; i < 15; i++) kf.Update(-40);

        // 新しいレベルに追従
        kf.Current!.Value.Should().BeApproximately(-40, 5.0);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var kf = new KalmanRssiFilter();
        kf.Update(-50);
        kf.Reset();
        kf.Current.Should().BeNull();
        kf.SampleCount.Should().Be(0);
    }

    [Fact]
    public void Update_HighProcessNoise_RespondsFaster()
    {
        var responsive = new KalmanRssiFilter(processNoise: 5.0, measurementNoise: 2.0);
        var sluggish   = new KalmanRssiFilter(processNoise: 0.1, measurementNoise: 10.0);

        foreach (var kf in new[] { responsive, sluggish })
            for (int i = 0; i < 10; i++) kf.Update(-70);

        responsive.Update(-40);
        sluggish.Update(-40);

        // 高プロセスノイズの方が新しい値に速く反応
        responsive.Current!.Value.Should().BeGreaterThan(sluggish.Current!.Value);
    }

    [Theory]
    [InlineData(0.0)]    // R=0 は収束後にゲイン 0/0 = NaN を生む
    [InlineData(-1.0)]
    public void Ctor_NonPositiveMeasurementNoise_Throws(double r)
    {
        var act = () => new KalmanRssiFilter(measurementNoise: r);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Ctor_NegativeProcessNoise_Throws()
    {
        var act = () => new KalmanRssiFilter(processNoise: -0.1);
        act.Should().Throw<ArgumentOutOfRangeException>();
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
