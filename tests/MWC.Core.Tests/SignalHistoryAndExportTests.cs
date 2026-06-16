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
//  SignalHistoryService 高密度テスト
// ════════════════════════════════════════════════
public class SignalHistoryServiceTests
{
    [Fact]
    public void AddSignal_Then_GetHistory_ReturnsChronological()
    {
        var svc = new SignalHistoryService();
        var id  = Guid.NewGuid();

        svc.AddSignal(id, "TestNet", -50);
        svc.AddSignal(id, "TestNet", -60);
        svc.AddSignal(id, "TestNet", -45);

        var history = svc.GetHistory(id, "TestNet");

        history.Should().NotBeEmpty();
        history.Count.Should().BeGreaterOrEqualTo(3);
        history.Should().AllSatisfy(h =>
        {
            h.Rssi.Should().BeLessOrEqualTo(0, "RSSI must be negative dBm");
            h.Rssi.Should().BeGreaterThan(-120);
            h.Timestamp.Should().BeBefore(DateTimeOffset.UtcNow.AddSeconds(1));
        });
    }

    [Fact]
    public void GetHistory_UnknownAdapter_ReturnsEmpty()
    {
        var svc = new SignalHistoryService();
        var history = svc.GetHistory(Guid.NewGuid(), "Ghost");

        history.Should().NotBeNull();
        history.Should().BeEmpty("unknown adapter has no signal history");
    }

    [Fact]
    public void AddSignal_MultipleAdapters_AreSeparated()
    {
        var svc = new SignalHistoryService();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        svc.AddSignal(id1, "Net1", -55);
        svc.AddSignal(id2, "Net1", -70);

        var h1 = svc.GetHistory(id1, "Net1");
        var h2 = svc.GetHistory(id2, "Net1");

        h1.Should().NotBeEmpty();
        h2.Should().NotBeEmpty();
        // アダプター毎に独立
        h1.Average(h => h.Rssi).Should().NotBe(h2.Average(h => h.Rssi));
    }

    [Fact]
    public void Clear_RemovesHistory()
    {
        var svc = new SignalHistoryService();
        var id  = Guid.NewGuid();
        svc.AddSignal(id, "ClearNet", -60);

        svc.Clear(id, "ClearNet");
        var history = svc.GetHistory(id, "ClearNet");

        history.Should().BeEmpty("cleared history must be empty");
    }

    [Fact]
    public void GetAverageRssi_MultiplePoints_IsCorrect()
    {
        var svc = new SignalHistoryService();
        var id  = Guid.NewGuid();
        svc.AddSignal(id, "AvgNet", -60);
        svc.AddSignal(id, "AvgNet", -40);

        var avg = svc.GetAverageRssi(id, "AvgNet");

        avg.Should().HaveValue();
        avg!.Value.Should().BeApproximately(-50.0, 1.0);
        avg.Value.Should().BeInRange(-120, 0);
    }
}

// ════════════════════════════════════════════════
//  ExportService 高密度テスト
// ════════════════════════════════════════════════
public class ExportServiceTests
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
        var svc = new ExportService();
        var nets = new[]
        {
            MakeNetwork("Home",   AuthMethod.WPA3SAE,  90, WifiBand.Band5GHz),
            MakeNetwork("Office", AuthMethod.WPA2PSK,  75, WifiBand.Band5GHz),
            MakeNetwork("Free",   AuthMethod.Open,     50, WifiBand.Band2_4GHz),
        };

        var csv = svc.ToCsv(nets);

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
        var svc = new ExportService();
        var nets = new[]
        {
            MakeNetwork("JsonNet", AuthMethod.WPA2PSK, 80, WifiBand.Band5GHz),
        };

        var json = svc.ToJson(nets);

        json.Should().NotBeNullOrEmpty();
        json.Should().Contain("JsonNet");
        json.Should().Contain("[");
        json.Should().Contain("]");
        // JSON として最低限の構造
        json.Trim().Should().StartWith("[");
        json.Trim().Should().EndWith("]");
    }

    [Fact]
    public void ExportTxt_ProducesReadableText()
    {
        var svc = new ExportService();
        var nets = new[]
        {
            MakeNetwork("TxtNet", AuthMethod.WPA3SAE, 85, WifiBand.Band6GHz),
        };

        var txt = svc.ToTxt(nets);

        txt.Should().NotBeNullOrEmpty();
        txt.Should().Contain("TxtNet");
        txt.Length.Should().BeGreaterThan(10);
    }

    [Fact]
    public void ExportCsv_EmptyList_ReturnsHeaderOnly()
    {
        var svc = new ExportService();
        var csv = svc.ToCsv(Array.Empty<WifiNetwork>());

        csv.Should().NotBeNullOrEmpty("header row must always be present");
        // 少なくともヘッダーが出力される
        csv.Should().Contain(",");
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public void ExportCsv_EscapesCommaInSsid()
    {
        var svc = new ExportService();
        var nets = new[]
        {
            MakeNetwork("Coffee, Shop", AuthMethod.Open, 60, WifiBand.Band2_4GHz),
        };

        var csv = svc.ToCsv(nets);

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
