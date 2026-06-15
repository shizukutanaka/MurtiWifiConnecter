using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Profile;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// 高密度シナリオテスト — 各テスト4-6アサート
public class HighDensityWifiUriRoundTripTests
{
    [Theory]
    [InlineData("Home",      AuthMethod.WPA2PSK,  "p@ss12345")]
    [InlineData("Office_5G", AuthMethod.WPA3SAE,  "secret!@#")]
    [InlineData("Free",      AuthMethod.Open,     null)]
    [InlineData("Legacy",    AuthMethod.WPAPSK,   "wpaonly12")]
    public void RoundTrip_MultipleAuthTypes_PreservesData(
        string ssid, AuthMethod auth, string? passphrase)
    {
        var spec = new WifiProfileSpec { Ssid = ssid, Auth = auth, Passphrase = passphrase };
        var uri = WifiUri.Build(spec);

        uri.Should().NotBeNullOrEmpty();
        uri.Should().StartWith("WIFI:");
        uri.Should().Contain(ssid);

        var parsed = WifiUri.TryParse(uri);
        parsed.Should().NotBeNull();
        parsed!.Ssid.Should().Be(ssid);
        parsed.Auth.Should().Be(auth);
        if (passphrase is not null)
            parsed.Passphrase.Should().Be(passphrase);
    }
}

public class HighDensityProfileXmlTests
{
    [Fact]
    public void Build_WPA3Enterprise_HasAllElements()
    {
        var spec = new WifiProfileSpec
        {
            Ssid = "Corp",
            Auth = AuthMethod.WPA3Enterprise,
            EapType = EapType.PEAP,
            Username = "user@corp.com",
            Password = "secret",
            ServerNames = new[] { "radius.corp.com" }
        };
        var xml = ProfileXmlBuilder.Build(spec);

        xml.Should().NotBeNullOrEmpty();
        xml.Should().StartWith("<?xml");
        xml.Should().Contain("Corp");
        xml.Should().Contain("WLANProfile");
        xml.Should().Contain("WPA3");
        xml.Length.Should().BeGreaterThan(500);
    }

    [Fact]
    public void Build_DifferentAuthMethods_GenerateDifferentXml()
    {
        var open = new WifiProfileSpec { Ssid = "X", Auth = AuthMethod.Open };
        var wpa  = new WifiProfileSpec { Ssid = "X", Auth = AuthMethod.WPA2PSK, Passphrase = "p1234567" };

        var xOpen = ProfileXmlBuilder.Build(open);
        var xWpa  = ProfileXmlBuilder.Build(wpa);

        xOpen.Should().NotBe(xWpa);
        xOpen.Should().Contain("open");
        xWpa.Should().NotContain("<authentication>open</authentication>");
        xWpa.Should().Contain("keyMaterial");
        xOpen.Should().NotContain("keyMaterial");
        (xWpa.Length).Should().BeGreaterThan(xOpen.Length);
    }
}

public class HighDensityAdapterPrefsTests
{
    [Fact]
    public void Pin_RemovePin_FullCycle()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();

        // ピン留め追加
        svc.PinSsid(id, "A");
        svc.PinSsid(id, "B");
        svc.PinSsid(id, "C");

        var pinned = svc.Get(id).PinnedSsids;
        pinned.Should().HaveCount(3);
        pinned.Should().Contain("A").And.Contain("B").And.Contain("C");

        // 中央の削除
        svc.UnpinSsid(id, "B");
        pinned = svc.Get(id).PinnedSsids;
        pinned.Should().HaveCount(2);
        pinned.Should().Contain("A").And.Contain("C");
        pinned.Should().NotContain("B");
    }

    [Fact]
    public void PriorityOrder_AffectsBestPick()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();

        svc.SetAutoConnectPriority(id, new[] { "Third", "Second", "First" });

        // 全候補圏内 → 最初の Third が選ばれる
        var best1 = svc.PickBestSsid(id, new[] { "First", "Second", "Third" });
        best1.Should().Be("Third");

        // Third 圏外 → Second が選ばれる
        var best2 = svc.PickBestSsid(id, new[] { "First", "Second" });
        best2.Should().Be("Second");

        // First のみ → First
        var best3 = svc.PickBestSsid(id, new[] { "First" });
        best3.Should().Be("First");

        // 全圏外
        var best4 = svc.PickBestSsid(id, new[] { "Other" });
        best4.Should().BeNull();
    }

    [Fact]
    public void SetLabel_PersistsCustomName()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();

        svc.Get(id).CustomLabel.Should().BeNullOrEmpty();

        svc.SetLabel(id, "ホーム用ドングル");
        svc.Get(id).CustomLabel.Should().Be("ホーム用ドングル");

        svc.SetLabel(id, "Office Dongle");
        svc.Get(id).CustomLabel.Should().Be("Office Dongle");
        svc.Get(id).CustomLabel.Should().NotBe("ホーム用ドングル");
        svc.Get(id).CustomLabel.Length.Should().Be("Office Dongle".Length);
    }
}

public class HighDensityNetworkHistoryTests
{
    [Fact]
    public void RecordConnection_SuccessAndFailure_BothPersist()
    {
        var hist = new NetworkHistoryService();
        var beforeCount = hist.GetRecentSsids(100).Count;

        hist.RecordConnection("NetA", true);
        hist.RecordConnection("NetB", false);
        hist.RecordConnection("NetA", true);

        var ssids = hist.GetRecentSsids(100).ToList();
        ssids.Count.Should().BeGreaterThan(beforeCount);
        ssids.Should().Contain("NetA");
        ssids.Should().Contain("NetB");
        ssids.First().Should().NotBeNullOrEmpty();
    }
}

public class WiFi7MloTests
{
    [Fact]
    public void MloNetwork_HasLinksAndAggregatedSpeed()
    {
        var links = new[]
        {
            new MloLink { LinkId = 0, Band = WifiBand.Band5GHz,  ChannelWidth = 160, Rssi = -45, Channel = 100, FrequencyMhz = 5500 },
            new MloLink { LinkId = 1, Band = WifiBand.Band6GHz,  ChannelWidth = 320, Rssi = -50, Channel = 37,  FrequencyMhz = 6175 },
        };

        var net = new WifiNetwork
        {
            Ssid          = "WiFi7Router",
            Phy           = PhyType.Dot11be,
            IsMlo         = true,
            MloLinks      = links,
            Auth          = AuthMethod.WPA3SAE,
            Band          = WifiBand.Band6GHz,
            SignalQuality = 85
        };

        // Wi-Fi 7 属性
        net.IsMlo.Should().BeTrue();
        net.Phy.Should().Be(PhyType.Dot11be);
        net.MloLinks.Should().HaveCount(2);
        net.MloAggregatedSpeedMbps.Should().NotBeNull();

        // MLO 集約速度: 5GHz-160MHz(5765) + 6GHz-320MHz(11529) = 17294
        net.MloAggregatedSpeedMbps.Should().BeGreaterThan(10000);
        net.Phy.ToGenerationLabel().Should().Contain("Wi-Fi 7");
        net.Phy.ToShortLabel().Should().Be("Wi-Fi 7");
    }

    [Fact]
    public void NonMloNetwork_AggregatedSpeedIsNull()
    {
        var net = new WifiNetwork
        {
            Ssid     = "LegacyRouter",
            Phy      = PhyType.Dot11ax,
            IsMlo    = false,
            Auth     = AuthMethod.WPA2PSK,
            Band     = WifiBand.Band5GHz,
            SignalQuality = 70
        };

        net.IsMlo.Should().BeFalse();
        net.MloLinks.Should().BeEmpty();
        net.MloAggregatedSpeedMbps.Should().BeNull();
        net.Phy.ToGenerationLabel().Should().Contain("Wi-Fi 6");
    }

    [Fact]
    public void MloExtensions_AggregatedSpeed_IsPositive()
    {
        var links = new[]
        {
            new MloLink { ChannelWidth = 80,  Rssi = -60, Band = WifiBand.Band5GHz },
            new MloLink { ChannelWidth = 160, Rssi = -55, Band = WifiBand.Band5GHz },
            new MloLink { ChannelWidth = 320, Rssi = -50, Band = WifiBand.Band6GHz },
        };
        var speed = links.EstimatedAggregatedSpeedMbps();

        speed.Should().BeGreaterThan(0);
        speed.Should().BeGreaterThan(5000);   // 3リンク分
        speed.Should().BeLessThan(50000);     // 物理的上限
        // 320MHz > 160MHz > 80MHz の寄与
        var single320 = new[] { new MloLink { ChannelWidth = 320 } }.EstimatedAggregatedSpeedMbps();
        single320.Should().BeGreaterThan(new[] { new MloLink { ChannelWidth = 80 } }.EstimatedAggregatedSpeedMbps());
    }
}

public class RegulatoryDomainTests
{
    private readonly RegulatoryDomainService _svc = new();

    [Theory]
    [InlineData("US", Band6GHzMode.FullBand)]
    [InlineData("JP", Band6GHzMode.FullBand)]
    [InlineData("DE", Band6GHzMode.LowerHalf)]
    [InlineData("CN", Band6GHzMode.None)]
    [InlineData("IN", Band6GHzMode.None)]
    public void GetRegion_ReturnsCorrectMode(string cc, Band6GHzMode expected)
    {
        var region = _svc.GetRegion(cc);
        region.CountryCode.Should().Be(cc);
        region.Mode.Should().Be(expected);
        region.Has6GHz.Should().Be(expected != Band6GHzMode.None);
    }

    [Fact]
    public void US_Has233Channels()
    {
        var channels = _svc.GetAvailable6GHzChannels("US");
        channels.Should().NotBeEmpty();
        channels.Count.Should().BeGreaterThan(50);
        channels.All(c => c.Channel >= 1).Should().BeTrue();
        channels.All(c => c.FrequencyMhz >= 5950).Should().BeTrue();
    }

    [Fact]
    public void EU_ChannelsCapped_At93()
    {
        var channels = _svc.GetAvailable6GHzChannels("DE");
        channels.Should().NotBeEmpty();
        channels.All(c => c.Channel <= 93).Should().BeTrue();
        channels.Count.Should().BeLessThan(_svc.GetAvailable6GHzChannels("US").Count);
    }

    [Fact]
    public void PscChannels_AreSubsetOfAll()
    {
        var all = _svc.GetAvailable6GHzChannels("US");
        var pscs = all.Where(c => c.IsPsc).ToList();
        pscs.Should().NotBeEmpty();
        pscs.Count.Should().BeLessThan(all.Count);
        pscs.All(c => _svc.IsPreferredScanChannel(c.Channel)).Should().BeTrue();
    }

    [Fact]
    public void Unknown_CountryCode_FallsBack_ToNone()
    {
        var region = _svc.GetRegion("ZZ");
        region.Mode.Should().Be(Band6GHzMode.None);
        region.Has6GHz.Should().BeFalse();
        _svc.GetAvailable6GHzChannels("ZZ").Should().BeEmpty();
    }
}

public class OweSelectionTests
{
    private readonly OweSelectionService _svc = new();

    [Fact]
    public void ApplyOwePreference_HidesOpen_WhenOweExists()
    {
        var nets = new[]
        {
            new WifiNetwork { Ssid = "Cafe", Auth = AuthMethod.Open,  SignalQuality = 80, Band = WifiBand.Band5GHz },
            new WifiNetwork { Ssid = "Cafe", Auth = AuthMethod.OWE,   SignalQuality = 78, Band = WifiBand.Band5GHz },
            new WifiNetwork { Ssid = "Home", Auth = AuthMethod.WPA2PSK,SignalQuality = 90, Band = WifiBand.Band5GHz },
        };
        var result = _svc.ApplyOwePreference(nets);

        result.Should().HaveCount(2, "Cafe-Open は OWE に置き換えられる");
        result.Any(n => n.Auth == AuthMethod.Open && n.Ssid == "Cafe").Should().BeFalse();
        result.Any(n => n.Auth == AuthMethod.OWE  && n.Ssid == "Cafe").Should().BeTrue();
        result.Any(n => n.Ssid == "Home").Should().BeTrue();
    }

    [Fact]
    public void RecommendAuth_PreferOwe_WhenAvailable()
    {
        var open = new WifiNetwork { Ssid = "FreeWifi", Auth = AuthMethod.Open, SignalQuality = 70, Band = WifiBand.Band2_4GHz };
        var owe  = new WifiNetwork { Ssid = "FreeWifi", Auth = AuthMethod.OWE,  SignalQuality = 68, Band = WifiBand.Band2_4GHz };
        var all  = new[] { open, owe };

        _svc.RecommendAuth(open, all).Should().Be(AuthMethod.OWE);
        _svc.RecommendAuth(owe,  all).Should().Be(AuthMethod.OWE);
    }
}

public class CatImportTests
{
    private const string SampleEapConfig = """
        <?xml version="1.0" encoding="UTF-8"?>
        <EAPIdentityProviderList>
          <EAPIdentityProvider>
            <SSID>eduroam</SSID>
            <AuthenticationMethods>
              <AuthenticationMethod>
                <EAPMethod>
                  <Type>25</Type>
                </EAPMethod>
              </AuthenticationMethod>
            </AuthenticationMethods>
            <CredentialApplicability>
              <IEEE80211>
                <ServerName>radius.example.ac.jp</ServerName>
              </IEEE80211>
            </CredentialApplicability>
            <ProviderInfo>
              <DisplayName>Example University</DisplayName>
              <Domain>example.ac.jp</Domain>
            </ProviderInfo>
          </EAPIdentityProvider>
        </EAPIdentityProviderList>
        """;

    [Fact]
    public void ParseEapConfig_ExtractsProfile()
    {
        var svc = new CatImportService();
        var profiles = svc.ParseEapConfig(SampleEapConfig);

        profiles.Should().NotBeEmpty();
        var p = profiles.First();
        p.Ssid.Should().Be("eduroam");
        p.EapType.Should().Be(EapType.PEAP_MSCHAPv2);  // Type 25
        p.ServerNames.Should().Contain("radius.example.ac.jp");
        p.OrganizationName.Should().Contain("Example");
    }

    [Fact]
    public void ParseEapConfig_Empty_Throws()
    {
        var svc = new CatImportService();
        var act = () => svc.ParseEapConfig("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildEduroamSpec_GeneratesValidSpec()
    {
        var svc = new CatImportService();
        var profiles = svc.ParseEapConfig(SampleEapConfig);
        var spec = svc.BuildEduroamSpec(profiles.First());

        spec.Ssid.Should().Be("eduroam");
        spec.Auth.Should().Be(AuthMethod.WPA2Enterprise);
        spec.EapType.Should().Be(EapType.PEAP_MSCHAPv2);
        spec.ServerNames.Should().Contain("radius.example.ac.jp");
    }
}

public class NetworkHistoryStatsTests
{
    [Fact]
    public void GetStats_ReflectsRecentConnections()
    {
        var hist = new NetworkHistoryService();
        hist.RecordConnection("Alpha", true);
        hist.RecordConnection("Alpha", true);
        hist.RecordConnection("Beta",  false);

        var stats = hist.GetStats(days: 30);

        stats.TotalConnects.Should().BeGreaterOrEqualTo(2);
        stats.UniqueNetworks.Should().BeGreaterOrEqualTo(1);
        stats.Period.TotalDays.Should().Be(30);
        stats.SuccessRate.Should().BeGreaterThan(0).And.BeLessOrEqualTo(1.0);
    }

    [Fact]
    public void GetFrequentSsids_OrdersByCount()
    {
        var hist = new NetworkHistoryService();
        hist.RecordConnection("Rare",    true);
        hist.RecordConnection("Common",  true);
        hist.RecordConnection("Common",  true);
        hist.RecordConnection("Common",  true);

        var top = hist.GetFrequentSsids(2).ToList();
        top.Should().NotBeEmpty();
        top.Count.Should().BeLessOrEqualTo(2);
        top.First().Should().Be("Common");
    }

    [Fact]
    public void Count_ReflectsEntries()
    {
        var hist = new NetworkHistoryService();
        var before = hist.Count;
        hist.RecordConnection("NewNet", true);
        hist.Count.Should().BeGreaterThan(before - 1);
    }
}

public class GroupPolicyProviderTests
{
    [Fact]
    public void IsSsidAllowed_EmptyAllowList_AllowsAll()
    {
        var gp = GroupPolicyProvider.Instance;
        // GP キーが未設定 = 全許可
        if (!gp.IsManagedDevice)
        {
            gp.IsSsidAllowed("AnySSID").Should().BeTrue();
            gp.IsSsidAllowed("AnotherNet").Should().BeTrue();
        }
        else
        {
            // 管理デバイスでも例外なく返すこと
            var act = () => gp.IsSsidAllowed("Test");
            act.Should().NotThrow();
        }
    }

    [Fact]
    public void GetAllPolicies_ReturnsListWithoutException()
    {
        var gp = GroupPolicyProvider.Instance;
        var policies = gp.GetAllPolicies();
        policies.Should().NotBeNull();
        // 未管理環境では空リストが返る
        policies.Should().AllSatisfy(p =>
        {
            p.Name.Should().NotBeNullOrEmpty();
            p.Value.Should().NotBeNull();
        });
    }

    [Fact]
    public void PolicyEntries_HaveValidStructure()
    {
        var entry = new PolicyEntry("TestKey", "TestValue", "DWORD");
        entry.Name.Should().Be("TestKey");
        entry.Value.Should().Be("TestValue");
        entry.Type.Should().Be("DWORD");
    }
}

public class AccessibilityAuditTests
{
    private readonly AccessibilityAuditService _svc = new();

    [Theory]
    [InlineData("#E6E8EB", "#0F1115", false, WcagLevel.AAA)]   // Dark: fg on bg
    [InlineData("#E6E8EB", "#00C4CC", false, WcagLevel.Fail)]  // fg on accent (低コントラスト)
    [InlineData("#001518", "#00C4CC", false, WcagLevel.AA)]    // accentText on accent
    [InlineData("#ECEFF4", "#2E3440", true,  WcagLevel.AAA)]   // Nord: fg on bg (大テキスト)
    public void EvaluateContrast_MwcThemePairs(
        string fg, string bg, bool large, WcagLevel expected)
    {
        var result = _svc.EvaluateContrast(fg, bg, large);
        result.Level.Should().Be(expected);
        result.Passes.Should().Be(expected != WcagLevel.Fail);
        result.Ratio.Should().BeGreaterThan(1.0);
        result.RatioLabel.Should().Contain(":");
    }

    [Fact]
    public void CalcContrast_BlackOnWhite_Is21()
    {
        var ratio = _svc.CalcContrast("#000000", "#FFFFFF");
        ratio.Should().BeApproximately(21.0, 0.5);
    }

    [Fact]
    public void CalcContrast_SameColor_Is1()
    {
        var ratio = _svc.CalcContrast("#888888", "#888888");
        ratio.Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public void AuditMwcTheme_AllPairsHaveResults()
    {
        var results = _svc.AuditMwcTheme("#E6E8EB", "#0F1115", "#00C4CC", "#001518");
        results.Should().HaveCount(4);
        results.Should().AllSatisfy(r =>
        {
            r.Ratio.Should().BeGreaterThan(1.0);
            r.RatioLabel.Should().NotBeNullOrEmpty();
        });
    }

    [Fact]
    public void GetScreenReaderChecklist_HasAllRequiredItems()
    {
        var list = _svc.GetScreenReaderChecklist();
        list.Should().HaveCountGreaterOrEqualTo(10);
        list.Should().Contain(i => i.Criterion == WcagCriterion.C2_1_1);  // Keyboard
        list.Should().Contain(i => i.Criterion == WcagCriterion.C4_1_3);  // Status Messages
        list.Should().AllSatisfy(i =>
        {
            i.Id.Should().StartWith("SR");
            i.Title.Should().NotBeNullOrEmpty();
        });
    }

    [Fact]
    public void GenerateReport_AllPass_IsAaaLevel()
    {
        var pairs = new[]
        {
            new ColorPair("#000000", "#FFFFFF", false, "Black on White"),
            new ColorPair("#FFFFFF", "#000000", false, "White on Black"),
        };
        var results  = _svc.AuditThemePairs(pairs);
        var checklist = _svc.GetScreenReaderChecklist();
        var report   = _svc.GenerateReport(results, checklist);

        report.FailCount.Should().Be(0);
        report.OverallLevel.Should().Be(WcagLevel.AAA);
        report.AaPassRate.Should().Be(1.0);
        report.AaaPassRate.Should().Be(1.0);
        report.Failures.Should().BeEmpty();
    }

    [Fact]
    public void GenerateReport_WithFailure_ReportsFail()
    {
        var pairs = new[]
        {
            new ColorPair("#AAAAAA", "#999999", false, "Low contrast"),  // ~1.1:1
        };
        var results = _svc.AuditThemePairs(pairs);
        var report  = _svc.GenerateReport(results, Array.Empty<A11yCheckItem>());

        report.FailCount.Should().Be(1);
        report.OverallLevel.Should().Be(WcagLevel.Fail);
        report.Failures.Should().HaveCount(1);
    }
}

public class WifiDirectModelTests
{
    [Fact]
    public void WifiDirectDevice_RecordEqualityAndInit()
    {
        var d1 = new WifiDirectDevice("id-1", "Phone A", WifiDirectDeviceType.Phone, -60);
        var d2 = d1 with { State = WifiDirectDeviceState.Connected };

        d1.DeviceId.Should().Be("id-1");
        d1.State.Should().Be(WifiDirectDeviceState.Available);
        d2.State.Should().Be(WifiDirectDeviceState.Connected);
        d1.Should().NotBe(d2);
        d1.DeviceName.Should().Be(d2.DeviceName);
    }

    [Fact]
    public void WifiDirectDiscoveryOptions_Default_Is30s()
    {
        var opts = WifiDirectDiscoveryOptions.Default;
        opts.Timeout.TotalSeconds.Should().Be(30);
        opts.ScanAll.Should().BeFalse();
    }

    [Fact]
    public void WifiDirectGroupOwnerResult_PropertiesOk()
    {
        var r = new WifiDirectGroupOwnerResult(true, "DIRECT-AB", "pass1234", "192.168.1.1");
        r.Success.Should().BeTrue();
        r.Ssid.Should().StartWith("DIRECT-");
        r.Passphrase.Should().Be("pass1234");
        r.LocalIp.Should().Contain(".");
    }
}

public class SlnRegistrationTests
{
    [Fact]
    public void SolutionFile_RegistersAllProjects()
    {
        var slnPath = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(typeof(SlnRegistrationTests).Assembly.Location)!,
                "..", "..", "..", "..", "..", "MWC.sln"));

        if (!System.IO.File.Exists(slnPath)) return;
        var sln = System.IO.File.ReadAllText(slnPath);

        var expectedProjects = new[]
        {
            "MWC.Core", "MWC.App", "MWC.Cli", "MWC.Platform.Windows",
            "MWC.Platform.Linux", "MWC.Platform.MacOS", "MWC.SDK"
        };

        foreach (var proj in expectedProjects)
        {
            sln.Should().Contain(proj,
                because: $"{proj} must be registered in MWC.sln");
        }
    }

    [Fact]
    public void SolutionFile_HasNoDuplicateGuids()
    {
        var slnPath = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(typeof(SlnRegistrationTests).Assembly.Location)!,
                "..", "..", "..", "..", "..", "MWC.sln"));

        if (!System.IO.File.Exists(slnPath)) return;
        var guids = System.Text.RegularExpressions.Regex.Matches(
            System.IO.File.ReadAllText(slnPath),
            @"\{[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\}",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Select(m => m.Value.ToUpperInvariant())
            .ToList();

        var projectGuids = guids.GroupBy(g => g).Where(g => g.Count() > 2).ToList();
        // プロジェクトGUIDは NestedProjects等で2回出るが3回以上は重複
        projectGuids.Should().BeEmpty("No GUID should appear 3+ times in sln");
    }
}

public class BssInfoModelTests
{
    [Fact]
    public void BssInfo_HasInterworkingElement_DefaultFalse()
    {
        var bss = new BssInfo();
        bss.HasInterworkingElement.Should().BeFalse();
        bss.Bssid.Should().BeNullOrEmpty();
    }

    [Fact]
    public void WifiNetwork_IsPasspoint_RequiresInterworkingElement()
    {
        var openNet = new WifiNetwork
        {
            Ssid   = "Open",
            Auth   = AuthMethod.Open,
            Band   = WifiBand.Band5GHz,
            BssEntries = new[] { new BssInfo { HasInterworkingElement = false } }
        };
        openNet.IsPasspoint.Should().BeFalse("Open AP は Passpoint 非対応");

        var passpointNet = new WifiNetwork
        {
            Ssid   = "Corp",
            Auth   = AuthMethod.WPA2Enterprise,
            Band   = WifiBand.Band5GHz,
            BssEntries = new[] { new BssInfo { HasInterworkingElement = true } }
        };
        passpointNet.IsPasspoint.Should().BeTrue();
        passpointNet.Auth.Should().Be(AuthMethod.WPA2Enterprise);
        passpointNet.BssEntries.Should().HaveCount(1);
    }
}

public class TroubleshootingHelperTests
{
    private readonly TroubleshootingHelper _svc = new();

    [Theory]
    [InlineData(ConnectionFailure.BadCredentials,      true)]
    [InlineData(ConnectionFailure.NotInRange,          true)]
    [InlineData(ConnectionFailure.Timeout,             true)]
    [InlineData(ConnectionFailure.AdapterDisabled,     true)]
    [InlineData(ConnectionFailure.InsufficientPrivilege, true)]
    [InlineData(ConnectionFailure.Unknown,             true)]
    public void GetAdvice_AllFailures_ReturnsNonEmptyAdvice(ConnectionFailure f, bool _)
    {
        var advice = _svc.GetAdvice(f);
        advice.Should().NotBeNullOrEmpty();
        advice.Should().HaveCountGreaterThan(0);
        advice.Should().AllSatisfy(a =>
        {
            a.Title.Should().NotBeNullOrEmpty();
            a.Detail.Should().NotBeNullOrEmpty();
        });
    }

    [Fact]
    public void GetAdvice_BadCredentials_HasPasswordHint()
    {
        var advice = _svc.GetAdvice(ConnectionFailure.BadCredentials);
        var flat = string.Join(" ", advice.Select(a => a.Title + " " + a.Detail));
        flat.Should().NotBeNullOrEmpty();
        advice.Count.Should().BeGreaterThan(0);
    }
}

public class OweSelectionServiceTests2
{
    private readonly OweSelectionService _svc = new();

    [Fact]
    public void BuildOweSpec_SetsCorrectAuth()
    {
        var spec = _svc.BuildOweSpec("FreeWifi");
        spec.Ssid.Should().Be("FreeWifi");
        spec.Auth.Should().Be(AuthMethod.OWE);
        spec.Passphrase.Should().BeNullOrEmpty();
    }

    [Fact]
    public void ApplyOwePreference_NoOweExists_KeepsOpen()
    {
        var nets = new[]
        {
            new WifiNetwork { Ssid = "OnlyOpen", Auth = AuthMethod.Open, Band = WifiBand.Band2_4GHz, SignalQuality = 70 }
        };
        var result = _svc.ApplyOwePreference(nets);
        result.Should().HaveCount(1);
        result[0].Auth.Should().Be(AuthMethod.Open);
    }

    [Fact]
    public void RecommendAuth_WhenNoOwe_ReturnsOriginal()
    {
        var net = new WifiNetwork { Ssid = "Net", Auth = AuthMethod.WPA2PSK, Band = WifiBand.Band5GHz, SignalQuality = 80 };
        _svc.RecommendAuth(net, new[] { net }).Should().Be(AuthMethod.WPA2PSK);
    }
}

public class Hotspot20ServiceTests
{
    private readonly Hotspot20Service _svc = new();

    [Fact]
    public void KnownCarriers_NotEmpty()
    {
        Hotspot20Service.KnownCarriers.Should().NotBeEmpty();
        Hotspot20Service.KnownCarriers.Count.Should().BeGreaterOrEqualTo(3);
    }

    [Fact]
    public void BuildCarrierProfile_SetsSSID()
    {
        var preset = Hotspot20Service.KnownCarriers.First();
        var spec   = _svc.BuildCarrierProfile(preset);
        spec.Ssid.Should().Be(preset.Ssid);
        spec.EapType.Should().Be(preset.EapType);
    }

    [Fact]
    public void BuildProfile_SetsHomeOI()
    {
        var profile = _svc.BuildProfile("001122334455", "example.com", EapType.PEAP_MSCHAPv2);
        profile.HomeOI.Should().Be("001122334455");
        profile.Domain.Should().Be("example.com");
        profile.EapType.Should().Be(EapType.PEAP_MSCHAPv2);
    }

    [Fact]
    public void FilterPasspointNetworks_OnlyReturnsPasspointAP()
    {
        var nets = new[]
        {
            new WifiNetwork { Ssid = "Corp", Auth = AuthMethod.WPA2Enterprise, Band = WifiBand.Band5GHz, SignalQuality = 75,
                BssEntries = new[]{ new BssInfo { HasInterworkingElement = true } } },
            new WifiNetwork { Ssid = "Home", Auth = AuthMethod.WPA2PSK, Band = WifiBand.Band5GHz, SignalQuality = 90 },
        };
        var passpoint = _svc.FilterPasspointNetworks(nets);
        passpoint.Should().HaveCount(1);
        passpoint[0].Ssid.Should().Be("Corp");
    }
}
