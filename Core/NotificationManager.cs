using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Drawing;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// Windows notification manager for connection events
    /// </summary>
    public class NotificationManager : IDisposable
    {
        private NotifyIcon? _notifyIcon;
        private bool _isInitialized;
        private readonly System.Windows.Window? _mainWindow;

        public NotificationManager(System.Windows.Window? mainWindow = null)
        {
            _mainWindow = mainWindow;
            Initialize();
        }

        private void Initialize()
        {
            try
            {
                _notifyIcon = new NotifyIcon
                {
                    Icon = SystemIcons.Information,
                    Visible = true,
                    Text = "MurtiWifiConnecter"
                };

                // Create context menu
                var contextMenu = new ContextMenuStrip();

                contextMenu.Items.Add("Show", null, (s, e) => ShowMainWindow());
                contextMenu.Items.Add("Scan Networks", null, (s, e) => OnScanRequested());
                contextMenu.Items.Add("-");
                contextMenu.Items.Add("Exit", null, (s, e) => OnExitRequested());

                _notifyIcon.ContextMenuStrip = contextMenu;
                _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to initialize notification manager: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Show a notification
        /// </summary>
        public void ShowNotification(string title, string message, NotificationLevel level = NotificationLevel.Info)
        {
            if (!_isInitialized || _notifyIcon == null)
                return;

            try
            {
                var icon = level switch
                {
                    NotificationLevel.Success => ToolTipIcon.Info,
                    NotificationLevel.Warning => ToolTipIcon.Warning,
                    NotificationLevel.Error => ToolTipIcon.Error,
                    _ => ToolTipIcon.None
                };

                _notifyIcon.ShowBalloonTip(3000, title, message, icon);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to show notification: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Show connection success notification
        /// </summary>
        public void ShowConnectedNotification(string ssid)
        {
            ShowNotification(
                "Connected",
                $"Successfully connected to {ssid}",
                NotificationLevel.Success
            );
        }

        /// <summary>
        /// Show disconnection notification
        /// </summary>
        public void ShowDisconnectedNotification(string? ssid = null)
        {
            var message = string.IsNullOrEmpty(ssid)
                ? "WiFi disconnected"
                : $"Disconnected from {ssid}";

            ShowNotification(
                "Disconnected",
                message,
                NotificationLevel.Warning
            );
        }

        /// <summary>
        /// Show connection error notification
        /// </summary>
        public void ShowErrorNotification(string message)
        {
            ShowNotification(
                "Connection Error",
                message,
                NotificationLevel.Error
            );
        }

        /// <summary>
        /// Enable system tray mode (minimize to tray)
        /// </summary>
        public void EnableSystemTrayMode()
        {
            if (!_isInitialized || _notifyIcon == null)
                return;

            _notifyIcon.Visible = true;

            if (_mainWindow != null)
            {
                _mainWindow.ShowInTaskbar = false;
                _mainWindow.WindowState = WindowState.Minimized;
                _mainWindow.Hide();
            }

            ShowNotification(
                "Minimized to Tray",
                "MurtiWifiConnecter is running in the background",
                NotificationLevel.Info
            );
        }

        /// <summary>
        /// Show the main window
        /// </summary>
        public void ShowMainWindow()
        {
            if (_mainWindow != null)
            {
                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.ShowInTaskbar = true;
                _mainWindow.Activate();
            }
        }

        /// <summary>
        /// Hide to system tray
        /// </summary>
        public void HideToTray()
        {
            EnableSystemTrayMode();
        }

        /// <summary>
        /// Update tray icon tooltip
        /// </summary>
        public void UpdateTooltip(string text)
        {
            if (_notifyIcon != null)
            {
                // NotifyIcon.Text is limited to 63 characters
                if (text.Length > 63)
                    text = text.Substring(0, 60) + "...";

                _notifyIcon.Text = text;
            }
        }

        /// <summary>
        /// Event triggered when scan is requested from tray menu
        /// </summary>
        public event EventHandler? ScanRequested;

        private void OnScanRequested()
        {
            ScanRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Event triggered when exit is requested from tray menu
        /// </summary>
        public event EventHandler? ExitRequested;

        private void OnExitRequested()
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
            System.Windows.Application.Current?.Shutdown();
        }

        public void Dispose()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            _isInitialized = false;
        }
    }

    public enum NotificationLevel
    {
        Info,
        Success,
        Warning,
        Error
    }
}