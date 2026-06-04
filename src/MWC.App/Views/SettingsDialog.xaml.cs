using System.Windows;
using MWC.App.Services;
using MWC.App.ViewModels;

namespace MWC.App.Views;

public partial class SettingsDialog : Window
{
    private readonly SettingsViewModel _vm;
    private readonly ThemeService?     _theme;

    public SettingsDialog(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        _theme = App.Host.Services.GetService(typeof(ThemeService)) as ThemeService;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _vm.SaveCommand.Execute(null);
        // テーマを即時適用
        _theme?.Apply(_vm.Theme);
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
