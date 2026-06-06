using System.Xml.Linq;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Profile;
using Xunit;

namespace MWC.Core.Tests;

public class ProfileXmlBuilderTests
{
    [Fact]
    public void Open_NoSharedKey()
    {
        var xml = ProfileXmlBuilder.Build(new() { Ssid = "FreeWiFi", Auth = AuthMethod.Open });

        var doc = XDocument.Parse(xml);
        var ns = (XNamespace)"http://www.microsoft.com/networking/WLAN/profile/v1";
        var auth = doc.Descendants(ns + "authentication").Single().Value;
        var enc  = doc.Descendants(ns + "encryption").Single().Value;
        auth.Should().Be("open");
        enc.Should().Be("none");
        doc.Descendants(ns + "sharedKey").Should().BeEmpty();
    }

    [Fact]
    public void OWE_HasAES_NoSharedKey()
    {
        var xml = ProfileXmlBuilder.Build(new() { Ssid = "OpenSecure", Auth = AuthMethod.OWE });
        var doc = XDocument.Parse(xml);
        var ns = (XNamespace)"http://www.microsoft.com/networking/WLAN/profile/v1";
        doc.Descendants(ns + "authentication").Single().Value.Should().Be("OWE");
        doc.Descendants(ns + "encryption").Single().Value.Should().Be("AES");
        doc.Descendants(ns + "sharedKey").Should().BeEmpty();
    }

    [Fact]
    public void WPA2PSK_HasPassphrase()
    {
        var xml = ProfileXmlBuilder.Build(new()
        {
            Ssid = "Home-WiFi",
            Auth = AuthMethod.WPA2PSK,
            Passphrase = "correct horse battery staple"
        });
        var doc = XDocument.Parse(xml);
        var ns = (XNamespace)"http://www.microsoft.com/networking/WLAN/profile/v1";
        doc.Descendants(ns + "authentication").Single().Value.Should().Be("WPA2PSK");
        doc.Descendants(ns + "keyMaterial").Single().Value.Should().Be("correct horse battery staple");
        doc.Descendants(ns + "keyType").Single().Value.Should().Be("passPhrase");
    }

    [Fact]
    public void WPA3SAE_AuthIsCorrect()
    {
        var xml = ProfileXmlBuilder.Build(new()
        {
            Ssid = "Modern-WiFi",
            Auth = AuthMethod.WPA3SAE,
            Passphrase = "supersecret123"
        });
        var doc = XDocument.Parse(xml);
        var ns = (XNamespace)"http://www.microsoft.com/networking/WLAN/profile/v1";
        doc.Descendants(ns + "authentication").Single().Value.Should().Be("WPA3SAE");
    }

    [Fact]
    public void WEP_HexKey_UsesNetworkKey()
    {
        var xml = ProfileXmlBuilder.Build(new()
        {
            Ssid = "Old",
            Auth = AuthMethod.WEP,
            Passphrase = "0123456789"   // 10桁hex
        });
        var doc = XDocument.Parse(xml);
        var ns = (XNamespace)"http://www.microsoft.com/networking/WLAN/profile/v1";
        doc.Descendants(ns + "keyType").Single().Value.Should().Be("networkKey");
        doc.Descendants(ns + "encryption").Single().Value.Should().Be("WEP");
    }

    [Fact]
    public void Enterprise_PEAP_UseOneXIsTrue()
    {
        var xml = ProfileXmlBuilder.Build(new()
        {
            Ssid = "Corp",
            Auth = AuthMethod.WPA2Enterprise,
            EapType = EapType.PEAP_MSCHAPv2,
            Username = "alice",
            Password = "p",
            ServerNames = new[] { "radius.example.com" },
            TrustedRootCaThumbprints = new[] { "ABCDEF1234567890" }
        });
        var doc = XDocument.Parse(xml);
        var ns = (XNamespace)"http://www.microsoft.com/networking/WLAN/profile/v1";
        doc.Descendants(ns + "useOneX").Single().Value.Should().Be("true");
        xml.Should().Contain("OneX");
        xml.Should().Contain("EapHostConfig");
    }

    [Fact]
    public void Enterprise_192_UsesGCMP256()
    {
        var xml = ProfileXmlBuilder.Build(new()
        {
            Ssid = "GovNet",
            Auth = AuthMethod.WPA3Enterprise192,
            EapType = EapType.EAP_TLS,
            ClientCertThumbprint = "ABCDEF1234567890ABCDEF1234567890ABCDEF12",
            ServerNames = new[] { "radius.gov.example" },
            TrustedRootCaThumbprints = new[] { "DEADBEEF" }
        });
        var doc = XDocument.Parse(xml);
        var ns = (XNamespace)"http://www.microsoft.com/networking/WLAN/profile/v1";
        doc.Descendants(ns + "authentication").Single().Value.Should().Be("WPA3ENT192");
        doc.Descendants(ns + "encryption").Single().Value.Should().Be("GCMP256");
    }

    [Fact]
    public void Enterprise_TTLS_BuildsEapTtlsConfig()
    {
        var xml = ProfileXmlBuilder.Build(new()
        {
            Ssid = "Campus",
            Auth = AuthMethod.WPA2Enterprise,
            EapType = EapType.EAP_TTLS,
            Username = "student",
            Password = "p@ss",
            ServerNames = new[] { "radius.uni.example" },
            TrustedRootCaThumbprints = new[] { "ABCDEF1234567890" }
        });
        var doc = XDocument.Parse(xml);
        var ns = (XNamespace)"http://www.microsoft.com/networking/WLAN/profile/v1";
        var ttlsNs = (XNamespace)"http://www.microsoft.com/provisioning/EapTtlsConnectionPropertiesV1";

        doc.Descendants(ns + "useOneX").Single().Value.Should().Be("true");
        // EapMethod Type は 21 (EAP-TTLS)
        var ecNs = (XNamespace)"http://www.microsoft.com/provisioning/EapCommon";
        doc.Descendants(ecNs + "Type").First().Value.Should().Be("21");
        // TTLS 固有要素が生成される
        doc.Descendants(ttlsNs + "EapTtls").Should().ContainSingle();
        doc.Descendants(ttlsNs + "MSCHAPv2Authentication").Should().ContainSingle();
        doc.Descendants(ttlsNs + "TrustedRootCAHash").Single().Value.Should().Be("ABCDEF1234567890");
    }

    [Fact]
    public void Enterprise_AKA_IsRejected()
    {
        var act = () => ProfileXmlBuilder.Build(new()
        {
            Ssid = "Carrier",
            Auth = AuthMethod.WPA2Enterprise,
            EapType = EapType.EAP_AKA
        });
        act.Should().Throw<System.ArgumentException>();
    }

    [Theory]
    [InlineData("Has\"Quote", "")]
    [InlineData("",           "")]
    [InlineData("OK",         "tooshort")]
    public void Validation_Rejects_Invalid(string ssid, string pw)
    {
        var act = () => ProfileXmlBuilder.Build(new()
        {
            Ssid = ssid,
            Auth = AuthMethod.WPA2PSK,
            Passphrase = pw
        });
        act.Should().Throw<System.ArgumentException>();
    }

    [Fact]
    public void Injection_AttemptIsEscaped()
    {
        // SSID内に閉じタグ含めても XElement が自動エスケープ
        var xml = ProfileXmlBuilder.Build(new()
        {
            Ssid = "Evil&<>",
            Auth = AuthMethod.WPA2PSK,
            Passphrase = "12345678"
        });
        // ロードして例外なし=構造破壊なし
        var doc = XDocument.Parse(xml);
        var ns = (XNamespace)"http://www.microsoft.com/networking/WLAN/profile/v1";
        doc.Descendants(ns + "name").First().Value.Should().Be("Evil&<>");
    }

    [Fact]
    public void NonBroadcast_AddsHiddenFlag()
    {
        var xml = ProfileXmlBuilder.Build(new()
        {
            Ssid = "Hidden",
            Auth = AuthMethod.WPA2PSK,
            Passphrase = "12345678",
            NonBroadcast = true
        });
        xml.Should().Contain("nonBroadcast");
    }

    [Fact]
    public void ManualConnection_SetsManual()
    {
        var xml = ProfileXmlBuilder.Build(new()
        {
            Ssid = "OnDemand",
            Auth = AuthMethod.Open,
            AutoConnect = false
        });
        var doc = XDocument.Parse(xml);
        var ns = (XNamespace)"http://www.microsoft.com/networking/WLAN/profile/v1";
        doc.Descendants(ns + "connectionMode").Single().Value.Should().Be("manual");
    }
}
