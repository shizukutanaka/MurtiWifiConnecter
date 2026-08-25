using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace MWC.App.Services;

/// <summary>
/// アプリ全体のキーボードショートカット定義+登録サービス。
///
/// Apple HIG "Keyboard Shortcuts":
///   "Make every action keyboard-accessible. Show shortcuts in menus."
///
/// 役割は **一覧の提示のみ**。実際のキー割り当ては MainWindow.OnKeyDown の
/// スイッチが行う。このクラスが持つのは F1 ヘルプダイアログに出す定義表で、
/// 両者が食い違わないことは tools/verify.sh が静的に検査する
/// (以前は Ctrl+Tab / Ctrl+Shift+Tab がここにだけ存在し、押しても何も起きなかった)。
/// </summary>
public sealed class KeyboardShortcutService
{
    public IReadOnlyList<ShortcutDefinition> Shortcuts { get; }

    public KeyboardShortcutService()
    {
        Shortcuts = BuildDefinitions();
    }

    private static IReadOnlyList<ShortcutDefinition> BuildDefinitions() =>
    [
        new(Category.Navigation, Key.R,      ModifierKeys.Control,
            MWC.App.Resources.L.Get("Shortcut_Refresh_T"), MWC.App.Resources.L.Get("Shortcut_Refresh_D")),
        new(Category.Navigation, Key.F,      ModifierKeys.Control,
            MWC.App.Resources.L.Get("Shortcut_Search_T"),       MWC.App.Resources.L.Get("Shortcut_Search_D")),
        new(Category.Navigation, Key.Tab,    ModifierKeys.Control,
            MWC.App.Resources.L.Get("Shortcut_NextAdapter_T"), MWC.App.Resources.L.Get("Shortcut_NextAdapter_D")),
        new(Category.Navigation, Key.Tab,    ModifierKeys.Control | ModifierKeys.Shift,
            MWC.App.Resources.L.Get("Shortcut_PrevAdapter_T"), MWC.App.Resources.L.Get("Shortcut_PrevAdapter_D")),
        new(Category.Navigation, Key.Up,     ModifierKeys.None,
            MWC.App.Resources.L.Get("Shortcut_PrevNet_T"), MWC.App.Resources.L.Get("Shortcut_PrevNet_D")),
        new(Category.Navigation, Key.Down,   ModifierKeys.None,
            MWC.App.Resources.L.Get("Shortcut_NextNet_T"), MWC.App.Resources.L.Get("Shortcut_NextNet_D")),

        new(Category.Action,     Key.Return, ModifierKeys.None,
            MWC.App.Resources.L.Get("Shortcut_Connect_T"),       MWC.App.Resources.L.Get("Shortcut_Connect_D")),
        new(Category.Action,     Key.D,      ModifierKeys.Control,
            MWC.App.Resources.L.Get("Shortcut_Disconnect_T"),       MWC.App.Resources.L.Get("Shortcut_Disconnect_D")),
        new(Category.Action,     Key.Q,      ModifierKeys.Control,
            MWC.App.Resources.L.Get("Shortcut_QR_T"),   MWC.App.Resources.L.Get("Shortcut_QR_D")),
        new(Category.Action,     Key.E,      ModifierKeys.Control,
            MWC.App.Resources.L.Get("Shortcut_Export_T"), MWC.App.Resources.L.Get("Shortcut_Export_D")),
        new(Category.Action,     Key.K,      ModifierKeys.Control,
            MWC.App.Resources.L.Get("Shortcut_Quality_T"),    MWC.App.Resources.L.Get("Shortcut_Quality_D")),

        new(Category.View,       Key.OemComma, ModifierKeys.Control,
            MWC.App.Resources.L.Get("Shortcut_Settings_T"),       MWC.App.Resources.L.Get("Shortcut_Settings_D")),
        new(Category.View,       Key.M,      ModifierKeys.Control,
            MWC.App.Resources.L.Get("Shortcut_ToggleMode_T"), MWC.App.Resources.L.Get("Shortcut_ToggleMode_D")),
        new(Category.View,       Key.W,      ModifierKeys.Control,
            MWC.App.Resources.L.Get("Shortcut_Hide_T"), MWC.App.Resources.L.Get("Shortcut_Hide_D")),
        new(Category.View,       Key.A,      ModifierKeys.Control | ModifierKeys.Shift,
            MWC.App.Resources.L.Get("Shortcut_AllAdapters_T"), MWC.App.Resources.L.Get("Shortcut_AllAdapters_D")),
        new(Category.View,       Key.F1,     ModifierKeys.None,
            MWC.App.Resources.L.Get("Shortcut_Help_T"), MWC.App.Resources.L.Get("Shortcut_Help_D")),
        new(Category.View,       Key.Escape, ModifierKeys.None,
            MWC.App.Resources.L.Get("Shortcut_Escape_T"), MWC.App.Resources.L.Get("Shortcut_Escape_D"))
    ];

    // CreateBindings(IDictionary<string, ICommand>) は削除した。
    // 呼び出し元がゼロで、かつ設計として壊れていた: コマンド表を `s.Title` で
    // 引いていたが Title は resx から来る**翻訳済みの表示文字列**なので、
    // 表のキーが UI 言語ごとに変わってしまう。実際の割り当ては
    // MainWindow.OnKeyDown のスイッチが行っており、そちらが唯一の実装である。
}

public sealed record ShortcutDefinition(
    Category     Category,
    Key          Key,
    ModifierKeys Modifiers,
    string       Title,
    string       Description)
{
    public string DisplayKey => Modifiers switch
    {
        ModifierKeys.None                                  => Key.ToString(),
        ModifierKeys.Control                               => $"Ctrl+{Key}",
        ModifierKeys.Control | ModifierKeys.Shift          => $"Ctrl+Shift+{Key}",
        ModifierKeys.Alt                                   => $"Alt+{Key}",
        _                                                   => $"{Modifiers}+{Key}"
    };
}

public enum Category
{
    Navigation, Action, View
}
