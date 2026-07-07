using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MWC.App.Services;
using MWC.App.Views;
using MWC.Core.Models;
using MWC.Core.Services;

namespace MWC.App.ViewModels;

/// <summary>
/// Apple "Feedback" 完全配線版:
///   ConnectionProgressDialog 4ステップ → 成功/失敗 → トースト通知
///   失敗 → TroubleshootingDialog → 再試行(最大3回)
///   成功+キャプティブ → CaptivePortalDialog 自動起動
///
/// 接続は <see cref="ConnectionExecutor"/> 経由で実行する(単一エントリポイント原則)。
/// 子機ごとのセマフォ排他・OTel・PII マスクログ・履歴記録は executor が一元管理するため、
/// ここでは履歴を二重記録しない。
/// </summary>
public static class AdapterConnectExtension
{
    public static async Task ConnectWithAppleFlowAsync(
        AdapterViewModel vm,
        ConnectionExecutor executor,
        string ssid,
        string passphrase,
        AuthMethod auth,
        NotificationService notify,
        Window? owner = null)
    {
        // 一時的失敗 (電波・タイムアウト等) はユーザーにダイアログを見せる前に
        // ジッター付きバックオフで静かに自動再試行する。決定的失敗 (認証・権限・
        // プロファイル・キャンセル) の分類は RetryPolicy.IsRetriable が担う。
        // 上限: 自動再試行 2 回 (MaxAttempts=3 の残り) + ユーザー承認制 3 ラウンド。
        var retryPolicy = new RetryPolicy();
        int autoRetries = 0;
        int userRounds  = 0;

        while (true)
        {
            using var progress = new ConnectionProgressDialog(ssid) { Owner = owner };
            var cts = progress.CancellationToken;

            var connectTask = RunConnectionAsync(executor, vm.Id, ssid, passphrase, auth, progress, cts);
            progress.Show();

            ConnectionResult result;
            try { result = await connectTask; }
            catch (OperationCanceledException) { progress.Close(); return; }

            if (result.Success)
            {
                // 成功 (履歴は executor が記録済み)
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

            // 失敗 (履歴は executor が記録済み)
            var failure = result.Failure ?? ConnectionFailure.Unknown;

            if (RetryPolicy.IsRetriable(failure) && autoRetries < retryPolicy.MaxAttempts - 1)
            {
                autoRetries++;
                progress.SetResult(result, MWC.App.Resources.L.Get("Progress_AutoRetry"));
                // バックオフ待機中はダイアログを開いたまま「自動再試行中…」を見せる。
                // 待機中のキャンセル (×/Cancel) は次の試行に入らず即終了する。
                try { await Task.Delay(retryPolicy.ComputeDelay(autoRetries), cts); }
                catch (OperationCanceledException) { progress.Close(); return; }
                progress.Close();
                continue;
            }

            progress.SetResult(result, MWC.App.Resources.L.StatusConnectionFailed);
            await Task.Delay(500);
            progress.Close();

            userRounds++;
            var advice  = MWC.App.Resources.L.GetTroubleshootingAdvice(failure, auth);
            var trouble = new TroubleshootingDialog(ssid, advice) { Owner = owner };
            trouble.ShowDialog();

            if (!trouble.ShouldRetry || userRounds >= 3)
            {
                notify.NotifyFailed(ssid, failure);
                return;
            }
        }
    }

    private static async Task<ConnectionResult> RunConnectionAsync(
        ConnectionExecutor executor, Guid adapterId,
        string ssid, string passphrase, AuthMethod auth,
        ConnectionProgressDialog progress, CancellationToken ct)
    {
        try
        {
            // executor.ConnectAsync が プロファイル登録 + 接続 + 履歴記録 を一括実行する。
            // 旧実装の「登録」「認証」2ステップは executor の単一呼び出しに統合される。
            progress.SetStep(0, StepState.Active, MWC.App.Resources.L.Get("Progress_Connecting"));
            var res = await executor.ConnectAsync(
                adapterId, ssid, auth, passphrase, TimeSpan.FromSeconds(25), ct);
            if (!res.Success)
            {
                progress.SetStep(0, StepState.Done);
                progress.SetStep(1, StepState.Error);
                return res;
            }
            progress.SetStep(0, StepState.Done);

            progress.SetStep(1, StepState.Active, MWC.App.Resources.L.Get("Progress_Authenticating"));
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
