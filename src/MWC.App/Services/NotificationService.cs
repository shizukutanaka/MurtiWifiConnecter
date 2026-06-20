using System;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Extensions.Logging;
using MWC.Core.Models;
using ToolTipIcon = System.Windows.Forms.ToolTipIcon;

namespace MWC.App.Services;

/// <summary>
/// Windows アクションセンター(トースト)通知サービス。
/// Apple の「明確なフィードバック」原則:
///   接続成功・失敗・キャプティブポータル検出をユーザーに即知らせる。
///
/// 実装: Win32 Shell_NotifyIcon + WPF ToolTip 併用。
/// .NET 8 時点で WinRT Toast は MSIX 外から呼びにくいため
/// NotifyIcon.ShowBalloonTip でフォールバック。
/// </summary>
public sealed class NotificationService
{
    private readonly ILogger<NotificationService> _log;
    private readonly System.Windows.Forms.NotifyIcon? _tray;
    private const string AppName = "MWC";

    public NotificationService(
        ILogger<NotificationService> log,
        System.Windows.Forms.NotifyIcon? tray = null)
    {
        _log  = log;
        _tray = tray;
    }

    public void NotifyConnected(string ssid, bool hasInternet, bool captive)
    {
        if (captive)
        {
            Show(MWC.App.Resources.L.NotifyConnectedTo(ssid),
                MWC.App.Resources.L.Get("Notify_CaptivePortal"),
                ToolTipIcon.Warning);
        }
        else if (hasInternet)
        {
            Show(MWC.App.Resources.L.NotifyConnectedComplete(ssid),
                MWC.App.Resources.L.Get("Notify_InternetOk"),
                ToolTipIcon.Info);
        }
        else
        {
            Show(MWC.App.Resources.L.NotifyConnectedTo(ssid),
                MWC.App.Resources.L.Get("Notify_NoInternet"),
                ToolTipIcon.Warning);
        }
    }

    public void NotifyDisconnected(string ssid)
        => Show(MWC.App.Resources.L.NotifyDisconnected(ssid), "", ToolTipIcon.Info);

    public void NotifyFailed(string ssid, ConnectionFailure failure)
    {
        var msg = failure switch
        {
            ConnectionFailure.BadCredentials      => MWC.App.Resources.L.Get("Notify_BadCredentials"),
            ConnectionFailure.Timeout             => MWC.App.Resources.L.Get("Notify_Timeout"),
            ConnectionFailure.NotInRange          => MWC.App.Resources.L.Get("Notify_NotInRange"),
            ConnectionFailure.AdapterDisabled     => MWC.App.Resources.L.Get("Notify_AdapterDisabled"),
            ConnectionFailure.InsufficientPrivilege => MWC.App.Resources.L.Get("Notify_InsufficientPrivilege"),
            _ => MWC.App.Resources.L.Get("Notify_GenericFailure")
        };
        Show(MWC.App.Resources.L.NotifyCannotConnect(ssid), msg, ToolTipIcon.Error);
    }

    private void Show(string title, string text, ToolTipIcon icon)
    {
        // タイトル/本文には SSID が埋め込まれているため(例: "Connected to MyWifi")、
        // 永続ログには内容を出さず重要度のみ記録する。SSID の平文ログ化を防ぐ
        // (DiagnosticBundle / 各接続ログと同じ PII 方針)。
        _log.LogInformation("Notification shown (severity={icon})", icon);
        try
        {
            _tray?.ShowBalloonTip(3000,
                $"{AppName} — {title}",
                string.IsNullOrEmpty(text) ? title : text,
                icon);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "BalloonTip failed");
        }
    }
}
