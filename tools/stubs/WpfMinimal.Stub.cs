// ─────────────────────────────────────────────────────────────────────────────
//  WPF の **ごく一部**の型検査専用スタブ。製品には含めない。
//
//  なぜこれだけなのか (2026-08 の実測に基づく。範囲を広げる前に必ず読むこと):
//    MWC.App の 46 ファイルを「何が阻んでいるか」で分類したところ:
//      15 … *.xaml.cs        → XAML が生成する partial (InitializeComponent と
//                              x:Name フィールド) が要る。XAML コンパイラ無しでは**不可能**。
//       8 … CommunityToolkit.Mvvm の **ソースジェネレータ** ([ObservableProperty] 等)。
//                              生成物を手書きすれば形は作れるが、それは「生成器の出力を
//                              私が推測したもの」を検査するだけで意味が無い。
//       8 … WPF 非依存        → tools/typecheck-app-services.sh が既に検査済み。
//      12 … WPF を使うがそれ以外は素直
//       2 … 描画 (DrawingContext / Typeface)
//       1 … IValueConverter
//
//    12 件のうち **9 件は諦めた**。理由:
//      - `MainWindowCommands` / `AdapterConnectExtension` は 8 個のダイアログクラスと
//        ViewModel を必要とする。それらは上の「不可能」な 2 群に属するので、
//        スタブを書くと**呼び出し側が使う署名をこちらで定義する**ことになり、
//        検査が循環して何も確かめられない。
//      - `SystemTrayService` / `JumpListService` は WinForms の NotifyIcon と
//        System.Drawing に依存し、ロジックの大半が WPF/WinForms 操作そのもの。
//        スタブに対して通っても、確かめたことにならない。
//      - `KeyboardShortcutService` は `Key` 列挙 (約 170 メンバ) を要する。
//        メンバ名を**検査対象のコードから逆算して**書くことになるため、
//        その次元については検査が空になる。
//
//    残る 3 件だけが「小さく、循環せず、実ロジックを含む」条件を満たした。
//    ここに在るのはそのための最小限であり、**WPF スタブを育てないこと**。
//    育てたくなったら、それは本物の参照パックを用意すべきという合図である。
//
//  ★ 信用してよい範囲: 対象 3 ファイルの中の Core 呼び出し・BCL 利用・制御フロー。
//    信用しては×: WPF の意味論 (ディスパッチャ親和性、クリップボードの実挙動など)。
// ─────────────────────────────────────────────────────────────────────────────
using System;

namespace System.Windows
{
    public class DependencyObject { }

    public class UIElement : DependencyObject { }

    public class FrameworkElement : UIElement
    {
        public object? DataContext { get; set; }
    }

    public class Window : FrameworkElement
    {
        public Window? Owner { get; set; }
        public string Title { get; set; } = "";
        public bool? ShowDialog() => true;
        public void Show() { }
        public void Close() { }
        public void Hide() { }
    }

    public class Application
    {
        public static Application Current { get; } = new();
        public Window? MainWindow { get; set; }
        public void Shutdown() { }
    }

    public class RoutedEventArgs : EventArgs
    {
        public bool Handled { get; set; }
    }

    /// <summary>
    /// SensitiveClipboard がクリップボード履歴/クラウド同期の除外フォーマットを
    /// 付与するために使う。型が合えばよく、実挙動は持たない。
    /// </summary>
    public class DataObject
    {
        public void SetText(string text) { }
        public void SetData(string format, object data) { }
        public object? GetData(string format) => null;
    }

    public static class Clipboard
    {
        public static void SetText(string text) { }
        public static void SetDataObject(object data, bool copy) { }
        public static void Clear() { }
    }
}

namespace System.Windows.Controls
{
    using System.Windows;

    public class Control : FrameworkElement { }

    public class TextBlock : FrameworkElement
    {
        public string Text { get; set; } = "";
    }
}

namespace System.Windows.Automation.Peers
{
    using System.Windows;

    /// <summary>スクリーンリーダー通知の種類 (AccessibilityService が使う)。</summary>
    public enum AutomationNotificationKind
    {
        ItemAdded, ItemRemoved, ActionCompleted, ActionAborted, Other
    }

    /// <summary>通知の処理方針。</summary>
    public enum AutomationNotificationProcessing
    {
        ImportantAll, ImportantMostRecent, All, MostRecent, CurrentThenMostRecent
    }

    public class AutomationPeer
    {
        public void RaiseNotificationEvent(AutomationNotificationKind kind,
                                           AutomationNotificationProcessing processing,
                                           string displayString, string activityId) { }
    }

    public class UIElementAutomationPeer : AutomationPeer
    {
        public UIElementAutomationPeer(UIElement owner) { }
        public static AutomationPeer? FromElement(UIElement element) => null;
        public static AutomationPeer CreatePeerForElement(UIElement element) => new();
    }
}

namespace System.Windows.Automation
{
    using System.Windows;

    public static class AutomationProperties
    {
        public static string GetName(DependencyObject element) => "";
        public static void SetName(DependencyObject element, string value) { }
        public static void SetHelpText(DependencyObject element, string value) { }
        public static void SetLiveSetting(DependencyObject element, object value) { }
    }
}
