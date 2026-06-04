using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MWC.App.Services;
using MWC.App.Views;
using MWC.Core.Abstractions;
using MWC.Core.Models;
using MWC.Core.Profile;
using MWC.Core.Services;

namespace MWC.App.ViewModels;

/// <summary>
/// Apple "Feedback" 完全配線版:
///   ConnectionProgressDialog 4ステップ → 成功/失敗 → トースト通知
///   失敗 → TroubleshootingDialog → 再試行(最大3回)
///   成功+キャプティブ → CaptivePortalDialog 自動起動
///
/// IWifiService は DI から直接受け取る (StaticRegistry廃止)。
/// </summary>
public static class AdapterConnectExtension
{
    public static async Task ConnectWithAppleFlowAsync(
        AdapterViewModel vm,
        IWifiService wifiService,
        string ssid,
        string passphrase,
        AuthMethod auth,
        NotificationService notify,
        NetworkHistoryService history,
        Window? owner = null)
    {
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            using var progress = new ConnectionProgressDialog(ssid) { Owner = owner };
            var cts = progress.CancellationToken;

            var connectTask = RunConnectionAsync(wifiService, vm.Id, ssid, passphrase, auth, progress, cts);
            progress.Show();

            ConnectionResult result;
            try { result = await connectTask; }
            catch (OperationCanceledException) { progress.Close(); return; }

            if (result.Success)
            {
                // 成功
                history.RecordConnection(ssid, true);
                progress.SetResult(result, result.BehindCaptivePortal
                    ? MWC.App.Resources.L.CaptiveSignInRequired
                    : MWC.App.Resources.L.StatusConnectedOk);
                await Task.Delay(700);
                progress.Close();
                notify.NotifyConnected(ssid, result.HasInternet, result.BehindCaptivePortal);

                if (result.BehindCaptivePortal)
                {
                    var captive = new CaptivePortalDialog(ssid) { Owner = owner };
                    captive.ShowDialog();
                }
                await vm.RefreshAsync();
                return;
            }

            // 失敗
            history.RecordConnection(ssid, false);
            progress.SetResult(result, MWC.App.Resources.L.StatusConnectionFailed);
            await Task.Delay(500);
            progress.Close();

            var advice  = TroubleshootingHelper.GetAdvice(result.Failure ?? ConnectionFailure.Unknown, auth);
            var trouble = new TroubleshootingDialog(ssid, advice) { Owner = owner };
            trouble.ShowDialog();

            if (!trouble.ShouldRetry || attempt >= 3)
            {
                notify.NotifyFailed(ssid, result.Failure ?? ConnectionFailure.Unknown);
                return;
            }
        }
    }

    private static async Task<ConnectionResult> RunConnectionAsync(
        IWifiService svc, Guid adapterId,
        string ssid, string passphrase, AuthMethod auth,
        ConnectionProgressDialog progress, CancellationToken ct)
    {
        try
        {
            progress.SetStep(0, StepState.Active, MWC.App.Resources.L.Get("Progress_Connecting"));
            var xml = ProfileXmlBuilder.Build(new WifiProfileSpec
                { Ssid = ssid, Auth = auth, Passphrase = passphrase });
            await svc.RegisterProfileAsync(adapterId, xml, overwrite: true, ct);
            progress.SetStep(0, StepState.Done);

            progress.SetStep(1, StepState.Active, MWC.App.Resources.L.Get("Progress_Authenticating"));
            var res = await svc.ConnectAsync(adapterId, ssid, ssid, TimeSpan.FromSeconds(25), ct);
            if (!res.Success) { progress.SetStep(1, StepState.Error); return res; }
            progress.SetStep(1, StepState.Done);

            progress.SetStep(2, StepState.Active, MWC.App.Resources.L.Get("Progress_IPObtaining"));
            await Task.Delay(400, ct);
            progress.SetStep(2, StepState.Done);

            progress.SetStep(3, StepState.Active, MWC.App.Resources.L.Get("Progress_CheckInternet"));
            await Task.Delay(300, ct);
            progress.SetStep(3, StepState.Done);

            return res;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            progress.SetStep(0, StepState.Error, MWC.App.Resources.L.Format("Progress_Error", ex.Message));
            return ConnectionResult.Fail(ConnectionFailure.OsError);
        }
    }
}
