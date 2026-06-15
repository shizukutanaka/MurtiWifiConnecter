using System;
using System.Linq;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Profile;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  WifiUri パーサー ファズテスト (不正入力耐性)
// ══════════════════════════════════════════════════════════════
public class WifiUriFuzzTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-wifi-uri")]
    [InlineData("WIFI:")]
    [InlineData("WIFI:;;;;")]
    [InlineData("WIFI:S:;;")]
    [InlineData("WIFI:T:WPA;S:;P:;;")]
    [InlineData("WIFI:S:\\;\\:\\\\;;")]      // エスケープ文字
    [InlineData("WIFI:S:" + "とても長いSSID名前ABCDEFG")]
    [InlineData("garbage\x00\x01\x02")]       // 制御文字
    [InlineData("WIFI:T:INVALID;S:Net;P:pass;;")]  // 不正な auth
    [InlineData("WIFI:S:Net;T:WPA;P:short;H:true;;")]
    public void TryParse_MalformedInput_DoesNotThrow(string input)
    {
        // ファズ: どんな不正入力でも例外を投げず null か有効な結果を返す
        var act = () => WifiUri.TryParse(input);
        act.Should().NotThrow($"input '{input}' must not crash the parser");
    }

    [Fact]
    public void TryParse_RandomGarbage_NeverThrows()
    {
        var rng = new Random(42);
        for (int i = 0; i < 200; i++)
        {
            var len = rng.Next(0, 100);
            var chars = Enumerable.Range(0, len)
                .Select(_ => (char)rng.Next(32, 127))
                .ToArray();
            var garbage = new string(chars);

            var act = () => WifiUri.TryParse(garbage);
            act.Should().NotThrow($"random input must not crash: {garbage}");
        }
    }

    [Fact]
    public void TryParse_ValidUri_RoundTrips()
    {
        var spec = new WifiProfileSpec
        {
            Ssid = "TestNet", Auth = AuthMethod.WPA2PSK, Passphrase = "pass12345"
        };
        var uri = WifiUri.Build(spec);
        var parsed = WifiUri.TryParse(uri);

        parsed.Should().NotBeNull();
        parsed!.Ssid.Should().Be("TestNet");
    }
}

// ══════════════════════════════════════════════════════════════
//  エッジケーステスト (境界値)
// ══════════════════════════════════════════════════════════════
public class SignalEdgeCaseTests
{
    [Theory]
    [InlineData(-200)]   // ありえない低 RSSI
    [InlineData(-100)]
    [InlineData(-50)]
    [InlineData(0)]      // ありえない高 RSSI
    [InlineData(100)]
    public void RssiToQuality_ExtremeValues_StayInBounds(int rssi)
    {
        var quality = SignalIconService.RssiToQuality(rssi);
        quality.Should().BeInRange(0, 100);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(101)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void Describe_AnyQuality_ReturnsValidIndicator(int quality)
    {
        var ind = SignalIconService.Describe(quality);
        ind.Bars.Should().BeInRange(0, 4);
        ind.Glyph.Should().NotBeNullOrEmpty();
        ind.TextLabel.Should().NotBeNullOrEmpty();
    }
}

// ══════════════════════════════════════════════════════════════
//  SignalIconService — 非色覚依存表現 (WCAG 1.4.1)
// ══════════════════════════════════════════════════════════════
public class SignalIconServiceTests
{
    [Theory]
    [InlineData(90, 4, SignalLevel.Excellent)]
    [InlineData(70, 3, SignalLevel.Good)]
    [InlineData(50, 2, SignalLevel.Fair)]
    [InlineData(30, 1, SignalLevel.Weak)]
    [InlineData(10, 0, SignalLevel.VeryWeak)]
    public void Describe_MapsQualityToBarsAndLevel(int quality, int bars, SignalLevel level)
    {
        var ind = SignalIconService.Describe(quality);
        ind.Bars.Should().Be(bars);
        ind.Level.Should().Be(level);
    }

    [Fact]
    public void Describe_RedundantEncoding_NotColorOnly()
    {
        // WCAG 1.4.1: 色以外の手がかり (バー/記号/テキスト) が必ずある
        foreach (var q in new[] { 10, 30, 50, 70, 90 })
        {
            var ind = SignalIconService.Describe(q);
            ind.Glyph.Should().NotBeNullOrEmpty("記号による冗長符号化");
            ind.TextLabel.Should().NotBeNullOrEmpty("テキストによる冗長符号化");
            // バー数も手がかり
            ind.Bars.Should().BeInRange(0, 4);
        }
    }

    [Fact]
    public void Glyph_BarCount_MatchesBars()
    {
        // 記号の塗りつぶしバー (▰) の数が Bars と一致
        foreach (var q in new[] { 10, 30, 50, 70, 90 })
        {
            var ind = SignalIconService.Describe(q);
            var filledBars = ind.Glyph.Count(c => c == '▰');
            filledBars.Should().Be(ind.Bars);
        }
    }

    [Fact]
    public void AccessibleLabel_ContainsNonColorInfo()
    {
        var label = SignalIconService.AccessibleLabel(70);
        label.Should().Contain("Good");
        label.Should().Contain("3/4");
        label.Should().Contain("70%");
        // 色名は含まない (色覚に依存しない)
        label.Should().NotContain("緑");
        label.Should().NotContain("赤");
    }

    [Theory]
    [InlineData(-50, 100)]
    [InlineData(-75, 50)]
    [InlineData(-100, 0)]
    [InlineData(-40, 100)]   // クランプ
    public void RssiToQuality_LinearConversion(int rssi, int expected)
    {
        SignalIconService.RssiToQuality(rssi).Should().Be(expected);
    }
}

// ══════════════════════════════════════════════════════════════
//  NetworkRecommendationEngine.Explain — 説明可能性
// ══════════════════════════════════════════════════════════════
public class RecommendationExplainabilityTests
{
    private readonly NetworkRecommendationEngine _engine = new();

    private static WifiNetwork Net(AuthMethod auth, WifiBand band, int signal,
        PmfStatus pmf = PmfStatus.Unknown, bool ft = false) =>
        new()
        {
            Ssid = "X", Auth = auth, Band = band, SignalQuality = signal,
            Pmf = pmf, FastTransition = ft, Channel = 36
        };

    [Fact]
    public void Explain_SecureProfile_TopFactorIsSecurity()
    {
        var net = Net(AuthMethod.WPA3SAE, WifiBand.Band5GHz, 60, PmfStatus.Required);
        var score = _engine.Score(net, UsageProfile.Secure);
        var explanation = _engine.Explain(score);

        explanation.Summary.Should().NotBeNullOrEmpty();
        explanation.ProfileReason.Should().Contain("security");
        explanation.Contributions.Should().HaveCount(4);
        // Secure プロファイルではセキュリティの重みが最大
        explanation.Contributions.First().Dimension.Should().Be("Security");
    }

    [Fact]
    public void Explain_ContributionsSumToTotal()
    {
        var net = Net(AuthMethod.WPA3SAE, WifiBand.Band6GHz, 80, PmfStatus.Required, ft: true);
        var score = _engine.Score(net);
        var explanation = _engine.Explain(score);

        var sum = explanation.Contributions.Sum(c => c.WeightedContribution);
        sum.Should().BeApproximately(score.Total, 0.5,
            "各次元の重み付き寄与の合計は総合スコアに一致する");
    }

    [Fact]
    public void Explain_ContributionsSortedDescending()
    {
        var net = Net(AuthMethod.WPA3SAE, WifiBand.Band6GHz, 90, PmfStatus.Required, ft: true);
        var score = _engine.Score(net);
        var explanation = _engine.Explain(score);

        for (int i = 1; i < explanation.Contributions.Count; i++)
            explanation.Contributions[i].WeightedContribution
                .Should().BeLessOrEqualTo(explanation.Contributions[i - 1].WeightedContribution);

        explanation.TopFactor.Should().Be(explanation.Contributions.First().Dimension);
    }

    [Fact]
    public void Explain_SummaryIncludesScoreAndGrade()
    {
        var net = Net(AuthMethod.WPA3SAE, WifiBand.Band6GHz, 90, PmfStatus.Required, ft: true);
        var score = _engine.Score(net);
        var explanation = _engine.Explain(score);

        explanation.Summary.Should().Contain(score.Total.ToString("F0"));
        explanation.Summary.Should().Contain("100");
    }
}
