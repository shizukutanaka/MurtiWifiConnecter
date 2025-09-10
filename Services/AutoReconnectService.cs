using System;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Services
{
    /// <summary>
    /// 自動再接続サービス - WiFi切断時の自動復旧
    /// </summary>
    public class AutoReconnectService : IDisposable
    {
        private readonly OptimizedWifiManager _wifiManager;
        private readonly NetworkPriorityManager _priorityManager;
        private Timer _checkTimer;
        private bool _isReconnecting = false;
        private string _lastConnectedSSID;
        private string _lastConnectedPassword;
        private DateTime _lastDisconnectTime = DateTime.MinValue;
        private int _reconnectAttempts = 0;
        private bool _disposed = false;

        public event EventHandler<ReconnectEventArgs> ReconnectAttempted;
        public event EventHandler<ReconnectEventArgs> ReconnectSucceeded;
        public event EventHandler<ReconnectEventArgs> ReconnectFailed;

        public bool IsEnabled { get; private set; }
        public int MaxReconnectAttempts { get; set; } = 5;
        public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(10);

        public AutoReconnectService(OptimizedWifiManager wifiManager, NetworkPriorityManager priorityManager = null)
        {
            _wifiManager = wifiManager ?? throw new ArgumentNullException(nameof(wifiManager));
            _priorityManager = priorityManager ?? new NetworkPriorityManager();
            
            // 設定から自動再接続の有効/無効を取得
            IsEnabled = QuickSettingsManager.GetSetting("connection.auto_reconnect", true);
            MaxReconnectAttempts = QuickSettingsManager.GetSetting("connection.reconnect_attempts", 5);
            
            // WiFi接続変更イベントを監視
            _wifiManager.ConnectionChanged += OnConnectionChanged;
        }

        public void Start()
        {
            if (_disposed || IsEnabled) return;
            
            IsEnabled = true;
            QuickSettingsManager.SetSetting("connection.auto_reconnect", true);
            
            // 定期チェック開始
            _checkTimer = new Timer(async _ => await CheckAndReconnectAsync(), null, 
                CheckInterval, CheckInterval);
            
            SimpleLoggingService.LogInfo("Auto-reconnect service started");
        }

        public void Stop()
        {
            if (_disposed || !IsEnabled) return;
            
            IsEnabled = false;
            QuickSettingsManager.SetSetting("connection.auto_reconnect", false);
            
            _checkTimer?.Dispose();
            _checkTimer = null;
            _isReconnecting = false;
            _reconnectAttempts = 0;
            
            SimpleLoggingService.LogInfo("Auto-reconnect service stopped");
        }

        public void SaveCredentials(string ssid, string password)
        {
            if (string.IsNullOrWhiteSpace(ssid)) return;
            
            _lastConnectedSSID = ssid;
            _lastConnectedPassword = password;
            
            // 暗号化して保存（セキュリティ考慮）
            if (QuickSettingsManager.GetSetting("security.save_credentials", true))
            {
                QuickSettingsManager.SetSetting($"credentials.{ssid}.saved", true);
                // 実際のパスワード保存は暗号化が必要（ここでは保存しない）
            }
        }

        private async void OnConnectionChanged(object sender, WifiConnectionEventArgs e)
        {
            if (e.IsConnected)
            {
                // 接続成功時
                _lastConnectedSSID = e.SSID;
                _reconnectAttempts = 0;
                _isReconnecting = false;
                
                SimpleLoggingService.LogInfo($"Connected to {e.SSID}");
            }
            else if (!string.IsNullOrEmpty(_lastConnectedSSID))
            {
                // 切断時
                _lastDisconnectTime = DateTime.Now;
                SimpleLoggingService.LogWarning($"Disconnected from {_lastConnectedSSID}");
                
                // 自動再接続が有効なら再接続試行
                if (IsEnabled && !_isReconnecting)
                {
                    await Task.Delay(ReconnectDelay);
                    await TryReconnectAsync();
                }
            }
        }

        private async Task CheckAndReconnectAsync()
        {
            if (_disposed || !IsEnabled || _isReconnecting) return;
            
            try
            {
                // 現在の接続状態を確認
                var currentSSID = await _wifiManager.GetCurrentSSIDAsync();
                
                if (string.IsNullOrEmpty(currentSSID))
                {
                    // 未接続状態
                    if (!string.IsNullOrEmpty(_lastConnectedSSID) && 
                        DateTime.Now - _lastDisconnectTime > TimeSpan.FromSeconds(30))
                    {
                        // 30秒以上切断されている場合、優先ネットワークを使用した再接続試行
                        await TryReconnectWithPriorityAsync();
                    }
                }
                else
                {
                    // 接続中
                    _reconnectAttempts = 0;
                    _isReconnecting = false;
                    
                    // インターネット接続確認
                    var hasInternet = await _wifiManager.TestInternetConnectionAsync();
                    if (!hasInternet)
                    {
                        SimpleLoggingService.LogWarning($"Connected to {currentSSID} but no internet access");
                        
                        // インターネット接続がない場合、より良いネットワークを探す
                        await TryUpgradeNetworkAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                SimpleLoggingService.LogError("Auto-reconnect check failed", ex);
            }
        }

        private async Task TryReconnectWithPriorityAsync()
        {
            if (_isReconnecting) return;
            
            // 利用可能なネットワークをスキャンして最優先を選択
            try
            {
                var availableNetworks = await _wifiManager.ScanNetworksAsync();
                var bestNetwork = await _priorityManager.GetBestNetworkAsync(availableNetworks);
                
                if (!string.IsNullOrEmpty(bestNetwork))
                {
                    await TryReconnectAsync(bestNetwork);
                }
                else if (!string.IsNullOrEmpty(_lastConnectedSSID))
                {
                    // フォールバック: 最後に接続したネットワークに再接続
                    await TryReconnectAsync(_lastConnectedSSID);
                }
            }
            catch (Exception ex)
            {
                SimpleLoggingService.LogError("Priority-based reconnect failed", ex);
            }
        }
        
        private async Task TryReconnectAsync(string targetSSID = null)
        {
            var ssid = targetSSID ?? _lastConnectedSSID;
            if (_isReconnecting || string.IsNullOrEmpty(ssid)) return;
            
            if (_reconnectAttempts >= MaxReconnectAttempts)
            {
                SimpleLoggingService.LogWarning($"Max reconnect attempts ({MaxReconnectAttempts}) reached for {ssid}");
                ReconnectFailed?.Invoke(this, new ReconnectEventArgs 
                { 
                    SSID = ssid, 
                    Attempt = _reconnectAttempts,
                    Reason = "Max attempts reached"
                });
                return;
            }
            
            _isReconnecting = true;
            _reconnectAttempts++;
            
            try
            {
                SimpleLoggingService.LogInfo($"Attempting to reconnect to {ssid} (attempt {_reconnectAttempts}/{MaxReconnectAttempts})");
                
                ReconnectAttempted?.Invoke(this, new ReconnectEventArgs 
                { 
                    SSID = ssid, 
                    Attempt = _reconnectAttempts 
                });
                
                // 再接続試行
                var result = await _wifiManager.ConnectAsync(ssid, _lastConnectedPassword);
                
                if (result.Success)
                {
                    SimpleLoggingService.LogInfo($"Successfully reconnected to {ssid}");
                    _reconnectAttempts = 0;
                    
                    ReconnectSucceeded?.Invoke(this, new ReconnectEventArgs 
                    { 
                        SSID = ssid, 
                        Attempt = _reconnectAttempts 
                    });
                }
                else
                {
                    SimpleLoggingService.LogWarning($"Failed to reconnect to {ssid}: {result.ErrorMessage}");
                    
                    // 指数バックオフ
                    var delay = TimeSpan.FromSeconds(Math.Min(60, ReconnectDelay.TotalSeconds * Math.Pow(2, _reconnectAttempts - 1)));
                    await Task.Delay(delay);
                }
            }
            catch (Exception ex)
            {
                SimpleLoggingService.LogError($"Reconnect to {ssid} failed", ex);
            }
            finally
            {
                _isReconnecting = false;
            }
        }
        
        /// <summary>
        /// より良いネットワークへのアップグレードを試行
        /// </summary>
        private async Task TryUpgradeNetworkAsync()
        {
            try
            {
                var availableNetworks = await _wifiManager.ScanNetworksAsync();
                var bestNetwork = await _priorityManager.GetBestNetworkAsync(availableNetworks);
                
                // 現在のネットワークより良いものがあるか確認
                var currentSSID = await _wifiManager.GetCurrentSSIDAsync();
                if (!string.IsNullOrEmpty(bestNetwork) && 
                    !string.Equals(bestNetwork, currentSSID, StringComparison.OrdinalIgnoreCase))
                {
                    SimpleLoggingService.LogInfo($"Upgrading from {currentSSID} to better network {bestNetwork}");
                    await _wifiManager.ConnectAsync(bestNetwork, null);
                }
            }
            catch (Exception ex)
            {
                SimpleLoggingService.LogError("Network upgrade failed", ex);
            }
        }

        public ReconnectStatus GetStatus()
        {
            return new ReconnectStatus
            {
                IsEnabled = IsEnabled,
                IsReconnecting = _isReconnecting,
                LastConnectedSSID = _lastConnectedSSID,
                ReconnectAttempts = _reconnectAttempts,
                MaxAttempts = MaxReconnectAttempts,
                LastDisconnectTime = _lastDisconnectTime
            };
        }

        public void ResetAttempts()
        {
            _reconnectAttempts = 0;
            _isReconnecting = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            Stop();
            
            if (_wifiManager != null)
                _wifiManager.ConnectionChanged -= OnConnectionChanged;
                
            _priorityManager?.Dispose();
        }
    }

    public class ReconnectEventArgs : EventArgs
    {
        public string SSID { get; set; }
        public int Attempt { get; set; }
        public string Reason { get; set; }
    }

    public class ReconnectStatus
    {
        public bool IsEnabled { get; set; }
        public bool IsReconnecting { get; set; }
        public string LastConnectedSSID { get; set; }
        public int ReconnectAttempts { get; set; }
        public int MaxAttempts { get; set; }
        public DateTime LastDisconnectTime { get; set; }
        
        public string GetStatusText()
        {
            if (!IsEnabled) return "自動再接続: 無効";
            if (IsReconnecting) return $"再接続中... ({ReconnectAttempts}/{MaxAttempts})";
            if (!string.IsNullOrEmpty(LastConnectedSSID)) return $"監視中: {LastConnectedSSID}";
            return "自動再接続: 待機中";
        }
    }
}