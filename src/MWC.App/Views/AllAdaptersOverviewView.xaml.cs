using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using MWC.App.Resources;
using MWC.App.Services;
using MWC.App.ViewModels;
using MWC.Core.Models;
using MWC.Core.Services;

namespace MWC.App.Views;

public partial class AllAdaptersOverviewView : Window
{
    private readonly ConnectionExecutor  _executor;
    private readonly NotificationService _notify;

    public AllAdaptersOverviewView(AllAdaptersOverviewViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        var svc   = App.Host.Services;
        _executor = svc.GetRequiredService<ConnectionExecutor>();
        _notify   = svc.GetRequiredService<NotificationService>();

        Loaded += async (_, _) => await AsyncEventHelper.SafeRunAsync(
            null, "AllAdaptersLoad", () => vm.LoadCommand.ExecuteAsync(null));
    }

    private async void OnConnectClickInPanel(object sender, RoutedEventArgs e)
    {
        await AsyncEventHelper.SafeRunAsync(null, "OnConnectClickInPanel", async () =>
        {
            if (sender is not Button btn) return;
            if (btn.DataContext is not AdapterPanelViewModel panel) return;
            var net = panel.Selected;
            if (net is null) return;

            string passphrase = "";
            if (net.Auth is not (AuthMethod.Open or AuthMethod.OWE))
            {
                var dlg = new ConnectDialog(net.Ssid, net.Auth) { Owner = this };
                if (dlg.ShowDialog() != true) return;
                passphrase = dlg.Passphrase ?? "";
            }

            var progress = new ConnectionProgressDialog(net.Ssid) { Owner = this };
            progress.Show();
            try
            {
                progress.SetStep(0, StepState.Active, L.Get("Progress_Connecting"));

                var res = await _executor.ConnectAsync(
                    panel.Id, net.Ssid, net.Auth, passphrase,
                    TimeSpan.FromSeconds(25), progress.CancellationToken);

                if (res.Success)
                {
                    progress.SetStep(0, StepState.Done);
                    progress.SetStep(1, StepState.Done);
                    progress.SetStep(2, StepState.Done);
                    progress.SetStep(3, StepState.Done);
                    progress.SetResult(res, L.StatusConnectedOk);
                    _notify.NotifyConnected(net.Ssid, res.HasInternet, res.BehindCaptivePortal);
                }
                else
                {
                    progress.SetStep(0, StepState.Error);
                    progress.SetResult(res, L.StatusConnectionFailed);
                    _notify.NotifyFailed(net.Ssid, res.Failure ?? ConnectionFailure.Unknown);
                }
                await Task.Delay(700);
            }
            finally
            {
                progress.Close();
                await panel.RefreshAsync();
                if (DataContext is AllAdaptersOverviewViewModel vm) vm.UpdateSummary();
            }
        });
    }
}
