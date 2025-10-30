using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// Simplified WiFi Manager - Core functionality only (Legacy - use WifiManagerFactory instead)
    /// </summary>
    [Obsolete("Use WifiManagerFactory.CreateWifiManager() instead")]
    public sealed class SimplifiedWifiManager : IWifiManager, IDisposable
    {
        private readonly WifiOperations _wifiOps;
        private readonly ProcessExecutor _processExecutor;
        private readonly OptimizedWifiScanner _scanner;
        private readonly ConnectionManager _connectionManager;
        private readonly ProfileManager _profileManager;
        private readonly SemaphoreSlim _operationLock;
        private CancellationTokenSource? _autoReconnectCts;
        private string? _lastConnectedSSID;
        private DateTime _lastConnectionTime;

        public event EventHandler<string>? StatusChanged;

        public SimplifiedWifiManager()
        {
            _processExecutor = new ProcessExecutor();
            _wifiOps = new WifiOperations(_processExecutor);
            _scanner = new OptimizedWifiScanner();
            _connectionManager = new ConnectionManager(this);
            _profileManager = new ProfileManager();
            _operationLock = new SemaphoreSlim(1, 1);
            _lastConnectionTime = DateTime.MinValue;
        }

        /// <summary>
        /// Connect to WiFi network
        /// </summary>
        public async Task<bool> ConnectAsync(string ssid, string password, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(ssid) || string.IsNullOrWhiteSpace(password))
            {
                OnStatusChanged("Invalid SSID or password");
                return false;
            }

            await _operationLock.WaitAsync(ct);
            try
            {
                OnStatusChanged($"Connecting to {ssid}...");

                var result = await _wifiOps.ConnectAsync(ssid, password, ct);

                if (result.IsSuccess)
                {
                    _lastConnectedSSID = ssid;
                    _lastConnectionTime = DateTime.UtcNow;
                    OnStatusChanged($"Connected to {ssid}");
                    return true;
                }

                OnStatusChanged($"Failed to connect: {result.Error}");
                return false;
            }
            finally
            {
                _operationLock.Release();
            }
        }

        /// <summary>
        /// Disconnect from current network
        /// </summary>
        public async Task<bool> DisconnectAsync(CancellationToken ct = default)
        {
            await _operationLock.WaitAsync(ct);
            try
            {
                StopAutoReconnect();

                OnStatusChanged("Disconnecting...");
                var result = await _wifiOps.DisconnectAsync(ct);

                if (result.IsSuccess)
                {
                    _lastConnectedSSID = null;
                    OnStatusChanged("Disconnected");
                    return true;
                }

                OnStatusChanged($"Failed to disconnect: {result.Error}");
                return false;
            }
            finally
            {
                _operationLock.Release();
            }
        }

        /// <summary>
        /// Scan for available networks
        /// </summary>
        public async Task<List<WifiNetwork>> ScanNetworksAsync(CancellationToken ct = default)
        {
            try
            {
                return await _scanner.ScanNetworksAsync(false, ct);
            }
            catch (Exception ex)
            {
                OnStatusChanged($"Scan failed: {ex.Message}");
                return new List<WifiNetwork>();
            }
        }

        /// <summary>
        /// Get current connected SSID
        /// </summary>
        public async Task<string?> GetCurrentSSIDAsync(CancellationToken ct = default)
        {
            var result = await _wifiOps.GetCurrentSSIDAsync(ct);
            return result.IsSuccess ? result.Value : null;
        }

        /// <summary>
        /// Get saved profiles
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
            catch (Exception ex)
            {
                OnStatusChanged($"Failed to get profiles: {ex.Message}");
            }

            return profiles;
        }

        /// <summary>
        /// Delete saved profile
        /// </summary>
        public async Task<bool> DeleteProfileAsync(string ssid, CancellationToken ct = default)
        {
            try
            {
                var deleteCmd = $"wlan delete profile name=\"{ssid}\"";
                var result = await _processExecutor.RunAsync("netsh", deleteCmd, 3000);

                if (result.Success)
                {
                    OnStatusChanged($"Profile '{ssid}' deleted");

                    if (_lastConnectedSSID?.Equals(ssid, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        _lastConnectedSSID = null;
                    }

                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get saved profiles
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
            catch (Exception ex)
            {
                OnStatusChanged($"Failed to get profiles: {ex.Message}");
            }

            return profiles;
        }

        private void OnStatusChanged(string message)
        {
            StatusChanged?.Invoke(this, message);
        }

        public void Dispose()
        {
            StopAutoReconnect();
            _scanner?.Dispose();
            _connectionManager?.Dispose();
            _operationLock?.Dispose();
            _wifiOps?.Dispose();
        }
    }
}