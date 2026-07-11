using System;
using System.Linq;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  SecurityAdvisoryService — Dragonblood / deauth / MFP 診断
//  (arXiv: Vanhoef & Ronen 2020, Schepers et al. WiSec 2022)
// ══════════════════════════════════════════════════════════════
public class SecurityAdvisoryServiceTests
{
    private readonly SecurityAdvisoryService _svc = new();

    private static WifiNetwork Net(
        AuthMethod auth, PmfStatus pmf = PmfStatus.Unknown, bool transition = false) =>
        new()
        {
            Ssid                 = "TestNet",
            Auth                 = auth,
            Pmf                  = pmf,
            IsWpa3TransitionMode = transition,
            Band                 = WifiBand.Band5GHz,
            SignalQuality        = 80
        };

    [Fact]
    public void Analyze_Wpa3TransitionMode_WarnsDragonblood()
    {
        var advisories = _svc.Analyze(Net(AuthMethod.WPA3SAE, PmfStatus.Capable, transition: true));

        advisories.Should().Contain(a => a.Code == "MWC-SEC-001");
        var dragonblood = advisories.First(a => a.Code == "MWC-SEC-001");
        dragonblood.Severity.Should().Be(AdvisorySeverity.Warning);
        dragonblood.Reference.Should().Contain("Dragonblood");
        dragonblood.Detail.Should().Contain("downgrade");
    }

    [Fact]
    public void Analyze_NoMfp_WarnsDeauthAttack()
    {
        var advisories = _svc.Analyze(Net(AuthMethod.WPA2PSK, PmfStatus.Disabled));

        advisories.Should().Contain(a => a.Code == "MWC-SEC-002");
        var mfp = advisories.First(a => a.Code == "MWC-SEC-002");
        mfp.Severity.Should().Be(AdvisorySeverity.Warning);
        mfp.Detail.Should().Contain("802.11w");
    }

    [Fact]
    public void Analyze_Wep_IsCritical()
    {
        var advisories = _svc.Analyze(Net(AuthMethod.WEP));
        advisories.Should().Contain(a => a.Code == "MWC-SEC-003" && a.Severity == AdvisorySeverity.Critical);
    }

    // WEP networks that were previously misclassified as Open (Auth=Open, Cipher=WEP)
    // must trigger MWC-SEC-003, NOT MWC-SEC-005. This test documents the expectation
    // that the platform layer (WindowsWifiService) sets Auth=WEP when Cipher=WEP,
    // regardless of the underlying 802.11 auth algorithm (Open or SharedKey both use WEP cipher).
    [Fact]
    public void Analyze_Wep_DoesNotTriggerOpenNetworkAdvisory()
    {
        var advisories = _svc.Analyze(Net(AuthMethod.WEP));
        // Open-network advisory must NOT fire for WEP — WEP has its own, stronger advisory
        advisories.Should().NotContain(a => a.Code == "MWC-SEC-005",
            because: "WEP triggers MWC-SEC-003 (Critical) which is stricter than the open-network Warning");
    }

    [Fact]
    public void Analyze_Wep_SecurityScoreIsLowerThanOpen()
    {
        // WEP score=10 must be below Open score=20 — false-sense-of-security makes
        // WEP worse than openly admitting no encryption.
        var wepScore  = _svc.ComputeScore(Net(AuthMethod.WEP));
        var openScore = _svc.ComputeScore(Net(AuthMethod.Open));
        wepScore.Should().BeLessThan(openScore,
            because: "false sense of security makes WEP more dangerous than unencrypted Open");
    }

    [Fact]
    public void Analyze_HardenedNetwork_GivesPositiveFeedback()
    {
        var advisories = _svc.Analyze(Net(AuthMethod.WPA3SAE, PmfStatus.Required, transition: false));

        advisories.Should().Contain(a => a.Code == "MWC-SEC-100");
        advisories.First(a => a.Code == "MWC-SEC-100").Severity.Should().Be(AdvisorySeverity.Good);
    }

    // 2026-07: WPA3 でも SSID がハンドシェイクに暗号学的束縛されないという 2025 年の
    // 指摘に基づく情報提供 (Evil Twin 検査の重要性を伝える)。
    [Fact]
    public void Analyze_PureWpa3Sae_IncludesSsidNotBoundInfoAdvisory()
    {
        var advisories = _svc.Analyze(Net(AuthMethod.WPA3SAE, PmfStatus.Required, transition: false));

        advisories.Should().Contain(a => a.Code == "MWC-SEC-008");
        advisories.First(a => a.Code == "MWC-SEC-008").Severity.Should().Be(AdvisorySeverity.Info);
    }

    [Fact]
    public void Analyze_Wpa3TransitionMode_DoesNotDuplicateSsidNotBoundAdvisory()
    {
        // Transition mode already gets the stricter MWC-SEC-001 (Dragonblood downgrade warning);
        // MWC-SEC-008 is reserved for pure WPA3-SAE to avoid redundant/conflicting advice.
        var advisories = _svc.Analyze(Net(AuthMethod.WPA3SAE, PmfStatus.Capable, transition: true));

        advisories.Should().NotContain(a => a.Code == "MWC-SEC-008");
    }

    [Theory]
    [InlineData(AuthMethod.WPA2PSK)]
    [InlineData(AuthMethod.WPA3Enterprise)]
    [InlineData(AuthMethod.Open)]
    public void Analyze_NonPureWpa3Sae_DoesNotIncludeSsidNotBoundAdvisory(AuthMethod auth)
    {
        _svc.Analyze(Net(auth, PmfStatus.Required))
            .Should().NotContain(a => a.Code == "MWC-SEC-008");
    }

    [Fact]
    public void Analyze_OpenNetwork_WarningLevel()
    {
        var advisories = _svc.Analyze(Net(AuthMethod.Open));
        advisories.Should().Contain(a => a.Code == "MWC-SEC-005" && a.Severity == AdvisorySeverity.Warning);
    }

    [Fact]
    public void Analyze_EncryptedWithoutMfpRequired_InfoFragAttacks()
    {
        var advisories = _svc.Analyze(Net(AuthMethod.WPA2PSK, PmfStatus.Capable));

        advisories.Should().Contain(a => a.Code == "MWC-SEC-006" && a.Severity == AdvisorySeverity.Info);
        var frag = advisories.First(a => a.Code == "MWC-SEC-006");
        frag.Reference.Should().Contain("FragAttacks");
        frag.Detail.Should().Contain("CVE-2020-24586");
    }

    [Fact]
    public void Analyze_WpsEnabled_WarnsPinBruteForce()
    {
        var advisories = _svc.Analyze(Net(AuthMethod.WPA2PSK, PmfStatus.Required) with { WpsEnabled = true });

        advisories.Should().Contain(a => a.Code == "MWC-SEC-007" && a.Severity == AdvisorySeverity.Warning);
        advisories.First(a => a.Code == "MWC-SEC-007").Detail.Should().Contain("WPS");
    }

    [Fact]
    public void Analyze_WpsDisabled_NoWpsAdvisory()
    {
        var advisories = _svc.Analyze(Net(AuthMethod.WPA2PSK, PmfStatus.Required));
        advisories.Should().NotContain(a => a.Code == "MWC-SEC-007");
    }

    [Fact]
    public void ComputeScore_WpsEnabled_LowersScore()
    {
        var baseNet = Net(AuthMethod.WPA2PSK, PmfStatus.Required);
        var withWps = baseNet with { WpsEnabled = true };
        _svc.ComputeScore(withWps).Should().BeLessThan(_svc.ComputeScore(baseNet));
    }

    [Fact]
    public void Analyze_MfpRequiredOrOpen_NoFragAttacksAdvisory()
    {
        // MFP 必須 (緩和済み) では FragAttacks 情報を出さない
        _svc.Analyze(Net(AuthMethod.WPA3SAE, PmfStatus.Required))
            .Should().NotContain(a => a.Code == "MWC-SEC-006");
        // 非暗号化 (別のより強い勧告あり) では出さない
        _svc.Analyze(Net(AuthMethod.Open))
            .Should().NotContain(a => a.Code == "MWC-SEC-006");
    }

    [Theory]
    [InlineData(AuthMethod.WPA3Enterprise192, PmfStatus.Required, false, 100)]
    [InlineData(AuthMethod.WPA3SAE,           PmfStatus.Required, false, 100)]
    [InlineData(AuthMethod.WPA2PSK,           PmfStatus.Disabled, false, 60)]
    [InlineData(AuthMethod.WEP,               PmfStatus.Unknown,  false, 10)]
    [InlineData(AuthMethod.Open,              PmfStatus.Unknown,  false, 20)]
    public void ComputeScore_ReflectsSecurityLevel(
        AuthMethod auth, PmfStatus pmf, bool transition, int expectedMin)
    {
        var score = _svc.ComputeScore(Net(auth, pmf, transition));
        score.Should().BeInRange(0, 100);
        if (expectedMin == 100) score.Should().Be(100);
        else score.Should().BeLessOrEqualTo(expectedMin + 10);
    }

    [Fact]
    public void ComputeScore_TransitionMode_PenalizesScore()
    {
        var hardened   = _svc.ComputeScore(Net(AuthMethod.WPA3SAE, PmfStatus.Required, transition: false));
        var transition = _svc.ComputeScore(Net(AuthMethod.WPA3SAE, PmfStatus.Required, transition: true));

        transition.Should().BeLessThan(hardened, "Dragonblood penalty must lower the score");
    }

    [Fact]
    public void RecommendMostSecure_PrefersHardenedNetwork()
    {
        var networks = new[]
        {
            Net(AuthMethod.WPA2PSK, PmfStatus.Disabled),                       // 脆弱
            Net(AuthMethod.WPA3SAE, PmfStatus.Required, transition: false),    // 堅牢
            Net(AuthMethod.WPA3SAE, PmfStatus.Capable, transition: true),      // transition リスク
        };

        var best = _svc.RecommendMostSecure(networks, "TestNet");

        best.Should().NotBeNull();
        best!.Hardening.Should().Be(SecurityHardening.Hardened);
    }
}

// ══════════════════════════════════════════════════════════════
//  SignalQualityPredictor — EMA 線形結合 (arXiv 2509.18933)
// ══════════════════════════════════════════════════════════════
public class SignalQualityPredictorTests
{
    [Fact]
    public void Predict_NoObservations_ReturnsNull()
    {
        var p = new SignalQualityPredictor();
        p.Predict().Should().BeNull();
        p.SampleCount.Should().Be(0);
    }

    [Fact]
    public void Predict_StableSignal_ConvergesToValue()
    {
        var p = new SignalQualityPredictor();
        for (int i = 0; i < 20; i++) p.Observe(-50);

        var prediction = p.Predict();
        prediction.Should().NotBeNull();
        prediction!.Value.Should().BeApproximately(-50, 1.0, "stable input converges to its value");
        p.SampleCount.Should().Be(20);
    }

    [Fact]
    public void EvaluateTrend_ImprovingSignal_DetectsImproving()
    {
        var p = new SignalQualityPredictor();
        // 弱い → 強い (RSSI が -80 から -50 に上昇)
        foreach (var rssi in new[] { -80, -78, -75, -70, -65, -60, -55, -50 })
            p.Observe(rssi);

        p.EvaluateTrend().Should().Be(SignalTrend.Improving);
    }

    [Fact]
    public void EvaluateTrend_DegradingSignal_DetectsDegrading()
    {
        var p = new SignalQualityPredictor();
        foreach (var rssi in new[] { -50, -55, -60, -65, -70, -75, -80, -85 })
            p.Observe(rssi);

        p.EvaluateTrend().Should().Be(SignalTrend.Degrading);
    }

    [Fact]
    public void EvaluateTrend_FewSamples_ReturnsUnknown()
    {
        var p = new SignalQualityPredictor();
        p.Observe(-60);
        p.EvaluateTrend().Should().Be(SignalTrend.Unknown);
    }

    [Fact]
    public void PredictFromHistory_StaticHelper_Works()
    {
        var history = new[] { -60, -58, -62, -59, -61, -60 };
        var prediction = SignalQualityPredictor.PredictFromHistory(history);

        prediction.Should().NotBeNull();
        prediction!.Value.Should().BeInRange(-65, -55, "prediction should be near the recent average");
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var p = new SignalQualityPredictor();
        p.Observe(-50);
        p.Reset();
        p.SampleCount.Should().Be(0);
        p.Predict().Should().BeNull();
    }

    [Fact]
    public void FastEma_RespondsQuickerThanSlow()
    {
        // 急激な変化に対し、fast 重視の予測器は slow 重視より速く追従する
        var fast = new SignalQualityPredictor(wFast: 0.9, wMid: 0.05, wSlow: 0.05);
        var slow = new SignalQualityPredictor(wFast: 0.05, wMid: 0.05, wSlow: 0.9);

        foreach (var p in new[] { fast, slow }) { for (int i = 0; i < 10; i++) p.Observe(-70); }
        // 急に信号が強くなる
        fast.Observe(-40);
        slow.Observe(-40);

        // fast の方が新しい値 (-40) に近い
        fast.Predict()!.Value.Should().BeGreaterThan(slow.Predict()!.Value);
    }

    [Fact]
    public void Ctor_ZeroWeightSum_Throws()
    {
        // 重み合計0だと正規化で 0/0 = NaN が全予測に伝播する
        var act = () => new SignalQualityPredictor(wFast: 0, wMid: 0, wSlow: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0.0)]    // alpha は (0,1] 範囲外不可
    [InlineData(1.5)]
    [InlineData(-0.1)]
    public void Ctor_AlphaOutOfRange_Throws(double alpha)
    {
        var act = () => new SignalQualityPredictor(alphaFast: alpha);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

// ══════════════════════════════════════════════════════════════
//  WifiNetwork.Hardening プロパティ
// ══════════════════════════════════════════════════════════════
public class SecurityHardeningTests
{
    [Fact]
    public void Hardening_Wpa3SaeWithRequiredMfp_IsHardened()
    {
        var net = new WifiNetwork
        {
            Ssid = "Secure", Auth = AuthMethod.WPA3SAE,
            Pmf = PmfStatus.Required, IsWpa3TransitionMode = false,
            Band = WifiBand.Band6GHz
        };
        net.Hardening.Should().Be(SecurityHardening.Hardened);
    }

    [Fact]
    public void Hardening_TransitionMode_IsTransitionRisk()
    {
        var net = new WifiNetwork
        {
            Ssid = "Mixed", Auth = AuthMethod.WPA3SAE,
            Pmf = PmfStatus.Capable, IsWpa3TransitionMode = true,
            Band = WifiBand.Band5GHz
        };
        net.Hardening.Should().Be(SecurityHardening.TransitionModeRisk);
    }

    [Fact]
    public void Hardening_Wpa2NoMfp_IsNoMfpRisk()
    {
        var net = new WifiNetwork
        {
            Ssid = "OldNet", Auth = AuthMethod.WPA2PSK,
            Pmf = PmfStatus.Disabled, Band = WifiBand.Band2_4GHz
        };
        net.Hardening.Should().Be(SecurityHardening.NoMfpRisk);
    }
}
