using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using MWC.App.Controls;
using MWC.App.Services;
using MWC.App.ViewModels;
using MWC.App.Views;
using MWC.Core.Abstractions;
using MWC.Core.Models;
using MWC.Core.Services;

using MWC.App.Resources;
namespace MWC.App;

/// <summary>
/// MainWindow の各コマンド実装を集約。
/// MainWindow.xaml.cs から行動ロジックを分離して責務を明確化する。
///
/// 設計:
///   - MainWindow は「ウィンドウとイベント受け」のみ
///   - このクラスが「コマンド実行とサービス連携」を担う
///   - DI でサービスを直接受け取る
/// </summary>
public sealed class MainWindowCommands
{
    private readonly IWifiService              _wifi;
    private readonly NotificationService       _notify;
    private readonly NetworkHistoryService     _history;
    private readonly NetworkQualityService     _quality;
    private readonly SettingsService           _settings;
    private readonly ThemeService              _theme;
    private readonly ErrorHandlerService       _errors;
    private readonly KeyboardShortcutService   _shortcuts;
    private readonly IServiceProvider          _services;

    public MainWindowCommands(
        IWifiService wifi,
        NotificationService notify,
        NetworkHistoryService history,
        NetworkQualityService quality,
        SettingsService settings,
        ThemeService theme,
        ErrorHandlerService errors,
        KeyboardShortcutService shortcuts,
        IServiceProvider services)
    {
        _wifi = wifi; _notify = notify; _history = history;
        _quality = quality; _settings = settings; _theme = theme;
        _errors = errors; _shortcuts = shortcuts; _services = services;
    }

    /// <summary>
    /// 接続フロー(認証ダイアログ + ProgressDialog + アニメーション)。
    /// 戻り値: 成功したか
    /// </summary>
    public async Task<bool> ConnectAsync(MainViewModel vm, Window owner)
    {
        var net = vm.SelectedAdapter?.Selected;
        if (net is null) return false;

        string passphrase = "";
        if (net.Auth is not (AuthMethod.Open or AuthMethod.OWE))
        {
            var dlg = new ConnectDialog(net.Ssid, net.Auth) { Owner = owner };
            if (dlg.ShowDialog() != true) return false;
            passphrase = dlg.Passphrase ?? "";
        }

        if (vm.SelectedAdapter is null) return false;

        await AdapterConnectExtension.ConnectWithAppleFlowAsync(
            vm.SelectedAdapter, _wifi, net.Ssid, passphrase, net.Auth,
            _notify, _history, owner: owner);

        bool success = vm.SelectedAdapter.ConnectedSsid == net.Ssid;
        if (success)
        {
            AnimationHelper.PulseSuccessAsync(owner).Forget();
            AccessibilityService.AnnounceConnectionStatus(L.AnnounceConnected(net.Ssid));
        }
        else
        {
            AnimationHelper.ShakeAsync(owner).Forget();
            AccessibilityService.AnnounceError(L.AnnounceConnectFailed(net.Ssid));
        }
        return success;
    }

    public void ShowQrCode(MainViewModel vm, Window owner)
    {
        var net = vm.SelectedAdapter?.Selected;
        if (net is null) return;
        new QrCodeDialog(new WifiProfileSpec { Ssid = net.Ssid, Auth = net.Auth })
            { Owner = owner }.ShowDialog();
    }

    public void CopySsid(MainViewModel vm)
    {
        var ssid = vm.SelectedAdapter?.Selected?.Ssid;
        if (string.IsNullOrEmpty(ssid)) return;
        try
        {
            Clipboard.SetText(ssid);
            vm.StatusMessage = L.Format("Status_Copied", ssid);
            AccessibilityService.AnnounceConnectionStatus(L.AnnounceSsidCopied(ssid));
        }
        catch (Exception ex)
        {
            vm.StatusMessage = _errors.Handle(ex, "SSID コピー");
        }
    }

    public async Task DisconnectAsync(MainViewModel vm)
    {
        var ad = vm.SelectedAdapter;
        if (ad is null) return;
        await ad.DisconnectCommand.ExecuteAsync(null);
        vm.StatusMessage = L.Format("Status_Disconnected", ad.DisplayName);
    }

    public async Task<string> ExportAsync(MainViewModel vm, string format)
    {
        var nets = vm.SelectedAdapter?.SourceNetworks;
        if (nets is null || nets.Count == 0) return MWC.App.Resources.L.Get("Export_NoData");

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName   = $"mwc-scan-{DateTime.Now:yyyyMMdd-HHmmss}",
            DefaultExt = format == "json" ? "json" : format == "txt" ? "txt" : "csv",
            Filter     = format switch
            {
                "json" => "JSON (*.json)|*.json",
                "txt"  => "Text (*.txt)|*.txt",
                _      => "CSV (*.csv)|*.csv"
            }
        };
        if (dlg.ShowDialog() != true) return "";

        var result = await _errors.TryAsync(async () =>
        {
            await Task.Run(() =>
            {
                switch (format)
                {
                    case "json": ExportService.ToJson(nets, dlg.FileName); break;
                    case "txt":  ExportService.ToText(nets, dlg.FileName); break;
                    default:     ExportService.ToCsv (nets, dlg.FileName); break;
                }
            });
            return Path.GetFileName(dlg.FileName);
        }, MWC.App.Resources.L.Get("Export_Op"), $"format={format}");

        return result.Success
            ? $"Export → {result.Value}"
            : result.ErrorMessage ?? MWC.App.Resources.L.Get("Status_Failed");
    }

    public async Task<string> MeasureQualityAsync(string statusMessage)
    {
        var result = await _errors.TryAsync(
            () => _quality.MeasureAsync().AsTask(),
            MWC.App.Resources.L.Get("Quality_Op"));
        if (result.IsCancelled) return MWC.App.Resources.L.Get("Quality_Cancelled");
        if (!result.Success)    return result.ErrorMessage ?? MWC.App.Resources.L.Get("Quality_Failed");
        var r = result.Value;
        return L.QualityResultFormat(r.LatencyLabel, r.LossLabel, r.GradeLabel);
    }

    public void ShowSettings(Window owner, MainViewModel vm)
    {
        var svm = _services.GetService(typeof(SettingsViewModel)) as SettingsViewModel;
        if (svm is null) return;
        var dlg = new SettingsDialog(svm) { Owner = owner };
        if (dlg.ShowDialog() == true)
        {
            // 即時反映
            _theme.Apply(_settings.Current.Theme);
            vm.ApplySettings(_settings.Current);
        }
    }

    public void ShowAbout(Window owner)
        => new AboutDialog { Owner = owner }.ShowDialog();

    /// <summary>全無線子機を俯瞰するウィンドウを表示する。</summary>
    public void ShowAllAdapters(Window owner)
    {
        if (_services.GetService(typeof(AllAdaptersOverviewViewModel)) is not AllAdaptersOverviewViewModel vm)
            return;
        new AllAdaptersOverviewView(vm) { Owner = owner }.ShowDialog();
    }

    public void ShowShortcutHelp(Window owner)
        => new ShortcutHelpDialog(_shortcuts) { Owner = owner }.ShowDialog();

    public async Task ShowProfileManagerAsync(MainViewModel vm, Window owner)
    {
        var ad = vm.SelectedAdapter;
        if (ad is null) return;
        var pmVm = _services.GetService(typeof(ProfileManagerViewModel)) as ProfileManagerViewModel;
        if (pmVm is null) return;
        await pmVm.LoadAsync(ad.Id);
        new ProfileManagerDialog(pmVm) { Owner = owner }.ShowDialog();
        await ad.RefreshAsync();
    }

    public void PinNetwork(MainViewModel vm)
    {
        var ssid = vm.SelectedAdapter?.Selected?.Ssid;
        if (string.IsNullOrEmpty(ssid) || vm.SelectedAdapter is null) return;
        vm.SelectedAdapter.PinSsid(ssid);
        vm.StatusMessage = MWC.App.Resources.L.Format("Status_Pinned", ssid);
    }

    public AdapterPreferences? OpenAdapterPreferences(AdapterViewModel adapter, Window owner)
    {
        var dlg = new AdapterPreferencesDialog(adapter) { Owner = owner };
        return dlg.ShowDialog() == true ? adapter.Preferences : null;
    }
}
