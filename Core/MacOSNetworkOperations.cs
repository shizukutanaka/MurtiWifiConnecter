using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// macOS固有のネットワーク操作実装
    /// </summary>
    public class MacOSNetworkOperations : INetworkOperations
    {
        private static readonly int MaxRetryAttempts = 3;
        private static readonly TimeSpan AirportTimeout = TimeSpan.FromSeconds(30);
        private static readonly string AirportExecutablePath = "/System/Library/PrivateFrameworks/Apple80211.framework/Versions/Current/Resources/airport";
        private static readonly string NetworksetupExecutablePath = "/usr/sbin/networksetup";

        // Cache and synchronization
        private static readonly MemoryCache _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = 500,
            CompactionPercentage = 0.15,
            ExpirationScanFrequency = TimeSpan.FromMinutes(2)
        });
        private static readonly SemaphoreSlim _scanLock = new SemaphoreSlim(1, 1);
        private static readonly object _connectLock = new();

        public PlatformType Platform => PlatformType.macOS;

        public async Task<List<NetworkInfo>> ScanNetworksAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            const string cacheKey = "available_networks";

            // Rate limiting check
            var rateLimitResult = await SecurityManager.CheckRateLimitAsync("scan_networks");
            if (!rateLimitResult.Allowed)
            {
                await Logger.LogWarning("Network scan rate limited", nameof(MacOSNetworkOperations), new Dictionary<string, object>
                {
                    ["scope"] = rateLimitResult.Scope.ToString()
                });
                throw new InvalidOperationException($"Rate limit exceeded for network scanning. Please wait before trying again.");
            }

            var config = await ConfigManager.LoadConfig(cancellationToken);
            var scanCacheDuration = TimeSpan.FromSeconds(Math.Clamp(config?.CacheDuration ?? 30, 0, 600));

            cancellationToken.ThrowIfCancellationRequested();

            if (scanCacheDuration == TimeSpan.Zero)
            {
                _cache.Remove(cacheKey);
            }
            else if (!forceRefresh && _cache.Get(cacheKey) is List<NetworkInfo> cached)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Logger.LogDebug("ScanNetworksAsync cache hit", nameof(MacOSNetworkOperations), new Dictionary<string, object>
                {
                    ["forceRefresh"] = forceRefresh,
                    ["count"] = cached.Count,
                    ["cacheDurationSeconds"] = scanCacheDuration.TotalSeconds
                });
                return cached;
            }

            await Logger.LogInfo("Scanning networks", nameof(MacOSNetworkOperations), new Dictionary<string, object>
            {
                ["forceRefresh"] = forceRefresh,
                ["cacheDurationSeconds"] = scanCacheDuration.TotalSeconds
            });

            return await ErrorHandler.HandleNetworkOperationWithRecovery(async () =>
            {
                await _scanLock.WaitAsync(cancellationToken);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Use airport command to scan networks
                    var output = await ExecuteCommandAsync($"{AirportExecutablePath} -s", cancellationToken);

                    if (string.IsNullOrWhiteSpace(output))
                    {
                        return new List<NetworkInfo>();
                    }

                    var networks = ParseAirportScanOutput(output);
                    networks = networks.OrderByDescending(n => n.Signal).ToList();

                    // Cache results
                    if (scanCacheDuration > TimeSpan.Zero)
                    {
                        var cacheOptions = new MemoryCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = scanCacheDuration,
                            Size = 1,
                            Priority = CacheItemPriority.Normal
                        };
                        _cache.Set(cacheKey, networks, cacheOptions);
                    }

                    await Logger.LogInfo("Scanning networks completed", nameof(MacOSNetworkOperations), new Dictionary<string, object>
                    {
                        ["forceRefresh"] = forceRefresh,
                        ["count"] = networks.Count,
                        ["cacheDurationSeconds"] = scanCacheDuration.TotalSeconds
                    });

                    return networks;
                }
                finally
                {
                    _scanLock.Release();
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

            await Logger.LogInfo("Connection attempt", nameof(MacOSNetworkOperations), new Dictionary<string, object>
            {
                ["ssid"] = safeSsid,
                ["hasPassword"] = !string.IsNullOrEmpty(safePassword)
            });

            var connectionResult = await ErrorHandler.ExecuteWithRetryBool(async () =>
            {
                lock (_connectLock)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        // First, try to connect to the network
                        string command;
                        if (!string.IsNullOrEmpty(safePassword))
                        {
                            // Create a temporary networksetup command with password
                            command = $"{NetworksetupExecutablePath} -setairportnetwork en0 \"{safeSsid}\" \"{safePassword}\"";
                        }
                        else
                        {
                            // Try to connect without password (open network)
                            command = $"{NetworksetupExecutablePath} -setairportnetwork en0 \"{safeSsid}\"";
                        }

                        var result = await ExecuteCommandAsync(command, cancellationToken);

                        // Check if connection was successful
                        var statusResult = await ExecuteCommandAsync($"{AirportExecutablePath} -I", cancellationToken);
                        var isConnected = statusResult.Contains($"SSID: {safeSsid}");

                        if (isConnected)
                        {
                            InvalidateCache();
                            await Logger.LogInfo("Connected successfully", nameof(MacOSNetworkOperations), new Dictionary<string, object>
                            {
                                ["ssid"] = safeSsid
                            });
                            return true;
                        }
                        else
                        {
                            await Logger.LogWarning("Connection failed", nameof(MacOSNetworkOperations), new Dictionary<string, object>
                            {
                                ["ssid"] = safeSsid,
                                ["result"] = result
                            });
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        await Logger.LogError("Connection attempt failed", nameof(MacOSNetworkOperations), new Dictionary<string, object>
                        {
                            ["ssid"] = safeSsid,
                            ["error"] = ex.Message
                        }, ex);
                        return false;
                    }
                }
            });

            await Logger.LogInfo("Connection attempt completed", nameof(MacOSNetworkOperations), new Dictionary<string, object>
            {
                ["ssid"] = safeSsid,
                ["result"] = connectionResult
            });

            return connectionResult;
        }

        public async Task<bool> DisconnectAsync(CancellationToken cancellationToken = default)
        {
            await Logger.LogInfo("Disconnect requested", nameof(MacOSNetworkOperations));

            // Disconnect from current WiFi network
            var result = await ExecuteCommandAsync($"{NetworksetupExecutablePath} -setairportpower en0 off", cancellationToken);
            await Task.Delay(1000, cancellationToken); // Wait a bit
            var powerOnResult = await ExecuteCommandAsync($"{NetworksetupExecutablePath} -setairportpower en0 on", cancellationToken);

            InvalidateCache();

            var success = powerOnResult.Contains("enabled", StringComparison.OrdinalIgnoreCase) ||
                         powerOnResult.Contains("on", StringComparison.OrdinalIgnoreCase);

            if (success)
            {
                await Logger.LogInfo("Disconnect successful", nameof(MacOSNetworkOperations));
            }
            else
            {
                await Logger.LogWarning("Disconnect failed", nameof(MacOSNetworkOperations), new Dictionary<string, object>
                {
                    ["result"] = powerOnResult
                });
            }

            return success;
        }

        public async Task<ConnectionStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            const string cacheKey = "connection_status";

            var config = await ConfigManager.LoadConfig();
            var statusCacheSeconds = Math.Clamp(Math.Max(1, (config?.ScanInterval ?? 30) / 2), 1, 60);

            if (statusCacheSeconds > 0 && _cache.Get(cacheKey) is ConnectionStatus cached)
            {
                await Logger.LogDebug("Status served from cache", nameof(MacOSNetworkOperations), new Dictionary<string, object>
                {
                    ["status"] = cached.Status,
                    ["ssid"] = cached.Ssid,
                    ["cacheDurationSeconds"] = statusCacheSeconds
                });
                return cached;
            }

            var status = new ConnectionStatus { Status = "Disconnected" };

            try
            {
                // Get airport interface information
                var output = await ExecuteCommandAsync($"{AirportExecutablePath} -I", cancellationToken);

                if (!string.IsNullOrEmpty(output))
                {
                    status = ParseAirportStatusOutput(output);
                }

                // Get IP address information
                if (status.Status == "Connected")
                {
                    try
                    {
                        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                        {
                            if (ni.OperationalStatus == OperationalStatus.Up &&
                                ni.Name.Contains("en"))
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
                        // IP address retrieval failed, continue without it
                    }
                }
            }
            catch (Exception ex)
            {
                await Logger.LogError("Failed to get connection status", nameof(MacOSNetworkOperations), null, ex);
            }

            status.CheckedAtUtc = DateTime.UtcNow;

            if (statusCacheSeconds > 0)
            {
                _cache.Set(cacheKey, status, DateTimeOffset.Now.AddSeconds(statusCacheSeconds));
            }

            await Logger.LogDebug("Status refreshed", nameof(MacOSNetworkOperations), new Dictionary<string, object>
            {
                ["status"] = status.Status,
                ["ssid"] = status.Ssid,
                ["signal"] = status.Signal,
                ["bssid"] = status.Bssid,
                ["checkedAtUtc"] = status.CheckedAtUtc
            });

            return status;
        }

        public async Task<List<string>> GetSavedProfilesAsync(CancellationToken cancellationToken = default)
        {
            const string cacheKey = "saved_profiles";

            var config = await ConfigManager.LoadConfig();
            var profileCacheDuration = TimeSpan.FromSeconds(Math.Clamp(config?.CacheDuration ?? 30, 0, 600));

            if (profileCacheDuration == TimeSpan.Zero)
            {
                _cache.Remove(cacheKey);
            }
            else if (_cache.Get(cacheKey) is List<string> cached)
            {
                await Logger.LogDebug("Saved profiles cache hit", nameof(MacOSNetworkOperations), new Dictionary<string, object>
                {
                    ["count"] = cached.Count,
                    ["cacheDurationSeconds"] = profileCacheDuration.TotalSeconds
                });
                return cached;
            }

            var profiles = new List<string>();

            try
            {
                // Get preferred networks from networksetup
                var output = await ExecuteCommandAsync($"{NetworksetupExecutablePath} -listpreferredwirelessnetworks en0", cancellationToken);

                if (!string.IsNullOrEmpty(output))
                {
                    var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed) && !trimmed.Contains("Preferred networks"))
                        {
                            profiles.Add(trimmed);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await Logger.LogError("Failed to get saved profiles", nameof(MacOSNetworkOperations), null, ex);
            }

            var sanitized = profiles
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (profileCacheDuration > TimeSpan.Zero)
            {
                _cache.Set(cacheKey, sanitized, DateTimeOffset.Now.Add(profileCacheDuration));
            }

            await Logger.LogInfo("Saved profiles refreshed", nameof(MacOSNetworkOperations), new Dictionary<string, object>
            {
                ["rawCount"] = profiles.Count,
                ["sanitizedCount"] = sanitized.Count,
                ["cacheDurationSeconds"] = profileCacheDuration.TotalSeconds
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

            await Logger.LogInfo("Deleting profile", nameof(MacOSNetworkOperations), new Dictionary<string, object>
            {
                ["ssid"] = safeSsid
            });

            try
            {
                // Remove from preferred networks
                var result = await ExecuteCommandAsync($"{NetworksetupExecutablePath} -removepreferredwirelessnetwork en0 \"{safeSsid}\"", cancellationToken);

                InvalidateCache();

                var success = string.IsNullOrEmpty(result) || !result.Contains("Error");

                if (success)
                {
                    await Logger.LogInfo("Profile deleted", nameof(MacOSNetworkOperations), new Dictionary<string, object>
                    {
                        ["ssid"] = safeSsid
                    });
                }
                else
                {
                    await Logger.LogWarning("Profile deletion failed", nameof(MacOSNetworkOperations), new Dictionary<string, object>
                    {
                        ["ssid"] = safeSsid,
                        ["result"] = result
                    });
                }

                return success;
            }
            catch (Exception ex)
            {
                await Logger.LogError("Profile deletion failed", nameof(MacOSNetworkOperations), new Dictionary<string, object>
                {
                    ["ssid"] = safeSsid,
                    ["error"] = ex.Message
                }, ex);
                return false;
            }
        }

        private static void InvalidateCache()
        {
            _cache.Remove("available_networks");
            _cache.Remove("connection_status");
            _cache.Remove("saved_profiles");
        }

        private static async Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = startInfo };
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(AirportTimeout);

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                await Logger.LogError("Command execution failed to start", nameof(MacOSNetworkOperations), new Dictionary<string, object>
                {
                    ["command"] = command,
                    ["error"] = ex.Message
                });
                return string.Empty;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
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
                await Logger.LogError("Command execution timed out", nameof(MacOSNetworkOperations), new Dictionary<string, object>
                {
                    ["command"] = command,
                    ["timeoutSeconds"] = AirportTimeout.TotalSeconds
                });
                return string.Empty;
            }

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0 && !string.IsNullOrEmpty(error))
            {
                await Logger.LogWarning("Command execution error", nameof(MacOSNetworkOperations), new Dictionary<string, object>
                {
                    ["command"] = command,
                    ["exitCode"] = process.ExitCode,
                    ["error"] = error
                });
            }

            return output ?? string.Empty;
        }

        private static List<NetworkInfo> ParseAirportScanOutput(string output)
        {
            var networks = new List<NetworkInfo>();
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Skip header lines
            var dataLines = lines.SkipWhile(line => !line.Contains("SSID")).Skip(1);

            foreach (var line in dataLines)
            {
                try
                {
                    // Parse airport scan output format
                    // Example: "SomeNetwork 5GHz WPA2(PSK/AES/AES) -50 123:45:67:89:ab:cd"
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length >= 4)
                    {
                        var network = new NetworkInfo
                        {
                            Ssid = parts[0],
                            Security = parts.Length > 2 ? parts[2] : "Unknown",
                            Band = parts.Length > 1 && parts[1].Contains("5GHz") ? "5GHz" : "2.4GHz"
                        };

                        // Parse signal strength (negative dBm value)
                        if (parts.Length > 3 && int.TryParse(parts[3], out var signalDbm))
                        {
                            // Convert dBm to percentage (rough approximation)
                            network.Signal = Math.Max(0, Math.Min(100, (signalDbm + 100) * 2));
                        }
                        else
                        {
                            network.Signal = 50; // Default
                        }

                        networks.Add(network);
                    }
                }
                catch (Exception ex)
                {
                    // Log parsing error but continue
                    Console.WriteLine($"Failed to parse network line: {line}, Error: {ex.Message}");
                }
            }

            return networks;
        }

        private static ConnectionStatus ParseAirportStatusOutput(string output)
        {
            var status = new ConnectionStatus { Status = "Disconnected" };
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                var colonIndex = trimmed.IndexOf(':');

                if (colonIndex > 0)
                {
                    var key = trimmed.Substring(0, colonIndex).Trim();
                    var value = trimmed.Substring(colonIndex + 1).Trim();

                    switch (key)
                    {
                        case "SSID":
                            status.Ssid = value;
                            status.Status = "Connected";
                            break;
                        case "BSSID":
                            status.Bssid = value;
                            break;
                        case "agrCtlRSSI":
                            if (int.TryParse(value, out var rssi))
                            {
                                status.Signal = Math.Max(0, Math.Min(100, (rssi + 100) * 2));
                            }
                            break;
                        case "agrCtlNoise":
                            // Could use noise for signal quality calculation
                            break;
                        case "channel":
                            if (int.TryParse(value, out var channel))
                            {
                                status.Channel = channel;
                                status.Band = channel >= 36 ? "5GHz" : "2.4GHz";
                            }
                            break;
                        case "lastTxRate":
                            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate))
                            {
                                status.TransmitRateMbps = rate;
                            }
                            break;
                    }
                }
            }

            return status;
        }
    }
}
