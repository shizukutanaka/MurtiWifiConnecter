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
//  ★ XAML コードビハインド 15 件を取り込まなかった理由 (2026-08 に実測して判断)
//    .xaml から partial (InitializeComponent + x:Name フィールド) を生成すること自体は
//    可能で、実際に試作した: **15 クラス / 72 フィールド / 20 のコントロール型**が要る。
//    .resx → .resources と同じく「MSBuild がやることを自前でやる」だけなので、
//    生成それ自体は循環しない。
//
//    それでも見送るのは **メンバ表面**が理由である。TextBox.Text や
//    ComboBox.SelectedIndex, WebBrowser.Navigate … を стаб に足していく作業は、
//    「コードが要求したから足す」形になりやすい。個々のメンバ名は公表 API だとしても、
//    **何を足すかをコードに決めさせた時点で、その次元の検査は空に近づく**。
//    Key 列挙のように「標準の集合を丸ごと書ける」小さく閉じた定義とは性質が違う。
//
//    加えて、コードビハインドの正しさの大半は**実行時の挙動** (バインディング、
//    レイアウト、イベント順序) であり、型が合うことの確認価値は他の層より低い。
//
//    → ここは **本物の WPF 参照パック (Microsoft.WindowsDesktop.App.Ref)** を
//      用意するのが筋。環境設定 1 つで正しく解決する問題に、
//      数百行の近似を積むべきではない。
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

    /// <summary>WPF の ResourceDictionary。ThemeService がテーマ .xaml を差し替えるのに使う。</summary>
    public class ResourceDictionary : System.Collections.Generic.Dictionary<object, object>
    {
        public Uri? Source { get; set; }
        public System.Collections.Generic.List<ResourceDictionary> MergedDictionaries { get; } = new();
    }

    public class Application
    {
        public static Application Current { get; } = new();
        public Window? MainWindow { get; set; }
        public ResourceDictionary Resources { get; } = new();
        /// <summary>UI スレッドのディスパッチャ。ThemeService が OS 通知を UI へ渡すのに使う。</summary>
        public System.Windows.Threading.Dispatcher Dispatcher { get; } = new();
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

namespace System.Windows.Input
{
    /// <summary>
    /// WPF の <c>Key</c> 列挙。**公表された安定した API** を再現したもので、
    /// 検査対象のコードから逆算していない — 実際 MWC が使うのは 17 個だけだが、
    /// ここには WPF の標準メンバを一通り置いてある。
    /// そうすることで「実在しないメンバを参照している」誤りがここで落ちる。
    /// (コードに合わせてメンバを足すと検査が空になる。絶対にやらないこと。)
    /// 値は列挙の順序のみ意味を持ち、実際の仮想キーコードとは対応させていない。
    /// </summary>
    public enum Key
    {
        None = 0, Cancel, Back, Tab, LineFeed, Clear, Return, Pause, Capital, CapsLock,
        Escape, Space, Prior, PageUp, Next, PageDown, End, Home,
        Left, Up, Right, Down, Select, Print, Execute, PrintScreen, Snapshot, Insert, Delete, Help,
        D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
        A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        LWin, RWin, Apps, Sleep,
        NumPad0, NumPad1, NumPad2, NumPad3, NumPad4, NumPad5, NumPad6, NumPad7, NumPad8, NumPad9,
        Multiply, Add, Separator, Subtract, Decimal, Divide,
        F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
        F13, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23, F24,
        NumLock, Scroll, LeftShift, RightShift, LeftCtrl, RightCtrl, LeftAlt, RightAlt,
        OemSemicolon, OemPlus, OemComma, OemMinus, OemPeriod, OemQuestion, OemTilde,
        OemOpenBrackets, OemPipe, OemCloseBrackets, OemQuotes, OemBackslash,
        System, Attn, CrSel, ExSel, EraseEof, Play, Zoom, NoName, Pa1, OemClear
    }

    /// <summary>WPF の <c>ModifierKeys</c>。同じく公表された定義。</summary>
    [Flags]
    public enum ModifierKeys
    {
        None = 0, Alt = 1, Control = 2, Shift = 4, Windows = 8
    }

    /// <summary>InputBinding / KeyBinding は型として参照されるだけ。</summary>
    public class InputBinding
    {
        public ICommand? Command { get; set; }
    }

    public class KeyBinding : InputBinding
    {
        public KeyBinding() { }
        public KeyBinding(ICommand command, Key key, ModifierKeys modifiers)
        { Command = command; Key = key; Modifiers = modifiers; }
        public Key Key { get; set; }
        public ModifierKeys Modifiers { get; set; }
    }
}

namespace System.Windows.Shell
{
    using System.Collections.Generic;

    /// <summary>タスクバーのジャンプリスト。公表された WPF の定義を再現。</summary>
    public class JumpItem { }

    public class JumpTask : JumpItem
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? ApplicationPath { get; set; }
        public string? Arguments { get; set; }
        public string? CustomCategory { get; set; }
        public string? IconResourcePath { get; set; }
        public int IconResourceIndex { get; set; }
        public string? WorkingDirectory { get; set; }
    }

    public class JumpList
    {
        public List<JumpItem> JumpItems { get; } = new();
        public bool ShowRecentCategory { get; set; }
        public bool ShowFrequentCategory { get; set; }
        public void Apply() { }
        public static void SetJumpList(System.Windows.Application application, JumpList value) { }
        public static JumpList? GetJumpList(System.Windows.Application application) => null;
    }
}

namespace Microsoft.Win32
{
    /// <summary>OS の設定変更通知。ThemeService が System テーマ追従に使う。</summary>
    public enum UserPreferenceCategory
    {
        Accessibility, Color, Desktop, General, Icon, Keyboard, Locale,
        Menu, Mouse, Policy, Power, Screensaver, VisualStyle, Window
    }

    public class UserPreferenceChangedEventArgs : EventArgs
    {
        public UserPreferenceChangedEventArgs(UserPreferenceCategory category) { Category = category; }
        public UserPreferenceCategory Category { get; }
    }

    public delegate void UserPreferenceChangedEventHandler(object sender, UserPreferenceChangedEventArgs e);

    public static class SystemEvents
    {
        public static event UserPreferenceChangedEventHandler? UserPreferenceChanged;
        public static void RaiseForStub() => UserPreferenceChanged?.Invoke(null!, new(UserPreferenceCategory.General));
    }
}
