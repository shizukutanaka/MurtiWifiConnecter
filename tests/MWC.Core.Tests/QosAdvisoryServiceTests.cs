using System;
using System.Linq;
using FluentAssertions;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  QosAdvisoryService — WMM × bufferbloat → 用途別適性
// ══════════════════════════════════════════════════════════════
public class QosAdvisoryServiceTests
{
    private readonly QosAdvisoryService _svc = new();

    private static ResponsivenessResult Resp(BufferbloatGrade grade)
        => new(IdleLatencyMs: 20, WorkingLatencyMs: 40, Rpm: 1500,
               Grade: grade, MeasuredAt: DateTimeOffset.UtcNow);

    private static WmmParameters Wmm()
        => new(QosInfo: 0x02, AcParams: new[]
        {
            new WmmAcParam(WmmAccessCategory.BestEffort, 3, false, 4, 10, 0),
            new WmmAcParam(WmmAccessCategory.Background, 7, false, 4, 10, 0),
            new WmmAcParam(WmmAccessCategory.Video, 2, false, 3, 4, 188),
            new WmmAcParam(WmmAccessCategory.Voice, 2, false, 2, 3, 102),
        });

    private AppSuitability For(AppClass app, BufferbloatGrade grade, WmmParameters? wmm)
        => _svc.Evaluate(Resp(grade), wmm).First(s => s.App == app);

    [Fact]
    public void GradeA_AllAppsExcellentOrGood()
    {
        var results = _svc.Evaluate(Resp(BufferbloatGrade.A), null);
        results.Should().HaveCount(4);
        results.Should().OnlyContain(s =>
            s.Level == SuitabilityLevel.Excellent || s.Level == SuitabilityLevel.Good);
    }

    [Fact]
    public void GradeF_GamingPoor_BrowsingStillGood()
    {
        For(AppClass.RealtimeGaming, BufferbloatGrade.F, null).Level
            .Should().Be(SuitabilityLevel.Poor);
        For(AppClass.WebBrowsing, BufferbloatGrade.F, null).Level
            .Should().Be(SuitabilityLevel.Good, "Web 閲覧は遅延耐性が高い");
    }

    [Fact]
    public void Wmm_UpgradesRealtimeByOneStep()
    {
        // グレードC: WMM なしならゲームは Marginal、WMM ありなら Good
        For(AppClass.RealtimeGaming, BufferbloatGrade.C, null).Level
            .Should().Be(SuitabilityLevel.Marginal);
        For(AppClass.RealtimeGaming, BufferbloatGrade.C, Wmm()).Level
            .Should().Be(SuitabilityLevel.Good, "WMM の優先制御で1段階改善");
    }

    [Fact]
    public void Wmm_DoesNotAffectNonRealtime()
    {
        // 動画ストリーミングは WMM の有無で変わらない (リアルタイムでない)
        var without = For(AppClass.VideoStreaming, BufferbloatGrade.C, null).Level;
        var with    = For(AppClass.VideoStreaming, BufferbloatGrade.C, Wmm()).Level;
        with.Should().Be(without);
    }

    [Fact]
    public void VideoStreaming_ToleratesGradeD()
    {
        For(AppClass.VideoStreaming, BufferbloatGrade.D, null).Level
            .Should().Be(SuitabilityLevel.Good, "ストリーミングはバッファで D まで許容");
    }

    [Fact]
    public void UnknownGrade_ReturnsUnknownForAll()
    {
        var results = _svc.Evaluate(Resp(BufferbloatGrade.Unknown), Wmm());
        results.Should().OnlyContain(s => s.Level == SuitabilityLevel.Unknown);
    }

    [Fact]
    public void NullResponsiveness_ReturnsUnknown()
    {
        var results = _svc.Evaluate(null, Wmm());
        results.Should().OnlyContain(s => s.Level == SuitabilityLevel.Unknown);
    }

    [Fact]
    public void WmmActiveFlag_Propagated()
    {
        For(AppClass.VideoConferencing, BufferbloatGrade.A, Wmm()).WmmActive.Should().BeTrue();
        For(AppClass.VideoConferencing, BufferbloatGrade.A, null).WmmActive.Should().BeFalse();
    }

    [Fact]
    public void Reason_MentionsWmmState()
    {
        For(AppClass.RealtimeGaming, BufferbloatGrade.A, null).Reason
            .Should().Contain("WMM disabled");
        For(AppClass.RealtimeGaming, BufferbloatGrade.A, Wmm()).Reason
            .Should().Contain("WMM active");
    }
}
