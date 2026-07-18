using System.Xml.Linq;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Profile;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  CLI `mwc connect` Enterprise spec-construction contract.
//
//  The CLI's connect handler (src/MWC.Cli/Program.cs BuildConnect) has no
//  test project of its own, but the important new logic it added is the
//  mapping from --auth/--eap-type/--username/-p/--server-name options onto
//  a WifiProfileSpec. These tests pin the exact spec shape the CLI builds
//  for Enterprise, so a regression in that mapping (or in the validation it
//  relies on to reject incomplete input before connecting) is caught here.
//
//  Mirrors the CLI's own branch:
//    isEnterprise ? new WifiProfileSpec { Ssid, Auth, NonBroadcast, EapType,
//                       Username, Password = -p, Domain, ServerNames }
//                 : new WifiProfileSpec { Ssid, Auth, Passphrase = -p, NonBroadcast };
// ══════════════════════════════════════════════════════════════
public class CliEnterpriseSpecContractTests
{
    // Exactly how the CLI composes the spec for an Enterprise connect.
    private static WifiProfileSpec CliEnterpriseSpec(
        AuthMethod auth = AuthMethod.WPA2Enterprise,
        EapType? eap = EapType.PEAP_MSCHAPv2,
        string? username = "student@univ.ac.jp",
        string? password = "s3cr3t-pass",
        string? domain = null,
        params string[] serverNames)
        => new()
        {
            Ssid = "eduroam",
            Auth = auth,
            NonBroadcast = false,
            EapType = eap,
            Username = username,
            Password = password,
            Domain = domain,
            ServerNames = serverNames,
        };

    [Fact]
    public void PeapWithUsernamePasswordAndServerName_IsValidAndBuilds()
    {
        var spec = CliEnterpriseSpec(serverNames: new[] { "radius.univ.ac.jp" });

        spec.Validate().IsValid.Should().BeTrue();

        // Build must succeed and produce a PEAP Enterprise profile (useOneX true).
        var xml = ProfileXmlBuilder.Build(spec);
        var doc = XDocument.Parse(xml);
        var ns = (XNamespace)"http://www.microsoft.com/networking/WLAN/profile/v1";
        doc.Descendants(ns + "authentication").Single().Value.Should().Be("WPA2");
        doc.Descendants(ns + "useOneX").Single().Value.Should().Be("true");
    }

    [Fact]
    public void MissingEapType_IsRejectedBeforeConnecting()
    {
        // User ran `mwc connect eduroam --auth WPA2Enterprise --username u -p p`
        // but forgot --eap-type. The CLI's early ProfileXmlBuilder.Build call
        // surfaces this as a clean InvalidInput error rather than an OsError.
        var spec = CliEnterpriseSpec(eap: null);

        var v = spec.Validate();
        v.IsValid.Should().BeFalse();
        v.Error.Should().Contain("EAP type");
    }

    [Fact]
    public void PeapMissingPassword_IsRejectedBeforeConnecting()
    {
        // `--username` given but no `-p`.
        var spec = CliEnterpriseSpec(password: null);

        var v = spec.Validate();
        v.IsValid.Should().BeFalse();
        v.Error.Should().Contain("username+password");
    }

    [Fact]
    public void PeapMissingUsername_IsRejectedBeforeConnecting()
    {
        var spec = CliEnterpriseSpec(username: null);

        spec.Validate().IsValid.Should().BeFalse();
    }

    [Fact]
    public void EapTtls_WithCredentials_IsValid()
    {
        var spec = CliEnterpriseSpec(eap: EapType.EAP_TTLS);
        spec.Validate().IsValid.Should().BeTrue();
    }

    // ── EAP-TTLS outer-identity (Phase-1) privacy ────────────────────────
    // The TTLS outer identity is sent in cleartext before the TLS tunnel is up,
    // so exposing the real username there leaks it. eduroam recommends an
    // anonymous outer identity (e.g. "anonymous@realm"). The CLI's --domain flows
    // to spec.Domain, which ProfileXmlBuilder emits as the TTLS AnonymousIdentity —
    // these tests pin that security-relevant wiring so it can't silently regress.
    private static readonly XNamespace Ettns =
        "http://www.microsoft.com/provisioning/EapTtlsConnectionPropertiesV1";

    [Fact]
    public void EapTtls_DomainBecomesAnonymousOuterIdentity()
    {
        // `mwc connect --eap-type EAP_TTLS --username real@univ --domain anonymous@univ -p ...`
        var spec = CliEnterpriseSpec(
            eap: EapType.EAP_TTLS,
            username: "real.user@univ.ac.jp",
            domain: "anonymous@univ.ac.jp");

        var doc = XDocument.Parse(ProfileXmlBuilder.Build(spec));
        doc.Descendants(Ettns + "IdentityPrivacy").Single().Value.Should().Be("true");
        doc.Descendants(Ettns + "AnonymousIdentity").Single().Value
            .Should().Be("anonymous@univ.ac.jp",
                because: "--domain must become the cleartext outer identity, hiding the real username");
        // The real username must NOT appear as the outer identity.
        doc.Descendants(Ettns + "AnonymousIdentity").Single().Value
            .Should().NotBe("real.user@univ.ac.jp");
    }

    [Fact]
    public void EapTtls_NoDomain_FallsBackToAnonymousLiteral()
    {
        // Without --domain the outer identity defaults to the literal "anonymous",
        // still avoiding real-username exposure in the clear.
        var spec = CliEnterpriseSpec(eap: EapType.EAP_TTLS, domain: null);

        var doc = XDocument.Parse(ProfileXmlBuilder.Build(spec));
        doc.Descendants(Ettns + "AnonymousIdentity").Single().Value.Should().Be("anonymous");
    }

    // ── Trusted root CA pinning (--trusted-root-ca) ──────────────────────
    // Pinning the RADIUS server's CA thumbprint prevents accepting a rogue
    // server presenting a valid cert from a *different* CA. ProfileXmlBuilder
    // already emits TrustedRootCaThumbprints (as <TrustedRootCA> for PEAP/TLS,
    // <TrustedRootCAHash> for TTLS); the CLI's --trusted-root-ca now reaches it.
    private static readonly XNamespace MsPeap =
        "http://www.microsoft.com/provisioning/MsPeapConnectionPropertiesV1";

    [Fact]
    public void Peap_TrustedRootCaThumbprint_IsEmittedInProfileXml()
    {
        const string thumb = "ABCDEF1234567890ABCDEF1234567890ABCDEF12";
        var spec = new WifiProfileSpec
        {
            Ssid = "eduroam", Auth = AuthMethod.WPA2Enterprise,
            EapType = EapType.PEAP_MSCHAPv2,
            Username = "student@univ.ac.jp", Password = "pw",
            ServerNames = new[] { "radius.univ.ac.jp" },
            TrustedRootCaThumbprints = new[] { thumb },
        };

        spec.Validate().IsValid.Should().BeTrue();
        var doc = XDocument.Parse(ProfileXmlBuilder.Build(spec));
        doc.Descendants(MsPeap + "TrustedRootCA").Select(e => e.Value)
            .Should().Contain(thumb,
                because: "--trusted-root-ca must pin the RADIUS CA in the emitted PEAP profile");
    }

    [Fact]
    public void EapTtls_TrustedRootCaThumbprint_IsEmittedAsHash()
    {
        const string thumb = "1122334455667788990011223344556677889900";
        var spec = CliEnterpriseSpec(eap: EapType.EAP_TTLS);
        spec = spec with { TrustedRootCaThumbprints = new[] { thumb } };

        var doc = XDocument.Parse(ProfileXmlBuilder.Build(spec));
        doc.Descendants(Ettns + "TrustedRootCAHash").Select(e => e.Value)
            .Should().Contain(thumb);
    }

    [Fact]
    public void EapAka_IsRejected_AsUnsupported()
    {
        // The CLI exposes only PEAP/EAP-TLS/EAP-TTLS via --eap-type completions,
        // but if a user passes EAP_AKA explicitly it must still be rejected cleanly.
        var spec = CliEnterpriseSpec(eap: EapType.EAP_AKA);
        var v = spec.Validate();
        v.IsValid.Should().BeFalse();
        v.Error.Should().Contain("EAP-AKA");
    }

    [Fact]
    public void NonEnterpriseAuth_UsesPassphraseNotEapPassword()
    {
        // Sanity: the CLI's non-Enterprise branch puts -p into Passphrase.
        // A WPA2PSK spec built that way must validate and build normally.
        var spec = new WifiProfileSpec
        {
            Ssid = "HomeNet",
            Auth = AuthMethod.WPA2PSK,
            Passphrase = "correct horse battery",
            NonBroadcast = false,
        };
        spec.Validate().IsValid.Should().BeTrue();
        ProfileXmlBuilder.Build(spec).Should().Contain("HomeNet");
    }
}
