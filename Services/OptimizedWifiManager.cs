using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MurtiWifiConnecter.Interfaces;

namespace MurtiWifiConnecter.Services
{
    /// <summary>
    /// 統合WiFi管理サービス - 実用的で軽量な実装
    /// </summary>
    public class OptimizedWifiManager : IDisposable
    {
        private readonly WifiService _wifiService;
        private readonly ConnectionManagementService _connectionService;
        private readonly LightweightMonitoringService _monitoringService;
        private Timer _autoScanTimer;
        private bool _disposed = false;

        public event EventHandler<WifiNetworkEventArgs> NetworksFound;
        public event EventHandler<WifiConnectionEventArgs> ConnectionChanged;
        public event EventHandler<MonitoringAlertEventArgs> Alert;

        public OptimizedWifiManager(
            WifiService wifiService,
            ConnectionManagementService connectionService,
            LightweightMonitoringService monitoringService)
        {
            _wifiService = wifiService ?? throw new ArgumentNullException(nameof(wifiService));
            _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
            _monitoringService = monitoringService ?? throw new ArgumentNullException(nameof(monitoringService));

            // イベント統合
            _connectionService.ConnectionStatusChanged += OnConnectionStatusChanged;
            _monitoringService.AlertTriggered += OnMonitoringAlert;

            // 自動スキャン開始
            StartAutoScan();
        }

        public async Task<List<WifiNetwork>> ScanNetworksAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var networks = await _wifiService.ScanNetworksAsync(cancellationToken);
                NetworksFound?.Invoke(this, new WifiNetworkEventArgs { Networks = networks });
                return networks;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("OptimizedWifiManager.ScanNetworks", ex);
                return new List<WifiNetwork>();
            }
        }

        public async Task<WifiConnectionResult> ConnectAsync(string ssid, string password, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(ssid))
                return new WifiConnectionResult { Success = false, ErrorMessage = "SSID cannot be empty" };

            try
            {
                // 設定から再試行回数を取得
                var retryAttempts = QuickSettingsManager.GetSetting("max_retry_attempts", 3);
                
                WifiConnectionResult result;
                if (retryAttempts > 1)
                {
                    result = await _connectionService.ConnectWithRetryAsync(ssid, password, cancellationToken);
                }
                else
                {
                    result = await _wifiService.ConnectAsync(ssid, password, cancellationToken);
                }

                // 接続イベント発火
                ConnectionChanged?.Invoke(this, new WifiConnectionEventArgs 
                { 
                    SSID = ssid, 
                    IsConnected = result.Success,
                    Timestamp = DateTime.Now
                });

                return result;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError($"OptimizedWifiManager.Connect_{ssid}", ex);
                return new WifiConnectionResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        public async Task<bool> DisconnectAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var currentSSID = await _wifiService.GetCurrentConnectedSSIDAsync(cancellationToken);
                var result = await _wifiService.DisconnectAsync(cancellationToken);

                if (result && !string.IsNullOrEmpty(currentSSID))
                {
                    ConnectionChanged?.Invoke(this, new WifiConnectionEventArgs
                    {
                        SSID = currentSSID,
                        IsConnected = false,
                        Timestamp = DateTime.Now
                    });
                }

                return result;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("OptimizedWifiManager.Disconnect", ex);
                return false;
            }
        }

        public async Task<string> GetCurrentSSIDAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await _wifiService.GetCurrentConnectedSSIDAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("OptimizedWifiManager.GetCurrentSSID", ex);
                return null;
            }
        }

        public async Task<bool> TestInternetConnectionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await NetworkUtils.TestConnectionAsync("8.8.8.8", cancellationToken);
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("OptimizedWifiManager.TestInternet", ex);
                return false;
            }
        }

        public async Task<SpeedTestResult> RunSpeedTestAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await NetworkUtils.RunQuickSpeedTestAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("OptimizedWifiManager.RunSpeedTest", ex);
                return new SpeedTestResult 
                { 
                    Success = false, 
                    Message = "Speed test failed" 
                };
            }
        }

        public WifiManagerStatus GetStatus()
        {
            try
            {
                var metrics = _monitoringService.GetCurrentMetrics();
                var isHealthy = _monitoringService.IsHealthy();
                var healthSummary = _monitoringService.GetHealthSummary();

                return new WifiManagerStatus
                {
                    IsHealthy = isHealthy,
                    HealthSummary = healthSummary,
                    MemoryUsageMB = metrics.GetValueOrDefault("memory_usage_mb", 0),
                    UptimeSeconds = metrics.GetValueOrDefault("uptime_seconds", 0),
                    IsWifiConnected = metrics.GetValueOrDefault("wifi_connected", 0) == 1,
                    WifiSignalStrength = (int)metrics.GetValueOrDefault("wifi_signal_strength", 0)
                };
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("OptimizedWifiManager.GetStatus", ex);
                return new WifiManagerStatus { IsHealthy = false, HealthSummary = "Status unavailable" };
            }
        }

        private void StartAutoScan()
        {
            if (!QuickSettingsManager.GetSetting("wifi.auto_scan_enabled", true))
                return;

            var intervalSeconds = QuickSettingsManager.GetSetting("wifi.scan_interval_seconds", 30);
            var interval = TimeSpan.FromSeconds(intervalSeconds);

            _autoScanTimer = new Timer(async _ =>
            {
                if (_disposed) return;

                try
                {
                    await ScanNetworksAsync();
                }
                catch (Exception ex)
                {
                    ErrorHandler.LogError("OptimizedWifiManager.AutoScan", ex);
                }
            }, null, TimeSpan.FromSeconds(5), interval);
        }

        private void StopAutoScan()
        {
            _autoScanTimer?.Dispose();
            _autoScanTimer = null;
        }

        public void UpdateAutoScanSettings()
        {
            StopAutoScan();
            if (QuickSettingsManager.GetSetting("wifi.auto_scan_enabled", true))
            {
                StartAutoScan();
            }
        }

        private void OnConnectionStatusChanged(object sender, ConnectionStatusChangedEventArgs e)
        {
            ConnectionChanged?.Invoke(this, new WifiConnectionEventArgs
            {
                SSID = e.SSID,
                IsConnected = e.IsConnected,
                Timestamp = DateTime.Now
            });
        }

        private void OnMonitoringAlert(object sender, MonitoringAlertEventArgs e)
        {
            Alert?.Invoke(this, e);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            StopAutoScan();

            // イベント解除
            if (_connectionService != null)
                _connectionService.ConnectionStatusChanged -= OnConnectionStatusChanged;
            
            if (_monitoringService != null)
                _monitoringService.AlertTriggered -= OnMonitoringAlert;

            // サービス破棄
            _connectionService?.Dispose();
            _monitoringService?.Dispose();
        }
    }

    // イベント引数クラス
    public class WifiNetworkEventArgs : EventArgs
    {
        public List<WifiNetwork> Networks { get; set; } = new();
    }

    public class WifiConnectionEventArgs : EventArgs
    {
        public string SSID { get; set; }
        public bool IsConnected { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class WifiManagerStatus
    {
        public bool IsHealthy { get; set; }
        public string HealthSummary { get; set; } = string.Empty;
        public double MemoryUsageMB { get; set; }
        public double UptimeSeconds { get; set; }
        public bool IsWifiConnected { get; set; }
        public int WifiSignalStrength { get; set; }

        public string GetUptimeString()
        {
            var uptime = TimeSpan.FromSeconds(UptimeSeconds);
            if (uptime.TotalHours >= 1)
                return $"{uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";
            else
                return $"{uptime.Minutes:D2}:{uptime.Seconds:D2}";
        }
    }
}