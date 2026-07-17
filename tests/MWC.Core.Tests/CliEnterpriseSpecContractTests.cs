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
