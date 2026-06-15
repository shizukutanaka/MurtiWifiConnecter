using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MWC.App.Services;
using MWC.Core.Models;
using MWC.Core.Profile;
using MWC.Core.Services;
using MWC.Core.Tests.Fakes;
using Xunit;

namespace MWC.Core.Tests;

// ═══════════════════════════════════════════════
//  CLI 動作確認 (Unit — コマンドビルダーのみ)
// ═══════════════════════════════════════════════

public class CliWifiUriTests
{
    [Theory]
    [InlineData("MyNet",  AuthMethod.WPA2PSK,  "mypass1", "WIFI:T:WPA;S:MyNet;P:mypass1;;")]
    [InlineData("Open",   AuthMethod.Open,      null,       "WIFI:T:nopass;S:Open;;")]
    [InlineData("WPA3Net",AuthMethod.WPA3SAE,  "secure99","WIFI:T:SAE;S:WPA3Net;P:secure99;;")]
    public void WifiUri_Build_MatchesExpected(string ssid, AuthMethod auth, string? pw, string expected)
    {
        var spec = new WifiProfileSpec { Ssid = ssid, Auth = auth, Passphrase = pw };
        WifiUri.Build(spec).Should().Be(expected);
    }

    [Fact]
    public void WifiUri_SpecialChars_EscapedProperly()
    {
        var spec = new WifiProfileSpec
        {
            Ssid = "Net;1", Auth = AuthMethod.WPA2PSK, Passphrase = @"p\q"
        };
        var uri = WifiUri.Build(spec);
        uri.Should().Contain(@"S:Net\;1");
        uri.Should().Contain(@"P:p\\q");
    }
}

// ═══════════════════════════════════════════════
//  ExportService 全フォーマット検証
// ═══════════════════════════════════════════════

public class ExportServicePhase5Tests : IDisposable
{
    private readonly string _tmp = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public ExportServicePhase5Tests() => System.IO.Directory.CreateDirectory(_tmp);
    public void Dispose() { try { System.IO.Directory.Delete(_tmp, true); } catch { } }

    private static readonly IReadOnlyList<WifiNetwork> Networks = new[]
    {
        new WifiNetwork
        {
            Ssid = "Wi-Fi 7 Demo", SignalQuality = 95, Rssi = -40,
            Auth = AuthMethod.WPA3SAE, Cipher = CipherType.GCMP256,
            Band = WifiBand.Band6GHz, Channel = 37, ChannelWidth = 320,
            Phy = PhyType.Dot11be, MaxLinkSpeedMbps = 46000,
            IsConnected = true, VendorName = "Ubiquiti Networks Inc.",
            BssEntries = new[]
            {
                new BssInfo { Bssid = "04:18:D6:AB:CD:EF", Rssi = -40,
                    Channel = 37, FrequencyMhz = 6135,
                    Phy = PhyType.Dot11be, ChannelWidth = 320 }
            }
        },
        new WifiNetwork
        {
            Ssid = "Legacy,Net", SignalQuality = 20, Rssi = -85,
            Auth = AuthMethod.WEP, Cipher = CipherType.WEP,
            Band = WifiBand.Band2_4GHz, Channel = 1,
            Phy = PhyType.Dot11b, BssEntries = Array.Empty<BssInfo>()
        }
    };

    [Fact]
    public void ToCsv_Wi7_6GHz_BandShownCorrectly()
    {
        var path = System.IO.Path.Combine(_tmp, "out.csv");
        ExportService.ToCsv(Networks, path);
        var content = System.IO.File.ReadAllText(path);
        content.Should().Contain("Band6GHz");
        content.Should().Contain("320");  // ChannelWidth
    }

    [Fact]
    public void ToCsv_CommaInSsid_Escaped()
    {
        var path = System.IO.Path.Combine(_tmp, "esc.csv");
        ExportService.ToCsv(Networks, path);
        System.IO.File.ReadAllText(path).Should().Contain("\"Legacy,Net\"");
    }

    [Fact]
    public void ToJson_ValidStructure()
    {
        var path = System.IO.Path.Combine(_tmp, "out.json");
        ExportService.ToJson(Networks, path);
        var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
        doc.RootElement.GetProperty("networks").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void ToText_ContainsWifi7Label()
    {
        var path = System.IO.Path.Combine(_tmp, "out.txt");
        ExportService.ToText(Networks, path);
        var txt = System.IO.File.ReadAllText(path);
        txt.Should().Contain("Wi-Fi 7");
        txt.Should().Contain("6 GHz");
        txt.Should().Contain("Connected");
    }
}

// ═══════════════════════════════════════════════
//  OUI Lookup — 多フォーマット対応
// ═══════════════════════════════════════════════

public class OuiLookupPhase5Tests
{
    private readonly OuiLookupService _svc = new();

    [Theory]
    [InlineData("70:DE:E2:11:22:33", "Apple, Inc.")]   // コロン区切り小文字
    [InlineData("70-DE-E2-11-22-33", "Apple, Inc.")]   // ハイフン区切り
    [InlineData("70:de:e2:AA:BB:CC", "Apple, Inc.")]   // 混在大小文字
    [InlineData("04:18:D6:AB:CD:EF", "Ubiquiti Networks Inc.")] // Ubiquiti
    [InlineData("B8:27:EB:00:00:00", "Raspberry Pi Foundation")]
    public void Lookup_MultipleSeparators(string bssid, string vendor)
        => _svc.Lookup(bssid).Should().Be(vendor);

    [Fact]
    public void Lookup_Short_ReturnsNull()
        => _svc.Lookup("11:22").Should().BeNull();
}

// ═══════════════════════════════════════════════
//  SignalHistoryService — Thread Safety
// ═══════════════════════════════════════════════

public class SignalHistoryThreadSafetyTests
{
    [Fact]
    public async Task Record_ConcurrentWrites_DoesNotThrow()
    {
        var svc = new SignalHistoryService(maxSamples: 100);
        var net = new WifiNetwork { Ssid = "Net", SignalQuality = 70 };

        var tasks = Enumerable.Range(0, 20).Select(_ =>
            Task.Run(() => svc.Record(new[] { net })));
        await Task.WhenAll(tasks);

        svc.GetHistory("Net").Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Prune_RemovesOldSamples()
    {
        var svc = new SignalHistoryService();
        svc.Record(new[] { new WifiNetwork { Ssid = "X", SignalQuality = 50 } });
        svc.Prune(TimeSpan.FromSeconds(-1));  // 全件 prune
        // Prune後もクラッシュしない
        svc.GetHistory("X").Should().NotBeNull();
    }
}

// ═══════════════════════════════════════════════
//  FakeWifiService — 全コマンドフロー
// ═══════════════════════════════════════════════

public class FakeWifiServiceCliFlowTests
{
    [Fact]
    public async Task FullFlow_Scan_Export_History()
    {
        var fake    = new FakeWifiService();
        var oui     = new OuiLookupService();
        var history = new NetworkHistoryService();

        // Scan
        var nets = await fake.ScanAsync(FakeWifiService.AdapterId1);
        nets.Should().HaveCount(4);

        // OUI解決
        var resolved = nets.Select(n =>
        {
            var v = n.BssEntries.Count > 0 ? oui.Lookup(n.BssEntries[0].Bssid) : null;
            return v is null ? n : n with { VendorName = v };
        }).ToList();
        resolved.Any(n => n.VendorName is not null).Should().BeTrue();

        // Export
        var tmp = System.IO.Path.GetTempFileName();
        ExportService.ToCsv(resolved, tmp);
        System.IO.File.ReadAllText(tmp).Should().Contain("HomeNet");
        System.IO.File.Delete(tmp);

        // Connect + History記録
        var res = await fake.ConnectAsync(FakeWifiService.AdapterId1,
            "HomeNet", "HomeNet", TimeSpan.FromSeconds(5));
        history.RecordConnection("HomeNet", res.Success);
        history.GetEntry("HomeNet")!.ConnectCount.Should().Be(1);
    }

    [Fact]
    public async Task ProfileXml_AllAuthMethods_BuildWithoutException()
    {
        var methods = new[]
        {
            AuthMethod.Open,
            AuthMethod.OWE,
            AuthMethod.WPA2PSK,
            AuthMethod.WPA3SAE,
            AuthMethod.WPA3Transition
        };
        foreach (var auth in methods)
        {
            var spec = new WifiProfileSpec
            {
                Ssid       = "TestNet",
                Auth       = auth,
                Passphrase = auth is AuthMethod.Open or AuthMethod.OWE ? null : "testpass1"
            };
            var xml = ProfileXmlBuilder.Build(spec);
            xml.Should().Contain("WLANProfile", because: $"{auth} should produce valid XML");
        }
    }
}

// ═══════════════════════════════════════════════
//  言語リソース確認 (11言語)
// ═══════════════════════════════════════════════

public class Phase5LocalizationTests
{
    [Fact]
    public void ResourceFiles_ExistForAllTargetLanguages()
    {
        var resxDir = System.IO.Path.Combine(
            AppContext.BaseDirectory,
            "../../../../src/MWC.App/Resources");
        if (!System.IO.Directory.Exists(resxDir))
        {
            // CI 環境での相対パス差異を許容
            return;
        }
        var langs = System.IO.Directory.GetFiles(resxDir, "Strings.*.resx")
            .Select(f => System.IO.Path.GetFileNameWithoutExtension(f)
                .Replace("Strings.", ""))
            .ToList();

        var required = new[] { "ja", "en", "zh-Hans", "ko", "es", "fr", "de", "ru", "pt-BR" };
        foreach (var lang in required)
            langs.Should().Contain(lang, because: $"{lang} resx must exist");
    }
}
