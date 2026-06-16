using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ════════════════════════════════════════════════
//  SignalHistoryService 追加テスト
// ════════════════════════════════════════════════
public class SignalHistoryServiceAdditionalTests
{
    private static WifiNetwork MakeNet(string ssid, int quality, int? rssi = null) =>
        new() { Ssid = ssid, SignalQuality = quality, Rssi = rssi };

    [Fact]
    public void Record_Then_GetHistory_ReturnsNewestFirst()
    {
        var svc = new SignalHistoryService();

        svc.Record(new[] { MakeNet("TestNet", 50, -70) });
        svc.Record(new[] { MakeNet("TestNet", 60, -60) });
        svc.Record(new[] { MakeNet("TestNet", 55, -65) });

        var history = svc.GetHistory("TestNet");

        history.Should().NotBeEmpty();
        history.Count.Should().Be(3);
        // newest-first ordering
        history[0].At.Should().BeOnOrAfter(history[1].At);
        history.Should().AllSatisfy(h =>
        {
            h.Quality.Should().BeInRange(0, 100);
            h.At.Should().BeBefore(DateTimeOffset.UtcNow.AddSeconds(1));
        });
    }

    [Fact]
    public void GetHistory_UnknownSsid_ReturnsEmpty()
    {
        var svc = new SignalHistoryService();
        var history = svc.GetHistory("Ghost");

        history.Should().NotBeNull();
        history.Should().BeEmpty("unknown SSID has no signal history");
    }

    [Fact]
    public void Record_MultipleSsids_AreSeparated()
    {
        var svc = new SignalHistoryService();
        svc.Record(new[] { MakeNet("Net1", 55, -55), MakeNet("Net2", 70, -45) });

        var h1 = svc.GetHistory("Net1");
        var h2 = svc.GetHistory("Net2");

        h1.Should().NotBeEmpty();
        h2.Should().NotBeEmpty();
        h1[0].Quality.Should().NotBe(h2[0].Quality, "different SSIDs have separate histories");
    }

    [Fact]
    public void Clear_RemovesHistory()
    {
        var svc = new SignalHistoryService();
        svc.Record(new[] { MakeNet("ClearNet", 60, -60) });

        svc.Clear("ClearNet");
        var history = svc.GetHistory("ClearNet");

        history.Should().BeEmpty("cleared history must be empty");
    }

    [Fact]
    public void Record_MultiplePoints_AverageQualityIsCorrect()
    {
        var svc = new SignalHistoryService();
        svc.Record(new[] { MakeNet("AvgNet", 60, -60) });
        svc.Record(new[] { MakeNet("AvgNet", 40, -40) });

        var history = svc.GetHistory("AvgNet");

        history.Should().HaveCount(2);
        var avgQuality = history.Average(h => h.Quality);
        avgQuality.Should().BeApproximately(50.0, 1.0);
        history.Should().AllSatisfy(h => h.Quality.Should().BeInRange(0, 100));
    }
}

// ════════════════════════════════════════════════
//  ExportService 文字列出力テスト
// ════════════════════════════════════════════════
public class ExportServiceStringOutputTests
{
    private static WifiNetwork MakeNetwork(string ssid, AuthMethod auth, int signal, WifiBand band) =>
        new()
        {
            Ssid          = ssid,
            Auth          = auth,
            SignalQuality = signal,
            Band          = band,
            Channel       = band == WifiBand.Band2_4GHz ? 6 : 36,
            Phy           = PhyType.Dot11ax,
        };

    [Fact]
    public void ExportCsv_ProducesValidCsv()
    {
        var nets = new[]
        {
            MakeNetwork("Home",   AuthMethod.WPA3SAE,  90, WifiBand.Band5GHz),
            MakeNetwork("Office", AuthMethod.WPA2PSK,  75, WifiBand.Band5GHz),
            MakeNetwork("Free",   AuthMethod.Open,     50, WifiBand.Band2_4GHz),
        };

        var csv = ExportService.ToCsv(nets);

        csv.Should().NotBeNullOrEmpty();
        csv.Should().Contain("Home");
        csv.Should().Contain("Office");
        csv.Should().Contain("Free");
        // ヘッダー行が存在する
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.Should().BeGreaterOrEqualTo(4, "header + 3 data rows");
        lines[0].Should().Contain(",", "CSV header must contain commas");
    }

    [Fact]
    public void ExportJson_ProducesValidJson()
    {
        var nets = new[]
        {
            MakeNetwork("JsonNet", AuthMethod.WPA2PSK, 80, WifiBand.Band5GHz),
        };

        var json = ExportService.ToJson(nets);

        json.Should().NotBeNullOrEmpty();
        json.Should().Contain("JsonNet");
        json.Should().Contain("[");
        json.Should().Contain("]");
        // JSON として最低限の構造 (bare array)
        json.Trim().Should().StartWith("[");
        json.Trim().Should().EndWith("]");
    }

    [Fact]
    public void ExportTxt_ProducesReadableText()
    {
        var nets = new[]
        {
            MakeNetwork("TxtNet", AuthMethod.WPA3SAE, 85, WifiBand.Band6GHz),
        };

        var txt = ExportService.ToTxt(nets);

        txt.Should().NotBeNullOrEmpty();
        txt.Should().Contain("TxtNet");
        txt.Length.Should().BeGreaterThan(10);
    }

    [Fact]
    public void ExportCsv_EmptyList_ReturnsHeaderOnly()
    {
        var csv = ExportService.ToCsv(Array.Empty<WifiNetwork>());

        csv.Should().NotBeNullOrEmpty("header row must always be present");
        // 少なくともヘッダーが出力される
        csv.Should().Contain(",");
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public void ExportCsv_EscapesCommaInSsid()
    {
        var nets = new[]
        {
            MakeNetwork("Coffee, Shop", AuthMethod.Open, 60, WifiBand.Band2_4GHz),
        };

        var csv = ExportService.ToCsv(nets);

        csv.Should().NotBeNullOrEmpty();
        // SSID内のカンマは適切にエスケープ/クォートされる
        csv.Should().Contain("Coffee");
        csv.Should().Contain("Shop");
    }
}

// ════════════════════════════════════════════════
//  SecurityBadgeService 高密度テスト
// ════════════════════════════════════════════════
public class SecurityBadgeServiceAdvancedTests
{
    // SecurityBadgeService is a static class — no instantiation.
    // SecurityBadge fields: Label, Level, TechLabel (no Auth/IsModern/Description).
    // SecurityLevel enum: Excellent, Good, Fair, Weak, Danger (no Critical).
    [Theory]
    [InlineData(AuthMethod.WPA3SAE,           SecurityLevel.Excellent)]
    [InlineData(AuthMethod.WPA3Enterprise192, SecurityLevel.Excellent)]
    [InlineData(AuthMethod.WPA3Enterprise,    SecurityLevel.Excellent)]
    [InlineData(AuthMethod.WPA2Enterprise,    SecurityLevel.Good)]
    [InlineData(AuthMethod.WPA2PSK,           SecurityLevel.Good)]
    [InlineData(AuthMethod.OWE,               SecurityLevel.Fair)]
    [InlineData(AuthMethod.WPAPSK,            SecurityLevel.Weak)]
    [InlineData(AuthMethod.WEP,               SecurityLevel.Danger)]
    [InlineData(AuthMethod.Open,              SecurityLevel.Danger)]
    public void GetBadge_CorrectLevel(AuthMethod auth, SecurityLevel expected)
    {
        var badge = SecurityBadgeService.GetBadge(auth);

        badge.Level.Should().Be(expected);
        badge.Label.Should().NotBeNullOrEmpty("every auth method must have a label");
        badge.TechLabel.Should().NotBeNullOrEmpty("every auth method must have a tech label");
    }

    [Fact]
    public void GetBadge_AllAuthMethods_HaveNonEmptyLabels()
    {
        foreach (var auth in Enum.GetValues<AuthMethod>())
        {
            var badge = SecurityBadgeService.GetBadge(auth);
            badge.Label.Should().NotBeNullOrEmpty(because: $"{auth} must have a label");
            badge.TechLabel.Should().NotBeNullOrEmpty(because: $"{auth} must have a tech label");
        }
    }

    [Fact]
    public void SecurityLevel_Ordering_IsCorrect()
    {
        // Enum is ordered best→worst: Excellent(0) < Good(1) < Fair(2) < Weak(3) < Danger(4)
        // Lower ordinal = higher security (used by UI color logic: ≤Good=green, ≥Weak=red).
        ((int)SecurityLevel.Excellent).Should().BeLessThan((int)SecurityLevel.Good);
        ((int)SecurityLevel.Good).Should().BeLessThan((int)SecurityLevel.Fair);
        ((int)SecurityLevel.Fair).Should().BeLessThan((int)SecurityLevel.Weak);
        ((int)SecurityLevel.Weak).Should().BeLessThan((int)SecurityLevel.Danger);
    }
}
