using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace MurtiWifiConnecter.Core.Network.Windows
{
    /// <summary>
    /// Windows環境でのネットワークスキャン機能を提供するクラス
    /// </summary>
    public class WindowsNetworkScanner : INetworkScanner
    {
        private readonly ILogger<WindowsNetworkScanner> _logger;
        private readonly IMemoryCache _cache;
        private readonly IRateLimiter _rateLimiter;
        private readonly IWifiAdapterManager _adapterManager;

        public WindowsNetworkScanner(
            ILogger<WindowsNetworkScanner> logger,
            IMemoryCache cache,
            IRateLimiter rateLimiter,
            IWifiAdapterManager adapterManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
            _adapterManager = adapterManager ?? throw new ArgumentNullException(nameof(adapterManager));
        }

        public async Task<NetworkOperationResult<List<NetworkInfo>>> ScanAsync(CancellationToken cancellationToken = default)
        {
            const string cacheKey = "windows_network_scan";

            try
            {
                // レート制限チェック
                var rateLimitResult = await _rateLimiter.CheckRateLimitAsync("network_scan", cancellationToken);
                if (!rateLimitResult.IsAllowed)
                {
                    return NetworkOperationResult<List<NetworkInfo>>.Failure(
                        new Error(ErrorType.RateLimitExceeded, rateLimitResult.Message));
                }

                // キャッシュ確認
                if (_cache.TryGetValue(cacheKey, out List<NetworkInfo> cachedNetworks))
                {
                    _logger.LogDebug("Returning cached network scan results");
                    return NetworkOperationResult<List<NetworkInfo>>.Success(cachedNetworks);
                }

                // 実際のスキャン実行
                var networks = await PerformNetworkScanAsync(cancellationToken);

                // キャッシュに保存（5分）
                var cacheOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
                    Size = 1
                };
                _cache.Set(cacheKey, networks, cacheOptions);

                return NetworkOperationResult<List<NetworkInfo>>.Success(networks);
            }
            catch (OperationCanceledException)
            {
                return NetworkOperationResult<List<NetworkInfo>>.Failure(
                    new Error(ErrorType.Cancelled, "Network scan was cancelled"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Network scan failed");
                return NetworkOperationResult<List<NetworkInfo>>.Failure(
                    new Error(ErrorType.Unexpected, "An unexpected error occurred during network scan"));
            }
        }

        private async Task<List<NetworkInfo>> PerformNetworkScanAsync(CancellationToken cancellationToken)
        {
            var scanTasks = new List<Task<List<NetworkInfo>>>
            {
                ScanWithNativeApiAsync(cancellationToken),
                ScanWithNetshAsync(cancellationToken)
            };

            // 並列実行
            var results = await Task.WhenAll(scanTasks);

            // 結果のマージ（重複除去、信号強度順）
            var mergedNetworks = MergeScanResults(results);

            _logger.LogInformation("Network scan completed", new
            {
                TotalNetworks = mergedNetworks.Count,
                ScanMethodsUsed = results.Length
            });

            return mergedNetworks;
        }

        private async Task<List<NetworkInfo>> ScanWithNativeApiAsync(CancellationToken cancellationToken)
        {
            if (!IsNativeApiAvailable())
            {
                _logger.LogDebug("Native Wi-Fi API not available, skipping");
                return new List<NetworkInfo>();
            }

            var networks = new List<NetworkInfo>();

            try
            {
                var interfaces = await _adapterManager.GetWifiInterfacesAsync(cancellationToken);
                if (interfaces.Count == 0)
                {
                    _logger.LogWarning("No Wi-Fi interfaces found");
                    return networks;
                }

                foreach (var interfaceInfo in interfaces)
                {
                    var interfaceNetworks = await ScanInterfaceWithNativeApiAsync(interfaceInfo, cancellationToken);
                    networks.AddRange(interfaceNetworks);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Native API scan failed for some interfaces");
            }

            return networks;
        }

        private async Task<List<NetworkInfo>> ScanWithNetshAsync(CancellationToken cancellationToken)
        {
            try
            {
                var output = await ExecuteNetshCommandAsync("wlan show networks mode=bssid", cancellationToken);
                return ParseNetshOutput(output);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Netsh scan failed");
                return new List<NetworkInfo>();
            }
        }

        private List<NetworkInfo> MergeScanResults(List<NetworkInfo>[] results)
        {
            var networkMap = new Dictionary<string, NetworkInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var result in results)
            {
                foreach (var network in result)
                {
                    if (networkMap.TryGetValue(network.Ssid, out var existing))
                    {
                        // より良い結果を優先
                        if (network.Signal > existing.Signal ||
                            (!string.IsNullOrEmpty(network.Band) && string.IsNullOrEmpty(existing.Band)))
                        {
                            networkMap[network.Ssid] = network;
                        }
                    }
                    else
                    {
                        networkMap[network.Ssid] = network;
                    }
                }
            }

            return networkMap.Values
                .OrderByDescending(n => n.Signal)
                .ThenBy(n => n.Ssid)
                .ToList();
        }

        private bool IsNativeApiAvailable()
        {
            // Native APIの利用可能性チェック
            try
            {
                // 実際の実装ではWlanOpenHandleなどのAPIをチェック
                return Environment.OSVersion.Platform == PlatformID.Win32NT &&
                       Environment.OSVersion.Version.Major >= 6; // Windows Vista以降
            }
            catch
            {
                return false;
            }
        }

        private List<NetworkInfo> ParseNetshOutput(string output)
        {
            var networks = new List<NetworkInfo>();
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            NetworkInfo currentNetwork = null;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                if (trimmed.StartsWith("SSID", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentNetwork != null)
                    {
                        networks.Add(currentNetwork);
                    }

                    var ssid = trimmed.Substring(trimmed.IndexOf(':') + 1).Trim();
                    if (!string.IsNullOrEmpty(ssid))
                    {
                        currentNetwork = new NetworkInfo { Ssid = ssid };
                    }
                }
                else if (currentNetwork != null)
                {
                    if (trimmed.Contains("Signal", StringComparison.OrdinalIgnoreCase))
                    {
                        var match = Regex.Match(trimmed, @"(\d+)%");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out var signal))
                        {
                            currentNetwork.Signal = signal;
                        }
                    }
                    else if (trimmed.Contains("Authentication", StringComparison.OrdinalIgnoreCase))
                    {
                        currentNetwork.Security = trimmed.Substring(trimmed.IndexOf(':') + 1).Trim();
                    }
                    else if (trimmed.Contains("Band", StringComparison.OrdinalIgnoreCase))
                    {
                        currentNetwork.Band = trimmed.Contains("5") ? "5GHz" : "2.4GHz";
                    }
                }
            }

            if (currentNetwork != null)
            {
                networks.Add(currentNetwork);
            }

            return networks;
        }
    }

    /// <summary>
    /// ネットワークスキャナーのインターフェース
    /// </summary>
    public interface INetworkScanner
    {
        Task<NetworkOperationResult<List<NetworkInfo>>> ScanAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Wi-Fiアダプタマネージャーのインターフェース
    /// </summary>
    public interface IWifiAdapterManager
    {
        Task<List<WifiInterfaceInfo>> GetWifiInterfacesAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// レート制限機能のインターフェース
    /// </summary>
    public interface IRateLimiter
    {
        Task<RateLimitResult> CheckRateLimitAsync(string operation, CancellationToken cancellationToken = default);
    }
}
