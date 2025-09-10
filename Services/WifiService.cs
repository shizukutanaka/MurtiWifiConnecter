using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MurtiWifiConnecter.Interfaces;

namespace MurtiWifiConnecter.Services
{
    /// <summary>
    /// WiFiサービス実装 - FastWifiConnectorとNetworkUtilsのラッパー
    /// Adapter Pattern + Interface Segregation Principle
    /// </summary>
    public class WifiService : IWifiService
    {
        /// <summary>
        /// ネットワークスキャン実行
        /// </summary>
        public async Task<List<WifiNetwork>> ScanNetworksAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await NetworkUtils.ScanForWifiNetworksAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("WifiService.ScanNetworks", ex);
                throw new WifiOperationException("Network scan failed", ex);
            }
        }

        /// <summary>
        /// WiFi接続実行
        /// </summary>
        public async Task<WifiConnectionResult> ConnectAsync(string ssid, string password, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(ssid))
                throw new ArgumentException("SSID cannot be null or empty", nameof(ssid));

            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Password cannot be null or empty", nameof(password));

            try
            {
                return await FastWifiConnector.ConnectAsync(ssid, password, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError($"WifiService.Connect({ssid})", ex);
                throw new WifiOperationException($"Connection to {ssid} failed", ex);
            }
        }

        /// <summary>
        /// 現在接続中のSSID取得
        /// </summary>
        public async Task<string?> GetCurrentConnectedSSIDAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await NetworkUtils.GetCurrentConnectedSSIDAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("WifiService.GetCurrentSSID", ex);
                // 接続状態取得の失敗は例外を投げずnullを返す
                return null;
            }
        }

        /// <summary>
        /// WiFi切断実行
        /// </summary>
        public async Task<bool> DisconnectAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await NetworkUtils.ExecuteNetshCommandAsync("wlan disconnect", 5000, cancellationToken).ConfigureAwait(false);
                return result.Success;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("WifiService.Disconnect", ex);
                throw new WifiOperationException("Disconnect operation failed", ex);
            }
        }
    }
}