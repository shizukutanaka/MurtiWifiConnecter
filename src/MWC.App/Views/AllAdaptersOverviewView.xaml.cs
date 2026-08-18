using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<AllAdaptersOverviewView> _log;

    public AllAdaptersOverviewView(AllAdaptersOverviewViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        var svc   = App.Host.Services;
        _executor = svc.GetRequiredService<ConnectionExecutor>();
        _notify   = svc.GetRequiredService<NotificationService>();
        _log      = svc.GetRequiredService<ILogger<AllAdaptersOverviewView>>();

        // 以前は AsyncEventHelper に null ロガーを渡しており、log?.LogError(...) が
        // 無音の no-op になっていた (2026-07 品質パスで是正)。
        Loaded += async (_, _) => await AsyncEventHelper.SafeRunAsync(
            _log, "AllAdaptersLoad", () => vm.LoadCommand.ExecuteAsync(null));
    }

    private async void OnConnectClickInPanel(object sender, RoutedEventArgs e)
    {
        await AsyncEventHelper.SafeRunAsync(_log, "OnConnectClickInPanel", async () =>
        {
            if (sender is not Button btn) return;
            if (btn.DataContext is not AdapterPanelViewModel panel) return;
            var net = panel.Selected;
            if (net is null) return;

            // ダイアログから spec を受け取る (Enterprise の EAP 種別・ユーザー名等を運ぶため)。
            // Open/OWE はダイアログを出さないので直接組み立てる。
            var spec = new MWC.Core.Models.WifiProfileSpec
            {
                Ssid = net.Ssid, Auth = net.Auth, Passphrase = ""
            };
            if (net.Auth is not (AuthMethod.Open or AuthMethod.OWE))
            {
                var dlg = new ConnectDialog(net.Ssid, net.Auth) { Owner = this };
                if (dlg.ShowDialog() != true) return;
                spec = dlg.BuildSpec();
            }

            var progress = new ConnectionProgressDialog(net.Ssid) { Owner = this };
            progress.Show();
            try
            {
                progress.SetStep(0, StepState.Active, L.Get("Progress_Connecting"));

                var res = await _executor.ConnectAsync(
                    panel.Id, spec,
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
            catch (OperationCanceledException)
            {
                // ユーザーが ConnectionProgressDialog をキャンセル。エラー扱いしない。
            }
            catch (Exception ex)
            {
                // 以前は finally のみで catch が無く、_executor.ConnectAsync 以外の例外
                // (ダイアログ構築失敗等) が無音で握りつぶされていた
                // (2026-07 品質パスで是正。AdapterConnectExtension の同型対応と同じ方針)。
                _log.LogError(ex, "OnConnectClickInPanel failed for {Ssid}", PiiMask.Ssid(net.Ssid));
                progress.SetStep(0, StepState.Error, L.Format("Progress_Error", ex.Message));
                _notify.NotifyFailed(net.Ssid, ConnectionFailure.OsError);
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
