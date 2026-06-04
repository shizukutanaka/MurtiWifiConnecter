using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Profile;
using MWC.Core.Services;
using MWC.Core.Tests.Fakes;
using Xunit;

namespace MWC.Core.Tests;

public class FakeWifiServiceIntegrationTests
{
    // ─── GetAdapters ───
    [Fact]
    public async Task GetAdapters_ReturnsTwoFakeAdapters()
    {
        var svc = new FakeWifiService();
        var ads = await svc.GetAdaptersAsync();
        ads.Should().HaveCount(2);
        ads[0].Name.Should().Be("Wi-Fi");
        ads[0].State.Should().Be(AdapterState.Connected);
    }

    // ─── Scan ───
    [Fact]
    public async Task Scan_Adapter1_Returns4Networks()
    {
        var svc = new FakeWifiService();
        var nets = await svc.ScanAsync(FakeWifiService.AdapterId1);
        nets.Should().HaveCount(4);
        nets.Any(n => n.IsConnected).Should().BeTrue();
    }

    [Fact]
    public async Task Scan_Adapter2_ReturnsEmpty()
    {
        var svc = new FakeWifiService();
        var nets = await svc.ScanAsync(FakeWifiService.AdapterId2);
        nets.Should().BeEmpty();
    }

    // ─── WiFi 7 / 6GHz ───
    [Fact]
    public async Task Scan_ContainsWifi7_6GHz()
    {
        var svc = new FakeWifiService();
        var nets = await svc.ScanAsync(FakeWifiService.AdapterId1);
        var wifi7 = nets.First(n => n.Ssid == "WiFi7-Test");
        wifi7.Phy.Should().Be(PhyType.Dot11be);
        wifi7.Band.Should().Be(WifiBand.Band6GHz);
        wifi7.ChannelWidth.Should().Be(320);
        wifi7.Phy.ToShortLabel().Should().Be("Wi-Fi 7");
    }

    // ─── Connect ───
    [Fact]
    public async Task Connect_Success_ReturnsOk()
    {
        var svc = new FakeWifiService();
        var res = await svc.ConnectAsync(FakeWifiService.AdapterId1, "HomeNet", "HomeNet",
            TimeSpan.FromSeconds(5));
        res.Success.Should().BeTrue();
        res.HasInternet.Should().BeTrue();
        svc.ConnectCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Connect_Failure_ReturnsFail()
    {
        var svc = new FakeWifiService
        {
            NextConnectResult = ConnectionResult.Fail(ConnectionFailure.BadCredentials)
        };
        var res = await svc.ConnectAsync(FakeWifiService.AdapterId1, "X", "X",
            TimeSpan.FromSeconds(5));
        res.Success.Should().BeFalse();
        res.Failure.Should().Be(ConnectionFailure.BadCredentials);
    }

    // ─── SignalHistory + OUI 統合 ───
    [Fact]
    public async Task OuiLookup_OnFakeNetworks_ResolvesApple()
    {
        var svc = new FakeWifiService();
        var oui = new OuiLookupService();
        var nets = await svc.ScanAsync(FakeWifiService.AdapterId1);

        var homeNet = nets.First(n => n.Ssid == "HomeNet");
        var bssid   = homeNet.BssEntries[0].Bssid;  // 70:DE:E2:...
        oui.Lookup(bssid).Should().Be("Apple, Inc.");
    }

    [Fact]
    public async Task SignalHistory_Record_StoreSamples()
    {
        var svc  = new FakeWifiService();
        var hist = new SignalHistoryService();

        var nets = await svc.ScanAsync(FakeWifiService.AdapterId1);
        hist.Record(nets);
        hist.Record(nets);  // 2回

        var samples = hist.GetHistory("HomeNet");
        samples.Should().HaveCount(2);
        samples[0].Quality.Should().Be(90);
    }

    // ─── Export ───
    [Fact]
    public async Task Export_AllFormats_Succeed()
    {
        var svc  = new FakeWifiService();
        var nets = await svc.ScanAsync(FakeWifiService.AdapterId1);
        var tmp  = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);

        try
        {
            ExportService.ToCsv(nets,  Path.Combine(tmp, "scan.csv"));
            ExportService.ToJson(nets, Path.Combine(tmp, "scan.json"));
            ExportService.ToText(nets, Path.Combine(tmp, "scan.txt"));

            File.Exists(Path.Combine(tmp, "scan.csv")).Should().BeTrue();
            File.Exists(Path.Combine(tmp, "scan.json")).Should().BeTrue();
            File.Exists(Path.Combine(tmp, "scan.txt")).Should().BeTrue();

            var txt = File.ReadAllText(Path.Combine(tmp, "scan.txt"));
            txt.Should().Contain("HomeNet")
               .And.Contain("Wi-Fi 7")    // WiFi7-Test の PHY ラベル
               .And.Contain("6 GHz");     // Band ラベル
        }
        finally { Directory.Delete(tmp, true); }
    }

    // ─── ProfileXmlBuilder + FakeService 接続往復 ───
    [Fact]
    public async Task FullConnectFlow_BuildXml_Register_Connect()
    {
        var svc  = new FakeWifiService();
        var spec = new WifiProfileSpec
        {
            Ssid = "HomeNet", Auth = AuthMethod.WPA3SAE, Passphrase = "strongpass"
        };

        var xml = ProfileXmlBuilder.Build(spec);
        xml.Should().Contain("WPA3SAE");

        var reg = await svc.RegisterProfileAsync(FakeWifiService.AdapterId1, xml, true);
        reg.Should().BeTrue();

        var res = await svc.ConnectAsync(FakeWifiService.AdapterId1, "HomeNet", "HomeNet",
            TimeSpan.FromSeconds(5));
        res.Success.Should().BeTrue();
    }

    // ─── WifiUri ラウンドトリップ ───
    [Fact]
    public void WifiUri_Wpa3_RoundTrip()
    {
        var spec = new WifiProfileSpec
        {
            Ssid = "Test6G", Auth = AuthMethod.WPA3SAE, Passphrase = "pass1234"
        };
        var uri    = WifiUri.Build(spec);
        var parsed = WifiUri.Parse(uri);
        parsed!.Auth.Should().Be(AuthMethod.WPA3SAE);
        parsed.Ssid.Should().Be("Test6G");
    }
}
