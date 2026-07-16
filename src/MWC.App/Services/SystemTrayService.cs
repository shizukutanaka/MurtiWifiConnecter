using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using MWC.Core.Models;

namespace MWC.App.Services;

/// <summary>
/// Windows 通知領域常駐。
/// 各アダプター(子機)を独立したサブメニューに表示し、
/// 子機ごとに異なる SSID へ接続できるよう Apple "Multi-tasking" 流に拡張。
///
/// メニュー構造:
///   ├─ 接続中: HomeNet (Wi-Fi 1)
///   ├─ 接続中: GuestWiFi (Wi-Fi 2 / USB)
///   ├─ ─────────────
///   ├─ Wi-Fi 1 (内蔵 Intel AX211)         ▸ SSID 一覧 →
///   ├─ Wi-Fi 2 (USB Realtek)               ▸ SSID 一覧 →
///   ├─ ─────────────
///   ├─ MWC を開く
///   └─ 終了
/// </summary>
public sealed class SystemTrayService : IDisposable
{
    private readonly NotifyIcon                  _tray;
    private readonly Dispatcher                  _dispatcher;
    private readonly ILogger<SystemTrayService>  _log;
    private bool _disposed;

    public event Action? RequestOpenMainWindow;

    // Accept the shared NotifyIcon singleton so only one tray icon is visible.
    // Creating a second NotifyIcon here would show two icons in the taskbar.
    public SystemTrayService(NotifyIcon tray, Dispatcher dispatcher, ILogger<SystemTrayService> log)
    {
        _dispatcher = dispatcher; _log = log;
        _tray = tray;
        _tray.Text    = MWC.App.Resources.L.AppTitle;
        _tray.Visible = true;
        _tray.Icon    = BuildIcon(quality: 0, connected: false);
        _tray.DoubleClick += (_, _) => _dispatcher.Invoke(() => RequestOpenMainWindow?.Invoke());
    }

    /// <summary>子機毎のサブメニュー全体を再構築</summary>
    public void UpdateAdapterMenus(
        IReadOnlyList<AdapterMenuModel> adapters,
        Func<Guid, string, Task> connectCallback,
        Func<Guid, Task> disconnectCallback)
    {
        var menu = new ContextMenuStrip();

        // ── 各アダプターの現在の接続状態 ──
        foreach (var a in adapters.Where(x => x.ConnectedSsid is not null))
        {
            var item = new ToolStripMenuItem(
                MWC.App.Resources.L.Format("Tray_Connected", a.ConnectedSsid, a.Name))
            {
                Font    = new Font(SystemFonts.MenuFont!, FontStyle.Bold),
                Enabled = true
            };
            var idCopy = a.Id;
            item.Click += async (_, _) =>
            {
                try { await disconnectCallback(idCopy); }
                catch (Exception ex) { _log.LogWarning(ex, "tray disconnect"); }
            };
            menu.Items.Add(item);
        }
        if (adapters.Any(x => x.ConnectedSsid is not null))
            menu.Items.Add(new ToolStripSeparator());

        // ── 各アダプターのサブメニュー(子機ごとSSID選択) ──
        foreach (var a in adapters)
        {
            var adapterItem = new ToolStripMenuItem(MWC.App.Resources.L.Format("Tray_AdapterMenuItem", a.Name));
            if (a.Description.Length > 0)
                adapterItem.ToolTipText = a.Description;

            // 接続状態表示
            var status = new ToolStripMenuItem(
                a.ConnectedSsid is null
                    ? MWC.App.Resources.L.Get("Tray_NotConnected")
                    : MWC.App.Resources.L.Format("Status_ConnectedTo_Short", a.ConnectedSsid))
            {
                Enabled = false,
                Font    = new Font(SystemFonts.MenuFont!, FontStyle.Italic)
            };
            adapterItem.DropDownItems.Add(status);
            adapterItem.DropDownItems.Add(new ToolStripSeparator());

            // 上位8ネットワーク
            int n = 0;
            foreach (var net in a.Networks.OrderByDescending(x => x.IsConnected)
                                         .ThenByDescending(x => x.SignalQuality))
            {
                if (n++ >= 8) break;
                var item = new ToolStripMenuItem(
                    MWC.App.Resources.L.Format("Tray_NetworkItem", net.Ssid, net.SignalQuality))
                {
                    Checked      = net.IsConnected,
                    CheckOnClick = false
                };
                var ssidCopy    = net.Ssid;
                var adapterIdCopy = a.Id;
                item.Click += async (_, _) =>
                {
                    try { await connectCallback(adapterIdCopy, ssidCopy); }
                    catch (Exception ex) { _log.LogWarning(ex, "tray connect"); }
                };
                adapterItem.DropDownItems.Add(item);
            }

            if (n == 0)
                adapterItem.DropDownItems.Add(
                    new ToolStripMenuItem(MWC.App.Resources.L.TrayNoNetworks) { Enabled = false });

            // 切断 (接続中のみ有効)
            adapterItem.DropDownItems.Add(new ToolStripSeparator());
            var dcItem = new ToolStripMenuItem(MWC.App.Resources.L.Get("Tray_Disconnect"))
            {
                Enabled = a.ConnectedSsid is not null
            };
            var idCopy2 = a.Id;
            dcItem.Click += async (_, _) =>
            {
                try { await disconnectCallback(idCopy2); }
                catch (Exception ex) { _log.LogWarning(ex, "tray sub disconnect"); }
            };
            adapterItem.DropDownItems.Add(dcItem);

            menu.Items.Add(adapterItem);
        }

        // ── 操作 ──
        menu.Items.Add(new ToolStripSeparator());
        var openItem = new ToolStripMenuItem(MWC.App.Resources.L.TrayOpenApp);
        openItem.Click += (_, _) => _dispatcher.Invoke(() => RequestOpenMainWindow?.Invoke());
        menu.Items.Add(openItem);

        var exitItem = new ToolStripMenuItem(MWC.App.Resources.L.Get("Tray_Exit"));
        exitItem.Click += (_, _) => _dispatcher.Invoke(() =>
            System.Windows.Application.Current.Shutdown());
        menu.Items.Add(exitItem);

        // 旧メニューを破棄してから差し替え (GDI ハンドル/メモリリーク防止)
        var oldMenu = _tray.ContextMenuStrip;
        _tray.ContextMenuStrip = menu;
        oldMenu?.Dispose();
    }

    /// <summary>(後方互換) 単一アダプター用の旧 API</summary>
    public void UpdateNetworkMenu(
        IReadOnlyList<WifiNetwork> networks,
        Func<string, Task> connectCallback)
    {
        // 廃止予定。UpdateAdapterMenusを使う。
        _log.LogDebug("UpdateNetworkMenu called - prefer UpdateAdapterMenus");
    }

    public void UpdateStatus(string? ssid, int signalQuality)
    {
        var text = ssid is null
            ? MWC.App.Resources.L.TrayNotConnected
            : MWC.App.Resources.L.TrayStatusConnected(ssid, signalQuality);
        // WinForms NotifyIcon.Text throws ArgumentException above 63 chars.
        _tray.Text = text.Length > 63 ? text[..63] : text;
        // 旧アイコンを破棄してから差し替える (各 BuildIcon は独立した GDI ハンドルを
        // 確保するため、破棄しないと更新の度に HICON がリークする)。
        var old = _tray.Icon;
        _tray.Icon = BuildIcon(signalQuality, ssid is not null);
        old?.Dispose();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(
        System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    private static Icon BuildIcon(int quality, bool connected)
    {
        using var bmp = new Bitmap(16, 16);
        using var g   = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);

        int bars = quality switch { >= 75 => 4, >= 50 => 3, >= 25 => 2, > 0 => 1, _ => 0 };
        var c = connected ? Color.FromArgb(0, 196, 204) : Color.FromArgb(150, 150, 150);

        int[] heights = { 4, 6, 9, 12 };
        int[] xs      = { 1, 5, 9, 13 };
        for (int i = 0; i < 4; i++)
        {
            var color = i < bars ? c : Color.FromArgb(60, 60, 60);
            using var br = new SolidBrush(color);
            g.FillRectangle(br, xs[i], 15 - heights[i], 3, heights[i]);
        }
        // GetHicon() は所有権を移さない HICON を返す。Icon.FromHandle もハンドルを
        // 所有しないため、独立したマネージドコピー (Clone) を作ってから元の
        // ネイティブハンドルを解放することで GDI リークを防ぐ。
        IntPtr hicon = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hicon);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hicon);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tray.Visible = false;
        _tray.ContextMenuStrip?.Dispose();
        // Dispose only the GDI icon clone created by BuildIcon/UpdateStatus.
        // The NotifyIcon itself is owned by the DI container and disposed separately.
        var icon = _tray.Icon;
        _tray.Icon = null;
        icon?.Dispose();
    }
}

/// <summary>トレイメニュー用のアダプター情報モデル(MainWindow → SystemTrayService)</summary>
public sealed record AdapterMenuModel(
    Guid                       Id,
    string                     Name,
    string                     Description,
    string?                    ConnectedSsid,
    IReadOnlyList<WifiNetwork> Networks);
