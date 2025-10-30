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
    /// Linux固有のネットワーク操作実装 (NetworkManager使用)
    /// </summary>
    public class LinuxNetworkOperations : INetworkOperations
    {
        private static readonly int MaxRetryAttempts = 3;
        private static readonly TimeSpan NmcliTimeout = TimeSpan.FromSeconds(30);
        private static readonly string NmcliExecutablePath = "/usr/bin/nmcli";

        // Cache and synchronization
        private static readonly MemoryCache _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = 500,
            CompactionPercentage = 0.15,
            ExpirationScanFrequency = TimeSpan.FromMinutes(2)
        });
        private static readonly SemaphoreSlim _scanLock = new SemaphoreSlim(1, 1);
        private static readonly object _connectLock = new();

        public PlatformType Platform => PlatformType.Linux;

        public async Task<List<NetworkInfo>> ScanNetworksAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            const string cacheKey = "available_networks";

            // Rate limiting check
            var rateLimitResult = await SecurityManager.CheckRateLimitAsync("scan_networks");
            if (!rateLimitResult.Allowed)
            {
                await Logger.LogWarning("Network scan rate limited", nameof(LinuxNetworkOperations), new Dictionary<string, object>
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
                await Logger.LogDebug("ScanNetworksAsync cache hit", nameof(LinuxNetworkOperations), new Dictionary<string, object>
                {
                    ["forceRefresh"] = forceRefresh,
                    ["count"] = cached.Count,
                    ["cacheDurationSeconds"] = scanCacheDuration.TotalSeconds
                });
                return cached;
            }

            await Logger.LogInfo("Scanning networks", nameof(LinuxNetworkOperations), new Dictionary<string, object>
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

                    // Use nmcli to scan wireless networks
                    var output = await ExecuteCommandAsync($"{NmcliExecutablePath} -t -f SSID,SIGNAL,SECURITY,CHAN,FREQ device wifi list", cancellationToken);

                    if (string.IsNullOrWhiteSpace(output))
                    {
                        return new List<NetworkInfo>();
                    }

                    var networks = ParseNmcliScanOutput(output);
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

                    await Logger.LogInfo("Scanning networks completed", nameof(LinuxNetworkOperations), new Dictionary<string, object>
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

            await Logger.LogInfo("Connection attempt", nameof(LinuxNetworkOperations), new Dictionary<string, object>
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
                        string command;
                        if (!string.IsNullOrEmpty(safePassword))
                        {
                            // Connect with password
                            command = $"{NmcliExecutablePath} device wifi connect \"{safeSsid}\" password \"{safePassword}\"";
                        }
                        else
                        {
                            // Connect to open network
                            command = $"{NmcliExecutablePath} device wifi connect \"{safeSsid}\"";
                        }

                        var result = await ExecuteCommandAsync(command, cancellationToken);

                        // Check if connection was successful
                        var success = result.Contains("successfully", StringComparison.OrdinalIgnoreCase) ||
                                    !result.Contains("Error", StringComparison.OrdinalIgnoreCase);

                        if (success)
                        {
                            InvalidateCache();
                            await Logger.LogInfo("Connected successfully", nameof(LinuxNetworkOperations), new Dictionary<string, object>
                            {
                                ["ssid"] = safeSsid
                            });
                            return true;
                        }
                        else
                        {
                            await Logger.LogWarning("Connection failed", nameof(LinuxNetworkOperations), new Dictionary<string, object>
                            {
                                ["ssid"] = safeSsid,
                                ["result"] = result
                            });
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        await Logger.LogError("Connection attempt failed", nameof(LinuxNetworkOperations), new Dictionary<string, object>
                        {
                            ["ssid"] = safeSsid,
                            ["error"] = ex.Message
                        }, ex);
                        return false;
                    }
                }
            });

            await Logger.LogInfo("Connection attempt completed", nameof(LinuxNetworkOperations), new Dictionary<string, object>
            {
                ["ssid"] = safeSsid,
                ["result"] = connectionResult
            });

            return connectionResult;
        }

        public async Task<bool> DisconnectAsync(CancellationToken cancellationToken = default)
        {
            await Logger.LogInfo("Disconnect requested", nameof(LinuxNetworkOperations));

            // Disconnect from current WiFi connection
            var result = await ExecuteCommandAsync($"{NmcliExecutablePath} device disconnect wlan0", cancellationToken);

            InvalidateCache();

            var success = result.Contains("successfully", StringComparison.OrdinalIgnoreCase) ||
                         !result.Contains("Error", StringComparison.OrdinalIgnoreCase);

            if (success)
            {
                await Logger.LogInfo("Disconnect successful", nameof(LinuxNetworkOperations));
            }
            else
            {
                await Logger.LogWarning("Disconnect failed", nameof(LinuxNetworkOperations), new Dictionary<string, object>
                {
                    ["result"] = result
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
                await Logger.LogDebug("Status served from cache", nameof(LinuxNetworkOperations), new Dictionary<string, object>
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
                // Get general connection status
                var generalOutput = await ExecuteCommandAsync($"{NmcliExecutablePath} -t -f STATE,CONNECTION device status", cancellationToken);

                if (!string.IsNullOrEmpty(generalOutput))
                {
                    var lines = generalOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Split(':');
                        if (parts.Length >= 2 && parts[0] == "connected")
                        {
                            status.Status = "Connected";
                            status.Ssid = parts[1];
                            break;
                        }
                    }
                }

                // Get detailed WiFi information if connected
                if (status.Status == "Connected")
                {
                    var wifiOutput = await ExecuteCommandAsync($"{NmcliExecutablePath} -t -f ACTIVE,SIGNAL,CHAN,FREQ,SECURITY device wifi list", cancellationToken);

                    if (!string.IsNullOrEmpty(wifiOutput))
                    {
                        var lines = wifiOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            var parts = line.Split(':');
                            if (parts.Length >= 5 && parts[0] == "yes") // ACTIVE=yes
                            {
                                if (int.TryParse(parts[1], out var signal)) // SIGNAL
                                {
                                    status.Signal = signal;
                                }

                                if (int.TryParse(parts[2], out var channel)) // CHAN
                                {
                                    status.Channel = channel;
                                }

                                // FREQ contains frequency in MHz
                                if (double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var freq))
                                {
                                    status.Band = freq >= 5000 ? "5GHz" : "2.4GHz";
                                }

                                status.Authentication = parts[4]; // SECURITY
                                break;
                            }
                        }
                    }
                }

                // Get IP address information
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
                        // IP address retrieval failed, continue without it
                    }
                }
            }
            catch (Exception ex)
            {
                await Logger.LogError("Failed to get connection status", nameof(LinuxNetworkOperations), null, ex);
            }

            status.CheckedAtUtc = DateTime.UtcNow;

            if (statusCacheSeconds > 0)
            {
                _cache.Set(cacheKey, status, DateTimeOffset.Now.AddSeconds(statusCacheSeconds));
            }

            await Logger.LogDebug("Status refreshed", nameof(LinuxNetworkOperations), new Dictionary<string, object>
            {
                ["status"] = status.Status,
                ["ssid"] = status.Ssid,
                ["signal"] = status.Signal,
                ["channel"] = status.Channel,
                ["band"] = status.Band,
                ["authentication"] = status.Authentication,
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
                await Logger.LogDebug("Saved profiles cache hit", nameof(LinuxNetworkOperations), new Dictionary<string, object>
                {
                    ["count"] = cached.Count,
                    ["cacheDurationSeconds"] = profileCacheDuration.TotalSeconds
                });
                return cached;
            }

            var profiles = new List<string>();

            try
            {
                // Get saved WiFi connections
                var output = await ExecuteCommandAsync($"{NmcliExecutablePath} -t -f NAME,TYPE connection show", cancellationToken);

                if (!string.IsNullOrEmpty(output))
                {
                    var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Split(':');
                        if (parts.Length >= 2 && parts[1] == "802-11-wireless")
                        {
                            profiles.Add(parts[0]);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await Logger.LogError("Failed to get saved profiles", nameof(LinuxNetworkOperations), null, ex);
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

            await Logger.LogInfo("Saved profiles refreshed", nameof(LinuxNetworkOperations), new Dictionary<string, object>
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

            await Logger.LogInfo("Deleting profile", nameof(LinuxNetworkOperations), new Dictionary<string, object>
            {
                ["ssid"] = safeSsid
            });

            try
            {
                // Delete the connection
                var result = await ExecuteCommandAsync($"{NmcliExecutablePath} connection delete \"{safeSsid}\"", cancellationToken);

                InvalidateCache();

                var success = result.Contains("successfully", StringComparison.OrdinalIgnoreCase) ||
                             !result.Contains("Error", StringComparison.OrdinalIgnoreCase);

                if (success)
                {
                    await Logger.LogInfo("Profile deleted", nameof(LinuxNetworkOperations), new Dictionary<string, object>
                    {
                        ["ssid"] = safeSsid
                    });
                }
                else
                {
                    await Logger.LogWarning("Profile deletion failed", nameof(LinuxNetworkOperations), new Dictionary<string, object>
                    {
                        ["ssid"] = safeSsid,
                        ["result"] = result
                    });
                }

                return success;
            }
            catch (Exception ex)
            {
                await Logger.LogError("Profile deletion failed", nameof(LinuxNetworkOperations), new Dictionary<string, object>
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
            timeoutCts.CancelAfter(NmcliTimeout);

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                await Logger.LogError("Command execution failed to start", nameof(LinuxNetworkOperations), new Dictionary<string, object>
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
                await Logger.LogError("Command execution timed out", nameof(LinuxNetworkOperations), new Dictionary<string, object>
                {
                    ["command"] = command,
                    ["timeoutSeconds"] = NmcliTimeout.TotalSeconds
                });
                return string.Empty;
            }

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0 && !string.IsNullOrEmpty(error))
            {
                await Logger.LogWarning("Command execution error", nameof(LinuxNetworkOperations), new Dictionary<string, object>
                {
                    ["command"] = command,
                    ["exitCode"] = process.ExitCode,
                    ["error"] = error
                });
            }

            return output ?? string.Empty;
        }

        private static List<NetworkInfo> ParseNmcliScanOutput(string output)
        {
            var networks = new List<NetworkInfo>();
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                try
                {
                    // Parse nmcli tab-separated output
                    // Format: SSID:SIGNAL:SECURITY:CHAN:FREQ
                    var parts = line.Split(':');

                    if (parts.Length >= 5)
                    {
                        var network = new NetworkInfo
                        {
                            Ssid = parts[0],
                            Security = parts[2],
                            Band = "2.4GHz" // Default, will be updated if 5GHz detected
                        };

                        // Parse signal strength
                        if (int.TryParse(parts[1], out var signal))
                        {
                            network.Signal = signal;
                        }

                        // Parse channel
                        if (int.TryParse(parts[3], out var channel))
                        {
                            // Determine band based on channel
                            network.Band = channel >= 36 ? "5GHz" : "2.4GHz";
                        }

                        // Parse frequency for more accurate band detection
                        if (double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var freq))
                        {
                            network.Band = freq >= 5000 ? "5GHz" : "2.4GHz";
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
    }
}
