using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using MWC.App.Services;
using MWC.App.ViewModels;

namespace MWC.App.Views;

public partial class ProfileManagerDialog : Window
{
    private readonly ProfileManagerViewModel     _vm;
    private readonly ILogger<ProfileManagerDialog> _log;

    public ProfileManagerViewModel ViewModel => _vm;

    public ProfileManagerDialog(ProfileManagerViewModel vm, ILogger<ProfileManagerDialog> log)
    {
        InitializeComponent();
        _log = log;
        DataContext = _vm = vm;
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        await AsyncEventHelper.SafeRunAsync(_log, "OnRefresh",
            () => _vm.RefreshCommand.ExecuteAsync(null));
    }

    private async void OnDeleteOne(object sender, RoutedEventArgs e)
    {
        await AsyncEventHelper.SafeRunAsync(_log, "OnDeleteOne", async () =>
        {
            if (sender is not Button btn || btn.Tag is not string ssid) return;
            var r = MessageBox.Show(
                MWC.App.Resources.L.Format("Confirm_DeleteMessage", ssid),
                MWC.App.Resources.L.Get("Confirm_DeleteTitle"), MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (r != MessageBoxResult.OK) return;

            _vm.Selected = _vm.Profiles.FirstOrDefault(p => p.Name == ssid);
            await _vm.DeleteCommand.ExecuteAsync(null);
        });
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
