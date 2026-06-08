using System;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  RssiDistanceEstimator — 対数距離パスロスによる距離推定
// ══════════════════════════════════════════════════════════════
public class RssiDistanceEstimatorTests
{
    private readonly RssiDistanceEstimator _svc = new();

    [Fact]
    public void StrongerSignal_ShorterDistance()
    {
        var near = _svc.Estimate(rssiDbm: -40, frequencyMhz: 2442);
        var far  = _svc.Estimate(rssiDbm: -80, frequencyMhz: 2442);

        far.Meters.Should().BeGreaterThan(near.Meters, "弱い信号ほど遠い推定");
    }

    [Fact]
    public void HigherFrequency_ShorterDistanceAtSameRssi()
    {
        // 同 RSSI なら 6GHz は減衰が大きいため距離は短く出る
        var at24 = _svc.Estimate(rssiDbm: -60, frequencyMhz: 2442);
        var at6  = _svc.Estimate(rssiDbm: -60, frequencyMhz: 6500);

        at6.Meters.Should().BeLessThan(at24.Meters);
    }

    [Fact]
    public void EstimateHasUncertaintyBand()
    {
        var e = _svc.Estimate(rssiDbm: -65, frequencyMhz: 5500);
        e.MinMeters.Should().BeLessThan(e.Meters);
        e.MaxMeters.Should().BeGreaterThan(e.Meters);
    }

    [Fact]
    public void VeryStrongSignal_TreatedAsCloseRange()
    {
        // RSSI が送信出力以上 → 至近 (1m 未満)
        var e = _svc.Estimate(rssiDbm: 25, frequencyMhz: 2442);
        e.Meters.Should().BeLessThan(1.0);
    }

    [Fact]
    public void InvalidFrequency_Unknown()
    {
        var e = _svc.Estimate(rssiDbm: -60, frequencyMhz: 0);
        e.Confidence.Should().Be(DistanceConfidence.Unknown);
    }

    [Theory]
    [InlineData(-45, DistanceConfidence.High)]
    [InlineData(-65, DistanceConfidence.Medium)]
    [InlineData(-85, DistanceConfidence.Low)]
    public void Confidence_TracksSignalStrength(int rssi, DistanceConfidence expected)
    {
        _svc.Estimate(rssi, 2442).Confidence.Should().Be(expected);
    }

    [Fact]
    public void PathLossExponent_HigherMeansShorterForSamePathLoss()
    {
        // 遮蔽環境 (n=3.5) は同じパスロスでも近い距離に対応
        var los = new RssiDistanceEstimator(RssiDistanceEstimator.IndoorLineOfSight);
        var obstructed = new RssiDistanceEstimator(RssiDistanceEstimator.IndoorObstructed);

        var dLos = los.Estimate(-70, 2442).Meters;
        var dObs = obstructed.Estimate(-70, 2442).Meters;

        dObs.Should().BeLessThan(dLos);
    }

    [Fact]
    public void InvalidExponent_Throws()
    {
        Action act = () => new RssiDistanceEstimator(pathLossExponent: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void EstimateFromNetwork_UsesRssiAndFrequency()
    {
        var net = new WifiNetwork
        {
            Ssid = "Test", Rssi = -55, FrequencyMhz = 5500, Band = WifiBand.Band5GHz
        };
        var e = _svc.Estimate(net);
        e.Meters.Should().BeGreaterThan(0);
        e.Confidence.Should().Be(DistanceConfidence.Medium);
    }

    [Fact]
    public void EstimateFromNetwork_FallsBackToBandFreq_WhenFrequencyMissing()
    {
        var net = new WifiNetwork
        {
            Ssid = "Test", Rssi = -55, Band = WifiBand.Band6GHz  // FrequencyMhz null
        };
        var e = _svc.Estimate(net);
        e.Confidence.Should().NotBe(DistanceConfidence.Unknown, "バンドから周波数を補える");
    }

    [Fact]
    public void Label_FormatsWithRange()
    {
        var e = _svc.Estimate(-60, 2442);
        e.Label.Should().Contain("約");
        e.Label.Should().Contain("m");
    }
}
