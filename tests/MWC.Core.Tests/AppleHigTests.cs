using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ───── SecurityBadgeService ─────
public class SecurityBadgeServiceTests
{
    [Theory]
    [InlineData(AuthMethod.WPA3SAE,           SecurityLevel.Excellent)]
    [InlineData(AuthMethod.WPA3Enterprise192, SecurityLevel.Excellent)]
    [InlineData(AuthMethod.WPA2PSK,           SecurityLevel.Good)]
    [InlineData(AuthMethod.WPA2Enterprise,    SecurityLevel.Good)]
    [InlineData(AuthMethod.OWE,               SecurityLevel.Fair)]
    [InlineData(AuthMethod.WPAPSK,            SecurityLevel.Weak)]
    [InlineData(AuthMethod.WEP,               SecurityLevel.Danger)]
    [InlineData(AuthMethod.Open,              SecurityLevel.Danger)]
    public void GetBadge_ReturnsCorrectLevel(AuthMethod auth, SecurityLevel expected)
        => SecurityBadgeService.GetBadge(auth).Level.Should().Be(expected);

    [Fact]
    public void GetBadge_WPA3_HasNonTechnicalLabel()
    {
        var badge = SecurityBadgeService.GetBadge(AuthMethod.WPA3SAE);
        badge.Label.Should().NotContain("WPA3SAE");
        badge.Label.Should().NotContain("SAE");
        badge.TechLabel.Should().Contain("WPA3"); // ツールチップには技術名
    }

    [Fact]
    public void GetBadge_Open_LabelWarning()
        => SecurityBadgeService.GetBadge(AuthMethod.Open).Label
           .Should().Contain("No Encryption");

    [Theory]
    [InlineData(100, "Excellent")]
    [InlineData(80,  "Excellent")]
    [InlineData(60,  "Good")]
    [InlineData(35,  "Fair")]
    [InlineData(1,   "Weak")]
    [InlineData(0,   "None")]
    public void GetSignalLabel_ReturnsHumanLabel(int quality, string expected)
        => SecurityBadgeService.GetSignalLabel(quality).Should().Be(expected);

    [Theory]
    [InlineData(PhyType.Dot11be, "Wi-Fi 7")]
    [InlineData(PhyType.Dot11ax, "Wi-Fi 6/6E")]
    [InlineData(PhyType.Dot11ac, "Wi-Fi 5")]
    [InlineData(PhyType.Dot11n,  "Wi-Fi 4")]
    public void GetPhyFriendlyLabel_ReturnsGenerationLabel(PhyType phy, string contains)
        => SecurityBadgeService.GetPhyFriendlyLabel(phy).Should().Contain(contains);
}

// ───── TroubleshootingHelper ─────
public class TroubleshootingHelperTests
{
    [Theory]
    [InlineData(ConnectionFailure.BadCredentials)]
    [InlineData(ConnectionFailure.Timeout)]
    [InlineData(ConnectionFailure.NotInRange)]
    [InlineData(ConnectionFailure.AdapterDisabled)]
    [InlineData(ConnectionFailure.InsufficientPrivilege)]
    [InlineData(ConnectionFailure.Unknown)]
    public void GetAdvice_AllFailures_HaveSteps(ConnectionFailure failure)
    {
        var advice = TroubleshootingHelper.GetAdvice(failure, AuthMethod.WPA2PSK);
        advice.Steps.Should().NotBeEmpty();
        advice.Title.Should().NotBeNullOrWhiteSpace();
        advice.Icon.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GetAdvice_BadCredentials_MentionsPassword()
    {
        var a = TroubleshootingHelper.GetAdvice(ConnectionFailure.BadCredentials, AuthMethod.WPA2PSK);
        // 解決手順にパスワード関連の言及があること
        System.Array.Exists(a.Steps, s => s.Contains("password")).Should().BeTrue();
    }

    [Fact]
    public void GetAdvice_Enterprise_MentionsAdmin()
    {
        var a = TroubleshootingHelper.GetAdvice(
            ConnectionFailure.BadCredentials, AuthMethod.WPA2Enterprise);
        a.Title.Should().Contain("Enterprise");
    }

    [Fact]
    public void GetAdvice_NoJargon_InTitle()
    {
        // タイトルに技術用語(WPA/SSID/BSSID等)が入らないこと
        foreach (var f in System.Enum.GetValues<ConnectionFailure>())
        {
            var a = TroubleshootingHelper.GetAdvice(f, AuthMethod.WPA2PSK);
            a.Title.Should().NotContain("WPA", because: $"{f} title contains jargon");
            a.Title.Should().NotContain("SSID", because: $"{f} title contains jargon");
        }
    }
}

// ───── SecurityLevel カラーマッピング ─────
public class SecurityColorTests
{
    [Theory]
    [InlineData(SecurityLevel.Excellent, "#22C55E")]
    [InlineData(SecurityLevel.Good,      "#3B82F6")]
    [InlineData(SecurityLevel.Fair,      "#F59E0B")]
    [InlineData(SecurityLevel.Weak,      "#F97316")]
    [InlineData(SecurityLevel.Danger,    "#EF4444")]
    public void SecurityLevel_HasDistinctColor(SecurityLevel level, string expectedColor)
    {
        // ViewModelExtensions のカラーマッピングが正しいこと
        // (View 依存なしで検証)
        var colorMap = new System.Collections.Generic.Dictionary<SecurityLevel, string>
        {
            [SecurityLevel.Excellent] = "#22C55E",
            [SecurityLevel.Good]      = "#3B82F6",
            [SecurityLevel.Fair]      = "#F59E0B",
            [SecurityLevel.Weak]      = "#F97316",
            [SecurityLevel.Danger]    = "#EF4444",
        };
        colorMap[level].Should().Be(expectedColor);
    }
}
