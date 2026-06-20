using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MWC.App.Services;

/// <summary>
/// アプリ全体のキーボードショートカット定義+登録サービス。
///
/// Apple HIG "Keyboard Shortcuts":
///   "Make every action keyboard-accessible. Show shortcuts in menus."
///
/// 設計:
///   - InputBinding をMainWindow.InputBindingsに自動登録
///   - ヘルプダイアログ(F1)で一覧表示
///   - macOS慣習に近づけつつ、Windowsユーザーにも自然に
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
        new(Category.View,       Key.F1,     ModifierKeys.None,
            MWC.App.Resources.L.Get("Shortcut_Help_T"), MWC.App.Resources.L.Get("Shortcut_Help_D")),
        new(Category.View,       Key.Escape, ModifierKeys.None,
            MWC.App.Resources.L.Get("Shortcut_Escape_T"), MWC.App.Resources.L.Get("Shortcut_Escape_D"))
    ];

    /// <summary>Window.InputBindingsに登録するためのKeyBinding群を生成</summary>
    public IEnumerable<KeyBinding> CreateBindings(
        IDictionary<string, ICommand> commandMap)
    {
        foreach (var s in Shortcuts)
        {
            if (commandMap.TryGetValue(s.Title, out var cmd))
                yield return new KeyBinding(cmd, s.Key, s.Modifiers);
        }
    }
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
