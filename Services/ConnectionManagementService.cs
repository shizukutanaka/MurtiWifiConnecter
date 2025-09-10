using System;
using System.Threading;
using System.Threading.Tasks;
using MurtiWifiConnecter.Interfaces;

namespace MurtiWifiConnecter.Services
{
    /// <summary>
    /// 接続管理サービス実装 - ConnectionRetryManagerとAutoConnectManagerのラッパー
    /// </summary>
    public class ConnectionManagementService : IConnectionManagementService, IDisposable
    {
        private readonly ConnectionRetryManager _retryManager;
        private readonly AutoConnectManager _autoConnectManager;
        private readonly ConnectionMonitor _connectionMonitor;

        public event EventHandler<ConnectionStatusChangedEventArgs>? ConnectionStatusChanged;

        public ConnectionManagementService(
            ConnectionLogger logger,
            ConnectionRetryManager retryManager,
            AutoConnectManager autoConnectManager,
            ConnectionMonitor connectionMonitor)
        {
            _retryManager = retryManager ?? throw new ArgumentNullException(nameof(retryManager));
            _autoConnectManager = autoConnectManager ?? throw new ArgumentNullException(nameof(autoConnectManager));
            _connectionMonitor = connectionMonitor ?? throw new ArgumentNullException(nameof(connectionMonitor));
            
            // イベント統合
            _connectionMonitor.ConnectionStatusChanged += OnStatusChanged;
        }

        public async Task<WifiConnectionResult> ConnectWithRetryAsync(string ssid, string password, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _retryManager.ConnectWithRetryAsync(ssid, password, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError($"ConnectionManagementService.ConnectWithRetry({ssid})", ex);
                throw new WifiOperationException($"Retry connection to {ssid} failed", ex);
            }
        }

        public async Task<bool> TryAutoConnectAsync(string ssid, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _autoConnectManager.TryAutoConnectAsync(ssid, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError($"ConnectionManagementService.TryAutoConnect({ssid})", ex);
                return false; // オート接続の失敗は例外を投げずfalseを返す
            }
        }

        private void OnStatusChanged(object? sender, ConnectionStatusChangedEventArgs e)
        {
            ConnectionStatusChanged?.Invoke(this, e);
        }

        public void Dispose()
        {
            if (_connectionMonitor != null)
                _connectionMonitor.ConnectionStatusChanged -= OnStatusChanged;
                
            _connectionMonitor?.Dispose();
        }
    }
}