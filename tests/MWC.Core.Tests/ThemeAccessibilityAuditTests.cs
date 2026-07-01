using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using FluentAssertions;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  Theme accessibility audit — puts AccessibilityAuditService to use
//  for the first time anywhere in this codebase.
//
//  Context: ROADMAP.md claims "[x] WCAG AAA 全画面ナビゲーション検証" as
//  complete, but AccessibilityAuditService (the WCAG contrast calculator)
//  had zero call sites anywhere — nothing had ever actually run it against
//  the shipped theme colours. A manual audit (2026-07) computed the real
//  ratios and found four defects this test class now guards against:
//    1. Light.xaml's AccentTextBrush was #FFFFFF: 2.98:1 against AccentBrush
//       #00A6AD — below even AA (4.5). Fixed to #000000 (7.06:1, AAA).
//    2. MainWindow.xaml's BtnPrimary style hard-coded Foreground="#001518"
//       instead of {DynamicResource AccentTextBrush}, so every non-Dark/
//       Fluent theme silently ignored its own AccentTextBrush design value.
//       Fixed to reference the DynamicResource.
//    3. Dark.xaml and Nord.xaml's DangerTextBrush (white/near-white) scored
//       3.91:1 and 3.55:1 against their DangerBrush — below AA despite this
//       exact pairing having been "fixed" earlier in the same session for a
//       *visibility* bug (red-on-red) without checking the actual ratio.
//       Fixed both to #000000 (5.37:1 / 5.13:1).
//    4. Solarized.xaml's DangerTextBrush (base3, #FDF6E3) scored 4.29:1,
//       just short of AA. Fixed to pure white (4.63:1) — the only tested
//       option that clears AA against this red; canonical Solarized dark
//       tones (base03/base02) score worse (2.8–3.3:1) against this hue.
//
//  Solarized's body text (FgBrush #93A1A1 on BgBrush #002B36) is the
//  well-known, widely-shipped Ethan Schoonover palette and scores a genuine
//  5.61:1 — solid AA, but short of AAA. This is tested separately and is
//  intentionally NOT changed: retuning a globally recognised named palette
//  to chase AAA would defeat the reason users pick "Solarized" in the first
//  place. ROADMAP.md's AAA claim holds for Dark/Light/Nord/Catppuccin; the
//  optional Solarized skin is AA only.
//
//  Fluent's BgBrush/FgBrush reference {DynamicResource {x:Static
//  SystemColors.WindowColorKey}} — a live OS colour, not a static value —
//  so body-text contrast cannot be source-audited; only its (static)
//  accent/danger colours are covered here.
//
//  These tests read the theme .xaml SOURCE files directly (same technique
//  as ThemeContractTests) and feed the real colour values through
//  AccessibilityAuditService, so a future colour change that breaks
//  contrast fails CI instead of shipping silently.
// ══════════════════════════════════════════════════════════════
public class ThemeAccessibilityAuditTests
{
    // Themes with fully static Bg/FgBrush values that reach AAA for body text.
    private static readonly string[] AaaBodyTextThemes =
        { "Dark.xaml", "Light.xaml", "Nord.xaml", "Catppuccin.xaml" };

    // All themes with static (non-SystemColors) colour values throughout.
    private static readonly string[] AllStaticThemes =
        { "Dark.xaml", "Light.xaml", "Fluent.xaml", "Solarized.xaml", "Nord.xaml", "Catppuccin.xaml" };

    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string ThemesDir([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(thisFile)!, "..", "..", "src", "MWC.App", "Themes"));

    public static IEnumerable<object[]> AaaBodyTextThemeNames() => AaaBodyTextThemes.Select(f => new object[] { f });
    public static IEnumerable<object[]> AllStaticThemeNames() => AllStaticThemes.Select(f => new object[] { f });

    private static Dictionary<string, string> ColorsIn(string path)
        => XDocument.Load(path)
            .Descendants(Xaml + "SolidColorBrush")
            .Where(e => e.Attribute(X + "Key") is not null && e.Attribute("Color") is not null)
            .ToDictionary(
                e => (string)e.Attribute(X + "Key")!,
                e => (string)e.Attribute("Color")!);

    private readonly AccessibilityAuditService _audit = new();

    [Theory]
    [MemberData(nameof(AaaBodyTextThemeNames))]
    public void BodyText_MeetsAaa(string fileName)
    {
        // FgBrush-on-BgBrush is the single most common composition in the app
        // (every window's default Background/Foreground).
        var colors = ColorsIn(Path.Combine(ThemesDir(), fileName));
        var result = _audit.EvaluateContrast(colors["FgBrush"], colors["BgBrush"]);

        result.Level.Should().Be(WcagLevel.AAA,
            because: $"{fileName}: body text (FgBrush {colors["FgBrush"]} on BgBrush {colors["BgBrush"]}) " +
                     $"scored {result.RatioLabel}, below the AAA bar ROADMAP.md claims for whole-app navigation");
    }

    [Fact]
    public void Solarized_BodyText_MeetsAaButNotAaa()
    {
        // Documented exception: see class-level comment. Canonical Solarized
        // base0-on-base03 scores ~5.6:1 (solid AA); we do not retune the
        // well-known published palette to force AAA.
        var colors = ColorsIn(Path.Combine(ThemesDir(), "Solarized.xaml"));
        var result = _audit.EvaluateContrast(colors["FgBrush"], colors["BgBrush"]);

        result.Passes.Should().BeTrue(
            because: $"Solarized body text scored {result.RatioLabel}, below even WCAG AA (4.5:1)");
    }

    [Theory]
    [MemberData(nameof(AllStaticThemeNames))]
    public void AccentButtonText_MeetsAa(string fileName)
    {
        // AccentTextBrush-on-AccentBrush is what every primary/accent-styled
        // button actually renders (verified: every view that sets
        // Background="{DynamicResource AccentBrush}" pairs it with
        // Foreground="{DynamicResource AccentTextBrush}"). AA, not AAA,
        // because saturated accent colours make 7:1 hard to hit while
        // staying visually "accent-coloured" — AA is the floor a legible
        // primary action button needs.
        var colors = ColorsIn(Path.Combine(ThemesDir(), fileName));
        var result = _audit.EvaluateContrast(colors["AccentTextBrush"], colors["AccentBrush"]);

        result.Passes.Should().BeTrue(
            because: $"{fileName}: primary button text (AccentTextBrush {colors["AccentTextBrush"]} on " +
                     $"AccentBrush {colors["AccentBrush"]}) scored {result.RatioLabel}, below WCAG AA (4.5:1)");
    }

    [Theory]
    [MemberData(nameof(AllStaticThemeNames))]
    public void DangerText_MeetsAa(string fileName)
    {
        // DangerTextBrush-on-DangerBrush: the error banner / delete-button
        // composition fixed for *visibility* earlier this session
        // (ConnectDialog, ProfileManagerDialog) — this test additionally
        // guards the actual contrast *ratio*, not just "is it a different
        // colour than the background".
        var colors = ColorsIn(Path.Combine(ThemesDir(), fileName));
        var result = _audit.EvaluateContrast(colors["DangerTextBrush"], colors["DangerBrush"]);

        result.Passes.Should().BeTrue(
            because: $"{fileName}: danger banner text (DangerTextBrush {colors["DangerTextBrush"]} on " +
                     $"DangerBrush {colors["DangerBrush"]}) scored {result.RatioLabel}, below WCAG AA (4.5:1)");
    }
}
