using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  VpnAdvisoryService — VPN 使用推奨度の判定
//  (ROADMAP.md 「VPN 自動切替(信頼済み AP では VPN オフ)」の助言基盤)
//  本サービスは助言のみを返し、VPN 状態を一切変更しない。
// ══════════════════════════════════════════════════════════════
public class VpnAdvisoryServiceTests
{
    private readonly VpnAdvisoryService _svc = new();

    private static WifiNetwork Net(AuthMethod auth, bool transition = false) =>
        new()
        {
            Ssid                  = "TestNet",
            Auth                  = auth,
            Band                  = WifiBand.Band5GHz,
            SignalQuality         = 80,
            IsWpa3TransitionMode  = transition
        };

    [Fact]
    public void OpenNetwork_KnownOrNot_AlwaysStronglyRecommended()
    {
        _svc.Analyze(Net(AuthMethod.Open), isKnownTrustedNetwork: false)
            .Recommendation.Should().Be(VpnRecommendation.StronglyRecommended);

        _svc.Analyze(Net(AuthMethod.Open), isKnownTrustedNetwork: true)
            .Recommendation.Should().Be(VpnRecommendation.StronglyRecommended);
    }

    [Fact]
    public void UnknownNetwork_EvenIfStronglyEncrypted_IsRecommended()
    {
        var result = _svc.Analyze(Net(AuthMethod.WPA3SAE), isKnownTrustedNetwork: false);
        result.Recommendation.Should().Be(VpnRecommendation.Recommended);
        result.Reason.Should().Contain("Unfamiliar");
    }

    [Fact]
    public void KnownEnterpriseNetwork_IsNotNeeded()
    {
        var result = _svc.Analyze(Net(AuthMethod.WPA2Enterprise), isKnownTrustedNetwork: true);
        result.Recommendation.Should().Be(VpnRecommendation.NotNeeded);
        result.Reason.Should().Contain("enterprise");
    }

    [Fact]
    public void KnownWpa3EnterpriseNetwork_IsNotNeeded()
    {
        _svc.Analyze(Net(AuthMethod.WPA3Enterprise), isKnownTrustedNetwork: true)
            .Recommendation.Should().Be(VpnRecommendation.NotNeeded);
    }

    [Fact]
    public void KnownStrongPersonalNetwork_Wpa3Sae_IsOptional()
    {
        var result = _svc.Analyze(Net(AuthMethod.WPA3SAE), isKnownTrustedNetwork: true);
        result.Recommendation.Should().Be(VpnRecommendation.Optional);
    }

    [Fact]
    public void KnownWpa3TransitionMode_IsStillRecommended()
    {
        // Dragonblood downgrade risk — transition mode should not get the "Optional" pass
        // even though the reported Auth is WPA3SAE.
        var result = _svc.Analyze(Net(AuthMethod.WPA3SAE, transition: true), isKnownTrustedNetwork: true);
        result.Recommendation.Should().Be(VpnRecommendation.Recommended);
    }

    [Theory]
    [InlineData(AuthMethod.WPA2PSK)]
    [InlineData(AuthMethod.WPAPSK)]
    [InlineData(AuthMethod.WEP)]
    public void KnownWeakerPersonalNetwork_IsRecommended(AuthMethod auth)
    {
        _svc.Analyze(Net(auth), isKnownTrustedNetwork: true)
            .Recommendation.Should().Be(VpnRecommendation.Recommended);
    }

    [Fact]
    public void KnownOweNetwork_IsRecommended()
    {
        // OWE encrypts opportunistically but provides no authentication of the AP identity,
        // so it should not be treated as "strong personal" even when marked known/trusted.
        _svc.Analyze(Net(AuthMethod.OWE), isKnownTrustedNetwork: true)
            .Recommendation.Should().Be(VpnRecommendation.Recommended);
    }
}
