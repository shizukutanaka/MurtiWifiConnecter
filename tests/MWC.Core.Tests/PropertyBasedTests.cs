using System;
using System.Linq;
using FsCheck;
using FsCheck.Xunit;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Profile;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

/// <summary>
/// プロパティベーステスト — FsCheck が数百のランダム入力を自動生成。
/// 通常テストでは見つからない境界値バグを発見する。
/// </summary>
public class WifiUriPropertyTests
{
    [Property(MaxTest = 200)]
    public Property WifiUri_RoundTrip_PreservesNonEmptySsid()
    {
        var gen = Arb.Default.String().Generator
            .Where(s => !string.IsNullOrEmpty(s) && s.Length <= 32
                        && s.All(c => c >= ' ' && c <= '~'));

        return Prop.ForAll(gen, ssid =>
        {
            var spec   = new WifiProfileSpec { Ssid = ssid, Auth = AuthMethod.WPA2PSK, Passphrase = "pass12345" };
            var uri    = WifiUri.Build(spec);
            var parsed = WifiUri.TryParse(uri);
            return parsed?.Ssid == ssid;
        });
    }

    [Property(MaxTest = 200)]
    public Property ProfileXmlBuilder_WPA2_AlwaysHasKeyMaterial()
    {
        var gen = Arb.Default.String().Generator
            .Where(s => s != null && s.Length >= 8 && s.Length <= 63
                        && s.All(c => c >= ' ' && c <= '~') && !string.IsNullOrWhiteSpace(s));

        return Prop.ForAll(gen, pass =>
        {
            var spec = new WifiProfileSpec { Ssid = "TestNet", Auth = AuthMethod.WPA2PSK, Passphrase = pass };
            var xml  = ProfileXmlBuilder.Build(spec);
            return xml.Contains("<keyMaterial>") && xml.Contains(pass);
        });
    }

    [Property(MaxTest = 100)]
    public Property ProfileXmlBuilder_Open_NeverHasKeyMaterial()
    {
        var gen = Gen.Elements("TestNet", "Corp-Free", "Cafe");
        return Prop.ForAll(Arb.From(gen), ssid =>
        {
            var spec = new WifiProfileSpec { Ssid = ssid, Auth = AuthMethod.Open };
            var xml  = ProfileXmlBuilder.Build(spec);
            return !xml.Contains("<keyMaterial>");
        });
    }

    [Property(MaxTest = 150)]
    public Property WifiUri_Always_StartsWithWifi()
    {
        var authGen = Gen.Elements(AuthMethod.Open, AuthMethod.WPA2PSK, AuthMethod.WPA3SAE);
        return Prop.ForAll(Arb.From(authGen), auth =>
        {
            var spec = new WifiProfileSpec
            {
                Ssid       = "Net",
                Auth       = auth,
                Passphrase = auth == AuthMethod.Open ? null : "pass12345"
            };
            var uri    = WifiUri.Build(spec);
            var parsed = WifiUri.TryParse(uri);
            bool startsOk  = uri.StartsWith("WIFI:");
            bool parsedOk  = parsed?.Ssid == "Net";
            bool authMatch = parsed?.Auth == auth;
            return startsOk && parsedOk && authMatch;
        });
    }
}

public class AccessibilityPropertyTests
{
    private readonly AccessibilityAuditService _svc = new();

    [Property(MaxTest = 300)]
    public Property Contrast_AlwaysAtLeastOne()
    {
        var hexGen = Gen.Choose(0, 16777215).Select(n => $"#{n:X6}");
        return Prop.ForAll(Arb.From(hexGen), Arb.From(hexGen), (fg, bg) =>
        {
            var ratio    = _svc.CalcContrast(fg, bg);
            var result   = _svc.EvaluateContrast(fg, bg);
            // Property: ratio は常に1以上21以下
            // Property: EvaluateContrast の Ratio は CalcContrast と一致
            bool validRange  = ratio >= 1.0 && ratio <= 22.0;
            bool consistent  = Math.Abs(result.Ratio - ratio) < 0.001;
            bool labelOk     = result.RatioLabel.Contains(":");
            return validRange && consistent && labelOk;
        });
    }

    [Property(MaxTest = 200)]
    public Property LargeText_Level_AtLeastAsGoodAsNormal()
    {
        var hexGen = Gen.Choose(0, 16777215).Select(n => $"#{n:X6}");
        return Prop.ForAll(Arb.From(hexGen), Arb.From(hexGen), (fg, bg) =>
        {
            var normal = _svc.EvaluateContrast(fg, bg, isLargeText: false);
            var large  = _svc.EvaluateContrast(fg, bg, isLargeText: true);
            return (int)large.Level >= (int)normal.Level;
        });
    }
}

public class RegulatoryDomainPropertyTests
{
    private readonly RegulatoryDomainService _svc = new();

    [Property(MaxTest = 50)]
    public Property ChannelNumbers_AreValidIEEE80211ax()
    {
        var ccGen = Gen.Elements("US", "JP", "DE", "CN", "AU");
        return Prop.ForAll(Arb.From(ccGen), cc =>
        {
            var channels = _svc.GetAvailable6GHzChannels(cc);
            return channels.All(c => c.Channel >= 1 && c.Channel <= 233);
        });
    }

    [Property(MaxTest = 100)]
    public Property FrequencyMhz_IsDerivableFromChannel()
    {
        var ccGen = Gen.Elements("US", "JP");
        return Prop.ForAll(Arb.From(ccGen), cc =>
        {
            var channels = _svc.GetAvailable6GHzChannels(cc);
            return channels.All(c => c.FrequencyMhz == 5950 + c.Channel * 5);
        });
    }
}

public class AdapterPreferencesPropertyTests
{
    [Property(MaxTest = 100)]
    public Property PinSsid_NoDuplicates()
    {
        var ssidGen  = Gen.Elements("Home", "Office", "Cafe", "Hotel");
        var countGen = Gen.Choose(1, 5);
        return Prop.ForAll(Arb.From(ssidGen), Arb.From(countGen), (ssid, n) =>
        {
            var svc = new AdapterPreferencesService();
            var id  = Guid.NewGuid();
            for (int i = 0; i < n; i++) svc.PinSsid(id, ssid);
            var pinned = svc.Get(id).PinnedSsids;
            bool noDup  = pinned.Count(s => s == ssid) <= 1;
            bool exists = pinned.Contains(ssid);
            bool autoOk = svc.IsAutoReconnectEnabled(id) == (pinned.Count > 0);
            return noDup && exists && autoOk;
        });
    }

    [Property(MaxTest = 100)]
    public Property MoveUp_PreservesAll_NoLoss()
    {
        var ssids    = new[] { "A", "B", "C", "D" };
        var indexGen = Gen.Choose(0, ssids.Length - 1);
        return Prop.ForAll(Arb.From(indexGen), idx =>
        {
            var svc = new AdapterPreferencesService();
            var id  = Guid.NewGuid();
            svc.SetAutoConnectPriority(id, ssids);
            svc.MoveUp(id, ssids[idx]);
            var result = svc.GetPreferredNetworks(id).ToList();
            return result.Count == ssids.Length && ssids.All(s => result.Contains(s));
        });
    }
}
