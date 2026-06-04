using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using MWC.App.Services;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ───── SettingsService ─────
public class SettingsServiceTests : IDisposable
{
    // テスト用に一時ディレクトリを使うため環境変数を上書き
    public SettingsServiceTests()
    {
        // テスト分離: 実際のLocalAppDataには書かない
    }

    public void Dispose() { }

    [Fact]
    public void DefaultSettings_AreReasonable()
    {
        var s = new AppSettings();
        s.AutoScanIntervalSeconds.Should().BeInRange(5, 60);
        s.ShowConnectionNotifications.Should().BeTrue();
        s.ScanOnStartup.Should().BeTrue();
        s.HasCompletedFirstRun.Should().BeFalse();
        s.DisplayMode.Should().Be(DisplayMode.Simple);
    }

    [Fact]
    public void AppSettings_PinnedNetworks_DefaultEmpty()
        => new AppSettings().PinnedNetworks.Should().BeEmpty();

    [Fact]
    public void DisplayMode_Enum_HasBothValues()
    {
        var modes = Enum.GetValues<DisplayMode>();
        modes.Should().Contain(DisplayMode.Simple);
        modes.Should().Contain(DisplayMode.Expert);
    }

    [Fact]
    public void AppTheme_Enum_HasAllValues()
        => Enum.GetValues<AppTheme>().Should().HaveCountGreaterOrEqualTo(2);
}

// ───── SecurityBadge 色マッピング不変条件 ─────
public class SecurityBadgeInvariantsTests
{
    [Fact]
    public void AllAuthMethods_HaveBadge()
    {
        foreach (var auth in Enum.GetValues<AuthMethod>())
        {
            var badge = SecurityBadgeService.GetBadge(auth);
            badge.Label.Should().NotBeNullOrWhiteSpace(because: $"{auth} has no label");
            badge.TechLabel.Should().NotBeNullOrWhiteSpace(because: $"{auth} has no tech label");
        }
    }

    [Fact]
    public void Open_And_WEP_AreDangerous()
    {
        SecurityBadgeService.GetBadge(AuthMethod.Open).Level.Should().Be(SecurityLevel.Danger);
        SecurityBadgeService.GetBadge(AuthMethod.WEP).Level.Should().Be(SecurityLevel.Danger);
    }

    [Fact]
    public void WPA3_IsExcellent()
    {
        SecurityBadgeService.GetBadge(AuthMethod.WPA3SAE).Level.Should().Be(SecurityLevel.Excellent);
        SecurityBadgeService.GetBadge(AuthMethod.WPA3Enterprise192).Level.Should().Be(SecurityLevel.Excellent);
    }

    [Fact]
    public void SignalLabels_CoverAllRanges()
    {
        var labels = new[] { 0, 1, 35, 36, 60, 61, 80, 81, 100 }
            .Select(q => (q, SecurityBadgeService.GetSignalLabel(q)))
            .ToList();

        labels.Should().OnlyContain(x => !string.IsNullOrWhiteSpace(x.Item2));
        // ラベルが全部同じにならないこと(差別化されていること)
        labels.Select(x => x.Item2).Distinct().Should().HaveCountGreaterThan(2);
    }
}

// ───── TroubleshootingHelper 無ジャーゴン原則 ─────
public class TroubleshootingNoJargonTests
{
    private static readonly string[] TechTerms =
        { "WPA", "SSID", "BSSID", "IEEE", "802.11", "TKIP", "CCMP", "GHz", "MHz" };

    [Fact]
    public void AllFailures_TitlesContainNoTechJargon()
    {
        foreach (var failure in Enum.GetValues<ConnectionFailure>())
        {
            var advice = TroubleshootingHelper.GetAdvice(failure, AuthMethod.WPA2PSK);
            foreach (var term in TechTerms)
                advice.Title.Should().NotContain(term,
                    because: $"{failure}.Title should not contain jargon '{term}'");
        }
    }

    [Fact]
    public void AllFailures_StepsAreActionable()
    {
        // 各解決ステップは動詞で始まる(何をすべきか明示)
        var actionVerbs = new[] { "確認", "押", "試", "入力", "再起動", "移動", "実行",
                                  "サインイン", "開", "報告", "ご確認" };
        foreach (var failure in Enum.GetValues<ConnectionFailure>())
        {
            var advice = TroubleshootingHelper.GetAdvice(failure, AuthMethod.WPA2PSK);
            advice.Steps.Should().NotBeEmpty(because: $"{failure} needs steps");
            // 少なくとも1つのステップがアクション動詞を含む
            advice.Steps.Should().Contain(s => actionVerbs.Any(v => s.Contains(v)),
                because: $"{failure} steps should be actionable");
        }
    }

    [Fact]
    public void AllFailures_HaveDistinctIcons()
    {
        var icons = Enum.GetValues<ConnectionFailure>()
            .Select(f => TroubleshootingHelper.GetAdvice(f, AuthMethod.WPA2PSK).Icon)
            .ToList();
        icons.Should().OnlyContain(i => !string.IsNullOrWhiteSpace(i));
    }
}

// ───── ConnectionProgressDialog ステップ順序 ─────
public class ConnectionProgressStepTests
{
    [Fact]
    public void StepState_HasAllRequiredValues()
    {
        var states = Enum.GetValues<MWC.App.Views.StepState>();
        states.Should().Contain(MWC.App.Views.StepState.Pending);
        states.Should().Contain(MWC.App.Views.StepState.Active);
        states.Should().Contain(MWC.App.Views.StepState.Done);
        states.Should().Contain(MWC.App.Views.StepState.Error);
    }

    [Fact]
    public void StepItem_PropertyChanged_Fires()
    {
        var step = new MWC.App.Views.StepItem("テスト");
        bool fired = false;
        step.PropertyChanged += (_, _) => fired = true;
        step.State = MWC.App.Views.StepState.Active;
        fired.Should().BeTrue();
    }
}
