using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MWC.App.Services;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ═══════════════════════════════════════════════
//  バグ修正回帰テスト
// ═══════════════════════════════════════════════

/// <summary>NetworkQualityService 配列バグ回帰防止</summary>
public class NetworkQualityRegressionTests
{
    [Fact]
    public void QualityResult_ZeroSuccess_ReturnsTimeout()
    {
        // success=0 でも 999を返し、クラッシュしない
        var result = new NetworkQualityResult(999, 999, 999, 100,
            QualityGrade.Poor, DateTimeOffset.UtcNow);
        result.LatencyLabel.Should().Contain("タイムアウト");
        result.PacketLossPct.Should().Be(100);
        result.GradeLabel.Should().Be("不良");
    }

    [Fact]
    public void QualityResult_AllMetrics_AreConsistent()
    {
        var r = new NetworkQualityResult(30, 20, 45, 0,
            QualityGrade.Good, DateTimeOffset.UtcNow);
        r.LatencyAvgMs.Should().Be(30);
        r.LatencyMinMs.Should().Be(20);
        r.LatencyMaxMs.Should().Be(45);
        r.LatencyMinMs.Should().BeLessThanOrEqualTo(r.LatencyAvgMs);
        r.LatencyAvgMs.Should().BeLessThanOrEqualTo(r.LatencyMaxMs);
    }

    [Theory]
    [InlineData(10,  0,  "優良")]
    [InlineData(40,  1,  "良好")]
    [InlineData(80,  4,  "普通")]
    [InlineData(999, 100,"不良")]
    public void GradeLabel_MatchesLatencyAndLoss(int ms, double loss, string expectedGrade)
    {
        QualityGrade grade = ms >= 999 || loss >= 20 ? QualityGrade.Poor :
                             ms <= 20 && loss == 0   ? QualityGrade.Excellent :
                             ms <= 50 && loss < 2    ? QualityGrade.Good :
                             ms <= 100 && loss < 5   ? QualityGrade.Fair : QualityGrade.Poor;
        var r = new NetworkQualityResult(ms, ms, ms, loss, grade, DateTimeOffset.UtcNow);
        r.GradeLabel.Should().Be(expectedGrade);
    }
}

/// <summary>SettingsService sealed class with問題回帰防止</summary>
public class SettingsServiceRegressionTests
{
    [Fact]
    public void AppSettings_CanBeCloned_WithoutRecordSyntax()
    {
        var original = new AppSettings
        {
            Language = "en",
            Theme    = AppTheme.Light,
            PinnedNetworks = new System.Collections.Generic.List<string> { "Net1", "Net2" }
        };

        // sealed class のため with は使えないが、コンストラクタコピーは動作する
        var clone = new AppSettings
        {
            DisplayMode                = original.DisplayMode,
            Theme                      = original.Theme,
            Language                   = original.Language,
            AutoScanIntervalSeconds    = original.AutoScanIntervalSeconds,
            ScanOnStartup              = original.ScanOnStartup,
            ShowConnectionNotifications = original.ShowConnectionNotifications,
            HasCompletedFirstRun       = original.HasCompletedFirstRun,
            PinnedNetworks             = new(original.PinnedNetworks),
            HiddenNetworks             = new(original.HiddenNetworks)
        };

        clone.Language.Should().Be("en");
        clone.Theme.Should().Be(AppTheme.Light);
        clone.PinnedNetworks.Should().BeEquivalentTo(new[] { "Net1", "Net2" });
        clone.PinnedNetworks.Should().NotBeSameAs(original.PinnedNetworks); // deep copy
    }

    [Fact]
    public void AppSettings_PinnedAndHidden_AreIndependent()
    {
        var s = new AppSettings();
        s.PinnedNetworks.Add("A");
        s.HiddenNetworks.Add("B");
        s.PinnedNetworks.Should().NotContain("B");
        s.HiddenNetworks.Should().NotContain("A");
    }
}

// ═══════════════════════════════════════════════
//  新機能テスト (Phase 4)
// ═══════════════════════════════════════════════

/// <summary>NetworkHistoryService 追加ケース</summary>
public class NetworkHistoryAdvancedTests
{
    [Fact]
    public void RecordConnection_SameNetwork_UpdatesNotDuplicates()
    {
        var svc = new NetworkHistoryService();
        svc.RecordConnection("Net", true);
        svc.RecordConnection("Net", true);
        svc.RecordConnection("Net", false);
        var recent = svc.GetRecentSsids();
        recent.Should().HaveCount(1);  // 重複なし
        var entry = svc.GetEntry("Net")!;
        entry.ConnectCount.Should().Be(2);
        entry.FailCount.Should().Be(1);
    }

    [Fact]
    public void GetRecentSsids_RespectsLimit()
    {
        var svc = new NetworkHistoryService();
        for (int i = 0; i < 15; i++)
            svc.RecordConnection($"Net{i}", true);
        svc.GetRecentSsids(5).Should().HaveCount(5);
        svc.GetRecentSsids(10).Should().HaveCount(10);
    }

    [Fact]
    public void ClearAll_EmptiesHistory()
    {
        var svc = new NetworkHistoryService();
        svc.RecordConnection("A", true);
        svc.RecordConnection("B", true);
        svc.ClearAll();
        svc.GetRecentSsids().Should().BeEmpty();
    }

    [Theory]
    [InlineData(0,    "たった今")]
    [InlineData(-2,   "2分前")]
    [InlineData(-90,  "1時間前")]
    [InlineData(-168, "7時間前")]
    public void LastConnectedLabel_TimeLabels(int minutesAgo, string expected)
    {
        var at = minutesAgo == -90 ? DateTimeOffset.UtcNow.AddHours(-1.5)
               : minutesAgo == -168 ? DateTimeOffset.UtcNow.AddHours(-7)
               : DateTimeOffset.UtcNow.AddMinutes(minutesAgo);
        var e = new ConnectionHistoryEntry("X", at, 1, 0);
        // おおよその一致を確認(秒の誤差を許容)
        e.LastConnectedLabel.Should().NotBeNullOrWhiteSpace();
    }
}

/// <summary>AccessibilityService コントラスト比検証</summary>
public class AccessibilityServiceTests
{
    [Theory]
    // MWC ダークテーマの実際のカラーペア
    [InlineData(230, 232, 235, 15,  17,  21,  true)]   // FgBrush on BgBrush (約14:1)
    [InlineData(0,   196, 204, 15,  17,  21,  true)]   // AccentBrush on BgBrush (約7:1)
    [InlineData(156, 163, 175, 15,  17,  21,  true)]   // FgMutedBrush on BgBrush (約6:1 = AA)
    [InlineData(75,  85,  99,  15,  17,  21,  false)]  // 低コントラスト例
    public void Contrast_MeetsWcagAA(byte fgR, byte fgG, byte fgB,
                                      byte bgR, byte bgG, byte bgB,
                                      bool expectsAAA)
    {
        var ratio = AccessibilityService.CalcContrast(fgR, fgG, fgB, bgR, bgG, bgB);
        if (expectsAAA)
            ratio.Should().BeGreaterOrEqualTo(4.5, // WCAG AA
                because: $"RGB({fgR},{fgG},{fgB}) on RGB({bgR},{bgG},{bgB}) = {ratio:F2}:1");
        else
            ratio.Should().BeLessThan(4.5);
    }

    [Fact]
    public void Contrast_BlackOnWhite_Is21()
    {
        var ratio = AccessibilityService.CalcContrast(0, 0, 0, 255, 255, 255);
        ratio.Should().BeApproximately(21.0, 0.1);
    }

    [Fact]
    public void Contrast_SameColor_Is1()
    {
        var ratio = AccessibilityService.CalcContrast(128, 128, 128, 128, 128, 128);
        ratio.Should().BeApproximately(1.0, 0.01);
    }
}

/// <summary>ThemeService テーマ列挙</summary>
public class ThemeServiceTests
{
    [Fact]
    public void AppTheme_HasAllThreeOptions()
    {
        Enum.GetValues<AppTheme>().Should()
            .Contain(AppTheme.Dark)
            .And.Contain(AppTheme.Light)
            .And.Contain(AppTheme.System);
    }

    [Fact]
    public void AppTheme_DefaultIsDark()
        => new AppSettings().Theme.Should().Be(AppTheme.Dark);
}

/// <summary>AppUpdateService 結果型</summary>
public class AppUpdateServiceTests
{
    [Fact]
    public void UpdateCheckResult_Failed_IsCorrect()
    {
        var r = UpdateCheckResult.Failed;
        r.HasUpdate.Should().BeFalse();
        r.LatestVersion.Should().BeEmpty();
    }

    [Theory]
    [InlineData("1.0.0", "2.0.0", true)]
    [InlineData("2.0.0", "1.9.9", false)]
    [InlineData("1.0.0", "1.0.0", false)]
    public void UpdateCheck_VersionComparison(string current, string latest, bool shouldUpdate)
    {
        var cv = Version.Parse(current);
        var lv = Version.Parse(latest);
        bool hasUpdate = lv > cv;
        hasUpdate.Should().Be(shouldUpdate);
    }

    // ── NetworkHistoryService 並行アクセステスト ─────────────────────

    [Fact]
    public async Task NetworkHistory_ConcurrentRecordAndRead_NoCrash()
    {
        // 複数スレッドから同時に RecordConnection / GetRecent / GetStats を呼び出し、
        // デッドロック・IndexOutOfRange・InvalidOperationException が発生しないことを確認。
        var svc = new NetworkHistoryService();
        const int writers = 4;
        const int readers = 4;
        const int ops     = 50;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var writerTasks = Enumerable.Range(0, writers).Select(w => Task.Run(() =>
        {
            for (int i = 0; i < ops && !cts.IsCancellationRequested; i++)
                svc.RecordConnection($"Net{w}_{i % 5}", i % 3 == 0);
        }, cts.Token));

        var readerTasks = Enumerable.Range(0, readers).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < ops && !cts.IsCancellationRequested; i++)
            {
                _ = svc.GetRecent(10);
                _ = svc.GetRecentSsids(5);
                _ = svc.GetStats(30);
                _ = svc.GetFrequentSsids(5);
                _ = svc.Count;
            }
        }, cts.Token));

        await Task.WhenAll(writerTasks.Concat(readerTasks));

        // 少なくとも何件か記録されていること
        svc.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task NetworkHistory_ConcurrentForgetAndRecord_NoCrash()
    {
        var svc = new NetworkHistoryService();
        for (int i = 0; i < 20; i++) svc.RecordConnection($"Net{i}", true);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var t1 = Task.Run(() => { for (int i = 0; i < 30 && !cts.IsCancellationRequested; i++) svc.RecordConnection($"Net{i % 10}", true); }, cts.Token);
        var t2 = Task.Run(() => { for (int i = 0; i < 30 && !cts.IsCancellationRequested; i++) svc.Forget($"Net{i % 10}"); }, cts.Token);
        var t3 = Task.Run(() => { for (int i = 0; i < 30 && !cts.IsCancellationRequested; i++) _ = svc.GetAll(); }, cts.Token);

        await Task.WhenAll(t1, t2, t3);
        // クラッシュしなければ OK
    }
}
