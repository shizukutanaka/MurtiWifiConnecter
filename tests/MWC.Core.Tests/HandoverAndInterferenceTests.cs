using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  HandoverPredictor — ハンドオーバー予測 / スティッキー / フラッピング
// ══════════════════════════════════════════════════════════════
public class HandoverPredictorTests
{
    private static WifiNetwork Candidate(int signal) =>
        new() { Ssid = "Better", Band = WifiBand.Band5GHz, SignalQuality = signal, Channel = 36 };

    [Fact]
    public void Evaluate_GoodSignal_NoHandover()
    {
        var hp = new HandoverPredictor();
        var rec = hp.Evaluate(currentRssi: -50, predictedRssi: -52, SignalTrend.Stable);

        rec.ShouldHandover.Should().BeFalse();
        rec.Urgency.Should().Be(HandoverUrgency.None);
    }

    [Fact]
    public void Evaluate_DegradingWithCandidate_RecommendsHandover()
    {
        var hp = new HandoverPredictor();
        var rec = hp.Evaluate(
            currentRssi: -72, predictedRssi: -80, SignalTrend.Degrading,
            candidate: Candidate(75));

        rec.ShouldHandover.Should().BeTrue();
        rec.Candidate.Should().NotBeNull();
        rec.Urgency.Should().BeOneOf(HandoverUrgency.Medium, HandoverUrgency.High);
    }

    [Fact]
    public void Evaluate_VeryWeakSignal_HighUrgency()
    {
        var hp = new HandoverPredictor();
        var rec = hp.Evaluate(
            currentRssi: -80, predictedRssi: -85, SignalTrend.Degrading,
            candidate: Candidate(70));

        rec.ShouldHandover.Should().BeTrue();
        rec.Urgency.Should().Be(HandoverUrgency.High);
    }

    [Fact]
    public void Evaluate_WeakNoCandidate_WarnsSticky()
    {
        var hp = new HandoverPredictor();
        var rec = hp.Evaluate(currentRssi: -80, predictedRssi: null, SignalTrend.Stable);

        rec.ShouldHandover.Should().BeFalse();
        rec.Urgency.Should().Be(HandoverUrgency.Low);
        rec.Reason.Should().Contain("candidate");
    }

    [Fact]
    public void IsStickyClient_WeakAndLongConnected_True()
    {
        var hp = new HandoverPredictor();
        hp.IsStickyClient(currentRssi: -80, connectedDuration: TimeSpan.FromMinutes(5))
            .Should().BeTrue();
    }

    [Fact]
    public void IsStickyClient_StrongSignal_False()
    {
        var hp = new HandoverPredictor();
        hp.IsStickyClient(currentRssi: -55, connectedDuration: TimeSpan.FromMinutes(5))
            .Should().BeFalse();
    }

    [Fact]
    public void DetectFlapping_RepeatedPingPong_Detected()
    {
        var hp = new HandoverPredictor();
        var now = DateTimeOffset.UtcNow;

        // AP-A と AP-B 間で往復
        hp.RecordHandover("AA:AA:AA:AA:AA:AA", "BB:BB:BB:BB:BB:BB", now.AddSeconds(-25));
        hp.RecordHandover("BB:BB:BB:BB:BB:BB", "AA:AA:AA:AA:AA:AA", now.AddSeconds(-18));
        hp.RecordHandover("AA:AA:AA:AA:AA:AA", "BB:BB:BB:BB:BB:BB", now.AddSeconds(-10));

        var verdict = hp.DetectFlapping(now);

        verdict.IsFlapping.Should().BeTrue();
        verdict.RecentHandovers.Should().BeGreaterOrEqualTo(3);
        verdict.Detail.Should().Contain("back-and-forth");
    }

    [Fact]
    public void DetectFlapping_FewHandovers_NotFlapping()
    {
        var hp = new HandoverPredictor();
        var now = DateTimeOffset.UtcNow;
        hp.RecordHandover("AA:AA:AA:AA:AA:AA", "BB:BB:BB:BB:BB:BB", now.AddSeconds(-10));

        hp.DetectFlapping(now).IsFlapping.Should().BeFalse();
    }

    [Fact]
    public void RecordHandover_OldEventsPurged()
    {
        var hp = new HandoverPredictor();
        var now = DateTimeOffset.UtcNow;

        // 10分前の古いイベント
        hp.RecordHandover("AA", "BB", now.AddMinutes(-10));
        // 現在のイベント (これが古いものを掃除)
        hp.RecordHandover("CC", "DD", now);

        // 5分より古いイベントは掃除される
        hp.HistoryCount.Should().BeLessOrEqualTo(1);
    }
}

// ══════════════════════════════════════════════════════════════
//  InterferenceAnalyzer — Cross-Technology Interference
// ══════════════════════════════════════════════════════════════
public class InterferenceAnalyzerTests
{
    private readonly InterferenceAnalyzer _svc = new();

    private static WifiNetwork Net(WifiBand band, int channel) =>
        new() { Ssid = "N", Band = band, Channel = channel, SignalQuality = 70 };

    [Fact]
    public void Analyze_CleanChannel_LowInterference()
    {
        var target = Net(WifiBand.Band5GHz, 36);
        var report = _svc.Analyze(target, new[] { target });

        report.Level.Should().Be(InterferenceLevel.Low);
        report.Score.Should().BeGreaterOrEqualTo(80);
    }

    [Fact]
    public void Analyze_CoChannelCongestion_HigherInterference()
    {
        var target = Net(WifiBand.Band5GHz, 36);
        var others = Enumerable.Range(0, 4).Select(_ => Net(WifiBand.Band5GHz, 36)).ToList();
        var all = new List<WifiNetwork> { target };
        all.AddRange(others);

        var report = _svc.Analyze(target, all);

        report.Score.Should().BeLessThan(80);
        report.Factors.Should().Contain(f => f.Kind == InterferenceFactorKind.CoChannel);
    }

    [Fact]
    public void Analyze_24GHz_IncludesBluetoothRisk()
    {
        var target = Net(WifiBand.Band2_4GHz, 6);
        var report = _svc.Analyze(target, new[] { target });

        report.Factors.Should().Contain(f => f.Kind == InterferenceFactorKind.BluetoothCoexistence);
    }

    [Fact]
    public void Analyze_24GHzCongested_RecommendsBandChange()
    {
        var target = Net(WifiBand.Band2_4GHz, 6);
        var others = Enumerable.Range(0, 5).Select(_ => Net(WifiBand.Band2_4GHz, 6)).ToList();
        var all = new List<WifiNetwork> { target };
        all.AddRange(others);

        var report = _svc.Analyze(target, all);

        report.Recommendation.Should().Be(InterferenceRecommendationKind.SwitchBand);
    }

    [Fact]
    public void BluetoothCoexistenceScore_5GHz_Perfect()
    {
        _svc.BluetoothCoexistenceScore(Net(WifiBand.Band5GHz, 36), 10).Should().Be(100);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(6, true)]
    [InlineData(11, true)]
    [InlineData(3, false)]
    public void BluetoothCoexistenceScore_NonOverlapping_HigherScore(int channel, bool nonOverlap)
    {
        var score = _svc.BluetoothCoexistenceScore(Net(WifiBand.Band2_4GHz, channel), 2);
        if (nonOverlap)
            score.Should().BeGreaterThan(70, "非重複チャネルはBT共存スコアが高い");
        else
            score.Should().BeLessOrEqualTo(70);
    }

    [Fact]
    public void BluetoothCoexistenceScore_ManyNearbyAps_Lower()
    {
        var sparse = _svc.BluetoothCoexistenceScore(Net(WifiBand.Band2_4GHz, 6), 1);
        var dense  = _svc.BluetoothCoexistenceScore(Net(WifiBand.Band2_4GHz, 6), 10);
        dense.Should().BeLessThan(sparse);
    }

    [Fact]
    public void Analyze_ScoreAlwaysInBounds()
    {
        var target = Net(WifiBand.Band2_4GHz, 6);
        var crowd = Enumerable.Range(0, 20).Select(_ => Net(WifiBand.Band2_4GHz, 6)).ToList();
        var all = new List<WifiNetwork> { target };
        all.AddRange(crowd);

        var report = _svc.Analyze(target, all);
        report.Score.Should().BeInRange(0, 100);
    }
}
