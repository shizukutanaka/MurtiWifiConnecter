using System;
using System.Threading;
using System.Threading.Tasks;
using MurtiWifiConnecter.Interfaces;

namespace MurtiWifiConnecter.Services
{
    /// <summary>
    /// 接続管理サービス実装 - ConnectionRetryManagerとUnifiedProfileManagerのラッパー
    /// </summary>
    public class ConnectionManagementService : IConnectionManagementService, IDisposable
    {
        private readonly ConnectionRetryManager _retryManager;
        private readonly UnifiedProfileManager _profileManager;
        private readonly ConnectionMonitor _connectionMonitor;

        public event EventHandler<ConnectionStatusChangedEventArgs>? ConnectionStatusChanged;

        public ConnectionManagementService(
            ConnectionLogger logger,
            ConnectionRetryManager retryManager,
            UnifiedProfileManager profileManager,
            ConnectionMonitor connectionMonitor)
        {
            _retryManager = retryManager ?? throw new ArgumentNullException(nameof(retryManager));
            _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
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
                return await _profileManager.TryAutoConnectAsync(ssid, cancellationToken).ConfigureAwait(false);
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