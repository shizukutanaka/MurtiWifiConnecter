using System.Linq;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  `mwc privacy` の配線契約。
//
//  PrivacyAdvisoryService は研究ベース(arXiv 引用付き)の勧告を返すが、
//  2026-07 まで完全に未配線だった — 定義以外の参照は <see cref> の doc コメントだけで、
//  MacAddressMode を供給する層もゼロだった (docs/FEATURE-AUDIT.md §1b)。
//  `mwc privacy` はこれを CLI から到達可能にする。
//
//  唯一のプラットフォーム依存は「現在の MAC モードの検出」で、勧告ロジック自体は
//  純 Core。そこで import-cat と同じ分解を使う: 検出できない値はユーザーが --mac-mode で渡す。
//
//  勧告の出力そのものは PrivacyAdvisoryTests が網羅済み。ここで固定するのは重複しない
//  CLI 固有の契約だけ:
//   (1) --mac-mode 文字列 → MacAddressMode の変換規則(CLI の ParseMacMode と同じ規則を再現)
//   (2) --ssid 無し・未接続時に CLI が使う中立コンテキストの健全性
//   (3) 全勧告が参照(出典)を持つという横断不変条件
// ══════════════════════════════════════════════════════════════
public class PrivacyCliContractTests
{
    // PrivacyCommand.ParseMacMode と同一の変換規則。CLI 側は private static なので
    // ここに写し、両者が同じ規則であることを前提に契約を固定する
    // (import-cat の DialogSpec/CLI 再現と同じ方針)。
    private static MacAddressMode? ParseMacMode(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return MacAddressMode.Unknown;
        var key = s.Trim().Replace("-", "").Replace("_", "").ToLowerInvariant();
        return key switch
        {
            "hardware" or "fixed"                          => MacAddressMode.Hardware,
            "randompernetwork" or "pernetwork" or "random" => MacAddressMode.RandomPerNetwork,
            "randomdaily" or "daily"                       => MacAddressMode.RandomDaily,
            "unknown"                                      => MacAddressMode.Unknown,
            _                                              => null,
        };
    }

    private static WifiNetwork Net(AuthMethod auth) => new() { Ssid = "N", Auth = auth };

    // ── (1) --mac-mode の解釈 ─────────────────────────────────────

    [Theory]
    [InlineData("hardware", MacAddressMode.Hardware)]
    [InlineData("HARDWARE", MacAddressMode.Hardware)]
    [InlineData("fixed", MacAddressMode.Hardware)]
    [InlineData("random-per-network", MacAddressMode.RandomPerNetwork)]
    [InlineData("random_per_network", MacAddressMode.RandomPerNetwork)]
    [InlineData("random-daily", MacAddressMode.RandomDaily)]
    [InlineData("daily", MacAddressMode.RandomDaily)]
    public void MacMode_ParsesFriendlyForms(string input, MacAddressMode expected)
    {
        ParseMacMode(input).Should().Be(expected);
    }

    [Fact]
    public void MacMode_OmittedIsUnknown_NotAnError()
    {
        // 自動検出が未実装なので、省略時は Unknown を既定にする(エラーにはしない)。
        // Unknown が勧告ゼロになることは PrivacyAdvisoryTests が固定済み。
        ParseMacMode(null).Should().Be(MacAddressMode.Unknown);
        ParseMacMode("").Should().Be(MacAddressMode.Unknown);
    }

    [Fact]
    public void MacMode_GarbageIsRejected_SoTheCliCanErrorClearly()
    {
        // null を返せば CLI は InvalidInput で明示エラーを出せる。
        ParseMacMode("nope").Should().BeNull();
    }

    // ── (2) 中立コンテキスト(--ssid 無し・未接続の経路) ──────────

    [Fact]
    public void NeutralSecuredContext_StillProducesModeAdvice_WithoutThePublicWarning()
    {
        // 対象ネットワークが解決できないとき CLI は「セキュア扱いの中立ネットワーク」で
        // Analyze する。狙いは、公共特有の警告 (#1) を誤って出さずに、モード依存の
        // 一般助言 (#2) は失わないこと。
        var placeholder = new WifiNetwork { Ssid = "(no specific network)", Auth = AuthMethod.WPA2PSK };

        var advisories = new PrivacyAdvisoryService().Analyze(MacAddressMode.Hardware, placeholder);

        advisories.Should().Contain(a => a.Code == "MWC-PRIV-002");
        advisories.Should().NotContain(a => a.Code == "MWC-PRIV-001");
    }

    // ── (3) 横断不変条件 ──────────────────────────────────────────

    [Fact]
    public void EveryAdvisoryCitesAReference_SoUsersCanVerify()
    {
        // 研究ベースであることが本サービスの価値。CLI は各勧告の ref 行を出すため、
        // どの (モード × 公共/セキュア) の組合せでも参照が空でないことを保証する。
        foreach (var mode in new[] { MacAddressMode.Hardware, MacAddressMode.RandomPerNetwork,
                                     MacAddressMode.RandomDaily })
        foreach (var auth in new[] { AuthMethod.Open, AuthMethod.WPA2PSK })
            new PrivacyAdvisoryService().Analyze(mode, Net(auth))
                .Should().OnlyContain(a => !string.IsNullOrWhiteSpace(a.Reference));
    }
}
