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
    public void OpenNetworkKnownOrNotAlwaysStronglyRecommended()
    {
        _svc.Analyze(Net(AuthMethod.Open), isKnownTrustedNetwork: false)
            .Recommendation.Should().Be(VpnRecommendation.StronglyRecommended);

        _svc.Analyze(Net(AuthMethod.Open), isKnownTrustedNetwork: true)
            .Recommendation.Should().Be(VpnRecommendation.StronglyRecommended);
    }

    [Fact]
    public void UnknownNetworkEvenIfStronglyEncryptedIsRecommended()
    {
        var result = _svc.Analyze(Net(AuthMethod.WPA3SAE), isKnownTrustedNetwork: false);
        result.Recommendation.Should().Be(VpnRecommendation.Recommended);
        result.Reason.Should().Contain("Unfamiliar");
    }

    [Fact]
    public void KnownEnterpriseNetworkIsNotNeeded()
    {
        var result = _svc.Analyze(Net(AuthMethod.WPA2Enterprise), isKnownTrustedNetwork: true);
        result.Recommendation.Should().Be(VpnRecommendation.NotNeeded);
        result.Reason.Should().Contain("enterprise");
    }

    [Fact]
    public void KnownWpa3EnterpriseNetworkIsNotNeeded()
    {
        _svc.Analyze(Net(AuthMethod.WPA3Enterprise), isKnownTrustedNetwork: true)
            .Recommendation.Should().Be(VpnRecommendation.NotNeeded);
    }

    [Fact]
    public void KnownStrongPersonalNetworkWpa3SaeIsOptional()
    {
        var result = _svc.Analyze(Net(AuthMethod.WPA3SAE), isKnownTrustedNetwork: true);
        result.Recommendation.Should().Be(VpnRecommendation.Optional);
    }

    [Fact]
    public void KnownWpa3TransitionModeIsStillRecommended()
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
    public void KnownWeakerPersonalNetworkIsRecommended(AuthMethod auth)
    {
        _svc.Analyze(Net(auth), isKnownTrustedNetwork: true)
            .Recommendation.Should().Be(VpnRecommendation.Recommended);
    }

    [Fact]
    public void KnownOweNetworkIsRecommended()
    {
        // OWE encrypts opportunistically but provides no authentication of the AP identity,
        // so it should not be treated as "strong personal" even when marked known/trusted.
        _svc.Analyze(Net(AuthMethod.OWE), isKnownTrustedNetwork: true)
            .Recommendation.Should().Be(VpnRecommendation.Recommended);
    }

    // ── Captive portal ───────────────────────────────────────────────
    // A captive portal is access control, not encryption. Networks that have one are
    // overwhelmingly shared environments (hotels, airports, cafés), the portal itself is
    // often plain HTTP, and a rogue portal imitating the real one is a known way to harvest
    // credentials. So the portal state dominates the auth-method reasoning.

    [Fact]
    public void BehindCaptivePortalIsStronglyRecommendedEvenOnEncryptedNetwork()
    {
        _svc.Analyze(Net(AuthMethod.WPA2PSK), isKnownTrustedNetwork: true,
                     behindCaptivePortal: true)
            .Recommendation.Should().Be(VpnRecommendation.StronglyRecommended);
    }

    [Fact]
    public void BehindCaptivePortalOverridesEnterpriseNotNeeded()
    {
        // The key regression this guards: rule 3 says a known enterprise network needs no
        // personal VPN because traffic "already routes through the organisation's
        // firewall/VPN". That premise does not hold while the connection is still captured
        // by a portal — so the portal check must be evaluated first.
        var withoutPortal = _svc.Analyze(
            Net(AuthMethod.WPA2Enterprise), isKnownTrustedNetwork: true);
        withoutPortal.Recommendation.Should().Be(VpnRecommendation.NotNeeded);

        var withPortal = _svc.Analyze(
            Net(AuthMethod.WPA2Enterprise), isKnownTrustedNetwork: true,
            behindCaptivePortal: true);
        withPortal.Recommendation.Should().Be(VpnRecommendation.StronglyRecommended,
            because: "being behind a portal invalidates the 'already behind the corporate " +
                     "firewall' assumption");
    }

    [Fact]
    public void BehindCaptivePortalOverridesStrongWpa3Optional()
    {
        // Same reasoning for the "strong encryption, VPN optional" case: link-layer
        // encryption does not protect what the portal operator (or an impostor) sees.
        _svc.Analyze(Net(AuthMethod.WPA3SAE), isKnownTrustedNetwork: true,
                     behindCaptivePortal: true)
            .Recommendation.Should().Be(VpnRecommendation.StronglyRecommended);
    }

    [Fact]
    public void CaptivePortalAdviceExplainsWhyRatherThanJustAsserting()
    {
        var advice = _svc.Analyze(Net(AuthMethod.WPA3SAE), isKnownTrustedNetwork: true,
                                  behindCaptivePortal: true);

        advice.Reason.Should().Contain("captive portal");
        advice.Reason.Should().Contain("not encryption",
            because: "users need the reason, not just the verdict — this is advisory-only");
    }

    [Fact]
    public void DefaultingCaptivePortalToFalsePreservesExistingBehaviour()
    {
        // The parameter is optional so existing call sites (NetworkDetailViewModel,
        // mwc vpn-advice) keep compiling and behaving identically. Scans cannot know
        // portal state — it is only observable after connecting — so false is correct there.
        _svc.Analyze(Net(AuthMethod.WPA2Enterprise), isKnownTrustedNetwork: true)
            .Recommendation.Should().Be(
                _svc.Analyze(Net(AuthMethod.WPA2Enterprise), isKnownTrustedNetwork: true,
                             behindCaptivePortal: false).Recommendation);
    }
}
