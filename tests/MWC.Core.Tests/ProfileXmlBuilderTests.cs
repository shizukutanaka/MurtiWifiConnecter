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
    [InlineData("OK",         "short")]   // 5 文字 < 8 → 無効
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

    // ── 回帰: XElement(name, strayXName, content) 誤用で transitionMode /
    //    PerformServerValidation が壊れた値になっていたバグ ──
    [Fact]
    public void Wpa3Transition_EmitsWellFormedTransitionModeInV4Namespace()
    {
        var xml = ProfileXmlBuilder.Build(new()
        {
            Ssid = "Mixed-WiFi",
            Auth = AuthMethod.WPA3Transition,
            Passphrase = "supersecret123"
        });
        var doc = XDocument.Parse(xml);
        var v4 = (XNamespace)"http://www.microsoft.com/networking/WLAN/profile/v4";

        var tm = doc.Descendants(v4 + "transitionMode").Single();
        // 値はちょうど "true" — 名前空間文字列が混入していないこと (旧バグの検出)
        tm.Value.Should().Be("true");
        xml.Should().NotContain("{http://", "XName が要素内容に混入してはならない");
    }

    [Fact]
    public void EapTls_EmitsV2ServerValidationElements()
    {
        var xml = ProfileXmlBuilder.Build(new()
        {
            Ssid = "TlsNet",
            Auth = AuthMethod.WPA2Enterprise,
            EapType = EapType.EAP_TLS,
            ClientCertThumbprint = "ABCDEF1234567890ABCDEF1234567890ABCDEF12",
            ServerNames = new[] { "radius.example" },
            TrustedRootCaThumbprints = new[] { "DEADBEEF" }
        });
        var doc = XDocument.Parse(xml);
        var v2 = (XNamespace)"http://www.microsoft.com/provisioning/EapTlsConnectionPropertiesV2";

        doc.Descendants(v2 + "PerformServerValidation").Single().Value.Should().Be("true");
        doc.Descendants(v2 + "AcceptServerName").Single().Value.Should().Be("true");
        xml.Should().NotContain("{http://", "XName が要素内容に混入してはならない");
    }

    // ── WEP キー長検証 (5/13 ASCII or 10/26 hex) ──
    [Fact]
    public void Wep_5CharAsciiKey_Accepted()
    {
        var xml = ProfileXmlBuilder.Build(new()
        {
            Ssid = "LegacyWep", Auth = AuthMethod.WEP, Passphrase = "abcde"
        });
        var ns = (XNamespace)"http://www.microsoft.com/networking/WLAN/profile/v1";
        XDocument.Parse(xml).Descendants(ns + "keyType").Single().Value.Should().Be("passPhrase");
    }

    [Theory]
    [InlineData("abc")]                                  // 3 chars — invalid length
    [InlineData("abcdefg")]                              // 7 chars — invalid length
    [InlineData("01234567890123456789012345678901")]      // 32 hex — not a valid WEP length
    public void Wep_InvalidLengthKey_Rejected(string key)
    {
        var act = () => ProfileXmlBuilder.Build(new()
        {
            Ssid = "LegacyWep", Auth = AuthMethod.WEP, Passphrase = key
        });
        act.Should().Throw<System.ArgumentException>();
    }

    [Fact]
    public void Wep_13CharAsciiKey_Accepted()
    {
        var xml = ProfileXmlBuilder.Build(new()
        {
            Ssid = "LegacyWep", Auth = AuthMethod.WEP, Passphrase = "abcdefghijklm"
        });
        var ns = (XNamespace)"http://www.microsoft.com/networking/WLAN/profile/v1";
        XDocument.Parse(xml).Descendants(ns + "keyType").Single().Value.Should().Be("passPhrase");
    }

    [Fact]
    public void Wep_26HexKey_Accepted_AsNetworkKey()
    {
        var xml = ProfileXmlBuilder.Build(new()
        {
            Ssid = "LegacyWep", Auth = AuthMethod.WEP, Passphrase = "0123456789abcdef01234567ab"
        });
        var ns = (XNamespace)"http://www.microsoft.com/networking/WLAN/profile/v1";
        XDocument.Parse(xml).Descendants(ns + "keyType").Single().Value.Should().Be("networkKey");
    }

    [Fact]
    public void Wpa3Transition_RequiresPassphrase()
    {
        var act = () => ProfileXmlBuilder.Build(new()
        {
            Ssid = "Mixed-WiFi", Auth = AuthMethod.WPA3Transition  // passphrase 無し
        });
        act.Should().Throw<System.ArgumentException>();
    }

    // ── WifiProfileSpec.ValidatePassphrase: 非ASCII文字を拒否 ──────────────────
    [Theory]
    [InlineData("こんにちは123")]    // 8 chars, contains Japanese (passes length, must fail on non-ASCII)
    [InlineData("passé_wordX")]      // 10 chars, accented Latin (passes length, must fail on non-ASCII)
    [InlineData("pass\x01word1")]    // 11 chars, control char U+0001 (passes length, must fail on non-ASCII)
    public void WPA2PSK_NonAsciiPassphrase_Rejected(string pw)
    {
        var act = () => ProfileXmlBuilder.Build(new()
        {
            Ssid = "Net", Auth = AuthMethod.WPA2PSK, Passphrase = pw
        });
        act.Should().Throw<System.ArgumentException>();
    }

    // ── WPA3Transition 完全ゴールデン: auth/enc/passphrase/MFP欠如をすべて検証 ──
    // transitionMode 要素の形式は Wpa3Transition_EmitsWellFormedTransitionModeInV4Namespace で別途検証。
    [Fact]
    public void Wpa3Transition_FullProfile_AuthEncPassphraseMfpAbsent()
    {
        var xml = ProfileXmlBuilder.Build(new()
        {
            Ssid       = "Mixed-WiFi",
            Auth       = AuthMethod.WPA3Transition,
            Passphrase = "supersecret123"
        });
        var doc = XDocument.Parse(xml);
        var ns  = (XNamespace)"http://www.microsoft.com/networking/WLAN/profile/v1";
        var v3  = (XNamespace)"http://www.microsoft.com/networking/WLAN/profile/v3";

        // authentication はWPA3SAE (WPA2/WPA3混在モードのWindows XML値)
        doc.Descendants(ns + "authentication").Single().Value.Should().Be("WPA3SAE");
        // encryption は AES
        doc.Descendants(ns + "encryption").Single().Value.Should().Be("AES");
        // パスフレーズが keyMaterial に格納されている
        doc.Descendants(ns + "keyMaterial").Single().Value.Should().Be("supersecret123");
        doc.Descendants(ns + "keyType").Single().Value.Should().Be("passPhrase");
        // 802.1X は不要
        doc.Descendants(ns + "useOneX").Should().BeEmpty();
        // Transition は MFP-optional のため pmkCacheMode (v3) を含まない
        // (WPA3SAE 専用ネットワークとの区別: 純 WPA3SAE は v3 pmkCacheMode=enabled を持つ)
        doc.Descendants(v3 + "pmkCacheMode").Should().BeEmpty();
    }

    // ── 不足していたゴールデンテスト ─────────────────────────────────

    [Fact]
    public void WPAPSK_LegacyAuth_CorrectXml()
    {
        // WPA (TKIP/AES) — MapAuth が "WPAPSK" を返すことを保証
        var xml = ProfileXmlBuilder.Build(new()
        {
            Ssid       = "LegacyRouter",
            Auth       = AuthMethod.WPAPSK,
            Passphrase = "legacypassword1"
        });
        var doc = XDocument.Parse(xml);
        var ns  = (XNamespace)"http://www.microsoft.com/networking/WLAN/profile/v1";

        doc.Descendants(ns + "authentication").Single().Value.Should().Be("WPAPSK");
        doc.Descendants(ns + "encryption").Single().Value.Should().Be("AES");
        doc.Descendants(ns + "keyMaterial").Single().Value.Should().Be("legacypassword1");
        doc.Descendants(ns + "keyType").Single().Value.Should().Be("passPhrase");
        // WPA (PSK) は useOneX なし
        doc.Descendants(ns + "useOneX").Should().BeEmpty();
    }

    [Fact]
    public void WPA3Enterprise_NonSuite192_CorrectXml()
    {
        // WPA3-Enterprise (AES) — Suite B 192-bit 以外の Enterprise
        var xml = ProfileXmlBuilder.Build(new()
        {
            Ssid                     = "UniversityNet",
            Auth                     = AuthMethod.WPA3Enterprise,
            EapType                  = EapType.EAP_TLS,
            ClientCertThumbprint     = "ABCDEF1234567890ABCDEF1234567890ABCDEF12",
            ServerNames              = new[] { "radius.uni.example" },
            TrustedRootCaThumbprints = new[] { "CAFEBABE" }
        });
        var doc = XDocument.Parse(xml);
        var ns  = (XNamespace)"http://www.microsoft.com/networking/WLAN/profile/v1";

        doc.Descendants(ns + "authentication").Single().Value.Should().Be("WPA3");
        doc.Descendants(ns + "encryption").Single().Value.Should().Be("AES");
        doc.Descendants(ns + "useOneX").Single().Value.Should().Be("true");
        // EAP-TLS は GCMP-256 ではなく AES (192bit Suite B との区別)
        doc.Descendants(ns + "encryption").Single().Value.Should().NotBe("GCMP256");
        xml.Should().Contain("EapHostConfig");
    }
}
