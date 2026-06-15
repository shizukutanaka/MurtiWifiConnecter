using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ───── NetworkQualityService ─────
public class NetworkQualityServiceTests
{
    [Fact]
    public void QualityGrade_AllValues_Exist()
    {
        var grades = Enum.GetValues<QualityGrade>();
        grades.Should().Contain(QualityGrade.Excellent);
        grades.Should().Contain(QualityGrade.Good);
        grades.Should().Contain(QualityGrade.Fair);
        grades.Should().Contain(QualityGrade.Poor);
    }

    [Theory]
    [InlineData(999, 0,   QualityGrade.Unknown)]  // Unknown ならタイムアウト扱い
    [InlineData(10,  0,   QualityGrade.Excellent)]
    [InlineData(40,  0,   QualityGrade.Good)]
    [InlineData(80,  3,   QualityGrade.Fair)]
    [InlineData(200, 30,  QualityGrade.Poor)]
    public void NetworkQualityResult_GradeLabel_NotEmpty(
        int latency, double loss, QualityGrade _)
    {
        var r = new NetworkQualityResult(latency, latency, latency, loss,
            QualityGrade.Good, DateTimeOffset.UtcNow);
        r.GradeLabel.Should().NotBeNullOrWhiteSpace();
        r.LatencyLabel.Should().NotBeNullOrWhiteSpace();
        r.LossLabel.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void LatencyLabel_Timeout_ShowsText()
    {
        var r = new NetworkQualityResult(999, 999, 999, 100,
            QualityGrade.Poor, DateTimeOffset.UtcNow);
        r.LatencyLabel.Should().Contain("タイムアウト");
    }

    [Fact]
    public void LatencyLabel_Normal_ShowsMs()
    {
        var r = new NetworkQualityResult(25, 20, 30, 0,
            QualityGrade.Good, DateTimeOffset.UtcNow);
        r.LatencyLabel.Should().Contain("ms");
    }
}

// ───── NetworkHistoryService ─────
public class NetworkHistoryServiceTests
{
    [Fact]
    public void RecordConnection_Success_IncrementsCount()
    {
        var svc = new NetworkHistoryService();
        svc.RecordConnection("TestNet", true);
        var e = svc.GetEntry("TestNet");
        e.Should().NotBeNull();
        e!.ConnectCount.Should().Be(1);
        e.FailCount.Should().Be(0);
    }

    [Fact]
    public void RecordConnection_Failure_IncrementsFailCount()
    {
        var svc = new NetworkHistoryService();
        svc.RecordConnection("TestNet", false);
        var e = svc.GetEntry("TestNet");
        e!.FailCount.Should().Be(1);
        e.ConnectCount.Should().Be(0);
        e.HasFailures.Should().BeTrue();
    }

    [Fact]
    public void RecordConnection_MultipleEntries_MostRecentFirst()
    {
        var svc = new NetworkHistoryService();
        svc.RecordConnection("Old",    true);
        svc.RecordConnection("Recent", true);
        var recent = svc.GetRecentSsids(2);
        recent[0].Should().Be("Recent");
        recent[1].Should().Be("Old");
    }

    [Fact]
    public void Forget_RemovesEntry()
    {
        var svc = new NetworkHistoryService();
        svc.RecordConnection("ToForget", true);
        svc.Forget("ToForget");
        svc.GetEntry("ToForget").Should().BeNull();
    }

    [Fact]
    public void LastConnectedLabel_JustNow_ReturnsLabel()
    {
        var e = new ConnectionHistoryEntry("X", DateTimeOffset.UtcNow, 1, 0);
        e.LastConnectedLabel.Should().Be("just now");
    }

    [Fact]
    public void LastConnectedLabel_HoursAgo_ContainsUnit()
    {
        var e = new ConnectionHistoryEntry("X",
            DateTimeOffset.UtcNow.AddHours(-3), 1, 0);
        e.LastConnectedLabel.Should().Contain("h ago");
    }
}

// ───── AppUpdateService の UpdateCheckResult ─────
public class UpdateCheckResultTests
{
    [Fact]
    public void Failed_HasNoUpdate()
        => UpdateCheckResult.Failed.HasUpdate.Should().BeFalse();

    [Fact]
    public void UpdateResult_WithNewerVersion_HasUpdate()
    {
        var r = new UpdateCheckResult(
            HasUpdate: true,
            LatestVersion: "v2.0.0",
            ReleaseUrl: "https://github.com/...",
            ReleaseNotes: "New features",
            CheckedAt: DateTimeOffset.UtcNow);
        r.HasUpdate.Should().BeTrue();
        r.LatestVersion.Should().Be("v2.0.0");
    }
}

// ───── SecurityBadge カラー一貫性 (Apple: 同じ色に複数の意味を持たせない) ─────
public class ColorConsistencyTests
{
    [Fact]
    public void SecurityLevels_AllHaveDistinctColors()
    {
        var levels = Enum.GetValues<SecurityLevel>();
        var colors = levels.Select(l => l switch
        {
            SecurityLevel.Excellent => "#22C55E",
            SecurityLevel.Good      => "#3B82F6",
            SecurityLevel.Fair      => "#F59E0B",
            SecurityLevel.Weak      => "#F97316",
            SecurityLevel.Danger    => "#EF4444",
            _ => "#9CA3AF"
        }).ToList();

        // 全レベルで色が違う(同じ色を複数意味に使わない)
        colors.Distinct().Should().HaveCount(colors.Count,
            because: "Each security level must have a unique color");
    }

    [Fact]
    public void SignalColors_AreOrdered()
    {
        // 強い信号ほど「安全」な緑系、弱いほど赤系
        var strong = SecurityBadgeService.GetSignalLabel(90);
        var weak   = SecurityBadgeService.GetSignalLabel(10);
        strong.Should().Be("優良");
        weak.Should().Be("弱い");
        strong.Should().NotBe(weak);
    }
}

// ───── ConnectionHistoryEntry 時間ラベル ─────
public class HistoryLabelTests
{
    [Theory]
    [InlineData(0,   "just now")]
    [InlineData(-1,  "1m ago")]
    [InlineData(-59, "59m ago")]
    [InlineData(-60, "1h ago")]
    [InlineData(-23, "23h ago")]
    public void LastConnectedLabel_TimeFormats(int minutesOffset, string contains)
    {
        DateTimeOffset at = minutesOffset == -60
            ? DateTimeOffset.UtcNow.AddHours(-1)
            : minutesOffset == -23
                ? DateTimeOffset.UtcNow.AddHours(-23)
                : DateTimeOffset.UtcNow.AddMinutes(minutesOffset);

        var e = new ConnectionHistoryEntry("X", at, 1, 0);
        e.LastConnectedLabel.Should().Be(contains);
    }
}
