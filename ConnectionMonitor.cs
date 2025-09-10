using System;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// 軽量接続監視クラス - 削除されたConnectionMonitorの代替
    /// </summary>
    public class ConnectionMonitor : IDisposable
    {
        private readonly ConnectionLogger _logger;
        private Timer _monitorTimer;
        private bool _disposed = false;

        public event EventHandler<ConnectionStatusChangedEventArgs> ConnectionStatusChanged;

        public ConnectionMonitor(ConnectionLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            StartMonitoring();
        }

        private void StartMonitoring()
        {
            // 30秒間隔で接続状態をチェック
            _monitorTimer = new Timer(async _ => await CheckConnectionStatusAsync(), 
                null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
        }

        private async Task CheckConnectionStatusAsync()
        {
            if (_disposed) return;

            try
            {
                // 軽量な接続チェック
                var isConnected = await NetworkUtils.IsConnectedAsync();
                var currentSSID = isConnected ? await NetworkUtils.GetCurrentConnectedSSIDAsync() : null;

                ConnectionStatusChanged?.Invoke(this, new ConnectionStatusChangedEventArgs
                {
                    IsConnected = isConnected,
                    SSID = currentSSID,
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger?.Log(ConnectionLogger.LogLevel.Error, "Monitor", $"監視エラー: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _monitorTimer?.Dispose();
            _monitorTimer = null;
        }
    }

    public class ConnectionStatusChangedEventArgs : EventArgs
    {
        public bool IsConnected { get; set; }
        public string SSID { get; set; }
        public DateTime Timestamp { get; set; }
    }
}