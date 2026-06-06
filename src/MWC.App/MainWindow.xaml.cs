using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using MWC.App.Services;
using MWC.App.ViewModels;
using MWC.App.Views;
using MWC.Core.Models;

using MWC.App.Resources;
namespace MWC.App;

/// <summary>
/// MainWindow — Apple HIG: ウィンドウとイベント受けのみ。
/// すべてのロジックは MainWindowCommands 経由で実行。
/// 前バージョンの 409 行から責務を抽出して削減。
/// </summary>
public partial class MainWindow : Window
{
    private MainWindowCommands?       _cmd;
    private AppUpdateService?         _updater;
    private JumpListService?          _jumpList;
    private NetworkHistoryService?    _history;

    public MainWindow()
    {
        InitializeComponent();
        Loaded  += OnLoaded;
        Closed  += OnClosed;
        KeyDown += OnKeyDown;
    }

    // ── ライフサイクル ──────────────────────────────
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var svc = App.Host.Services;
            _cmd      = svc.GetRequiredService<MainWindowCommands>();
            _updater  = svc.GetService<AppUpdateService>();
            _jumpList = svc.GetService<JumpListService>();
            _history  = svc.GetService<NetworkHistoryService>();

            // トレイの「メインウィンドウを開く」要求は App 側で一元購読する
            // (ここで二重購読すると前面化処理が 2 回走るため購読しない)

            if (DataContext is not MainViewModel vm) return;
            await vm.LoadCommand.ExecuteAsync(null);
            UpdateJumpList(vm);
            CheckForUpdatesAsync(vm).Forget();
        }
        catch (Exception ex)
        {
            MessageBox.Show(MWC.App.Resources.L.Format("Error_Startup", ex.Message), "MWC",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.Dispose();
    }

    private async Task CheckForUpdatesAsync(MainViewModel vm)
    {
        await Task.Delay(3000);
        if (_updater is null) return;
        try
        {
            var r = await _updater.CheckAsync();
            if (r.HasUpdate)
                Dispatcher.Invoke(() => vm.StatusMessage = MWC.App.Resources.L.Format("Status_UpdateAvailable", r.LatestVersion));
        }
        catch { /* バックグラウンド更新の失敗は静かに */ }
    }

    // ── キーボードショートカット ──────────────────────
    private async void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm || _cmd is null) return;
        try
        {
            switch (e.Key, Keyboard.Modifiers)
            {
                case (Key.R,      ModifierKeys.Control):
                    vm.RefreshCommand.ExecuteAsync(null).Forget(); e.Handled = true; break;
                case (Key.F,      ModifierKeys.Control):
                    SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; break;
                case (Key.W,      ModifierKeys.Control):
                    Hide(); e.Handled = true; break;
                case (Key.D,      ModifierKeys.Control):
                    await _cmd.DisconnectAsync(vm); e.Handled = true; break;
                case (Key.Q,      ModifierKeys.Control):
                    _cmd.ShowQrCode(vm, this); e.Handled = true; break;
                case (Key.E,      ModifierKeys.Control):
                    vm.StatusMessage = await _cmd.ExportAsync(vm, "csv"); e.Handled = true; break;
                case (Key.K,      ModifierKeys.Control):
                    vm.StatusMessage = await _cmd.MeasureQualityAsync(vm.StatusMessage);
                    e.Handled = true; break;
                case (Key.OemComma, ModifierKeys.Control):
                    _cmd.ShowSettings(this, vm); e.Handled = true; break;
                case (Key.M,      ModifierKeys.Control):
                    vm.Filter.ToggleExpertModeCommand.Execute(null); e.Handled = true; break;
                case (Key.A,      ModifierKeys.Control | ModifierKeys.Shift):
                    _cmd.ShowAllAdapters(this); e.Handled = true; break;
                case (Key.F1,     ModifierKeys.None):
                    _cmd.ShowShortcutHelp(this); e.Handled = true; break;
                case (Key.Escape, ModifierKeys.None) when !string.IsNullOrEmpty(vm.Filter.SearchText):
                    vm.Filter.SearchText = ""; e.Handled = true; break;
                case (Key.Return, ModifierKeys.None) when vm.SelectedAdapter?.Selected is not null:
                    await _cmd.ConnectAsync(vm, this);
                    UpdateJumpList(vm); e.Handled = true; break;
            }
        }
        catch (Exception ex)
        {
            vm.StatusMessage = MWC.App.Resources.L.Format("Error_Operation", ex.Message);
        }
    }

    // ── ⋯ オーバーフローメニュー ──────────────────────
    private void OnOverflowMenu(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu is ContextMenu cm)
        {
            cm.PlacementTarget = btn;
            cm.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            cm.IsOpen = true;
        }
    }

    // ── イベントハンドラ(Commandsへ委譲) ─────────────
    internal async void OnConnectClick(object sender, RoutedEventArgs e)
    {
        await AsyncEventHelper.SafeRunAsync(null, "OnConnectClick", async () =>
        {
            if (DataContext is not MainViewModel vm || _cmd is null) return;
            await _cmd.ConnectAsync(vm, this);
            UpdateJumpList(vm);
        });
    }

    internal void OnShowQrClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) _cmd?.ShowQrCode(vm, this);
    }

    private void OnCopySsid(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) _cmd?.CopySsid(vm);
    }

    private void OnPinNetwork(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) _cmd?.PinNetwork(vm);
    }

    private void OnHideNetwork(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var ssid = vm.SelectedAdapter?.Selected?.Ssid;
        if (string.IsNullOrEmpty(ssid)) return;
        vm.StatusMessage = MWC.App.Resources.L.Format("Status_Hidden", ssid);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) _cmd?.ShowSettings(this, vm);
    }

    internal async void OnProfileManagerClick(object sender, RoutedEventArgs e)
    {
        await AsyncEventHelper.SafeRunAsync(null, "OnProfileManagerClick", async () =>
        {
            if (DataContext is MainViewModel vm && _cmd is not null)
                await _cmd.ShowProfileManagerAsync(vm, this);
        });
    }

    internal async void OnMeasureQualityClick(object sender, RoutedEventArgs e)
    {
        await AsyncEventHelper.SafeRunAsync(null, "OnMeasureQualityClick", async () =>
        {
            if (DataContext is not MainViewModel vm || _cmd is null) return;
            vm.StatusMessage = L.Get("Status_Scanning");
            vm.StatusMessage = await _cmd.MeasureQualityAsync(vm.StatusMessage);
        });
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
        => _cmd?.ShowAbout(this);

    private void OnAllAdaptersClick(object sender, RoutedEventArgs e)
        => _cmd?.ShowAllAdapters(this);

    // ── 子機(アダプター)固有メニュー ────────────────
    private static AdapterViewModel? GetAdapterFromMenu(object sender)
    {
        if (sender is not MenuItem mi) return null;
        if (mi.Parent is ContextMenu cm && cm.PlacementTarget is FrameworkElement fe)
            return fe.DataContext as AdapterViewModel;
        return null;
    }

    private void OnAdapterPreferencesClick(object sender, RoutedEventArgs e)
    {
        var ad = GetAdapterFromMenu(sender);
        if (ad is null || _cmd is null) return;
        var prefs = _cmd.OpenAdapterPreferences(ad, this);
        if (prefs is not null && DataContext is MainViewModel vm)
            vm.LoadCommand.ExecuteAsync(null).Forget();
    }

    private async void OnAdapterRefreshClick(object sender, RoutedEventArgs e)
    {
        await AsyncEventHelper.SafeRunAsync(null, "OnAdapterRefreshClick", async () =>
        {
            var ad = GetAdapterFromMenu(sender);
            if (ad is not null) await ad.RefreshAsync();
        });
    }

    private async void OnAdapterDisconnectClick(object sender, RoutedEventArgs e)
    {
        await AsyncEventHelper.SafeRunAsync(null, "OnAdapterDisconnectClick", async () =>
        {
            var ad = GetAdapterFromMenu(sender);
            if (ad is null) return;
            await ad.DisconnectCommand.ExecuteAsync(null);
            if (DataContext is MainViewModel vm)
                vm.StatusMessage = L.StatusDisconnected(ad.DisplayName);
        });
    }

    // ── チャンネルバンド切替 ──────────────────────────
    private void OnBandSelect2_4(object sender, RoutedEventArgs e)
        => ChannelCanvas.BandFilter = WifiBand.Band2_4GHz;
    private void OnBandSelect5(object sender, RoutedEventArgs e)
        => ChannelCanvas.BandFilter = WifiBand.Band5GHz;
    private void OnBandSelect6(object sender, RoutedEventArgs e)
        => ChannelCanvas.BandFilter = WifiBand.Band6GHz;

    // ── JumpList更新 ──────────────────────────────────
    private void UpdateJumpList(MainViewModel vm)
    {
        _jumpList?.Update(
            vm.SelectedAdapter?.SourceNetworks ?? Array.Empty<WifiNetwork>(),
            _history?.GetRecentSsids(5) ?? Array.Empty<string>());
    }
}
