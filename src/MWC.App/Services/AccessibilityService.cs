using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace MWC.App.Services;

/// <summary>
/// Apple HIG Accessibility:
///   "Design for everyone. If your app doesn't work for everyone, it doesn't work."
///
/// WCAG 2.1 AAA + Windows UI Automation 対応。
/// - コントラスト比 7:1 以上 (AAA)
/// - スクリーンリーダー (Narrator / NVDA) の Live Region 通知
/// - キーボードのみでの完全操作
/// - フォーカスリングを視覚的に明確化
/// </summary>
public static class AccessibilityService
{
    /// <summary>
    /// スクリーンリーダーへの接続状態変更通知。
    /// VoiceOver (Mac) / Narrator (Win) に相当する Live Region。
    /// </summary>
    public static void AnnounceConnectionStatus(string message)
    {
        // UIAutomation の LiveSetting を利用
        // 実際のコントロールが必要なため、App.Current.MainWindow に隠し要素を持つ
        if (Application.Current?.MainWindow is not Window w) return;
        if (w.FindName("_srLiveRegion") is not TextBlock tb) return;

        tb.Text = message;
        // Polite: 現在の読み上げを中断しない
        AutomationProperties.SetLiveSetting(tb, AutomationLiveSetting.Polite);
    }

    /// <summary>接続失敗等の緊急通知 (Assertive = 割り込み)</summary>
    public static void AnnounceError(string message)
    {
        if (Application.Current?.MainWindow is not Window w) return;
        if (w.FindName("_srLiveRegion") is not TextBlock tb) return;
        tb.Text = message;
        AutomationProperties.SetLiveSetting(tb, AutomationLiveSetting.Assertive);
    }

    /// <summary>
    /// ウィンドウに非表示の Live Region TextBlock を注入。
    /// OnLoaded で一度だけ呼ぶ。
    /// </summary>
    public static void InjectLiveRegion(Panel container)
    {
        var tb = new TextBlock
        {
            Name       = "_srLiveRegion",
            Visibility = Visibility.Collapsed,
            IsTabStop  = false
        };
        AutomationProperties.SetLiveSetting(tb, AutomationLiveSetting.Polite);
        container.Children.Add(tb);
    }

    /// <summary>
    /// コントラスト比検証(WCAG AAA: 7:1以上)。
    /// デバッグ用。テキスト色と背景色のコントラスト比を返す。
    /// </summary>
    public static double CalcContrast(byte fgR, byte fgG, byte fgB,
                                       byte bgR, byte bgG, byte bgB)
    {
        double rl = RelativeLuminance(fgR, fgG, fgB);
        double dl = RelativeLuminance(bgR, bgG, bgB);
        double lighter = System.Math.Max(rl, dl);
        double darker  = System.Math.Min(rl, dl);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(byte r, byte g, byte b)
    {
        double Srgb(byte c)
        {
            double v = c / 255.0;
            return v <= 0.04045 ? v / 12.92 : System.Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Srgb(r) + 0.7152 * Srgb(g) + 0.0722 * Srgb(b);
    }
}
