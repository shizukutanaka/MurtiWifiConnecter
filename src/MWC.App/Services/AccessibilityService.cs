using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;   // AutomationNotificationKind / ...Processing。
                                         // 下の UIElementAutomationPeer は完全修飾しているが、
                                         // この 2 つの列挙は未修飾で使っており using が要る。

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
    /// スクリーンリーダーへの接続状態変更通知 (Narrator / NVDA)。
    /// </summary>
    public static void AnnounceConnectionStatus(string message)
        => RaiseNotification(message,
               AutomationNotificationKind.ActionCompleted,
               AutomationNotificationProcessing.MostRecent);

    /// <summary>接続失敗等の緊急通知 (重要度高 = 割り込み)</summary>
    public static void AnnounceError(string message)
        => RaiseNotification(message,
               AutomationNotificationKind.ActionAborted,
               AutomationNotificationProcessing.ImportantMostRecent);

    // UIAutomation の通知イベントで直接読み上げさせる。
    // 旧実装は Collapsed の TextBlock を Live Region にしていたが、Collapsed 要素は
    // オートメーションツリーから除外されるため一切読み上げられなかった。
    // RaiseNotificationEvent は要素の可視性に依存せず、ウィンドウのピアから発火できる
    // (.NET Core 3.0+/Windows 10 1709+。未対応環境では no-op)。
    private static void RaiseNotification(
        string message,
        AutomationNotificationKind kind,
        AutomationNotificationProcessing processing)
    {
        if (Application.Current?.MainWindow is not Window w) return;
        try
        {
            var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(w)
                    ?? System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(w);
            peer?.RaiseNotificationEvent(kind, processing, message, "MWC.Status");
        }
        catch { /* スクリーンリーダー非実行/未対応 OS — 無視 */ }
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
