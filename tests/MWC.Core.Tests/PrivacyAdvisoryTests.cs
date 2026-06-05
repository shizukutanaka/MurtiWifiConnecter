using System.Linq;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  PrivacyAdvisoryService — MAC ランダム化 / プローブ追跡の診断
//  (arXiv: 2206.10927, 2412.10548, 1703.02874)
// ══════════════════════════════════════════════════════════════
public class PrivacyAdvisoryServiceTests
{
    private readonly PrivacyAdvisoryService _svc = new();

    private static WifiNetwork Net(AuthMethod auth) =>
        new()
        {
            Ssid          = "TestNet",
            Auth          = auth,
            Band          = WifiBand.Band5GHz,
            SignalQuality = 80
        };

    [Fact]
    public void HardwareMac_OnPublicNetwork_WarnsTracking()
    {
        var advisories = _svc.Analyze(MacAddressMode.Hardware, Net(AuthMethod.Open));

        advisories.Should().Contain(a => a.Code == "MWC-PRIV-001" && a.Severity == AdvisorySeverity.Warning);
        advisories.Should().NotContain(a => a.Code == "MWC-PRIV-002"); // 公共時は 001 のみ
        advisories.First(a => a.Code == "MWC-PRIV-001").Reference.Should().Contain("2206.10927");
    }

    [Fact]
    public void HardwareMac_OnPrivateNetwork_InfoRecommendRandomization()
    {
        var advisories = _svc.Analyze(MacAddressMode.Hardware, Net(AuthMethod.WPA2PSK));

        advisories.Should().Contain(a => a.Code == "MWC-PRIV-002" && a.Severity == AdvisorySeverity.Info);
        advisories.Should().NotContain(a => a.Code == "MWC-PRIV-001");
    }

    [Fact]
    public void RandomPerNetwork_SuggestsDailyRotation_AndFingerprintNote()
    {
        var advisories = _svc.Analyze(MacAddressMode.RandomPerNetwork, Net(AuthMethod.WPA3SAE));

        advisories.Should().Contain(a => a.Code == "MWC-PRIV-003");
        advisories.Should().Contain(a => a.Code == "MWC-PRIV-004"); // 指紋による限界の教育的情報
        advisories.Should().NotContain(a => a.Code == "MWC-PRIV-001" || a.Code == "MWC-PRIV-002");
    }

    [Fact]
    public void RandomDaily_IsGood_WithFingerprintNote()
    {
        var advisories = _svc.Analyze(MacAddressMode.RandomDaily, Net(AuthMethod.WPA3SAE));

        advisories.Should().Contain(a => a.Code == "MWC-PRIV-100" && a.Severity == AdvisorySeverity.Good);
        advisories.Should().Contain(a => a.Code == "MWC-PRIV-004");
    }

    [Fact]
    public void UnknownMode_ProducesNoAdvisories()
    {
        var advisories = _svc.Analyze(MacAddressMode.Unknown, Net(AuthMethod.Open));
        advisories.Should().BeEmpty();
    }
}
