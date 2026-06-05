using System;
using FluentAssertions;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  NetworkQualityService — responsiveness / bufferbloat (RPM)
//  (IETF draft-ietf-ippm-responsiveness, Apple RPM)
//  純粋関数のみ検証(ネットワーク I/O は対象外)。
// ══════════════════════════════════════════════════════════════
public class ResponsivenessTests
{
    [Theory]
    [InlineData(60, 1000)]
    [InlineData(25, 2400)]
    [InlineData(100, 600)]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    public void ComputeRpm_FromWorkingLatency(int workingMs, int expectedRpm)
        => NetworkQualityService.ComputeRpm(workingMs).Should().Be(expectedRpm);

    [Theory]
    [InlineData(20, 25,  BufferbloatGrade.A)]        // +5
    [InlineData(20, 70,  BufferbloatGrade.B)]        // +50
    [InlineData(20, 110, BufferbloatGrade.C)]        // +90
    [InlineData(20, 170, BufferbloatGrade.D)]        // +150
    [InlineData(10, 300, BufferbloatGrade.F)]        // +290
    [InlineData(20, 999, BufferbloatGrade.Unknown)]  // タイムアウト
    [InlineData(20, 0,   BufferbloatGrade.Unknown)]  // 無効
    public void GradeBufferbloat_ScalesWithLatencyIncrease(
        int idle, int working, BufferbloatGrade expected)
        => NetworkQualityService.GradeBufferbloat(idle, working).Should().Be(expected);

    [Fact]
    public void GradeBufferbloat_NeverNegativeIncrease()
    {
        // 負荷時の方が低い(計測ゆらぎ)場合でも A 扱い(増分 0)
        NetworkQualityService.GradeBufferbloat(80, 50).Should().Be(BufferbloatGrade.A);
    }

    [Fact]
    public void ResponsivenessResult_DerivedProperties()
    {
        var r = new ResponsivenessResult(
            IdleLatencyMs: 20, WorkingLatencyMs: 95, Rpm: 631,
            Grade: BufferbloatGrade.C, MeasuredAt: DateTimeOffset.UtcNow);

        r.LatencyIncreaseMs.Should().Be(75);
        r.GradeLabel.Should().Contain("C");
        r.RpmLabel.Should().Contain("631");
    }

    [Fact]
    public void ResponsivenessResult_ZeroRpm_ShowsPlaceholder()
    {
        var r = new ResponsivenessResult(0, 0, 0, BufferbloatGrade.Unknown, DateTimeOffset.UtcNow);
        r.RpmLabel.Should().Be("—");
        r.GradeLabel.Should().Be("—");
    }
}
