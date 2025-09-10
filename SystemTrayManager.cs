using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    public class SystemTrayManager : IDisposable
    {
        private NotifyIcon _notifyIcon;
        private readonly MainWindow _mainWindow;
        private bool _disposed = false;
        private DateTime _lastNotificationTime = DateTime.MinValue;
        private NotificationLevel _lastNotificationLevel = NotificationLevel.Info;
        
        public event EventHandler ShowMainWindowRequested;
        public event EventHandler ExitApplicationRequested;
        public event EventHandler QuickConnectRequested;
        
        public SystemTrayManager(MainWindow mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            InitializeNotifyIcon();
        }
        
        private void InitializeNotifyIcon()
        {
            try
            {
                _notifyIcon = new NotifyIcon
                {
                    Icon = CreateWifiIcon(false),
                    Text = "Murti WiFi Connector",
                    Visible = true
                };
                
                _notifyIcon.Click += NotifyIcon_Click;
                _notifyIcon.DoubleClick += NotifyIcon_DoubleClick;
                
                var contextMenu = CreateContextMenu();
                _notifyIcon.ContextMenuStrip = contextMenu;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("SystemTrayManager.InitializeNotifyIcon", ex);
            }
        }
        
        private ContextMenuStrip CreateContextMenu()
        {
            var contextMenu = new ContextMenuStrip();
            
            var showItem = new ToolStripMenuItem("ウィンドウを表示");
            showItem.Click += (s, e) => ShowMainWindowRequested?.Invoke(this, EventArgs.Empty);
            showItem.Font = new Font(showItem.Font, FontStyle.Bold);
            contextMenu.Items.Add(showItem);
            
            contextMenu.Items.Add(new ToolStripSeparator());
            
            var quickConnectItem = new ToolStripMenuItem("クイック接続");
            quickConnectItem.Click += (s, e) => QuickConnectRequested?.Invoke(this, EventArgs.Empty);
            contextMenu.Items.Add(quickConnectItem);
            
            var refreshItem = new ToolStripMenuItem("ネットワーク更新");
            refreshItem.Click += async (s, e) => await RefreshNetworks();
            contextMenu.Items.Add(refreshItem);
            
            // プリセット設定メニュー
            var presetMenu = new ToolStripMenuItem("設定プリセット");
            var presets = QuickSettingsManager.GetAvailablePresets();
            
            foreach (var preset in presets)
            {
                var presetItem = new ToolStripMenuItem(preset.Key);
                presetItem.Click += (s, e) => 
                {
                    try
                    {
                        preset.Value.Invoke();
                        ShowBalloonTip("設定変更", $"{preset.Key}を適用しました", ToolTipIcon.Info);
                    }
                    catch (Exception ex)
                    {
                        ErrorHandler.LogError($"SystemTrayManager.ApplyPreset.{preset.Key}", ex);
                        ShowBalloonTip("エラー", $"設定適用に失敗しました: {preset.Key}", ToolTipIcon.Error);
                    }
                };
                presetMenu.DropDownItems.Add(presetItem);
            }
            
            contextMenu.Items.Add(presetMenu);
            
            // 簡易ステータス表示
            var statusItem = new ToolStripMenuItem("システム状況");
            statusItem.Click += async (s, e) => await ShowSystemStatus();
            contextMenu.Items.Add(statusItem);
            
            contextMenu.Items.Add(new ToolStripSeparator());
            
            var exitItem = new ToolStripMenuItem("終了");
            exitItem.Click += (s, e) => ExitApplicationRequested?.Invoke(this, EventArgs.Empty);
            contextMenu.Items.Add(exitItem);
            
            return contextMenu;
        }
        
        private async Task RefreshNetworks()
        {
            try
            {
                ShowBalloonTip("ネットワーク更新中...", "WiFiネットワークを検索しています", ToolTipIcon.Info);
                
                // メインウィンドウのリフレッシュメソッドを呼び出し
                if (_mainWindow?.Dispatcher != null)
                {
                    await _mainWindow.Dispatcher.InvokeAsync(async () =>
                    {
                        // リフレッシュボタンのクリックイベントを模擬
                        var refreshMethod = typeof(MainWindow).GetMethod("LoadWifiNetworksAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (refreshMethod != null)
                        {
                            await (Task)refreshMethod.Invoke(_mainWindow, null);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("SystemTrayManager.RefreshNetworks", ex);
                ShowBalloonTip("エラー", "ネットワーク更新に失敗しました", ToolTipIcon.Error);
            }
        }
        
        private async Task ShowSystemStatus()
        {
            try
            {
                var healthStatus = await ErrorHandler.PerformSystemHealthCheckAsync();
                var errorSummary = ErrorHandler.GetErrorSummary();
                
                var statusMessage = $"システム健全性: {healthStatus.OverallHealth}\n" +
                                  $"メモリ使用量: {healthStatus.MemoryUsageMB}MB\n" +
                                  $"エラー統計: {errorSummary}\n" +
                                  $"ネットワーク接続: {(healthStatus.NetworkConnectivity ? "OK" : "NG")}";
                
                var icon = healthStatus.OverallHealth switch
                {
                    HealthLevel.Good => ToolTipIcon.Info,
                    HealthLevel.Warning => ToolTipIcon.Warning,
                    HealthLevel.Critical => ToolTipIcon.Error,
                    _ => ToolTipIcon.None
                };
                
                ShowBalloonTip("システム状況", statusMessage, icon, 5000);
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("SystemTrayManager.ShowSystemStatus", ex);
                ShowBalloonTip("エラー", "システム状況の取得に失敗しました", ToolTipIcon.Error);
            }
        }
        
        private void NotifyIcon_Click(object sender, EventArgs e)
        {
            if (e is MouseEventArgs mouseArgs && mouseArgs.Button == MouseButtons.Left)
            {
                ShowMainWindowRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        
        private void NotifyIcon_DoubleClick(object sender, EventArgs e)
        {
            ShowMainWindowRequested?.Invoke(this, EventArgs.Empty);
        }
        
        public void UpdateConnectionStatus(bool isConnected, string ssid = null)
        {
            try
            {
                if (_disposed || _notifyIcon == null) return;
                
                _notifyIcon.Icon = CreateWifiIcon(isConnected);
                
                if (isConnected && !string.IsNullOrWhiteSpace(ssid))
                {
                    _notifyIcon.Text = $"Murti WiFi Connector - 接続中: {ssid}";
                }
                else
                {
                    _notifyIcon.Text = "Murti WiFi Connector - 未接続";
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("SystemTrayManager.UpdateConnectionStatus", ex);
            }
        }
        
        public void ShowBalloonTip(string title, string text, ToolTipIcon icon = ToolTipIcon.Info, int timeout = 3000)
        {
            try
            {
                if (_disposed || _notifyIcon == null) return;
                
                _notifyIcon.ShowBalloonTip(timeout, title, text, icon);
                _lastNotificationTime = DateTime.Now;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("SystemTrayManager.ShowBalloonTip", ex);
            }
        }
        
        public void ShowProgressNotification(string operation, int progressPercent)
        {
            try
            {
                if (_disposed || _notifyIcon == null) return;
                
                var title = $"{operation} - {progressPercent}%";
                var text = progressPercent switch
                {
                    < 25 => "開始中...",
                    < 50 => "処理中...",
                    < 75 => "もうすぐ完了...",
                    < 100 => "最終処理中...",
                    _ => "完了"
                };
                
                var timeout = progressPercent >= 100 ? 2000 : 1500;
                ShowBalloonTip(title, text, ToolTipIcon.Info, timeout);
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("SystemTrayManager.ShowProgressNotification", ex);
            }
        }
        
        public void ShowConnectionStatusNotification(string ssid, bool connected, string qualityInfo = null)
        {
            try
            {
                if (_disposed || _notifyIcon == null) return;
                
                var title = connected ? "接続成功" : "接続失敗";
                var text = connected 
                    ? $"{ssid}に接続しました" + (string.IsNullOrEmpty(qualityInfo) ? "" : $"\n{qualityInfo}")
                    : $"{ssid}への接続に失敗しました";
                
                var icon = connected ? ToolTipIcon.Info : ToolTipIcon.Warning;
                var timeout = connected ? 3000 : 4000;
                
                ShowBalloonTip(title, text, icon, timeout);
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("SystemTrayManager.ShowConnectionStatusNotification", ex);
            }
        }
        
        public void ShowSecurityWarning(string networkName, string warningMessage, SecurityWarningLevel level)
        {
            try
            {
                if (_disposed || _notifyIcon == null) return;
                
                var now = DateTime.Now;
                var minInterval = level switch
                {
                    SecurityWarningLevel.Critical => TimeSpan.FromMinutes(1),
                    SecurityWarningLevel.High => TimeSpan.FromMinutes(5),
                    SecurityWarningLevel.Medium => TimeSpan.FromMinutes(10),
                    _ => TimeSpan.FromMinutes(30)
                };
                
                if (now - _lastNotificationTime < minInterval && _lastNotificationLevel != NotificationLevel.Critical)
                    return;
                
                var title = level switch
                {
                    SecurityWarningLevel.Critical => "🔴 緊急セキュリティ警告",
                    SecurityWarningLevel.High => "⚠️ セキュリティ警告",
                    SecurityWarningLevel.Medium => "⚡ セキュリティ注意",
                    _ => "ℹ️ セキュリティ情報"
                };
                
                var text = $"ネットワーク: {networkName}\n{warningMessage}";
                var icon = level >= SecurityWarningLevel.High ? ToolTipIcon.Error : ToolTipIcon.Warning;
                var timeout = level switch
                {
                    SecurityWarningLevel.Critical => 8000,
                    SecurityWarningLevel.High => 6000,
                    SecurityWarningLevel.Medium => 4000,
                    _ => 3000
                };
                
                ShowBalloonTip(title, text, icon, timeout);
                _lastNotificationLevel = level >= SecurityWarningLevel.High ? NotificationLevel.Critical : NotificationLevel.Warning;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("SystemTrayManager.ShowSecurityWarning", ex);
            }
        }
        
        public void ShowMaintenanceNotification(string maintenanceType, string details)
        {
            try
            {
                if (_disposed || _notifyIcon == null) return;
                
                var title = $"メンテナンス: {maintenanceType}";
                var text = details;
                
                ShowBalloonTip(title, text, ToolTipIcon.Info, 2000);
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("SystemTrayManager.ShowMaintenanceNotification", ex);
            }
        }
        
        public void SetVisible(bool visible)
        {
            try
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = visible;
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("SystemTrayManager.SetVisible", ex);
            }
        }
        
        private Icon CreateWifiIcon(bool isConnected)
        {
            try
            {
                using var bitmap = new Bitmap(16, 16);
                using var graphics = Graphics.FromImage(bitmap);
                
                graphics.Clear(Color.Transparent);
                
                // WiFiアイコンを描画
                var color = isConnected ? Color.Green : Color.Gray;
                using var brush = new SolidBrush(color);
                using var pen = new Pen(color, 1);
                
                // 簡単なWiFi波形パターン
                graphics.DrawArc(pen, 2, 8, 4, 4, 180, 180);
                graphics.DrawArc(pen, 1, 6, 6, 6, 180, 180);
                graphics.DrawArc(pen, 0, 4, 8, 8, 180, 180);
                
                // 接続点
                graphics.FillEllipse(brush, 3, 11, 2, 2);
                
                return Icon.FromHandle(bitmap.GetHicon());
            }
            catch
            {
                // フォールバック：システムアイコンを使用
                return SystemIcons.Information;
            }
        }
        
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                try
                {
                    if (_notifyIcon != null)
                    {
                        _notifyIcon.Visible = false;
                        _notifyIcon.Dispose();
                        _notifyIcon = null;
                    }
                }
                catch (Exception ex)
                {
                    ErrorHandler.LogError("SystemTrayManager.Dispose", ex);
                }
                finally
                {
                    _disposed = true;
                }
            }
        }
        
        ~SystemTrayManager()
        {
            Dispose(false);
        }
    }
    
    public enum NotificationLevel
    {
        Info,
        Warning,
        Critical
    }
    
    public enum SecurityWarningLevel
    {
        Low,
        Medium,
        High,
        Critical
    }
}