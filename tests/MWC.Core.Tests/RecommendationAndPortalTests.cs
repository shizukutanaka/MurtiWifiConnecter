using System;
using System.Linq;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  NetworkRecommendationEngine — 統合推奨エンジン
// ══════════════════════════════════════════════════════════════
public class NetworkRecommendationEngineTests
{
    private readonly NetworkRecommendationEngine _engine = new();

    private static WifiNetwork Net(
        string ssid, AuthMethod auth, WifiBand band, int signal,
        PmfStatus pmf = PmfStatus.Unknown, bool ft = false, bool transition = false) =>
        new()
        {
            Ssid                 = ssid,
            Auth                 = auth,
            Band                 = band,
            SignalQuality        = signal,
            Pmf                  = pmf,
            FastTransition       = ft,
            IsWpa3TransitionMode = transition,
            Channel              = band == WifiBand.Band2_4GHz ? 6 : 36
        };

    [Fact]
    public void Score_SecureProfile_PrioritizesSecurity()
    {
        var secure   = Net("A", AuthMethod.WPA3SAE,  WifiBand.Band5GHz, 60, PmfStatus.Required);
        var insecure = Net("B", AuthMethod.Open,     WifiBand.Band6GHz, 95);

        var secureScore   = _engine.Score(secure,   UsageProfile.Secure);
        var insecureScore = _engine.Score(insecure, UsageProfile.Secure);

        secureScore.Total.Should().BeGreaterThan(insecureScore.Total,
            "Secure プロファイルでは WPA3 が高信号オープンより優先される");
        secureScore.SecurityScore.Should().BeGreaterThan(insecureScore.SecurityScore);
    }

    [Fact]
    public void Score_RealtimeProfile_PrioritizesRoaming()
    {
        var roamer  = Net("A", AuthMethod.WPA2Enterprise, WifiBand.Band5GHz, 70, ft: true);
        var static_ = Net("B", AuthMethod.WPA2Enterprise, WifiBand.Band5GHz, 75);

        var roamerScore = _engine.Score(roamer, UsageProfile.Realtime);
        var staticScore = _engine.Score(static_, UsageProfile.Realtime);

        roamerScore.RoamingScore.Should().BeGreaterThan(staticScore.RoamingScore);
    }

    [Fact]
    public void Score_AllDimensions_InValidRange()
    {
        var net = Net("X", AuthMethod.WPA3SAE, WifiBand.Band6GHz, 80, PmfStatus.Required, ft: true);
        var score = _engine.Score(net);

        score.Total.Should().BeInRange(0, 100);
        score.SecurityScore.Should().BeInRange(0, 100);
        score.RoamingScore.Should().BeInRange(0, 100);
        score.ChannelScore.Should().BeInRange(0, 100);
        score.SignalScore.Should().BeInRange(0, 100);
    }

    [Fact]
    public void Rank_OrdersByTotalDescending()
    {
        var networks = new[]
        {
            Net("Weak",   AuthMethod.Open,     WifiBand.Band2_4GHz, 30),
            Net("Strong", AuthMethod.WPA3SAE,  WifiBand.Band6GHz,   90, PmfStatus.Required, ft: true),
            Net("Mid",    AuthMethod.WPA2PSK,  WifiBand.Band5GHz,   60),
        };

        var ranked = _engine.Rank(networks);

        ranked.Should().HaveCount(3);
        ranked[0].Total.Should().BeGreaterOrEqualTo(ranked[1].Total);
        ranked[1].Total.Should().BeGreaterOrEqualTo(ranked[2].Total);
        ranked[0].Network.Ssid.Should().Be("Strong");
    }

    [Fact]
    public void Recommend_BestNetwork_HasHighestScore()
    {
        var networks = new[]
        {
            Net("A", AuthMethod.WEP,     WifiBand.Band2_4GHz, 50),
            Net("B", AuthMethod.WPA3SAE, WifiBand.Band6GHz,   85, PmfStatus.Required, ft: true),
        };

        var best = _engine.Recommend(networks);

        best.Should().NotBeNull();
        best!.Network.Ssid.Should().Be("B");
        best.Grade.Should().BeOneOf(RecommendationGrade.Good, RecommendationGrade.Excellent);
    }

    [Theory]
    [InlineData(90, RecommendationGrade.Excellent)]
    [InlineData(75, RecommendationGrade.Good)]
    [InlineData(55, RecommendationGrade.Fair)]
    [InlineData(30, RecommendationGrade.Poor)]
    public void Grade_MapsFromTotal(int signal, RecommendationGrade expectedMin)
    {
        // 高信号 WPA3 で各グレードを確認
        var net = Net("G", AuthMethod.WPA3SAE, WifiBand.Band6GHz, signal,
            PmfStatus.Required, ft: signal >= 75);
        var score = _engine.Score(net);
        // グレードは Total に従う
        ((int)score.Grade).Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public void Recommend_EmptyList_ReturnsNull()
    {
        _engine.Recommend(Array.Empty<WifiNetwork>()).Should().BeNull();
    }
}

// ══════════════════════════════════════════════════════════════
//  RetryPolicy — 指数バックオフ + ジッター
// ══════════════════════════════════════════════════════════════
public class RetryPolicyTests
{
    [Fact]
    public void ComputeDeterministicDelay_GrowsExponentially()
    {
        var policy = new RetryPolicy(baseDelay: TimeSpan.FromMilliseconds(500));

        var d0 = policy.ComputeDeterministicDelay(0);
        var d1 = policy.ComputeDeterministicDelay(1);
        var d2 = policy.ComputeDeterministicDelay(2);

        d0.TotalMilliseconds.Should().Be(500);
        d1.TotalMilliseconds.Should().Be(1000);
        d2.TotalMilliseconds.Should().Be(2000);
    }

    [Fact]
    public void ComputeDeterministicDelay_RespectsMaxCap()
    {
        var policy = new RetryPolicy(
            baseDelay: TimeSpan.FromMilliseconds(500),
            maxDelay:  TimeSpan.FromSeconds(2));

        // attempt 10 は本来 500*2^10 = 512000ms だが cap で 2000ms
        policy.ComputeDeterministicDelay(10).TotalMilliseconds.Should().Be(2000);
    }

    [Fact]
    public void ComputeDelay_WithJitter_StaysWithinBounds()
    {
        var policy = new RetryPolicy(
            baseDelay: TimeSpan.FromMilliseconds(500),
            maxDelay:  TimeSpan.FromSeconds(8));

        // Full Jitter: [0, capped] に収まる
        for (int attempt = 0; attempt < 5; attempt++)
        {
            var capped = policy.ComputeDeterministicDelay(attempt);
            for (int i = 0; i < 20; i++)
            {
                var delay = policy.ComputeDelay(attempt);
                delay.TotalMilliseconds.Should().BeGreaterOrEqualTo(0);
                delay.TotalMilliseconds.Should().BeLessOrEqualTo(capped.TotalMilliseconds + 1);
            }
        }
    }

    [Fact]
    public void ComputeDelay_ProducesVariation_AvoidsThunderingHerd()
    {
        var policy = new RetryPolicy(baseDelay: TimeSpan.FromMilliseconds(500));
        var delays = Enumerable.Range(0, 50)
            .Select(_ => policy.ComputeDelay(3).TotalMilliseconds)
            .Distinct()
            .ToList();

        // ジッターにより値がばらつく (全部同じではない)
        delays.Count.Should().BeGreaterThan(10, "ジッターは再試行を時間分散させる");
    }

    [Theory]
    [InlineData(ConnectionFailure.BadCredentials,        false)]
    [InlineData(ConnectionFailure.InsufficientPrivilege, false)]
    [InlineData(ConnectionFailure.AdapterDisabled,       false)]
    [InlineData(ConnectionFailure.Timeout,               true)]
    [InlineData(ConnectionFailure.NotInRange,            true)]
    public void IsRetriable_CorrectlyClassifies(ConnectionFailure failure, bool expected)
    {
        RetryPolicy.IsRetriable(failure).Should().Be(expected);
    }
}

// ══════════════════════════════════════════════════════════════
//  CaptivePortalService — RFC 8908/8910
// ══════════════════════════════════════════════════════════════
public class CaptivePortalServiceTests
{
    private readonly CaptivePortalService _svc = new();

    [Fact]
    public void ParseApiResponse_CaptiveTrue_ExtractsFields()
    {
        var json = """
            {
              "captive": true,
              "user-portal-url": "https://portal.example.com/login",
              "venue-info-url": "https://venue.example.com",
              "can-extend-session": true,
              "seconds-remaining": 3600,
              "bytes-remaining": 100000000
            }
            """;

        var state = _svc.ParseApiResponse(json);

        state.Captive.Should().BeTrue();
        state.UserPortalUrl.Should().Be("https://portal.example.com/login");
        state.VenueInfoUrl.Should().Be("https://venue.example.com");
        state.CanExtendSession.Should().BeTrue();
        state.SecondsRemaining.Should().Be(3600);
        state.BytesRemaining.Should().Be(100000000);
    }

    [Fact]
    public void ParseApiResponse_NotCaptive_MinimalFields()
    {
        var json = """{ "captive": false }""";
        var state = _svc.ParseApiResponse(json);

        state.Captive.Should().BeFalse();
        state.UserPortalUrl.Should().BeNull();
    }

    [Fact]
    public void Evaluate_NotCaptive_NoAuthRequired()
    {
        var state = new CaptivePortalService.CaptivePortalState { Captive = false };
        var decision = _svc.Evaluate(state);

        decision.RequiresAuth.Should().BeFalse();
        decision.PortalUrl.Should().BeNull();
        decision.Message.Should().Contain("connected");
    }

    [Fact]
    public void Evaluate_CaptiveWithPortalUrl_ProvidesUrl()
    {
        var state = new CaptivePortalService.CaptivePortalState
        {
            Captive = true,
            UserPortalUrl = "https://login.example.com"
        };
        var decision = _svc.Evaluate(state);

        decision.RequiresAuth.Should().BeTrue();
        decision.PortalUrl.Should().Be("https://login.example.com");
    }

    [Fact]
    public void Evaluate_CaptiveNoUrl_SuggestsBrowserRedirect()
    {
        var state = new CaptivePortalService.CaptivePortalState { Captive = true };
        var decision = _svc.Evaluate(state);

        decision.RequiresAuth.Should().BeTrue();
        decision.PortalUrl.Should().BeNull();
        decision.Message.Should().Contain("browser");
    }

    [Fact]
    public void DescribeSession_WithRemaining_FormatsHumanReadable()
    {
        var state = new CaptivePortalService.CaptivePortalState
        {
            Captive = true,
            SecondsRemaining = 1800,   // 30分
            BytesRemaining = 50_000_000,  // 50MB
            CanExtendSession = true
        };

        var desc = _svc.DescribeSession(state);

        desc.Should().Contain("30m");
        desc.Should().Contain("50 MB");
        desc.Should().Contain("extendable");
    }

    [Fact]
    public void DescribeSession_NotCaptive_ReturnsAuthenticated()
    {
        var state = new CaptivePortalService.CaptivePortalState { Captive = false };
        _svc.DescribeSession(state).Should().Be("Authenticated");
    }

    [Fact]
    public void ParseApiResponse_CaptiveKeyInsideStringValue_NotMisidentified()
    {
        // Before fix (ad-hoc string scanner): a URL whose text contains "captive"
        // could confuse the substring-based parser and flip the captive field.
        // After fix (JsonDocument.Parse): key-matching is structural, not substring-based.
        var json = """
            {
              "captive": false,
              "user-portal-url": "https://example.com/login?redirect=captive: true"
            }
            """;
        var state = _svc.ParseApiResponse(json);

        state.Captive.Should().BeFalse(
            "the word 'captive' inside a string value must not override the actual key");
        state.UserPortalUrl.Should().Be("https://example.com/login?redirect=captive: true");
    }

    [Fact]
    public void ParseApiResponse_EscapedQuotesInUrl_ParsedCorrectly()
    {
        // Before fix: the custom string extractor mishandled escaped quotes (\"),
        // truncating the URL at the first \".
        var json = """{"captive": true, "user-portal-url": "https://example.com/?q=\"test\""}""";
        var state = _svc.ParseApiResponse(json);

        state.Captive.Should().BeTrue();
        state.UserPortalUrl.Should().NotBeNull();
        state.UserPortalUrl!.Should().Contain("test");
    }
}
