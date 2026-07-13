using FluentAssertions;
using MWC.App.ViewModels;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  SignalIconService GUI wiring (docs/FEATURE-AUDIT.md §1a follow-up)
//
//  NetworkItemViewModel.Bars previously re-implemented the signal-tier
//  calculation with its own ad-hoc thresholds (75/50/25/>0), diverging
//  from SignalIconService's designed thresholds (80/60/40/20 — chosen
//  for the WCAG 1.4.1 non-color-dependent representation). The two
//  standards coexisted in the same product; Bars now delegates to the
//  Core service so there is exactly one tier definition.
// ══════════════════════════════════════════════════════════════
public class SignalIconWiringTests
{
    private static NetworkItemViewModel Vm(int signal) => new(new WifiNetwork
    {
        Ssid = "SignalWiring", Auth = AuthMethod.WPA2PSK,
        Band = WifiBand.Band5GHz, SignalQuality = signal,
    });

    // 境界値: SignalIconService の設計閾値 (80/60/40/20) の両側を固定化。
    [Theory]
    [InlineData(0,   0)]
    [InlineData(19,  0)]
    [InlineData(20,  1)]
    [InlineData(39,  1)]
    [InlineData(40,  2)]
    [InlineData(59,  2)]
    [InlineData(60,  3)]
    [InlineData(79,  3)]  // 旧アドホック閾値では 4 本だった — 統一による意図的変更
    [InlineData(80,  4)]
    [InlineData(100, 4)]
    public void Bars_MatchesSignalIconServiceTiers(int signal, int expectedBars)
    {
        Vm(signal).Bars.Should().Be(expectedBars);
    }

    [Fact]
    public void Bars_AgreesWithSignalIconService_ForEveryQualityValue()
    {
        // 将来どちらかの実装だけが変更された場合に、2基準併存への逆行を全数検出する。
        for (int q = 0; q <= 100; q++)
        {
            Vm(q).Bars.Should().Be(SignalIconService.Describe(q).Bars,
                because: $"quality={q} で ViewModel と Core サービスの段階判定が一致すべき");
        }
    }
}
