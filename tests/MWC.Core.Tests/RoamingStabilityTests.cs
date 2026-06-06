using System;
using FluentAssertions;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  RoamingAdvisoryService.AnalyzeStability — sticky / flapping 検出
// ══════════════════════════════════════════════════════════════
public class RoamingStabilityTests
{
    private readonly RoamingAdvisoryService _svc = new();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static RoamEvent R(int secondsAgo, string bssid = "aa:bb")
        => new(bssid, Now.AddSeconds(-secondsAgo));

    [Fact]
    public void ManyRoamsInWindow_Flapping()
    {
        var roams = new[] { R(5, "a"), R(15, "b"), R(25, "a"), R(35, "b") };
        var s = _svc.AnalyzeStability(roams, -55, Now);
        s.State.Should().Be(RoamingStabilityState.Flapping);
        s.RoamCount.Should().Be(4);
        s.NeedsAttention.Should().BeTrue();
    }

    [Fact]
    public void WeakSignalNoRoam_Sticky()
    {
        var s = _svc.AnalyzeStability(Array.Empty<RoamEvent>(), -82, Now);
        s.State.Should().Be(RoamingStabilityState.Sticky);
    }

    [Fact]
    public void StrongSignalNoRoam_Stable()
    {
        var s = _svc.AnalyzeStability(Array.Empty<RoamEvent>(), -50, Now);
        s.State.Should().Be(RoamingStabilityState.Stable);
        s.NeedsAttention.Should().BeFalse();
    }

    [Fact]
    public void OldRoamsOutsideWindow_AreIgnored()
    {
        // 全て 60 秒より前 → window 外
        var roams = new[] { R(120, "a"), R(130, "b"), R(140, "a"), R(150, "b") };
        var s = _svc.AnalyzeStability(roams, -50, Now);
        s.State.Should().Be(RoamingStabilityState.Stable);
        s.RoamCount.Should().Be(0);
    }

    [Fact]
    public void SingleRoam_Stable()
    {
        var s = _svc.AnalyzeStability(new[] { R(5) }, -50, Now);
        s.State.Should().Be(RoamingStabilityState.Stable);
        s.RoamCount.Should().Be(1);
    }
}
