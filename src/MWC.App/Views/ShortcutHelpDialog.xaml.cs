using System.Linq;
using System.Windows;
using MWC.App.Services;

namespace MWC.App.Views;

public partial class ShortcutHelpDialog : Window
{
    public ShortcutHelpDialog(KeyboardShortcutService svc)
    {
        InitializeComponent();
        var grouped = svc.Shortcuts
            .GroupBy(s => s.Category)
            .Select(g => new ShortcutGroup(
                CategoryLabel(g.Key),
                g.ToList()))
            .ToList();
        ShortcutGroups.ItemsSource = grouped;
    }

    private static string CategoryLabel(Category cat) => cat switch
    {
        Category.Navigation => MWC.App.Resources.L.Get("Shortcut_Navigation"),
        Category.Action     => MWC.App.Resources.L.Get("Shortcut_Action"),
        Category.View       => MWC.App.Resources.L.Get("Shortcut_View"),
        _ => cat.ToString()
    };

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private sealed record ShortcutGroup(string Title, System.Collections.Generic.IReadOnlyList<ShortcutDefinition> Items);
}
