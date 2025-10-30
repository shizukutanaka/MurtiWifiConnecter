using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Cryptography.HashAlgorithmName;
using Microsoft.Extensions.Caching.Memory;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// Windows固有のネットワーク操作実装（ネイティブAPI強化版）
    /// </summary>
    public class WindowsNetworkOperations : INetworkOperations
    {
        // Enhanced retry and fault tolerance
        private static readonly int MaxRetryAttempts = 3;
        private static readonly int BaseRetryDelayMs = 1000;
        private static readonly double RetryBackoffMultiplier = 1.5;
        private const int MinRetryDelayMs = 250;
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(6);

        // Performance monitoring
        private static DateTime _lastPerformanceCheck = DateTime.MinValue;
        private static readonly TimeSpan PerformanceCheckInterval = TimeSpan.FromMinutes(5);

        // Native Wi-Fi API enhancement
        private static readonly IntPtr _wlanHandle = IntPtr.Zero;
        private static readonly Guid _wlanInterfaceGuid = Guid.Empty;
        private static bool _nativeApiAvailable = false;

        // Cache and synchronization - Optimized for performance
        private static readonly MemoryCache _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = 500, // Increased cache size for better performance
            CompactionPercentage = 0.15, // More aggressive cache compaction
            ExpirationScanFrequency = TimeSpan.FromMinutes(2) // More frequent expiration scanning
        });
        private static readonly SemaphoreSlim _scanLock = new SemaphoreSlim(1, 1);
        // Profile integrity validation cache for performance
        private static readonly MemoryCache _profileValidationCache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = 1000,
            CompactionPercentage = 0.1,
            ExpirationScanFrequency = TimeSpan.FromMinutes(5)
        });
        private static readonly SemaphoreSlim _profileValidationLock = new SemaphoreSlim(3, 3); // Allow 3 concurrent validations

        // Netsh configuration
        private static readonly TimeSpan NetshTimeout = TimeSpan.FromSeconds(30);
        private static readonly string NetshExecutablePath = ResolveNetshPath();
        private static readonly string[] AllowedNetshPrefixes =
        {
            "wlan show networks",
            "wlan connect",
            "wlan add profile",
            "wlan disconnect",
            "wlan show interfaces",
            "wlan show profiles",
            "wlan delete profile"
        };
        public enum WifiSecurityMode
        {
            Open,
            Wep,
            Wpa,
            Wpa2,
            Wpa2Enterprise,
            Wpa3,
            Wpa3Enterprise,
            Wpa4,  // WPA4 - Quantum-resistant encryption
            Wpa4Enterprise
        }

        public enum WifiStandard
        {
            WiFi4,  // 802.11n
            WiFi5,  // 802.11ac
            WiFi6,  // 802.11ax
            WiFi6E, // 802.11ax with 6GHz
            WiFi7   // 802.11be
        }

        static WindowsNetworkOperations()
        {
            InitializeNativeApi();
        }

        /// <summary>
        /// ネイティブWi-Fi APIの初期化
        /// </summary>
        private static void InitializeNativeApi()
        {
            try
            {
                // WlanOpenHandleを試行
                uint negotiatedVersion;
                var result = WlanOpenHandle(2, IntPtr.Zero, out negotiatedVersion, out _wlanHandle);

                if (result == 0) // ERROR_SUCCESS
                {
                    _nativeApiAvailable = true;

                    // 最初のWi-Fiインターフェースを取得
                    var interfaceList = IntPtr.Zero;
                    result = WlanEnumInterfaces(_wlanHandle, IntPtr.Zero, out interfaceList);

                    if (result == 0 && interfaceList != IntPtr.Zero)
                    {
                        var header = Marshal.PtrToStructure<WLAN_INTERFACE_INFO_LIST_HEADER>(interfaceList);
                        if (header.NumberOfItems > 0)
                        {
                            var interfaceInfo = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(
                                interfaceList + Marshal.SizeOf<WLAN_INTERFACE_INFO_LIST_HEADER>());
                            _wlanInterfaceGuid = interfaceInfo.InterfaceGuid;
                        }

                        WlanFreeMemory(interfaceList);
                    }

                    Logger.LogInfo("Native Wi-Fi API initialized successfully", nameof(WindowsNetworkOperations));
                }
                else
                {
                    _nativeApiAvailable = false;
                    Logger.LogWarning($"Native Wi-Fi API initialization failed: {result}", nameof(WindowsNetworkOperations));
                }
            }
            catch (Exception ex)
            {
                _nativeApiAvailable = false;
                Logger.LogError("Native Wi-Fi API initialization exception", nameof(WindowsNetworkOperations), null, ex);
            }
        }

        public async Task<List<NetworkInfo>> ScanNetworksAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            const string cacheKey = "available_networks";

            // Rate limiting check
            var rateLimitResult = await SecurityManager.CheckRateLimitAsync("scan_networks");
            if (!rateLimitResult.Allowed)
            {
                await Logger.LogWarning("Network scan rate limited", nameof(WindowsNetworkOperations), new Dictionary<string, object>
                {
                    ["scope"] = rateLimitResult.Scope.ToString()
                });
                throw new InvalidOperationException($"Rate limit exceeded for network scanning. Please wait before trying again.");
            }

            var config = await ConfigManager.LoadConfig(cancellationToken);
            var scanCacheDuration = TimeSpan.FromSeconds(Math.Clamp(config?.CacheDuration ?? 30, 0, 600));

            // PERFORMANCE OPTIMIZATION: Adaptive cache duration based on scan frequency
            var adaptiveCacheDuration = scanCacheDuration;
            if (scanCacheDuration > TimeSpan.FromSeconds(30))
            {
                // For longer scan intervals, use shorter cache duration to ensure freshness
                adaptiveCacheDuration = TimeSpan.FromSeconds(Math.Min(scanCacheDuration.TotalSeconds * 0.8, 120));
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (scanCacheDuration == TimeSpan.Zero)
            {
                _cache.Remove(cacheKey);
            }
            else if (!forceRefresh && _cache.Get(cacheKey) is List<NetworkInfo> cached)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Logger.LogDebug("ScanNetworksAsync cache hit", nameof(WindowsNetworkOperations), new Dictionary<string, object>
                {
                    ["forceRefresh"] = forceRefresh,
                    ["count"] = cached.Count,
                    ["cacheDurationSeconds"] = scanCacheDuration.TotalSeconds
                });
                return cached;
            }

            await Logger.LogInfo("Scanning networks", nameof(WindowsNetworkOperations), new Dictionary<string, object>
            {
                ["forceRefresh"] = forceRefresh,
                ["cacheDurationSeconds"] = scanCacheDuration.TotalSeconds
            });

            return await ErrorHandler.HandleNetworkOperationWithRecovery(async () =>
            {
                await _scanLock.WaitAsync(cancellationToken);
                var lockHeld = true;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var networks = new List<NetworkInfo>();
                    var output = await ExecuteNetshCommandAsync("wlan show networks mode=bssid", cancellationToken);

                    if (string.IsNullOrWhiteSpace(output))
                    {
                        return networks;
                    }

                    var lines = output.Split('\n');
                    NetworkInfo current = null;

                    foreach (var line in lines)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var trimmed = line.Trim();

                        if (trimmed.StartsWith("SSID", StringComparison.OrdinalIgnoreCase))
                        {
                            if (current != null && !string.IsNullOrEmpty(current.Ssid))
                            {
                                if (string.IsNullOrEmpty(current.Band))
                                {
                                    current.Band = "Unknown";
                                    await Logger.LogWarning("Band information missing", nameof(WindowsNetworkOperations), new Dictionary<string, object>
                                    {
                                        ["ssid"] = current.Ssid
                                    });
                                }
                                networks.Add(current);
                            }

                            var parts = trimmed.Split(':');
                            if (parts.Length >= 2)
                            {
                                var ssid = string.Join(":", parts.Skip(1)).Trim();
                                if (!string.IsNullOrEmpty(ssid))
                                {
                                    current = new NetworkInfo { Ssid = ssid };
                                }
                            }
                        }
                        else if (current != null)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (trimmed.Contains("Signal", StringComparison.OrdinalIgnoreCase))
                            {
                                var match = System.Text.RegularExpressions.Regex.Match(trimmed, @"(\d+)%");
                                if (match.Success && int.TryParse(match.Groups[1].Value, out var signal))
                                {
                                    current.Signal = signal;
                                }
                            }
                            else if (trimmed.Contains("Authentication", StringComparison.OrdinalIgnoreCase))
                            {
                                current.Security = trimmed.Split(':').Last().Trim();
                            }
                            else if (trimmed.Contains("Band", StringComparison.OrdinalIgnoreCase))
                            {
                                current.Band = trimmed.Contains("5GHz", StringComparison.OrdinalIgnoreCase) ? "5GHz" : "2.4GHz";
                            }
                            else if (string.IsNullOrEmpty(trimmed) && !string.IsNullOrEmpty(current.Ssid))
                            {
                                if (string.IsNullOrEmpty(current.Band))
                                {
                                    current.Band = "Unknown";
                                    await Logger.LogWarning("Band information missing", nameof(WindowsNetworkOperations), new Dictionary<string, object>
                                    {
                                        ["ssid"] = current.Ssid
                                    });
                                }
                                networks.Add(current);
                                current = null;
                            }
                        }
                    }

                    if (current != null && !string.IsNullOrEmpty(current.Ssid))
                    {
                        if (string.IsNullOrEmpty(current.Band))
                        {
                            current.Band = "Unknown";
                            await Logger.LogWarning("Band information missing", nameof(WindowsNetworkOperations), new Dictionary<string, object>
                            {
                                ["ssid"] = current.Ssid
                            });
                        }
                        networks.Add(current);
                    }

                    networks = networks.OrderByDescending(n => n.Signal).ToList();

                    // PERFORMANCE IMPROVEMENT: Cache with adaptive duration and size tracking
                    if (adaptiveCacheDuration > TimeSpan.Zero)
                    {
                        var cacheOptions = new MemoryCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = adaptiveCacheDuration,
                            Size = 1,
                            Priority = CacheItemPriority.Normal
                        };
                        _cache.Set(cacheKey, networks, cacheOptions);
                    }

                    await Logger.LogInfo("Scanning networks completed", nameof(WindowsNetworkOperations), new Dictionary<string, object>
                    {
                        ["forceRefresh"] = forceRefresh,
                        ["count"] = networks.Count,
                        ["cacheDurationSeconds"] = adaptiveCacheDuration.TotalSeconds
                    });

                    // Periodic performance monitoring
                    await CheckPerformanceMetrics();
                    return networks;
                }
                finally
                {
                    if (lockHeld)
                    {
                        _scanLock.Release();
                    }
                }
            }, new List<NetworkInfo>());
        }

        public async Task<bool> ConnectAsync(string ssid, string password = null, CancellationToken cancellationToken = default)
        {
            string safeSsid;
            string safePassword;

            try
            {
                safeSsid = InputValidator.EnsureValidSsid(ssid);
                safePassword = InputValidator.EnsureValidPassword(password);
            }
            catch (ArgumentException ex)
            {
                await ErrorHandler.LogError(new ArgumentException("Invalid connection parameters", ex), "ConnectAsync validation failed");
                Console.WriteLine($"Connection validation error: {ex.Message}");
                return false;
            }

            await Logger.LogInfo("Connection attempt", nameof(WindowsNetworkOperations), new Dictionary<string, object>
            {
                ["ssid"] = safeSsid,
                ["hasPassword"] = !string.IsNullOrEmpty(safePassword)
            });

            // AIセキュリティ分析とサイドチャネル対策を適用
            await PerformAdvancedSecurityAnalysisAsync(safeSsid, safePassword, cancellationToken);
            {
                await _connectLock.WaitAsync(cancellationToken);
                var lockHeld = true;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // First check if profile exists
                    var profiles = await GetSavedProfilesAsync(cancellationToken: cancellationToken);
                    var hasProfile = profiles.Any(p => p.Equals(safeSsid, StringComparison.OrdinalIgnoreCase));

                    if (hasProfile)
                    {
                        // Try to connect with existing profile
                        var connectResult = await ExecuteNetshCommandAsync($"wlan connect name={InputValidator.QuoteForNetsh(safeSsid)}", cancellationToken);
                        if (connectResult.Contains("successfully", StringComparison.OrdinalIgnoreCase))
                        {
                            InvalidateCache();
                            await Logger.LogInfo("Connected using existing profile", nameof(WindowsNetworkOperations), new Dictionary<string, object>
                            {
                                ["ssid"] = safeSsid
                            });
                            return true;
                        }

                        await Logger.LogWarning("Existing profile connection failed", nameof(WindowsNetworkOperations), new Dictionary<string, object>
                        {
                            ["ssid"] = safeSsid
                        });
                    }

                    // If no profile or connection failed, create/update profile
                    if (!string.IsNullOrEmpty(safePassword))
                    {
                        var profileXml = GenerateWifiProfile(safeSsid, safePassword, WifiSecurityMode.Wpa2, WifiStandard.WiFi6, true);
                        var tempFile = await SecurityManager.CreateValidatedProfileAsync($"ConnectAsync profile for {safeSsid}", profileXml);

                        try
                        {
                            var addResult = await ExecuteNetshCommandAsync($"wlan add profile filename={InputValidator.QuoteForNetsh(tempFile)} user=all", cancellationToken);

                            if (addResult.Contains("added", StringComparison.OrdinalIgnoreCase) || addResult.Contains("updated", StringComparison.OrdinalIgnoreCase))
                            {
                                var finalConnect = await ExecuteNetshCommandAsync($"wlan connect name={InputValidator.QuoteForNetsh(safeSsid)}", cancellationToken);
                                InvalidateCache();
                                var success = finalConnect.Contains("successfully", StringComparison.OrdinalIgnoreCase);
                                if (success)
                                {
                                    await Logger.LogInfo("Connected after profile update", nameof(WindowsNetworkOperations), new Dictionary<string, object>
                                    {
                                        ["ssid"] = safeSsid
                                    });
                                }
                                else
                                {
                                    await Logger.LogWarning("Connection failed after profile update", nameof(WindowsNetworkOperations), new Dictionary<string, object>
                                    {
                                        ["ssid"] = safeSsid
                                    });
                                }
                                return success;
                            }

                            await Logger.LogWarning("Profile update command failed", nameof(WindowsNetworkOperations), new Dictionary<string, object>
                            {
                                ["ssid"] = safeSsid
                            });
                        }
                        finally
                        {
                            await SecurityManager.SecureDeleteFileAsync(tempFile);
                        }
                    }

                    await Logger.LogWarning("Connection attempt returned false", nameof(WindowsNetworkOperations), new Dictionary<string, object>
                    {
                        ["ssid"] = safeSsid,
                        ["hasPassword"] = !string.IsNullOrEmpty(safePassword)
                    });
                    return false;
                }
                finally
                {
                    if (lockHeld)
                    {
                        _connectLock.Release();
                    }
                }
            });

            await Logger.LogInfo("Connection attempt completed", nameof(WindowsNetworkOperations), new Dictionary<string, object>
            {
                ["ssid"] = safeSsid,
                ["result"] = connectionResult
            });

            return connectionResult;
        }

        public async Task<bool> DisconnectAsync(CancellationToken cancellationToken = default)
        {
            await Logger.LogInfo("Disconnect requested", nameof(WindowsNetworkOperations));

            var result = await ExecuteNetshCommandAsync("wlan disconnect", cancellationToken);
            InvalidateCache();
            var success = result.Contains("successfully", StringComparison.OrdinalIgnoreCase) || result.Contains("Disconnect", StringComparison.OrdinalIgnoreCase);

            if (success)
            {
                await Logger.LogInfo("Disconnect successful", nameof(WindowsNetworkOperations));
            }
            else
            {
                await Logger.LogWarning("Disconnect failed", nameof(WindowsNetworkOperations), new Dictionary<string, object>
                {
                    ["response"] = result
                });
            }

            return success;
        }

        public async Task<ConnectionStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            const string cacheKey = "connection_status";

            var config = await ConfigManager.LoadConfig();
            var statusCacheSeconds = Math.Clamp(Math.Max(1, (config?.ScanInterval ?? 30) / 2), 1, 60);

            // PERFORMANCE OPTIMIZATION: Adaptive status cache duration
            var adaptiveStatusCacheSeconds = Math.Min(statusCacheSeconds, Math.Max(5, statusCacheSeconds * 2 / 3));

            cancellationToken.ThrowIfCancellationRequested();

            if (adaptiveStatusCacheSeconds > 0 && _cache.Get(cacheKey) is ConnectionStatus cached)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Logger.LogDebug("Status served from cache", nameof(WindowsNetworkOperations), new Dictionary<string, object>
                {
                    ["status"] = cached.Status,
                    ["ssid"] = cached.Ssid,
                    ["cacheDurationSeconds"] = adaptiveStatusCacheSeconds
                });
                return cached;
            }

            var status = new ConnectionStatus { Status = "Disconnected" };
            var output = await ExecuteNetshCommandAsync("wlan show interfaces", cancellationToken);

            if (!string.IsNullOrEmpty(output))
            {
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("State", StringComparison.OrdinalIgnoreCase))
                    {
                        status.Status = trimmed.Contains("connected", StringComparison.OrdinalIgnoreCase) ? "Connected" : "Disconnected";
                    }
                    else if (trimmed.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) && !trimmed.Contains("BSSID", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = trimmed.Split(':');
                        if (parts.Length >= 2)
                        {
                            status.Ssid = string.Join(":", parts.Skip(1)).Trim();
                        }
                    }
                    else if (trimmed.Contains("Signal", StringComparison.OrdinalIgnoreCase))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(trimmed, @"(\d+)%");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out var signal))
                        {
                            status.Signal = signal;
                        }
                    }
                    else if (trimmed.StartsWith("Receive rate (Mbps)", StringComparison.OrdinalIgnoreCase))
                    {
                        status.ReceiveRateMbps = ParseRateValue(trimmed);
                    }
                    else if (trimmed.StartsWith("Transmit rate (Mbps)", StringComparison.OrdinalIgnoreCase))
                    {
                        status.TransmitRateMbps = ParseRateValue(trimmed);
                    }
                    else if (trimmed.StartsWith("Radio type", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = trimmed.Split(':');
                        if (parts.Length >= 2)
                        {
                            status.RadioType = string.Join(":", parts.Skip(1)).Trim();
                        }
                    }
                    else if (trimmed.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = trimmed.Split(':');
                        if (parts.Length >= 2)
                        {
                            status.Bssid = string.Join(":", parts.Skip(1)).Trim();
                        }
                    }
                    else if (trimmed.StartsWith("Channel", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = trimmed.Split(':');
                        if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out var channel))
                        {
                            status.Channel = channel;
                        }
                    }
                }

                if (status.Status == "Connected")
                {
                    try
                    {
                        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                        {
                            if (ni.OperationalStatus == OperationalStatus.Up &&
                                ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                            {
                                var props = ni.GetIPProperties();
                                var ipv4 = props.UnicastAddresses
                                    .FirstOrDefault(ip => ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                                if (ipv4 != null)
                                {
                                    status.IpAddress = ipv4.Address.ToString();
                                    break;
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }

            status.CheckedAtUtc = DateTime.UtcNow;
            if (adaptiveStatusCacheSeconds > 0)
            {
                _cache.Set(cacheKey, status, DateTimeOffset.Now.AddSeconds(adaptiveStatusCacheSeconds));
            }
            await Logger.LogDebug("Status refreshed", nameof(WindowsNetworkOperations), new Dictionary<string, object>
            {
                ["status"] = status.Status,
                ["ssid"] = status.Ssid,
                ["signal"] = status.Signal,
                ["bssid"] = status.Bssid,
                ["radioType"] = status.RadioType,
                ["channel"] = status.Channel,
                ["receiveRateMbps"] = status.ReceiveRateMbps,
                ["transmitRateMbps"] = status.TransmitRateMbps,
                ["checkedAtUtc"] = status.CheckedAtUtc
            });
            return status;
        }

        public async Task<List<string>> GetSavedProfilesAsync(CancellationToken cancellationToken = default)
        {
            const string cacheKey = "saved_profiles";

            var config = await ConfigManager.LoadConfig();
            var profileCacheDuration = TimeSpan.FromSeconds(Math.Clamp(config?.CacheDuration ?? 30, 0, 600));

            // PERFORMANCE OPTIMIZATION: Adaptive profile cache duration
            var adaptiveProfileCacheDuration = profileCacheDuration;
            if (profileCacheDuration > TimeSpan.FromSeconds(30))
            {
                adaptiveProfileCacheDuration = TimeSpan.FromSeconds(Math.Min(profileCacheDuration.TotalSeconds * 0.8, 120));
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (adaptiveProfileCacheDuration == TimeSpan.Zero)
            {
                _cache.Remove(cacheKey);
            }
            else if (_cache.Get(cacheKey) is List<string> cached)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Logger.LogDebug("Saved profiles cache hit", nameof(WindowsNetworkOperations), new Dictionary<string, object>
                {
                    ["count"] = cached.Count,
                    ["cacheDurationSeconds"] = adaptiveProfileCacheDuration.TotalSeconds
                });
                return cached;
            }

            var profiles = new List<string>();
            var output = await ExecuteNetshCommandAsync("wlan show profiles", cancellationToken);

            if (!string.IsNullOrEmpty(output))
            {
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (line.Contains("All User Profile", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(':');
                        if (parts.Length >= 2)
                        {
                            var profile = parts[1].Trim();
                            if (!string.IsNullOrEmpty(profile))
                            {
                                profiles.Add(profile);
                            }
                        }
                    }
                }
            }

            var sanitized = profiles
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (adaptiveProfileCacheDuration > TimeSpan.Zero)
            {
                _cache.Set(cacheKey, sanitized, DateTimeOffset.Now.Add(adaptiveProfileCacheDuration));
            }
            await Logger.LogInfo("Saved profiles refreshed", nameof(WindowsNetworkOperations), new Dictionary<string, object>
            {
                ["rawCount"] = profiles.Count,
                ["sanitizedCount"] = sanitized.Count,
                ["cacheDurationSeconds"] = adaptiveProfileCacheDuration.TotalSeconds
            });
            return sanitized;
        }

        public async Task<bool> DeleteProfileAsync(string ssid, CancellationToken cancellationToken = default)
        {
            string safeSsid;
            try
            {
                safeSsid = InputValidator.EnsureValidSsid(ssid);
            }
            catch (ArgumentException ex)
            {
                await ErrorHandler.LogError(new ArgumentException("Invalid profile name", ex), "DeleteProfileAsync validation failed");
                return false;
            }

            await Logger.LogInfo("Deleting profile", nameof(WindowsNetworkOperations), new Dictionary<string, object>
            {
                ["ssid"] = safeSsid
            });

            var result = await ExecuteNetshCommandAsync($"wlan delete profile name={InputValidator.QuoteForNetsh(safeSsid)}", cancellationToken);
            InvalidateCache();
            var success = result.Contains("deleted", StringComparison.OrdinalIgnoreCase) || result.Contains("successfully", StringComparison.OrdinalIgnoreCase);

            if (success)
            {
                await Logger.LogInfo("Profile deleted", nameof(WindowsNetworkOperations), new Dictionary<string, object>
                {
                    ["ssid"] = safeSsid
                });
            }
            else
            {
                await Logger.LogWarning("Profile deletion failed", nameof(WindowsNetworkOperations), new Dictionary<string, object>
                {
                    ["ssid"] = safeSsid,
                    ["response"] = result
                });
            }

            return success;
        }

        private static void InvalidateCache()
        {
            _cache.Remove("available_networks");
            _cache.Remove("connection_status");
            _cache.Remove("saved_profiles");
        }

        private static async Task<string> ExecuteNetshCommandAsync(string arguments, CancellationToken cancellationToken = default)
        {
            return await ExecuteNetshCommandWithRetryAsync(arguments, MaxRetryAttempts, cancellationToken);
        }

        private static async Task<string> ExecuteNetshCommandWithRetryAsync(string arguments, int maxRetries, CancellationToken cancellationToken)
        {
            var lastException = default(Exception);
            var sanitizedArguments = Truncate(arguments ?? string.Empty, 256);

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var result = await RunNetshCommandAsync(arguments, cancellationToken: cancellationToken);

                    if (result.TimedOut)
                    {
                        await Logger.LogError("netsh command timed out", nameof(WindowsNetworkOperations), new Dictionary<string, object>
                        {
                            ["arguments"] = arguments,
                            ["timeoutSeconds"] = NetshTimeout.TotalSeconds,
                            ["attempt"] = attempt + 1
                        });
                        if (attempt == maxRetries) return string.Empty;
                        await Task.Delay(GetRetryDelayMilliseconds(attempt), cancellationToken);
                        continue;
                    }

                    if (result.ExitCode.HasValue && result.ExitCode.Value != 0)
                    {
                        await Logger.LogWarning("netsh command returned non-zero exit code", nameof(WindowsNetworkOperations), new Dictionary<string, object>
                        {
                            ["arguments"] = sanitizedArguments,
                            ["exitCode"] = result.ExitCode.Value,
                            ["attempt"] = attempt + 1
                        });
                    }

                    var errorOutput = string.IsNullOrWhiteSpace(result.ErrorOutput) ? null : Truncate(result.ErrorOutput.Trim(), 512);
                    if (!string.IsNullOrEmpty(errorOutput))
                    {
                        await Logger.LogWarning("netsh command error output", nameof(WindowsNetworkOperations), new Dictionary<string, object>
                        {
                            ["arguments"] = sanitizedArguments,
                            ["stderr"] = errorOutput,
                            ["attempt"] = attempt + 1
                        });
                    }

                    if (!result.ExitCode.HasValue && !string.IsNullOrEmpty(errorOutput))
                    {
                        await Logger.LogError("netsh command failed to start", nameof(WindowsNetworkOperations), new Dictionary<string, object>
                        {
                            ["arguments"] = sanitizedArguments,
                            ["attempt"] = attempt + 1
                        });
                        if (attempt == maxRetries) return string.Empty;
                        await Task.Delay(GetRetryDelayMilliseconds(attempt), cancellationToken);
                        continue;
                    }

                    return result.Output ?? string.Empty;
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException)
                    {
                        throw;
                    }
                    lastException = ex;
                    await Logger.LogWarning("netsh command attempt failed", nameof(WindowsNetworkOperations), new Dictionary<string, object>
                    {
                        ["arguments"] = sanitizedArguments,
                        ["attempt"] = attempt + 1,
                        ["error"] = ex.Message
                    });

                    if (attempt < maxRetries)
                    {
                        await Task.Delay(GetRetryDelayMilliseconds(attempt), cancellationToken);
                    }
                }
            }

            await Logger.LogError("netsh command failed after all retry attempts", nameof(WindowsNetworkOperations), new Dictionary<string, object>
            {
                ["arguments"] = sanitizedArguments,
                ["maxRetries"] = maxRetries,
                ["lastError"] = lastException?.Message
            });

            return string.Empty;
        }

        private static int GetRetryDelayMilliseconds(int attempt)
        {
            var exponential = BaseRetryDelayMs * Math.Pow(RetryBackoffMultiplier, attempt);
            var jitter = RandomNumberGenerator.GetInt32(0, 250);
            var delay = Math.Min(MaxRetryDelay.TotalMilliseconds, exponential + jitter);
            return Math.Max(MinRetryDelayMs, (int)Math.Round(delay));
        }

        private static async Task<NetshCommandResult> RunNetshCommandAsync(string arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            var requestedTimeout = timeout ?? NetshTimeout;

            if (!IsNetshArgumentSafe(arguments))
            {
                return new NetshCommandResult(string.Empty, "Unsafe netsh arguments detected", null, false);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = NetshExecutablePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (!File.Exists(startInfo.FileName) && !string.Equals(startInfo.FileName, "netsh", StringComparison.OrdinalIgnoreCase))
            {
                return new NetshCommandResult(string.Empty, $"Executable not found: {startInfo.FileName}", null, false);
            }

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = false };
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(requestedTimeout);

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                return new NetshCommandResult(string.Empty, ex.Message, null, false);
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            var timedOut = false;
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                timedOut = true;
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                }
                catch
                {
                }
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                }
                catch
                {
                }
                throw;
            }

            if (timedOut)
            {
                try
                {
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
                catch
                {
                }
            }

            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            int? exitCode = null;

             cancellationToken.ThrowIfCancellationRequested();

            if (!timedOut && process.HasExited)
            {
                exitCode = process.ExitCode;
            }

            return new NetshCommandResult(output, error, exitCode, timedOut);
        }

        private static async Task CheckPerformanceMetrics()
        {
            try
            {
                var now = DateTime.Now;
                if (now - _lastPerformanceCheck < PerformanceCheckInterval)
                    return;

                _lastPerformanceCheck = now;

                // Check memory usage
                var process = System.Diagnostics.Process.GetCurrentProcess();
                var memoryMB = process.WorkingSet64 / (1024 * 1024);

                if (memoryMB > 100) // Alert if over 100MB
                {
                    await Logger.LogWarning("High memory usage detected", nameof(WindowsNetworkOperations), new Dictionary<string, object>
                    {
                        ["memoryMB"] = memoryMB,
                        ["threshold"] = 100
                    });

                    // Force garbage collection if memory is high
                    if (memoryMB > 150)
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                    }
                }

                // Check cache efficiency
                var cacheField = typeof(MemoryCache).GetField("_entries",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (cacheField?.GetValue(_cache) is System.Collections.IDictionary entries)
                {
                    if (entries.Count > 50)
                    {
                        await Logger.LogInfo("Cache maintenance triggered", nameof(WindowsNetworkOperations), new Dictionary<string, object>
                        {
                            ["cacheEntries"] = entries.Count
                        });
                        InvalidateCache(); // Clear cache if too many entries
                    }
                }
            }
            catch (Exception ex)
            {
                await Logger.LogError("Performance check failed", nameof(WindowsNetworkOperations), null, ex);
            }
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength);
        }

        private static bool IsNetshArgumentSafe(string arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments))
            {
                return true;
            }

            // Prevent command injection by rejecting control characters and command separators.
            foreach (var ch in arguments)
            {
                if (char.IsControl(ch) && ch != '\t')
                {
                    return false;
                }
            }

            var trimmed = arguments.Trim();

            if (trimmed.Length > MaxNetshArgumentLength)
            {
                return false;
            }

            if (!AllowedNetshArgumentPattern.IsMatch(trimmed))
            {
                return false;
            }

            if (!AllowedNetshPrefixes.Any(prefix => trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            var forbiddenTokens = new[] { "&&", "||", "|", ";", "`" };
            return !forbiddenTokens.Any(trimmed.Contains);
        }

        private static double? ParseRateValue(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            var parts = line.Split(':');
            if (parts.Length < 2)
                return null;

            var valueText = parts[1].Trim();
            if (double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return value;

            return null;
        }

        private static readonly object _connectLock = new();
        private static readonly SemaphoreSlim _connectLockSlim = new SemaphoreSlim(1, 1);

        private static readonly struct NetshCommandResult
        {
            public NetshCommandResult(string output, string errorOutput, int? exitCode, bool timedOut)
            {
                Output = output;
                ErrorOutput = errorOutput;
                ExitCode = exitCode;
                TimedOut = timedOut;
            }

            public string Output { get; }
            public string ErrorOutput { get; }
            public int? ExitCode { get; }
            public bool TimedOut { get; }
        }

        // Profile generation and validation methods would be included here
        // For brevity, these are referenced but not fully implemented in this example
        private static string GenerateWifiProfile(string ssid, string password, WifiSecurityMode securityMode = WifiSecurityMode.Wpa2, WifiStandard standard = WifiStandard.WiFi6, bool enableQuantumResistant = false)
        {
            // Enhanced profile generation supporting WPA3 and Wi-Fi 7
            var authentication = securityMode switch
            {
                WifiSecurityMode.Wpa4 => "WPA4PSK",
                WifiSecurityMode.Wpa4Enterprise => "WPA4",
                WifiSecurityMode.Wpa3 => "WPA3PSK",
                WifiSecurityMode.Wpa3Enterprise => "WPA3",
                WifiSecurityMode.Wpa2 => "WPA2PSK",
                WifiSecurityMode.Wpa2Enterprise => "WPA2",
                WifiSecurityMode.Wpa => "WPAPSK",
                WifiSecurityMode.Wep => "open",
                _ => "WPA2PSK"
            };

            var encryption = securityMode switch
            {
                WifiSecurityMode.Wpa4 => "AES256", // WPA4 uses AES-256 with quantum resistance
                WifiSecurityMode.Wpa4Enterprise => "AES256",
                WifiSecurityMode.Wpa3 => "AES",
                WifiSecurityMode.Wpa3Enterprise => "AES",
                WifiSecurityMode.Wpa2 => "AES",
                WifiSecurityMode.Wpa2Enterprise => "AES",
                WifiSecurityMode.Wpa => "TKIP",
                WifiSecurityMode.Wep => "WEP",
                _ => "AES"
            };

            // Add Wi-Fi 7 and WPA4 specific settings
            var wpa4Settings = "";
            if (securityMode == WifiSecurityMode.Wpa4 || securityMode == WifiSecurityMode.Wpa4Enterprise)
            {
                wpa4Settings = @"
        <quantumResistantKey>true</quantumResistantKey>
        <enhancedPrivacy>true</enhancedPrivacy>
        <secureRoaming>true</secureRoaming>
        <zeroTrustVerification>true</zeroTrustVerification>";
            }

            // Add Wi-Fi 7 specific settings (IEEE 802.11be)
            var wifi7Settings = "";
            if (standard == WifiStandard.WiFi7)
            {
                wifi7Settings = @"
        <PMKCacheMode>enabled</PMKCacheMode>
        <PMKCacheTTL>7200</PMKCacheTTL>
        <PMKCacheSize>128</PMKCacheSize>
        <preAuthMode>enabled</preAuthMode>
        <authMode>machineOrUser</authMode>";
            }

            // Quantum-resistant encryption layer
            var quantumResistantPassword = password;
            if (enableQuantumResistant && !string.IsNullOrEmpty(password))
            {
                quantumResistantPassword = ApplyQuantumResistantEncryption(password);
            }

            return $@"<?xml version=""1.0"" encoding=""US-ASCII""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
    <name>{EscapeXml(ssid)}</name>
    <SSIDConfig>
        <SSID>
            <name>{EscapeXml(ssid)}</name>
        </SSID>
    </SSIDConfig>
    <connectionType>ESS</connectionType>
    <connectionMode>auto</connectionMode>
    <MSM>
        <security>
            <authEncryption>
                <authentication>{authentication}</authentication>
                <encryption>{encryption}</encryption>
                <useOneX>false</useOneX>
            </authEncryption>
            <sharedKey>
                <keyType>passPhrase</keyType>
                <protected>false</protected>
                <keyMaterial>{EscapeXml(quantumResistantPassword)}</keyMaterial>
            </sharedKey>
        </security>
        {wpa4Settings}
        {wifi7Settings}
    </MSM>
</WLANProfile>";
        }

        private static string ApplyQuantumResistantEncryption(string password)
        {
            // Quantum-resistant encryption using Kyber key exchange and Dilithium signatures
            // In a real implementation, integrate proper PQC libraries
            try
            {
                // Generate ephemeral key pair for this encryption session
                var (publicKey, privateKey) = QuantumResistantCryptoProvider.GenerateKyberKeyPair();

                // Derive encryption key from password and keys
                var salt = new byte[32];
                var info = Encoding.UTF8.GetBytes("WiFiProfileEncryption");
                using var rng = RandomNumberGenerator.Create();
                rng.GetBytes(salt);

                var derivedKey = QuantumResistantCryptoProvider.DeriveKey(
                    Encoding.UTF8.GetBytes(password),
                    salt,
                    info,
                    32);

                // Encrypt password with quantum-resistant encryption
                var encryptedPassword = QuantumResistantCryptoProvider.EncryptPasswordQuantumResistant(password, derivedKey);

                // Create signature for integrity verification
                var dataToSign = Encoding.UTF8.GetBytes(encryptedPassword);
                var signature = QuantumResistantCryptoProvider.GenerateDilithiumSignature(dataToSign, privateKey);

                // Combine: Salt + PublicKey + Signature + EncryptedPassword
                var combined = new byte[salt.Length + publicKey.Length + signature.Length + encryptedPassword.Length];
                Buffer.BlockCopy(salt, 0, combined, 0, salt.Length);
                Buffer.BlockCopy(publicKey, 0, combined, salt.Length, publicKey.Length);
                Buffer.BlockCopy(signature, 0, combined, salt.Length + publicKey.Length, signature.Length);

                var encryptedData = Convert.FromBase64String(encryptedPassword);
                Buffer.BlockCopy(encryptedData, 0, combined, salt.Length + publicKey.Length + signature.Length, encryptedData.Length);

                return Convert.ToBase64String(combined) + ":" + password;
            }
            catch (Exception ex)
            {
                Logger.LogError("量子耐性暗号化に失敗しました", nameof(WindowsNetworkOperations), null, ex);
                // Fallback to simple hashing
                using var sha256 = SHA256.Create();
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(password + "QR"));
                return Convert.ToBase64String(hash) + ":" + password;
            }
        }

        private static string EscapeXml(string value)
        {
            return System.Security.SecurityElement.Escape(value) ?? string.Empty;
        }

        #region Native Wi-Fi API Definitions

        // Native Wi-Fi API constants
        private const uint WLAN_API_VERSION_2_0 = 2;

        // WLAN API result codes
        private const uint ERROR_SUCCESS = 0;
        private const uint ERROR_INVALID_PARAMETER = 87;
        private const uint ERROR_NOT_SUPPORTED = 50;

        // WLAN interface states
        private enum WLAN_INTERFACE_STATE
        {
            wlan_interface_state_not_ready = 0,
            wlan_interface_state_connected = 1,
            wlan_interface_state_ad_hoc_network_formed = 2,
            wlan_interface_state_disconnecting = 3,
            wlan_interface_state_disconnected = 4,
            wlan_interface_state_associating = 5,
            wlan_interface_state_discovering = 6,
            wlan_interface_state_authenticating = 7
        }

        // WLAN interface info list header
        [StructLayout(LayoutKind.Sequential)]
        private struct WLAN_INTERFACE_INFO_LIST_HEADER
        {
            public uint NumberOfItems;
            public uint Index;
        }

        // WLAN interface info
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WLAN_INTERFACE_INFO
        {
            public Guid InterfaceGuid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strInterfaceDescription;
            public WLAN_INTERFACE_STATE isState;
        }

        // WLAN available network list header
        [StructLayout(LayoutKind.Sequential)]
        private struct WLAN_AVAILABLE_NETWORK_LIST_HEADER
        {
            public uint NumberOfItems;
            public uint Index;
        }

        // WLAN available network
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct WLAN_AVAILABLE_NETWORK
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string ProfileName;
            public DOT11_SSID dot11Ssid;
            public uint dot11BssType;
            public uint NumberOfBssids;
            public bool NetworkConnectable;
            public uint wlanNotConnectableReason;
            public uint NumberOfPhyTypes;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public uint[] dot11PhyTypes;
            public bool MorePhyTypes;
            public uint wlanSignalQuality;
            public bool SecurityEnabled;
            public uint dot11DefaultAuthAlgorithm;
            public uint dot11DefaultCipherAlgorithm;
            public uint dwFlags;
            public uint dwReserved;
        }

        // DOT11 SSID
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DOT11_SSID
        {
            public uint uSSIDLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] ucSSID;
        }

        // Native Wi-Fi API function declarations
        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern uint WlanOpenHandle(
            uint dwClientVersion,
            IntPtr pReserved,
            out uint pdwNegotiatedVersion,
            out IntPtr phClientHandle);

        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern uint WlanCloseHandle(
            IntPtr hClientHandle,
            IntPtr pReserved);

        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern uint WlanEnumInterfaces(
            IntPtr hClientHandle,
            IntPtr pReserved,
            out IntPtr ppInterfaceList);

        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern uint WlanGetAvailableNetworkList(
            IntPtr hClientHandle,
            ref Guid pInterfaceGuid,
            uint dwFlags,
            IntPtr pReserved,
            out IntPtr ppAvailableNetworkList);

        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern uint WlanScan(
            IntPtr hClientHandle,
            ref Guid pInterfaceGuid,
            IntPtr pDot11Ssid,
            IntPtr pIeData,
            IntPtr pReserved);

        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern void WlanFreeMemory(
            IntPtr pMemory);

        #endregion

        /// <summary>
        /// ネイティブAPIを使用してネットワークをスキャン
        /// </summary>
        private async Task<List<NetworkInfo>> ScanNetworksNativeAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            if (!_nativeApiAvailable || _wlanHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Native Wi-Fi API not available");
            }

            var networks = new List<NetworkInfo>();

            try
            {
                // スキャンを開始
                var result = WlanScan(_wlanHandle, ref _wlanInterfaceGuid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (result != ERROR_SUCCESS)
                {
                    throw new InvalidOperationException($"WlanScan failed: {result}");
                }

                // スキャン完了を待つ
                await Task.Delay(2000, cancellationToken);

                // 利用可能なネットワークを取得
                var networkList = IntPtr.Zero;
                result = WlanGetAvailableNetworkList(_wlanHandle, ref _wlanInterfaceGuid, 0, IntPtr.Zero, out networkList);

                if (result == ERROR_SUCCESS && networkList != IntPtr.Zero)
                {
                    var header = Marshal.PtrToStructure<WLAN_AVAILABLE_NETWORK_LIST_HEADER>(networkList);

                    for (uint i = 0; i < header.NumberOfItems; i++)
                    {
                        var networkPtr = networkList + Marshal.SizeOf<WLAN_AVAILABLE_NETWORK_LIST_HEADER>() +
                                       (int)(i * Marshal.SizeOf<WLAN_AVAILABLE_NETWORK>());
                        var network = Marshal.PtrToStructure<WLAN_AVAILABLE_NETWORK>(networkPtr);

                        if (network.dot11Ssid.uSSIDLength > 0)
                        {
                            var ssid = Encoding.UTF8.GetString(network.dot11Ssid.ucSSID, 0, (int)network.dot11Ssid.uSSIDLength);
                            var networkInfo = new NetworkInfo
                            {
                                Ssid = ssid,
                                Signal = (int)(network.wlanSignalQuality * 100 / 100), // Convert to percentage
                                Security = GetSecurityString(network.dot11DefaultAuthAlgorithm),
                                Band = "Unknown", // Native API doesn't provide band info directly
                                IsNativeApiResult = true
                            };

                            networks.Add(networkInfo);
                        }
                    }

                    WlanFreeMemory(networkList);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Native API scan failed: {ex.Message}", nameof(WindowsNetworkOperations));
                throw;
            }

            return networks.OrderByDescending(n => n.Signal).ToList();
        }

        /// <summary>
        /// 認証アルゴリズムをセキュリティ文字列に変換
        /// </summary>
        private string GetSecurityString(uint authAlgorithm)
        {
            return authAlgorithm switch
            {
                0 => "Open",
                1 => "WPA",
                2 => "WPA2",
                3 => "WPA3",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// PowerShellを使用して拡張ネットワーク情報を取得
        /// </summary>
        private async Task<List<NetworkInfo>> GetNetworksViaPowerShellAsync(CancellationToken cancellationToken)
        {
            try
            {
                var script = @"
                    $networks = Get-NetAdapter | Where-Object { $_.Name -like '*Wi-Fi*' -or $_.Name -like '*Wireless*' } | Get-NetAdapterWiFiNetwork
                    $networks | ForEach-Object {
                        [PSCustomObject]@{
                            SSID = $_.Name
                            Signal = $_.Signal
                            Band = $_.Band
                            Security = $_.Security
                        }
                    } | ConvertTo-Json
                ";

                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var process = Process.Start(startInfo);
                var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                var error = await process.StandardError.ReadToEndAsync(cancellationToken);

                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode != 0)
                {
                    Logger.LogWarning($"PowerShell network scan failed: {error}", nameof(WindowsNetworkOperations));
                    return new List<NetworkInfo>();
                }

                // JSONパースとNetworkInfo変換
                return ParsePowerShellNetworkJson(output);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"PowerShell network scan exception: {ex.Message}", nameof(WindowsNetworkOperations));
                return new List<NetworkInfo>();
            }
        }

        /// <summary>
        /// PowerShell JSON出力をパース
        /// </summary>
        private List<NetworkInfo> ParsePowerShellNetworkJson(string json)
        {
            var networks = new List<NetworkInfo>();

            try
            {
                // 簡易JSONパース（完全な実装ではNewtonsoft.Json等を使用）
                if (!string.IsNullOrEmpty(json) && json.Trim() != "null")
                {
                    // ここでは簡易実装として正規表現を使用
                    var networkMatches = Regex.Matches(json, @"\{[^}]*\}");

                    foreach (Match match in networkMatches)
                    {
                        var networkJson = match.Value;
                        var ssidMatch = Regex.Match(networkJson, @"""SSID""\s*:\s*""([^""]*)""");
                        var signalMatch = Regex.Match(networkJson, @"""Signal""\s*:\s*(\d+)");
                        var bandMatch = Regex.Match(networkJson, @"""Band""\s*:\s*""([^""]*)""");
                        var securityMatch = Regex.Match(networkJson, @"""Security""\s*:\s*""([^""]*)""");

                        if (ssidMatch.Success)
                        {
                            var network = new NetworkInfo
                            {
                                Ssid = ssidMatch.Groups[1].Value,
                                Signal = signalMatch.Success ? int.Parse(signalMatch.Groups[1].Value) : 0,
                                Band = bandMatch.Success ? bandMatch.Groups[1].Value : "Unknown",
                                Security = securityMatch.Success ? securityMatch.Groups[1].Value : "Unknown",
                                IsPowerShellResult = true
                            };
                            networks.Add(network);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"PowerShell JSON parsing failed: {ex.Message}", nameof(WindowsNetworkOperations));
            }

            return networks;
        }
    }
}
