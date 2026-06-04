using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Profile;
using Xunit;

namespace MWC.Core.Tests;

public class WifiUriTests
{
    [Fact]
    public void Build_WPA2_BasicShape()
    {
        var spec = new WifiProfileSpec
        {
            Ssid = "MyWiFi",
            Auth = AuthMethod.WPA2PSK,
            Passphrase = "secret123"
        };
        var uri = WifiUri.Build(spec);
        uri.Should().Be("WIFI:T:WPA;S:MyWiFi;P:secret123;;");
    }

    [Fact]
    public void Build_Open_NoPassword()
    {
        var uri = WifiUri.Build(new() { Ssid = "FreeSpot", Auth = AuthMethod.Open });
        uri.Should().Be("WIFI:T:nopass;S:FreeSpot;;");
    }

    [Fact]
    public void Build_Wpa3_UsesSAE()
    {
        var uri = WifiUri.Build(new()
        {
            Ssid = "Modern", Auth = AuthMethod.WPA3SAE, Passphrase = "p"
        });
        uri.Should().Contain("T:SAE");
    }

    [Fact]
    public void Build_EscapesSpecial()
    {
        var uri = WifiUri.Build(new()
        {
            Ssid = "Cafe;1",
            Auth = AuthMethod.WPA2PSK,
            Passphrase = @"a\b:c"
        });
        uri.Should().Contain(@"S:Cafe\;1");
        uri.Should().Contain(@"P:a\\b\:c");
    }

    [Fact]
    public void Parse_RoundTrip()
    {
        var src = new WifiProfileSpec
        {
            Ssid = "Test",
            Auth = AuthMethod.WPA2PSK,
            Passphrase = "abcd1234",
            NonBroadcast = true
        };
        var parsed = WifiUri.Parse(WifiUri.Build(src));
        parsed.Should().NotBeNull();
        parsed!.Ssid.Should().Be("Test");
        parsed.Passphrase.Should().Be("abcd1234");
        parsed.NonBroadcast.Should().BeTrue();
    }

    [Fact]
    public void Parse_EscapedSpecial()
    {
        var p = WifiUri.Parse(@"WIFI:T:WPA;S:Cafe\;1;P:a\\b\:c;;");
        p!.Ssid.Should().Be("Cafe;1");
        p.Passphrase.Should().Be(@"a\b:c");
    }

    [Fact]
    public void Parse_Invalid_ReturnsNull()
    {
        WifiUri.Parse("not a uri").Should().BeNull();
        WifiUri.Parse("").Should().BeNull();
        WifiUri.Parse("WIFI:T:WPA;;").Should().BeNull();  // SSID欠落
    }
}

public class WifiUriHighDensityTests
{
    [Fact]
    public void Parse_RoundTrip_AllAuthMethods()
    {
        foreach (var auth in new[] {
            MWC.Core.Models.AuthMethod.Open,
            MWC.Core.Models.AuthMethod.WPA2PSK,
            MWC.Core.Models.AuthMethod.WPA3SAE })
        {
            var spec = new MWC.Core.Models.WifiProfileSpec { Ssid = "Net", Auth = auth, Passphrase = auth == MWC.Core.Models.AuthMethod.Open ? null : "p1234567" };
            var uri = MWC.Core.Profile.WifiUri.Build(spec);
            uri.Should().NotBeNullOrEmpty();
            uri.Should().StartWith("WIFI:");
            var parsed = MWC.Core.Profile.WifiUri.TryParse(uri);
            parsed.Should().NotBeNull();
            parsed!.Ssid.Should().Be("Net");
            parsed.Auth.Should().Be(auth);
        }
    }

    [Fact]
    public void Build_EscapesSpecialCharacters()
    {
        var spec = new MWC.Core.Models.WifiProfileSpec
        {
            Ssid = "My;Network",
            Auth = MWC.Core.Models.AuthMethod.WPA2PSK,
            Passphrase = "pass:word"
        };
        var uri = MWC.Core.Profile.WifiUri.Build(spec);
        uri.Should().NotBeNullOrEmpty();
        uri.Should().Contain("WIFI:");
        // セミコロン・コロンはエスケープされる
        var parsed = MWC.Core.Profile.WifiUri.TryParse(uri);
        parsed.Should().NotBeNull();
        parsed!.Ssid.Should().Be("My;Network");
        parsed.Passphrase.Should().Be("pass:word");
    }
}
