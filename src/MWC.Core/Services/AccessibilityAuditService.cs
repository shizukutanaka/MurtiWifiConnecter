using System;
using System.Collections.Generic;
using System.Linq;

namespace MWC.Core.Services;

/// <summary>
/// WCAG 2.1 AA/AAA アクセシビリティ静的解析サービス。
///
/// WCAG 基準:
///   AA  — コントラスト比 4.5:1 (通常テキスト) / 3:1 (大テキスト)
///   AAA — コントラスト比 7:1   (通常テキスト) / 4.5:1 (大テキスト)
///
/// 機能:
///   1. HEX カラーペアのコントラスト比計算 (WCAG 2.1 / ISO 9241-3 準拠)
///   2. テーマファイルの全カラーペアを一括検証
///   3. スクリーンリーダー対応チェックリスト生成
///   4. キーボードナビゲーション要件チェック
/// </summary>
public sealed class AccessibilityAuditService
{
    // ── コントラスト計算 ─────────────────────────────────────────────

    /// <summary>
    /// 2色のコントラスト比を計算する (WCAG 2.1 式)。
    /// 比率: (L1 + 0.05) / (L2 + 0.05)  ← L1 > L2
    /// </summary>
    public double CalcContrast(string hexFg, string hexBg)
    {
        var lFg = RelativeLuminance(ParseHex(hexFg));
        var lBg = RelativeLuminance(ParseHex(hexBg));
        var l1  = Math.Max(lFg, lBg);
        var l2  = Math.Min(lFg, lBg);
        return (l1 + 0.05) / (l2 + 0.05);
    }

    /// <summary>テキストサイズと WCAG レベルに基づいて合格/不合格を判定。</summary>
    public ContrastResult EvaluateContrast(string hexFg, string hexBg,
        bool isLargeText = false)
    {
        var ratio  = CalcContrast(hexFg, hexBg);
        var aaMin  = isLargeText ? 3.0 : 4.5;
        var aaaMin = isLargeText ? 4.5 : 7.0;

        var level  = ratio >= aaaMin ? WcagLevel.AAA
                   : ratio >= aaMin  ? WcagLevel.AA
                   : WcagLevel.Fail;

        return new ContrastResult(hexFg, hexBg, ratio, level, isLargeText);
    }

    /// <summary>
    /// テーマのカラートークンペアを一括検証。
    /// </summary>
    public IReadOnlyList<ContrastResult> AuditThemePairs(IEnumerable<ColorPair> pairs)
        => pairs
            .Select(p => EvaluateContrast(p.Foreground, p.Background, p.IsLargeText))
            .ToList();

    /// <summary>
    /// MWC テーマの標準カラーペアを検証する。
    /// </summary>
    public IReadOnlyList<ContrastResult> AuditMwcTheme(
        string fgBrush, string bgBrush, string accentBrush, string accentTextBrush)
    {
        var pairs = new[]
        {
            new ColorPair(fgBrush,         bgBrush,      false,  "Body text"),
            new ColorPair(fgBrush,         accentBrush,  false,  "Body on accent"),
            new ColorPair(accentTextBrush, accentBrush,  false,  "Accent text"),
            new ColorPair(accentBrush,     bgBrush,      true,   "Accent (large text)"),
        };
        return AuditThemePairs(pairs);
    }

    // ── スクリーンリーダー要件チェック ─────────────────────────────

    /// <summary>
    /// スクリーンリーダー対応チェックリストを生成する。
    /// </summary>
    public IReadOnlyList<A11yCheckItem> GetScreenReaderChecklist()
        =>
        [
            new("SR01", "Set AutomationProperties.Name on all interactive elements",
                "Button, TextBox, ComboBox, CheckBox, RadioButton, ListBox",
                WcagCriterion.C1_3_1),
            new("SR02", "Set AutomationProperties.HelpText on icon-only buttons",
                "Icon-only buttons (e.g. ⚙️ Settings)",
                WcagCriterion.C1_1_1),
            new("SR03", "Announce error messages via AutomationProperties.LiveSetting",
                "StatusMessage, ErrorLabel",
                WcagCriterion.C4_1_3),
            new("SR04", "Set AutomationProperties.LabeledBy on form fields",
                "TextBox (passphrase input, etc.)",
                WcagCriterion.C1_3_1),
            new("SR05", "Set AutomationProperties.AutomationId on dialogs",
                "Window, UserControl",
                WcagCriterion.C4_1_2),
            new("SR06", "Announce connection state changes via LiveRegion",
                "ConnectedSsid, StatusMessage",
                WcagCriterion.C4_1_3),
            new("SR07", "Set ProgressBar Value/Maximum in progress dialogs",
                "ConnectionProgressDialog",
                WcagCriterion.C4_1_2),
            new("SR08", "Tab order matches visual layout",
                "MainWindow, AdapterPreferencesDialog",
                WcagCriterion.C2_4_3),
            new("SR09", "All features operable by keyboard alone",
                "KeyboardShortcutService 16 shortcuts",
                WcagCriterion.C2_1_1),
            new("SR10", "Focus is visible (focus ring always shown)",
                "All controls: focus-visible: outline 2px",
                WcagCriterion.C2_4_7),
            new("SR11", "No focus trap (Escape exits modal dialogs)",
                "ConnectDialog, SettingsDialog",
                WcagCriterion.C2_1_2),
            new("SR12", "Layout intact at 200% text zoom",
                "ScrollViewer, WrapPanel usage sites",
                WcagCriterion.C1_4_4),
        ];

    // ── 品質レポート ─────────────────────────────────────────────────

    /// <summary>
    /// WCAG 準拠レポートを生成する。
    /// </summary>
    public A11yReport GenerateReport(
        IReadOnlyList<ContrastResult> contrastResults,
        IReadOnlyList<A11yCheckItem> checklist)
    {
        var aaPass   = contrastResults.Count(r => r.Level >= WcagLevel.AA);
        var aaaPass  = contrastResults.Count(r => r.Level >= WcagLevel.AAA);
        var failures = contrastResults.Where(r => r.Level == WcagLevel.Fail).ToList();

        return new A11yReport(
            GeneratedAt:    DateTime.UtcNow,
            TotalPairs:     contrastResults.Count,
            AaPassCount:    aaPass,
            AaaPassCount:   aaaPass,
            FailCount:      failures.Count,
            Failures:       failures,
            Checklist:      checklist,
            OverallLevel:   failures.Count == 0
                             ? (aaaPass == contrastResults.Count ? WcagLevel.AAA : WcagLevel.AA)
                             : WcagLevel.Fail);
    }

    // ── Private: WCAG 相対輝度計算 ──────────────────────────────────

    private static (double R, double G, double B) ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 3)
            hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
        return (
            Convert.ToInt32(hex[..2], 16) / 255.0,
            Convert.ToInt32(hex[2..4], 16) / 255.0,
            Convert.ToInt32(hex[4..6], 16) / 255.0
        );
    }

    private static double RelativeLuminance((double R, double G, double B) c)
    {
        static double Lin(double x) =>
            x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }
}

// ── データ型 ─────────────────────────────────────────────────────────

public enum WcagLevel { Fail, AA, AAA }

public enum WcagCriterion
{
    C1_1_1, // Non-text Content
    C1_3_1, // Info and Relationships
    C1_4_3, // Contrast Minimum (AA)
    C1_4_4, // Resize Text
    C1_4_6, // Contrast Enhanced (AAA)
    C2_1_1, // Keyboard
    C2_1_2, // No Keyboard Trap
    C2_4_3, // Focus Order
    C2_4_7, // Focus Visible
    C4_1_2, // Name, Role, Value
    C4_1_3, // Status Messages
}

public sealed record ColorPair(
    string Foreground,
    string Background,
    bool   IsLargeText,
    string Description = "");

public sealed record ContrastResult(
    string    Foreground,
    string    Background,
    double    Ratio,
    WcagLevel Level,
    bool      IsLargeText)
{
    public bool Passes => Level != WcagLevel.Fail;
    public string RatioLabel => $"{Ratio:F2}:1";
}

public sealed record A11yCheckItem(
    string       Id,
    string       Title,
    string       Scope,
    WcagCriterion Criterion);

public sealed record A11yReport(
    DateTime                    GeneratedAt,
    int                         TotalPairs,
    int                         AaPassCount,
    int                         AaaPassCount,
    int                         FailCount,
    IReadOnlyList<ContrastResult> Failures,
    IReadOnlyList<A11yCheckItem>  Checklist,
    WcagLevel                   OverallLevel)
{
    public double AaPassRate   => TotalPairs > 0 ? (double)AaPassCount  / TotalPairs : 1.0;
    public double AaaPassRate  => TotalPairs > 0 ? (double)AaaPassCount / TotalPairs : 1.0;
}
