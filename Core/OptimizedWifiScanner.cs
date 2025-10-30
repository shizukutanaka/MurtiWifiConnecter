using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// Optimized WiFi scanner with caching and background scanning
    /// </summary>
    public sealed class OptimizedWifiScanner : IDisposable
    {
        private readonly ProcessExecutor _processExecutor;
        private readonly ConcurrentDictionary<string, CachedNetwork> _networkCache;
        private readonly SemaphoreSlim _scanLock;
        private readonly System.Timers.Timer _backgroundScanTimer;

        private DateTime _lastScanTime;
        private bool _isScanning;
        private CancellationTokenSource? _backgroundScanCts;

        // Configuration
        private const int CacheExpirationSeconds = 30;
        private const int BackgroundScanIntervalSeconds = 60;
        private const int MinScanIntervalMilliseconds = 5000;
        private const int MaxConcurrentScans = 3; // Increased for better performance
        private const int ScanTimeoutMilliseconds = 10000;

        public event EventHandler<ScanCompletedEventArgs>? ScanCompleted;
        public event EventHandler<NetworkDiscoveredEventArgs>? NetworkDiscovered;

        public OptimizedWifiScanner()
        {
            _processExecutor = new ProcessExecutor();
            _networkCache = new ConcurrentDictionary<string, CachedNetwork>();
            _scanLock = new SemaphoreSlim(MaxConcurrentScans, MaxConcurrentScans);
            _lastScanTime = DateTime.MinValue;

            _backgroundScanTimer = new System.Timers.Timer(BackgroundScanIntervalSeconds * 1000);
            _backgroundScanTimer.Elapsed += async (s, e) => await BackgroundScanAsync();
        }

        /// <summary>
        /// Start background scanning
        /// </summary>
        public void StartBackgroundScanning()
        {
            if (_backgroundScanCts == null || _backgroundScanCts.IsCancellationRequested)
            {
                _backgroundScanCts = new CancellationTokenSource();
                _backgroundScanTimer.Start();
            }
        }

        /// <summary>
        /// Stop background scanning
        /// </summary>
        public void StopBackgroundScanning()
        {
            _backgroundScanTimer.Stop();
            _backgroundScanCts?.Cancel();
            _backgroundScanCts?.Dispose();
            _backgroundScanCts = null;
        }

        /// <summary>
        /// Scan for networks with parallel processing for improved performance
        /// </summary>
        public async Task<List<WifiNetwork>> ScanNetworksParallelAsync(bool forceRefresh = false, CancellationToken ct = default)
        {
            // Return cached results if available and fresh
            if (!forceRefresh && !IsCacheExpired())
            {
                return GetCachedNetworks();
            }

            // Prevent too frequent scans
            var timeSinceLastScan = DateTime.UtcNow - _lastScanTime;
            if (!forceRefresh && timeSinceLastScan.TotalMilliseconds < MinScanIntervalMilliseconds)
            {
                return GetCachedNetworks();
            }

            // Perform parallel scan
            return await PerformParallelScanAsync(ct);
        }

        private async Task<List<WifiNetwork>> PerformParallelScanAsync(CancellationToken ct)
        {
            if (_isScanning)
            {
                await WaitForScanCompletionAsync(ct);
                return GetCachedNetworks();
            }

            await _scanLock.WaitAsync(ct);
            try
            {
                _isScanning = true;
                _lastScanTime = DateTime.UtcNow;

                // Parallel execution of netsh commands
                var scanTask = _processExecutor.RunAsync("netsh", "wlan show networks mode=bssid", ScanTimeoutMilliseconds);
                var refreshTask = _processExecutor.RunAsync("netsh", "wlan refresh", 2000);
                var interfaceTask = _processExecutor.RunAsync("netsh", "wlan show interfaces", 3000);

                // Wait for all tasks to complete
                await Task.WhenAll(scanTask, refreshTask, interfaceTask);

                var scanResult = await scanTask;
                var refreshResult = await refreshTask;
                var interfaceResult = await interfaceTask;

                if (scanResult.Success && !string.IsNullOrEmpty(scanResult.Output))
                {
                    var networks = ParseNetworkList(scanResult.Output);

                    // Process interface information in parallel with network parsing
                    if (interfaceResult.Success && !string.IsNullOrEmpty(interfaceResult.Output))
                    {
                        _ = Task.Run(() => ProcessInterfaceInformation(interfaceResult.Output), ct);
                    }

                    UpdateCache(networks);
                    OnScanCompleted(networks.Count, true);
                    return networks;
                }

                OnScanCompleted(0, false);
                return GetCachedNetworks();
            }
            catch (Exception ex)
            {
                Logger.Error($"Parallel scan failed: {ex.Message}", ex);
                OnScanCompleted(0, false);
                return GetCachedNetworks();
            }
            finally
            {
                _isScanning = false;
                _scanLock.Release();
            }
        }

        private void ProcessInterfaceInformation(string output)
        {
            try
            {
                // Process interface information for additional context
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("State", StringComparison.OrdinalIgnoreCase))
                    {
                        // Process interface state information
                        Logger.Debug($"Interface state: {trimmed}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Interface processing failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Get cached networks
        /// </summary>
        public List<WifiNetwork> GetCachedNetworks()
        {
            var validNetworks = _networkCache.Values
                .Where(cn => !IsNetworkExpired(cn))
                .Select(cn => cn.Network)
                .OrderByDescending(n => n.SignalStrength)
                .ToList();

            // Clean up expired entries
            CleanupExpiredNetworks();

            return validNetworks;
        }

        /// <summary>
        /// Quick scan for specific network
        /// </summary>
        public async Task<WifiNetwork?> QuickScanForNetworkAsync(string ssid, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(ssid))
                return null;

            // Check cache first
            if (_networkCache.TryGetValue(ssid, out var cached) && !IsNetworkExpired(cached))
            {
                return cached.Network;
            }

            // Perform targeted scan
            var networks = await PerformScanAsync(ct);
            return networks.FirstOrDefault(n =>
                string.Equals(n.SSID, ssid, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Get signal strength for specific network
        /// </summary>
        public async Task<int> GetSignalStrengthAsync(string ssid, CancellationToken ct = default)
        {
            var network = await QuickScanForNetworkAsync(ssid, ct);
            return network?.SignalStrength ?? 0;
        }

        private async Task<List<WifiNetwork>> PerformScanAsync(CancellationToken ct)
        {
            if (_isScanning)
            {
                // If already scanning, wait for it to complete
                await WaitForScanCompletionAsync(ct);
                return GetCachedNetworks();
            }

            await _scanLock.WaitAsync(ct);
            try
            {
                _isScanning = true;
                _lastScanTime = DateTime.UtcNow;

                // Use netsh with optimized parameters
                var scanTask = _processExecutor.RunAsync("netsh", "wlan show networks mode=bssid", ScanTimeoutMilliseconds);
                var refreshTask = _processExecutor.RunAsync("netsh", "wlan refresh", 2000);

                // Run refresh in parallel but don't wait for it
                _ = Task.Run(async () => await refreshTask, ct);

                // Wait for scan results
                var result = await scanTask;

                if (result.Success && !string.IsNullOrEmpty(result.Output))
                {
                    var networks = ParseNetworkList(result.Output);
                    UpdateCache(networks);
                    OnScanCompleted(networks.Count, true);
                    return networks;
                }

                OnScanCompleted(0, false);
                return GetCachedNetworks();
            }
            catch (Exception ex)
            {
                Logger.Error($"Scan failed: {ex.Message}", ex);
                OnScanCompleted(0, false);
                return GetCachedNetworks();
            }
            finally
            {
                _isScanning = false;
                _scanLock.Release();
            }
        }

        private async Task BackgroundScanAsync()
        {
            if (_backgroundScanCts == null || _backgroundScanCts.IsCancellationRequested)
                return;

            try
            {
                await PerformScanAsync(_backgroundScanCts.Token);
            }
            catch (Exception ex)
            {
                Logger.Error($"Background scan error: {ex.Message}", ex);
            }
        }

        private async Task WaitForScanCompletionAsync(CancellationToken ct)
        {
            var startTime = DateTime.UtcNow;
            while (_isScanning && !ct.IsCancellationRequested)
            {
                if ((DateTime.UtcNow - startTime).TotalMilliseconds > ScanTimeoutMilliseconds)
                    break;

                await Task.Delay(100, ct);
            }
        }

        private void UpdateCache(List<WifiNetwork> networks)
        {
            var now = DateTime.UtcNow;

            foreach (var network in networks)
            {
                var cached = new CachedNetwork
                {
                    Network = network,
                    LastUpdated = now
                };

                _networkCache.AddOrUpdate(network.SSID, cached, (key, old) =>
                {
                    // Check if signal strength changed significantly
                    if (Math.Abs(old.Network.SignalStrength - network.SignalStrength) > 10)
                    {
                        OnNetworkDiscovered(network, true);
                    }
                    return cached;
                });

                // New network discovered
                if (!_networkCache.ContainsKey(network.SSID))
                {
                    OnNetworkDiscovered(network, false);
                }
            }
        }

        private List<WifiNetwork> ParseNetworkList(string output)
        {
            var networks = new Dictionary<string, WifiNetwork>();
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            string? currentSSID = null;
            WifiNetwork? currentNetwork = null;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                if (trimmed.StartsWith("SSID", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = trimmed.Split(':', 2);
                    if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]))
                    {
                        currentSSID = parts[1].Trim();
                        if (!networks.ContainsKey(currentSSID))
                        {
                            currentNetwork = new WifiNetwork { SSID = currentSSID };
                            networks[currentSSID] = currentNetwork;
                        }
                        else
                        {
                            currentNetwork = networks[currentSSID];
                        }
                    }
                }
                else if (currentNetwork != null)
                {
                    if (trimmed.Contains("Authentication", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = trimmed.Split(':', 2);
                        if (parts.Length == 2)
                        {
                            currentNetwork.Authentication = parts[1].Trim();
                        }
                    }
                    else if (trimmed.Contains("Encryption", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = trimmed.Split(':', 2);
                        if (parts.Length == 2)
                        {
                            currentNetwork.Encryption = parts[1].Trim();
                        }
                    }
                    else if (trimmed.Contains("Signal", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = trimmed.Split(':', 2);
                        if (parts.Length == 2)
                        {
                            var signalStr = parts[1].Trim().TrimEnd('%');
                            if (int.TryParse(signalStr, out var signal))
                            {
                                // Keep the best signal for duplicate SSIDs
                                currentNetwork.SignalStrength = Math.Max(currentNetwork.SignalStrength, signal);
                            }
                        }
                    }
                }
            }

            return networks.Values.ToList();
        }

        private bool IsCacheExpired()
        {
            return (DateTime.UtcNow - _lastScanTime).TotalSeconds > CacheExpirationSeconds;
        }

        private bool IsNetworkExpired(CachedNetwork cached)
        {
            return (DateTime.UtcNow - cached.LastUpdated).TotalSeconds > CacheExpirationSeconds * 2;
        }

        private void CleanupExpiredNetworks()
        {
            var expiredKeys = _networkCache
                .Where(kvp => IsNetworkExpired(kvp.Value))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _networkCache.TryRemove(key, out _);
            }
        }

        private void OnScanCompleted(int networkCount, bool success)
        {
            ScanCompleted?.Invoke(this, new ScanCompletedEventArgs
            {
                NetworkCount = networkCount,
                Success = success,
                Timestamp = DateTime.UtcNow
            });
        }

        private void OnNetworkDiscovered(WifiNetwork network, bool signalChanged)
        {
            NetworkDiscovered?.Invoke(this, new NetworkDiscoveredEventArgs
            {
                Network = network,
                SignalChanged = signalChanged,
                Timestamp = DateTime.UtcNow
            });
        }

        public void Dispose()
        {
            StopBackgroundScanning();
            _backgroundScanTimer?.Dispose();
            _scanLock?.Dispose();
            _backgroundScanCts?.Dispose();
        }

        // Supporting classes
        private class CachedNetwork
        {
            public WifiNetwork Network { get; set; } = new();
            public DateTime LastUpdated { get; set; }
        }

        public class ScanCompletedEventArgs : EventArgs
        {
            public int NetworkCount { get; set; }
            public bool Success { get; set; }
            public DateTime Timestamp { get; set; }
        }

        public class NetworkDiscoveredEventArgs : EventArgs
        {
            public WifiNetwork Network { get; set; } = new();
            public bool SignalChanged { get; set; }
            public DateTime Timestamp { get; set; }
        }
    }
}