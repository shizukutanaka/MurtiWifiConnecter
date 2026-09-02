using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ───── OuiLookupService ─────
public class OuiLookupServiceTests
{
    private readonly OuiLookupService _sut = new();

    [Theory]
    [InlineData("70:DE:E2:11:22:33", "Apple, Inc.")]
    [InlineData("04:18:D6:AA:BB:CC", "Ubiquiti Networks Inc.")]
    [InlineData("1C:3B:F3:01:02:03", "TP-Link Technologies")]
    [InlineData("B8:27:EB:00:11:22", "Raspberry Pi Foundation")]
    public void Lookup_KnownBssid_ReturnsVendor(string bssid, string expected)
        => _sut.Lookup(bssid).Should().Be(expected);

    [Fact]
    public void Lookup_Unknown_ReturnsNull()
        => _sut.Lookup("FF:FF:FF:FF:FF:FF").Should().BeNull();

    [Fact]
    public void Lookup_Null_ReturnsNull()
        // オーバーロードを明示する。`null!` だけだと Lookup(string) と
        // Lookup(ReadOnlySpan<byte>) の双方に変換可能で曖昧になり得る。
        => _sut.Lookup((string)null!).Should().BeNull();

    [Fact]
    public void Lookup_DashSeparated_Works()
        => _sut.Lookup("70-DE-E2-11-22-33").Should().Be("Apple, Inc.");

    [Fact]
    public void Lookup_LowerCase_Works()
        => _sut.Lookup("70:de:e2:11:22:33").Should().Be("Apple, Inc.");
}

// ───── SignalHistoryService ─────
public class SignalHistoryServiceTests
{
    private static WifiNetwork MakeNet(string ssid, int quality, int rssi = -60) =>
        new() { Ssid = ssid, SignalQuality = quality, Rssi = rssi };

    [Fact]
    public void Record_And_GetHistory_ReturnsSamples()
    {
        var sut = new SignalHistoryService(100);
        sut.Record(new[] { MakeNet("TestNet", 75) });
        sut.Record(new[] { MakeNet("TestNet", 80) });

        var h = sut.GetHistory("TestNet");
        h.Should().HaveCount(2);
        h[0].Quality.Should().Be(80);  // 新しい順
        h[1].Quality.Should().Be(75);
    }

    [Fact]
    public void RingBuffer_WrapsAround_WhenFull()
    {
        var sut = new SignalHistoryService(maxSamples: 3);
        for (int i = 0; i < 5; i++)
            sut.Record(new[] { MakeNet("X", i * 10) });

        var h = sut.GetHistory("X");
        h.Should().HaveCount(3);
        h[0].Quality.Should().Be(40);  // 最新
        h[2].Quality.Should().Be(20);  // 最古
    }

    [Fact]
    public void GetHistory_UnknownSsid_ReturnsEmpty()
        => new SignalHistoryService().GetHistory("nobody").Should().BeEmpty();

    [Fact]
    public void Clear_RemovesEntries()
    {
        var sut = new SignalHistoryService();
        sut.Record(new[] { MakeNet("A", 50) });
        sut.Clear("A");
        sut.GetHistory("A").Should().BeEmpty();
    }

    [Fact]
    public void Record_EvictsOldestSsid_WhenOverCapacity()
    {
        // 上限2件: 3つ目の SSID を記録すると最も古い更新の SSID が退去する。
        // LastAt を確実に区別するため記録間に微小スリープを挟む。
        var sut = new SignalHistoryService(maxSamples: 10, maxSsids: 2);
        sut.Record(new[] { MakeNet("Old", 10) });   // 最古の更新
        System.Threading.Thread.Sleep(10);
        sut.Record(new[] { MakeNet("Mid", 20) });
        System.Threading.Thread.Sleep(10);
        sut.Record(new[] { MakeNet("New", 30) });   // ここで "Old" が退去

        sut.GetHistory("Old").Should().BeEmpty(because: "oldest SSID is evicted past the cap");
        sut.GetHistory("Mid").Should().HaveCount(1);
        sut.GetHistory("New").Should().HaveCount(1);
    }

    [Fact]
    public void Prune_RemovesEmptiedBuffersFromDictionary()
    {
        var sut = new SignalHistoryService(maxSamples: 10);
        sut.Record(new[] { MakeNet("Stale", 40) });
        // 全サンプルが olderThan より古いので空になり辞書から消える
        System.Threading.Thread.Sleep(5);
        sut.Prune(TimeSpan.Zero);
        sut.GetHistory("Stale").Should().BeEmpty();
    }
}

// ───── ExportService ─────
public class ExportServiceTests : IDisposable
{
    private readonly string _tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public ExportServiceTests() => Directory.CreateDirectory(_tmpDir);
    public void Dispose() { try { Directory.Delete(_tmpDir, true); } catch { } }

    private static IReadOnlyList<WifiNetwork> SampleNetworks() =>
    [
        new WifiNetwork
        {
            Ssid = "Alpha",
            SignalQuality = 90, Rssi = -45,
            Auth = AuthMethod.WPA3SAE, Cipher = CipherType.AES,
            Band = WifiBand.Band5GHz, Channel = 36, ChannelWidth = 80,
            Phy = PhyType.Dot11ax, MaxLinkSpeedMbps = 1201, IsConnected = true,
            VendorName = "Apple, Inc.",
            BssEntries = [new BssInfo { Bssid = "70:DE:E2:AA:BB:CC", Rssi = -45, Channel = 36,
                FrequencyMhz = 5180, Phy = PhyType.Dot11ax, ChannelWidth = 80 }]
        },
        new WifiNetwork
        {
            Ssid = "Beta,with,commas", SignalQuality = 40, Rssi = -75,
            Auth = AuthMethod.WPA2PSK, Cipher = CipherType.AES,
            Band = WifiBand.Band2_4GHz, Channel = 6, Phy = PhyType.Dot11n,
            BssEntries = []
        }
    ];

    [Fact]
    public void ToCsv_CreatesFile_WithHeader()
    {
        var path = Path.Combine(_tmpDir, "scan.csv");
        ExportService.ToCsv(SampleNetworks(), path);
        File.Exists(path).Should().BeTrue();
        var lines = File.ReadAllLines(path);
        lines[0].Should().Contain("SSID").And.Contain("Signal");
        lines.Length.Should().Be(3);  // header + 2 rows
    }

    [Fact]
    public void ToCsv_EscapesCommaInSsid()
    {
        var path = Path.Combine(_tmpDir, "escape.csv");
        ExportService.ToCsv(SampleNetworks(), path);
        var content = File.ReadAllText(path);
        content.Should().Contain("\"Beta,with,commas\"");
    }

    [Fact]
    public void ToJson_CreatesValidJson()
    {
        var path = Path.Combine(_tmpDir, "scan.json");
        ExportService.ToJson(SampleNetworks(), path);
        File.Exists(path).Should().BeTrue();
        var json = File.ReadAllText(path);
        json.Should().Contain("scannedAt").And.Contain("networks");
        // JSON パース可能
        var doc = System.Text.Json.JsonDocument.Parse(json);
        doc.RootElement.GetProperty("networks").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void ToText_CreatesReport_WithBars()
    {
        var path = Path.Combine(_tmpDir, "scan.txt");
        ExportService.ToText(SampleNetworks(), path);
        var txt = File.ReadAllText(path);
        txt.Should().Contain("Alpha").And.Contain("Connected").And.Contain("█");
    }
}

// ───── PhyTypeExtensions ─────
public class PhyTypeExtensionsTests
{
    [Theory]
    [InlineData(PhyType.Dot11ax, "Wi-Fi 6/6E")]
    [InlineData(PhyType.Dot11be, "Wi-Fi 7")]
    [InlineData(PhyType.Dot11ac, "Wi-Fi 5")]
    [InlineData(PhyType.Dot11n,  "Wi-Fi 4")]
    public void ToShortLabel_ReturnsExpected(PhyType phy, string expected)
        => phy.ToShortLabel().Should().Be(expected);

    [Fact]
    public void ToGenerationLabel_ContainsIeee802()
        => PhyType.Dot11ax.ToGenerationLabel().Should().Contain("802.11ax");
}
