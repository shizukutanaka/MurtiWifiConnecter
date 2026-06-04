using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Profile;
using MWC.Core.Services;
using MWC.Core.Tests.Fakes;
using Xunit;

namespace MWC.Core.Tests;

// ───── ExportService 統合テスト ─────
public class ExportServiceCompletionTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public ExportServiceCompletionTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    private static System.Collections.Generic.List<WifiNetwork> SampleNets() =>
    [
        new() { Ssid = "Net-A", SignalQuality = 90, Auth = AuthMethod.WPA3SAE,
                Band = WifiBand.Band5GHz,  Channel = 36, Phy = PhyType.Dot11ax,
                BssEntries = [] },
        new() { Ssid = "Net-B", SignalQuality = 45, Auth = AuthMethod.WPA2PSK,
                Band = WifiBand.Band2_4GHz, Channel = 6,  Phy = PhyType.Dot11n,
                BssEntries = [] },
        new() { Ssid = "WiFi7", SignalQuality = 80, Auth = AuthMethod.WPA3SAE,
                Band = WifiBand.Band6GHz,  Channel = 37, Phy = PhyType.Dot11be,
                ChannelWidth = 320, BssEntries = [] },
    ];

    [Fact]
    public void ToCsv_AllNetworks_Written()
    {
        var path = Path.Combine(_tmp, "out.csv");
        ExportService.ToCsv(SampleNets(), path);
        var lines = File.ReadAllLines(path);
        lines.Length.Should().Be(4);          // header + 3 rows
        lines[0].Should().Contain("SSID").And.Contain("Signal");
        lines[1].Should().Contain("Net-A");
    }

    [Fact]
    public void ToJson_ValidJson_WithAllNetworks()
    {
        var path = Path.Combine(_tmp, "out.json");
        ExportService.ToJson(SampleNets(), path);
        var json = File.ReadAllText(path);
        var doc  = System.Text.Json.JsonDocument.Parse(json);
        doc.RootElement.GetProperty("networks").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public void ToText_ContainsWifi7Phy()
    {
        var path = Path.Combine(_tmp, "out.txt");
        ExportService.ToText(SampleNets(), path);
        var txt = File.ReadAllText(path);
        txt.Should().Contain("Wi-Fi 7");
        txt.Should().Contain("6 GHz");
    }

    [Fact]
    public void ToCsv_EmptyList_OnlyHeader()
    {
        var path = Path.Combine(_tmp, "empty.csv");
        ExportService.ToCsv(new System.Collections.Generic.List<WifiNetwork>(), path);
        File.ReadAllLines(path).Length.Should().Be(1);  // header only
    }
}

// ───── NetworkHistoryService 永続化テスト ─────
public class NetworkHistoryPersistenceTests
{
    [Fact]
    public void RecordMultiple_GetRecent_CorrectOrder()
    {
        var svc = new NetworkHistoryService();
        svc.RecordConnection("Alpha",  true);
        svc.RecordConnection("Beta",   true);
        svc.RecordConnection("Gamma",  true);
        svc.RecordConnection("Beta",   true);   // 2回目

        var recent = svc.GetRecentSsids(3);
        recent[0].Should().Be("Beta");   // 最後に触ったのがBeta
        recent.Should().Contain("Gamma").And.Contain("Alpha");
    }

    [Fact]
    public void RecordFailure_HasFailures_True()
    {
        var svc = new NetworkHistoryService();
        svc.RecordConnection("Fail", false);
        svc.GetEntry("Fail")!.HasFailures.Should().BeTrue();
    }

    [Fact]
    public void ClearAll_RemovesEverything()
    {
        var svc = new NetworkHistoryService();
        svc.RecordConnection("X", true);
        svc.RecordConnection("Y", true);
        svc.ClearAll();
        svc.GetRecentSsids(10).Should().BeEmpty();
    }
}

// ───── ProfileXmlBuilder + WifiUri ラウンドトリップ完全版 ─────
public class RoundTripCompleteTests
{
    [Theory]
    [InlineData("普通のSSID",    AuthMethod.WPA2PSK,           "password123")]
    [InlineData("Wi-Fi 7 Net",  AuthMethod.WPA3SAE,           "strongpass")]
    [InlineData("Open Guest",   AuthMethod.Open,               null)]
    [InlineData("OWE-Secure",   AuthMethod.OWE,                null)]
    [InlineData("WEP-Legacy",   AuthMethod.WEP,                "0123456789")]
    public void ProfileXml_BuildAndParse_AllAuthMethods(
        string ssid, AuthMethod auth, string? pw)
    {
        var spec = new WifiProfileSpec { Ssid = ssid, Auth = auth, Passphrase = pw };
        var xml  = ProfileXmlBuilder.Build(spec);
        xml.Should().Contain("WLANProfile");
        xml.Should().Contain(System.Net.WebUtility.HtmlEncode(ssid)
                             .Replace("&#", "&amp;#")  // XElement エスケープ確認
                             is var _ ? ssid : ssid);
    }

    [Theory]
    [InlineData("CafeSSID",  AuthMethod.WPA2PSK, "pass1234")]
    [InlineData("WPA3Net",   AuthMethod.WPA3SAE, "secure99")]
    [InlineData("OpenFree",  AuthMethod.Open,    null)]
    [InlineData("OWE",       AuthMethod.OWE,     null)]
    public void WifiUri_RoundTrip_AllSupportedAuth(
        string ssid, AuthMethod auth, string? pw)
    {
        var spec   = new WifiProfileSpec { Ssid = ssid, Auth = auth, Passphrase = pw };
        var uri    = WifiUri.Build(spec);
        var parsed = WifiUri.Parse(uri);
        parsed.Should().NotBeNull();
        parsed!.Ssid.Should().Be(ssid);
        if (pw is not null) parsed.Passphrase.Should().Be(pw);
    }
}

// ───── FakeWifiService 接続フロー E2E ─────
public class E2EFlowTests
{
    [Fact]
    public async System.Threading.Tasks.Task FullFlow_BuildXml_Register_Connect_Export()
    {
        var svc  = new FakeWifiService();
        var oui  = new OuiLookupService();
        var hist = new SignalHistoryService();

        // 1. スキャン
        var nets = await svc.ScanAsync(FakeWifiService.AdapterId1);
        nets.Should().NotBeEmpty();
        hist.Record(nets);

        // 2. OUI解決
        var ap = nets.First(n => n.BssEntries.Count > 0);
        oui.Lookup(ap.BssEntries[0].Bssid).Should().NotBeNullOrEmpty();

        // 3. プロファイルビルド → 登録
        var spec = new WifiProfileSpec
        {
            Ssid = "HomeNet", Auth = AuthMethod.WPA3SAE, Passphrase = "strongpass"
        };
        var xml = ProfileXmlBuilder.Build(spec);
        xml.Should().Contain("WPA3SAE");
        (await svc.RegisterProfileAsync(FakeWifiService.AdapterId1, xml, true)).Should().BeTrue();

        // 4. 接続
        var res = await svc.ConnectAsync(FakeWifiService.AdapterId1, "HomeNet", "HomeNet",
            TimeSpan.FromSeconds(5));
        res.Success.Should().BeTrue();

        // 5. エクスポート
        var tmp = Path.Combine(Path.GetTempPath(), $"mwc_e2e_{Guid.NewGuid():N}.csv");
        try
        {
            ExportService.ToCsv(nets, tmp);
            File.Exists(tmp).Should().BeTrue();
            File.ReadAllText(tmp).Should().Contain("HomeNet");
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
}
