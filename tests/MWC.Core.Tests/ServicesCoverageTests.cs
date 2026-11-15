using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ═══════════════════════════════════════════════════════════════
//  OweSelectionService
// ═══════════════════════════════════════════════════════════════
public class OweSelectionServiceTests
{
    private readonly OweSelectionService _svc = new();

    private static WifiNetwork Net(string ssid, AuthMethod auth, int signal = 70)
        => new() { Ssid = ssid, Auth = auth, SignalQuality = signal, Band = WifiBand.Band5GHz };

    [Fact]
    public void ApplyOwePreferenceNoOweReturnsSameCount()
    {
        var nets = new[] { Net("A", AuthMethod.Open), Net("B", AuthMethod.WPA2PSK) };
        var result = _svc.ApplyOwePreference(nets);
        result.Should().HaveCount(2);
        result.Select(n => n.Ssid).Should().Contain("A").And.Contain("B");
    }

    [Fact]
    public void ApplyOwePreferenceOweReplacesOpen()
    {
        var nets = new[]
        {
            Net("FreeWifi", AuthMethod.Open,     80),
            Net("FreeWifi", AuthMethod.OWE,      78),
            Net("Home",     AuthMethod.WPA2PSK,  90),
        };
        var result = _svc.ApplyOwePreference(nets);
        result.Should().HaveCount(2);
        result.Any(n => n.Auth == AuthMethod.Open  && n.Ssid == "FreeWifi").Should().BeFalse();
        result.Any(n => n.Auth == AuthMethod.OWE   && n.Ssid == "FreeWifi").Should().BeTrue();
        result.Any(n => n.Ssid == "Home").Should().BeTrue();
    }

    [Fact]
    public void RecommendAuthOpenWithOweReturnsOwe()
    {
        var open = Net("Cafe", AuthMethod.Open);
        var owe  = Net("Cafe", AuthMethod.OWE);
        _svc.RecommendAuth(open, new[] { open, owe }).Should().Be(AuthMethod.OWE);
        _svc.RecommendAuth(owe,  new[] { open, owe }).Should().Be(AuthMethod.OWE);
    }

    [Fact]
    public void RecommendAuthOpenWithoutOweReturnsOpen()
    {
        var open = Net("Public", AuthMethod.Open);
        _svc.RecommendAuth(open, new[] { open }).Should().Be(AuthMethod.Open);
    }

    [Fact]
    public void BuildOweSpecGeneratesCorrectSpec()
    {
        var spec = _svc.BuildOweSpec("TestNet");
        spec.Ssid.Should().Be("TestNet");
        spec.Auth.Should().Be(AuthMethod.OWE);
        spec.Passphrase.Should().BeNullOrEmpty();
    }
}

// ═══════════════════════════════════════════════════════════════
//  CatImportService  (extended)
// ═══════════════════════════════════════════════════════════════
public class CatImportServiceExtendedTests
{
    private readonly CatImportService _svc = new();

    [Fact]
    public void ParseEapConfigMissingProvidersThrows()
    {
        var xml = "<?xml version='1.0'?><EAPIdentityProviderList></EAPIdentityProviderList>";
        Action act = () => _svc.ParseEapConfig(xml);
        act.Should().Throw<FormatException>().WithMessage("*EAPIdentityProvider*");
    }

    [Fact]
    public void ParseEapConfigInvalidXmlThrows()
    {
        Action act = () => _svc.ParseEapConfig("<broken xml");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ParseEapConfigXxeExternalEntityRejected()
    {
        // 信頼できない CAT XML に仕込んだ外部実体 (ローカルファイル漏洩 XXE) は、
        // DtdProcessing.Prohibit が <!DOCTYPE> 時点で拒否するため実体解決に到達しない。
        const string xxe = """
            <?xml version="1.0"?>
            <!DOCTYPE foo [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
            <EAPIdentityProviderList>
              <EAPIdentityProvider><SSID>&xxe;</SSID></EAPIdentityProvider>
            </EAPIdentityProviderList>
            """;
        Action act = () => _svc.ParseEapConfig(xxe);
        act.Should().Throw<FormatException>("a DOCTYPE must be prohibited before any entity is resolved");
    }

    [Fact]
    public void ParseEapConfigEntityExpansionDtdRejected()
    {
        // billion laughs (実体展開 DoS) も <!DOCTYPE> 拒否で封じられる。
        const string bomb = """
            <?xml version="1.0"?>
            <!DOCTYPE lolz [
              <!ENTITY lol "lol">
              <!ENTITY lol2 "&lol;&lol;&lol;&lol;&lol;">
            ]>
            <EAPIdentityProviderList>&lol2;</EAPIdentityProviderList>
            """;
        Action act = () => _svc.ParseEapConfig(bomb);
        act.Should().Throw<FormatException>("DTD entity expansion must be prohibited");
    }

    [Fact]
    public void BuildEduroamSpecIsValidProfile()
    {
        const string xml = """
            <EAPIdentityProviderList>
              <EAPIdentityProvider>
                <SSID>eduroam</SSID>
                <AuthenticationMethods>
                  <AuthenticationMethod><EAPMethod><Type>25</Type></EAPMethod></AuthenticationMethod>
                </AuthenticationMethods>
                <CredentialApplicability>
                  <IEEE80211><ServerName>radius.test.ac.jp</ServerName></IEEE80211>
                </CredentialApplicability>
                <ProviderInfo><DisplayName>Test Univ</DisplayName></ProviderInfo>
              </EAPIdentityProvider>
            </EAPIdentityProviderList>
            """;

        var profiles = _svc.ParseEapConfig(xml);
        profiles.Should().HaveCount(1);
        var spec = _svc.BuildEduroamSpec(profiles[0]);
        spec.Ssid.Should().Be("eduroam");
        spec.Auth.Should().Be(AuthMethod.WPA2Enterprise);
        spec.EapType.Should().Be(EapType.PEAP_MSCHAPv2);
        spec.ServerNames.Should().Contain("radius.test.ac.jp");
    }
}

// ═══════════════════════════════════════════════════════════════
//  RegulatoryDomainService  (extended)
// ═══════════════════════════════════════════════════════════════
public class RegulatoryDomainServiceExtendedTests
{
    private readonly RegulatoryDomainService _svc = new();

    [Fact]
    public void FullBandCountriesHaveMoreChannelsThanLowerHalf()
    {
        var usCh  = _svc.GetAvailable6GHzChannels("US");
        var euCh  = _svc.GetAvailable6GHzChannels("DE");
        usCh.Count.Should().BeGreaterThan(euCh.Count);
        euCh.All(c => c.Channel <= 93).Should().BeTrue();
    }

    [Fact]
    public void ChannelFrequencyFollowsStandard()
    {
        var ch5   = _svc.GetAvailable6GHzChannels("US").First(c => c.Channel == 5);
        // ch5: 5950 + 5×5 = 5975 MHz  (IEEE 802.11ax-2021 §27.3.23.2)
        ch5.FrequencyMhz.Should().Be(5975);
        ch5.FrequencyGHz.Should().BeApproximately(5.975, 0.001);
        ch5.IsPsc.Should().BeTrue();
        ch5.MaxWidthMhz.Should().BeGreaterOrEqualTo(20);
    }

    [Fact]
    public void AllRegionsReturnedByAllRegionsProperty()
    {
        _svc.AllRegions.Count.Should().BeGreaterOrEqualTo(15);
        _svc.AllRegions.Any(r => r.CountryCode == "US").Should().BeTrue();
        _svc.AllRegions.Any(r => r.CountryCode == "JP").Should().BeTrue();
        _svc.AllRegions.Any(r => r.CountryCode == "CN").Should().BeTrue();
    }

    [Fact]
    public void IsChannelLegalValidChannelUSReturnsTrue()
    {
        _svc.IsChannelLegal(5,   "US").Should().BeTrue();
        _svc.IsChannelLegal(233, "US").Should().BeTrue();
        _svc.IsChannelLegal(93,  "DE").Should().BeTrue();
        _svc.IsChannelLegal(97,  "DE").Should().BeFalse("EU limit is ch93");
        _svc.IsChannelLegal(1,   "CN").Should().BeFalse("China bans 6GHz");
    }
}

// ═══════════════════════════════════════════════════════════════
//  Hotspot20Service
// ═══════════════════════════════════════════════════════════════
public class Hotspot20ServiceTests
{
    private static readonly string[] RoamingOi = new[] { "001BC5" };

    private readonly Hotspot20Service _svc = new();

    [Fact]
    public void KnownCarriersHasJapaneseAndUS()
    {
        Hotspot20Service.KnownCarriers.Should().NotBeEmpty();
        Hotspot20Service.KnownCarriers.Any(c => c.CarrierName.Contains("au")).Should().BeTrue();
        Hotspot20Service.KnownCarriers.Any(c => c.CarrierName.Contains("SoftBank")).Should().BeTrue();
        Hotspot20Service.KnownCarriers.Any(c => c.CarrierName.Contains("AT&T")).Should().BeTrue();
        Hotspot20Service.KnownCarriers.Count.Should().BeGreaterOrEqualTo(5);
    }

    [Fact]
    public void BuildCarrierProfileAuGeneratesSpec()
    {
        var au = Hotspot20Service.KnownCarriers.First(c => c.CarrierName == "au Wi-Fi");
        var spec = _svc.BuildCarrierProfile(au);
        spec.Ssid.Should().Be(au.Ssid);
        spec.Auth.Should().BeOneOf(AuthMethod.WPA2Enterprise, AuthMethod.WPA3Enterprise);
        spec.EapType.Should().Be(EapType.EAP_AKA);
    }

    [Fact]
    public void BuildProfileGeneratesWithHomeOi()
    {
        var p = _svc.BuildProfile("001DE0", "au.kddi.com", EapType.EAP_AKA,
            roamingOis: RoamingOi);
        p.HomeOI.Should().Be("001DE0");
        p.Domain.Should().Be("au.kddi.com");
        p.EapType.Should().Be(EapType.EAP_AKA);
        p.RoamingOIs.Should().Contain("001BC5");
    }

    [Fact]
    public void FilterPasspointNetworksNoPasspointNetsReturnsEmpty()
    {
        var nets = new[]
        {
            new WifiNetwork { Ssid = "Home", Auth = AuthMethod.WPA2PSK, Band = WifiBand.Band5GHz },
            new WifiNetwork { Ssid = "Open", Auth = AuthMethod.Open,    Band = WifiBand.Band5GHz },
        };
        var result = _svc.FilterPasspointNetworks(nets);
        result.Should().BeEmpty("No Interworking IE = no Passpoint");
    }
}

// ═══════════════════════════════════════════════════════════════
public class CertificateStoreServiceTests
{
    private static readonly string[] RadiusCorpCom = new[] { "radius.corp.com" };

    private readonly CertificateStoreService _svc = new();

    [Fact]
    public void GetClientCertificatesReturnsListWithoutException()
    {
        // 実機証明書ストアへのアクセス(CIでは空リストで正常)
        var act = () => _svc.GetClientCertificates();
        act.Should().NotThrow();
    }

    [Fact]
    public void FindByThumbprintNonExistentReturnsNull()
    {
        var result = _svc.FindByThumbprint("DEADBEEF0000000000000000000000000000CAFE");
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateRadiusCertInvalidDerBytesReturnsFail()
    {
        var result = _svc.ValidateRadiusCert(new byte[] { 0x00, 0x01, 0x02 });
        result.IsValid.Should().BeFalse();
        result.Summary.Should().NotBeNullOrEmpty();
        result.Detail.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void BuildEapTlsSpecGeneratesCorrectSpec()
    {
        var certInfo = new ClientCertInfo(
            Subject:      "CN=test",
            Thumbprint:   "ABCDEF123456",
            Issuer:       "CN=TestCA",
            NotBefore:    DateTime.UtcNow.AddDays(-10),
            NotAfter:     DateTime.UtcNow.AddDays(365),
            HasPrivateKey: true,
            SubjectAltNames: Array.Empty<string>(),
            FriendlyName: "Test Cert");

        var spec = _svc.BuildEapTlsSpec("CorpWifi", certInfo,
            serverNames: RadiusCorpCom);

        spec.Ssid.Should().Be("CorpWifi");
        spec.Auth.Should().Be(AuthMethod.WPA2Enterprise);
        spec.EapType.Should().Be(EapType.EAP_TLS);
        spec.ClientCertThumbprint.Should().Be("ABCDEF123456");
        spec.ServerNames.Should().Contain("radius.corp.com");
    }

    [Fact]
    public void ClientCertInfoDaysUntilExpiryIsPositive()
    {
        var cert = new ClientCertInfo(
            "CN=x", "AA", "CN=CA",
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(90),
            true, Array.Empty<string>(), "x");
        cert.DaysUntilExpiry.Should().BeInRange(89, 91);
        cert.DisplayLabel.Should().Be("x");
    }
}
