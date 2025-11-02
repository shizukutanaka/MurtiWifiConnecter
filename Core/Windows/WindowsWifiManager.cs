using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core.Windows
{
    /// <summary>
    /// Windowsプラットフォーム用のWiFiマネージャー実装
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class WindowsWifiManager : IWifiManager
    {
        private readonly WifiOperations _wifiOps;
        private readonly ProcessExecutor _processExecutor;
        private readonly OptimizedWifiScanner _scanner;
        private readonly ConnectionManager _connectionManager;
        private readonly ProfileManager _profileManager;
        private readonly SemaphoreSlim _operationLock;

        public WindowsWifiManager()
        {
            _processExecutor = new ProcessExecutor();
            _wifiOps = new WifiOperations(_processExecutor);
            _scanner = new OptimizedWifiScanner();
            _connectionManager = new ConnectionManager(this);
            _profileManager = new ProfileManager();
            _operationLock = new SemaphoreSlim(1, 1);
        }

        /// <summary>
        /// ネットワークに接続する
        /// </summary>
        public async Task<bool> ConnectAsync(string ssid, string password, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(ssid) || string.IsNullOrWhiteSpace(password))
            {
                return false;
            }

            await _operationLock.WaitAsync(ct);
            try
            {
                var result = await _wifiOps.ConnectAsync(ssid, password, ct);
                return result.IsSuccess;
            }
            finally
            {
                _operationLock.Release();
            }
        }

        /// <summary>
        /// 現在のネットワークから切断する
        /// </summary>
        public async Task<bool> DisconnectAsync(CancellationToken ct = default)
        {
            await _operationLock.WaitAsync(ct);
            try
            {
                var result = await _wifiOps.DisconnectAsync(ct);
                return result.IsSuccess;
            }
            finally
            {
                _operationLock.Release();
            }
        }

        /// <summary>
        /// 利用可能なネットワークをスキャンする
        /// </summary>
        public async Task<List<WifiNetwork>> ScanNetworksAsync(CancellationToken ct = default)
        {
            try
            {
                return await _scanner.ScanNetworksAsync(false, ct);
            }
            catch
            {
                return new List<WifiNetwork>();
            }
        }

        /// <summary>
        /// 現在の接続先SSIDを取得する
        /// </summary>
        public async Task<string?> GetCurrentSSIDAsync(CancellationToken ct = default)
        {
            var result = await _wifiOps.GetCurrentSSIDAsync(ct);
            return result.IsSuccess ? result.Value : null;
        }

        /// <summary>
        /// 保存済みプロファイルを取得する
        /// </summary>
        public async Task<List<string>> GetSavedProfilesAsync(CancellationToken ct = default)
        {
            var profiles = new List<string>();

            try
            {
                var result = await _processExecutor.RunAsync("netsh", "wlan show profiles", 3000);

                if (result.Success && !string.IsNullOrEmpty(result.Output))
                {
                    var lines = result.Output.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.Contains("All User Profile") || line.Contains("Current User Profile"))
                        {
                            var colonIndex = line.IndexOf(':');
                            if (colonIndex > 0 && colonIndex < line.Length - 1)
                            {
                                var profileName = line.Substring(colonIndex + 1).Trim();
                                if (!string.IsNullOrWhiteSpace(profileName))
                                {
                                    profiles.Add(profileName);
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // エラーを無視
            }

            return profiles;
        }

        /// <summary>
        /// プロファイルを削除する
        /// </summary>
        public async Task<bool> DeleteProfileAsync(string ssid, CancellationToken ct = default)
        {
            try
            {
                var deleteCmd = $"wlan delete profile name=\"{ssid}\"";
                var result = await _processExecutor.RunAsync("netsh", deleteCmd, 3000);
                return result.Success;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 利用可能なアダプタ情報を取得する
        /// </summary>
        public async Task<Result<IReadOnlyList<WifiAdapterInfo>>> GetAvailableAdaptersAsync(CancellationToken ct = default)
        {
            return await _wifiOps.GetAvailableAdaptersAsync(ct);
        }

        /// <summary>
        /// 優先アダプタを設定する
        /// </summary>
        public void SetPreferredAdapter(string? adapterName)
        {
            _wifiOps.SetPreferredAdapter(adapterName);
        }

        /// <summary>
        /// 優先アダプタを取得する
        /// </summary>
        public string? GetPreferredAdapter()
        {
            return _wifiOps.GetPreferredAdapter();
        }

        /// <summary>
        /// リソースを解放する
        /// </summary>
        public void Dispose()
        {
            _scanner?.Dispose();
            _connectionManager?.Dispose();
            _operationLock?.Dispose();
            _wifiOps?.Dispose();
        }
    }
}
