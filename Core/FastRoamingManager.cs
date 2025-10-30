using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// Fast Roaming Manager implementing 802.11r/k/v/u standards
    /// Based on 2025 best practices for seamless handoff and reduced latency
    /// </summary>
    public class FastRoamingManager
    {
        private static FastRoamingManager? _instance;
        private static readonly object _lock = new object();
        private readonly Dictionary<string, RoamingConfiguration> _roamingConfigs = new();
        private readonly Dictionary<string, List<NeighborAP>> _neighborReports = new();
        private readonly List<RoamingEvent> _roamingHistory = new();

        public static FastRoamingManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new FastRoamingManager();
                    }
                }
                return _instance;
            }
        }

        private FastRoamingManager() { }

        public async Task InitializeAsync()
        {
            await Logger.LogInfo("Fast Roaming Manager initialized", "FastRoamingManager", new Dictionary<string, object>
            {
                ["supports_80211r"] = await Supports80211r(),
                ["supports_80211k"] = await Supports80211k(),
                ["supports_80211v"] = await Supports80211v(),
                ["supports_80211u"] = await Supports80211u()
            });
        }

        /// <summary>
        /// Enable 802.11r Fast BSS Transition for rapid roaming
        /// Eliminates need to re-authenticate to RADIUS server on each AP
        /// </summary>
        public async Task<bool> Enable80211rAsync(string ssid, bool adaptiveMode = true)
        {
            try
            {
                await Logger.LogInfo($"Enabling 802.11r Fast BSS Transition for {ssid}", "FastRoamingManager");

                if (!await Supports80211r())
                {
                    await Logger.LogWarning("802.11r not supported on this adapter", "FastRoamingManager");
                    return false;
                }

                // Configure Fast BSS Transition (FT)
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Enable FT for WPA2/WPA3 Enterprise
                    await ExecuteNetshCommand($"wlan set profileparameter name=\"{ssid}\" FastRoaming=enabled");

                    if (adaptiveMode)
                    {
                        // Use adaptive FT (switches between Over-the-Air and Over-the-DS)
                        await ExecuteNetshCommand($"wlan set profileparameter name=\"{ssid}\" FTMode=adaptive");
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    // Linux NetworkManager configuration
                    await ExecuteLinuxCommand($"nmcli connection modify \"{ssid}\" 802-11-wireless-security.pmf 2");
                    await ExecuteLinuxCommand($"nmcli connection modify \"{ssid}\" 802-11-wireless.powersave 2");
                }

                var config = GetOrCreateRoamingConfig(ssid);
                config.FT80211rEnabled = true;
                config.LastUpdated = DateTime.UtcNow;

                await Logger.LogInfo($"802.11r enabled for {ssid}", "FastRoamingManager", new Dictionary<string, object>
                {
                    ["adaptive_mode"] = adaptiveMode
                });

                return true;
            }
            catch (Exception ex)
            {
                await Logger.LogError($"Failed to enable 802.11r for {ssid}", "FastRoamingManager", ex);
                return false;
            }
        }

        /// <summary>
        /// Enable 802.11k Radio Measurement for efficient AP discovery
        /// Allows clients to request neighbor reports to limit scanning
        /// </summary>
        public async Task<bool> Enable80211kAsync(string ssid)
        {
            try
            {
                await Logger.LogInfo($"Enabling 802.11k Radio Measurement for {ssid}", "FastRoamingManager");

                if (!await Supports80211k())
                {
                    await Logger.LogWarning("802.11k not supported on this adapter", "FastRoamingManager");
                    return false;
                }

                // Enable Radio Resource Measurement
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    await ExecuteNetshCommand($"wlan set profileparameter name=\"{ssid}\" RadioMeasurement=enabled");

                    // Configure neighbor report requests
                    await ExecuteNetshCommand($"wlan set profileparameter name=\"{ssid}\" NeighborReports=enabled");
                }

                var config = GetOrCreateRoamingConfig(ssid);
                config.RM80211kEnabled = true;
                config.LastUpdated = DateTime.UtcNow;

                // Request initial neighbor report
                await RequestNeighborReport(ssid);

                await Logger.LogInfo($"802.11k enabled for {ssid}", "FastRoamingManager");
                return true;
            }
            catch (Exception ex)
            {
                await Logger.LogError($"Failed to enable 802.11k for {ssid}", "FastRoamingManager", ex);
                return false;
            }
        }

        /// <summary>
        /// Enable 802.11v Wireless Network Management for optimized roaming decisions
        /// Helps identify optimal wireless access points for roaming
        /// </summary>
        public async Task<bool> Enable80211vAsync(string ssid)
        {
            try
            {
                await Logger.LogInfo($"Enabling 802.11v Wireless Network Management for {ssid}", "FastRoamingManager");

                if (!await Supports80211v())
                {
                    await Logger.LogWarning("802.11v not supported on this adapter", "FastRoamingManager");
                    return false;
                }

                // Enable BSS Transition Management
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    await ExecuteNetshCommand($"wlan set profileparameter name=\"{ssid}\" BSSTransition=enabled");

                    // Enable DMS (Directed Multicast Service)
                    await ExecuteNetshCommand($"wlan set profileparameter name=\"{ssid}\" DMS=enabled");
                }

                var config = GetOrCreateRoamingConfig(ssid);
                config.BTM80211vEnabled = true;
                config.LastUpdated = DateTime.UtcNow;

                await Logger.LogInfo($"802.11v enabled for {ssid}", "FastRoamingManager");
                return true;
            }
            catch (Exception ex)
            {
                await Logger.LogError($"Failed to enable 802.11v for {ssid}", "FastRoamingManager", ex);
                return false;
            }
        }

        /// <summary>
        /// Enable 802.11u Interworking (Hotspot 2.0/Passpoint)
        /// Powers seamless roaming across WiFi networks with automatic authentication
        /// </summary>
        public async Task<bool> Enable80211uAsync(string ssid, Hotspot20Config config)
        {
            try
            {
                await Logger.LogInfo($"Enabling 802.11u Hotspot 2.0 for {ssid}", "FastRoamingManager");

                if (!await Supports80211u())
                {
                    await Logger.LogWarning("802.11u not supported on this adapter", "FastRoamingManager");
                    return false;
                }

                // Configure Hotspot 2.0 (Passpoint)
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    await ExecuteNetshCommand($"wlan set profileparameter name=\"{ssid}\" Hotspot20=enabled");

                    // Configure realm and domain
                    if (!string.IsNullOrEmpty(config.Realm))
                    {
                        await ExecuteNetshCommand($"wlan set profileparameter name=\"{ssid}\" Realm=\"{config.Realm}\"");
                    }
                }

                var roamingConfig = GetOrCreateRoamingConfig(ssid);
                roamingConfig.Hotspot2080211uEnabled = true;
                roamingConfig.Hotspot20Config = config;
                roamingConfig.LastUpdated = DateTime.UtcNow;

                await Logger.LogInfo($"802.11u Hotspot 2.0 enabled for {ssid}", "FastRoamingManager", new Dictionary<string, object>
                {
                    ["realm"] = config.Realm ?? "none"
                });

                return true;
            }
            catch (Exception ex)
            {
                await Logger.LogError($"Failed to enable 802.11u for {ssid}", "FastRoamingManager", ex);
                return false;
            }
        }

        /// <summary>
        /// Configure comprehensive roaming thresholds based on device type
        /// Mac: -75dBm, iOS: -70dBm (based on Apple research)
        /// </summary>
        public async Task<bool> ConfigureRoamingThresholdsAsync(string ssid, RoamingThresholds thresholds)
        {
            try
            {
                await Logger.LogInfo($"Configuring roaming thresholds for {ssid}", "FastRoamingManager", new Dictionary<string, object>
                {
                    ["rssi_threshold"] = thresholds.RSSIThreshold,
                    ["scan_threshold"] = thresholds.ScanThreshold
                });

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Set RSSI roaming trigger
                    await ExecuteNetshCommand($"wlan set profileparameter name=\"{ssid}\" RoamTrigger=\"{thresholds.RSSIThreshold}\"");

                    // Set scan threshold
                    await ExecuteNetshCommand($"wlan set profileparameter name=\"{ssid}\" ScanThreshold=\"{thresholds.ScanThreshold}\"");

                    // Configure roaming delta
                    await ExecuteNetshCommand($"wlan set profileparameter name=\"{ssid}\" RoamDelta=\"{thresholds.RoamDelta}\"");
                }

                var config = GetOrCreateRoamingConfig(ssid);
                config.Thresholds = thresholds;
                config.LastUpdated = DateTime.UtcNow;

                return true;
            }
            catch (Exception ex)
            {
                await Logger.LogError($"Failed to configure roaming thresholds for {ssid}", "FastRoamingManager", ex);
                return false;
            }
        }

        /// <summary>
        /// Request neighbor AP report (802.11k)
        /// Reduces roaming latency by providing candidate APs
        /// </summary>
        private async Task RequestNeighborReport(string ssid)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var processInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = $"wlan show networks mode=bssid",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = System.Diagnostics.Process.Start(processInfo);
                    if (process != null)
                    {
                        var output = await process.StandardOutput.ReadToEndAsync();
                        await process.WaitForExitAsync();

                        // Parse neighbor APs from output
                        var neighbors = ParseNeighborAPs(output, ssid);
                        _neighborReports[ssid] = neighbors;

                        await Logger.LogInfo($"Neighbor report received for {ssid}", "FastRoamingManager", new Dictionary<string, object>
                        {
                            ["neighbor_count"] = neighbors.Count
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                await Logger.LogWarning($"Failed to request neighbor report: {ex.Message}", "FastRoamingManager");
            }
        }

        private List<NeighborAP> ParseNeighborAPs(string output, string ssid)
        {
            var neighbors = new List<NeighborAP>();

            // Parse netsh output to extract neighbor APs
            // This is a simplified parser - production code would be more robust
            var lines = output.Split('\n');

            foreach (var line in lines)
            {
                if (line.Contains("BSSID") && line.Contains(":"))
                {
                    var parts = line.Split(':');
                    if (parts.Length >= 2)
                    {
                        var bssid = parts[1].Trim();
                        neighbors.Add(new NeighborAP
                        {
                            BSSID = bssid,
                            SSID = ssid,
                            LastSeen = DateTime.UtcNow
                        });
                    }
                }
            }

            return neighbors;
        }

        /// <summary>
        /// Monitor roaming events and collect analytics
        /// </summary>
        public async Task RecordRoamingEvent(string ssid, string fromBSSID, string toBSSID, TimeSpan roamDuration)
        {
            var roamEvent = new RoamingEvent
            {
                SSID = ssid,
                FromBSSID = fromBSSID,
                ToBSSID = toBSSID,
                RoamDuration = roamDuration,
                Timestamp = DateTime.UtcNow
            };

            _roamingHistory.Add(roamEvent);

            await Logger.LogInfo($"Roaming event recorded for {ssid}", "FastRoamingManager", new Dictionary<string, object>
            {
                ["from_bssid"] = fromBSSID,
                ["to_bssid"] = toBSSID,
                ["duration_ms"] = roamDuration.TotalMilliseconds
            });
        }

        public RoamingStatistics GetRoamingStatistics(string ssid)
        {
            var events = _roamingHistory.Where(e => e.SSID == ssid).ToList();

            if (!events.Any())
            {
                return new RoamingStatistics { SSID = ssid };
            }

            return new RoamingStatistics
            {
                SSID = ssid,
                TotalRoamingEvents = events.Count,
                AverageRoamDuration = TimeSpan.FromMilliseconds(events.Average(e => e.RoamDuration.TotalMilliseconds)),
                MinRoamDuration = events.Min(e => e.RoamDuration),
                MaxRoamDuration = events.Max(e => e.RoamDuration)
            };
        }

        private RoamingConfiguration GetOrCreateRoamingConfig(string ssid)
        {
            if (!_roamingConfigs.ContainsKey(ssid))
            {
                _roamingConfigs[ssid] = new RoamingConfiguration { SSID = ssid };
            }
            return _roamingConfigs[ssid];
        }

        private async Task ExecuteNetshCommand(string command)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new PlatformNotSupportedException("Netsh commands only supported on Windows");
            }

            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(processInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                if (process.ExitCode != 0)
                {
                    var error = await process.StandardError.ReadToEndAsync();
                    throw new InvalidOperationException($"Netsh command failed: {error}");
                }
            }
        }

        private async Task ExecuteLinuxCommand(string command)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                throw new PlatformNotSupportedException("Linux commands only supported on Linux");
            }

            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(processInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
            }
        }

        // Capability detection methods
        private async Task<bool> Supports80211r() => await Task.FromResult(false); // Placeholder
        private async Task<bool> Supports80211k() => await Task.FromResult(false); // Placeholder
        private async Task<bool> Supports80211v() => await Task.FromResult(false); // Placeholder
        private async Task<bool> Supports80211u() => await Task.FromResult(false); // Placeholder
    }

    public class RoamingConfiguration
    {
        public string SSID { get; set; } = string.Empty;
        public bool FT80211rEnabled { get; set; }
        public bool RM80211kEnabled { get; set; }
        public bool BTM80211vEnabled { get; set; }
        public bool Hotspot2080211uEnabled { get; set; }
        public RoamingThresholds? Thresholds { get; set; }
        public Hotspot20Config? Hotspot20Config { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public class RoamingThresholds
    {
        public int RSSIThreshold { get; set; } = -75; // Mac default
        public int ScanThreshold { get; set; } = -70; // iOS default
        public int RoamDelta { get; set; } = 5; // Signal difference to trigger roam
    }

    public class Hotspot20Config
    {
        public string? Realm { get; set; }
        public string? DomainName { get; set; }
        public List<string> RoamingConsortiums { get; set; } = new();
        public bool AutoConnect { get; set; } = true;
    }

    public class NeighborAP
    {
        public string BSSID { get; set; } = string.Empty;
        public string SSID { get; set; } = string.Empty;
        public int SignalStrength { get; set; }
        public int Channel { get; set; }
        public DateTime LastSeen { get; set; }
    }

    public class RoamingEvent
    {
        public string SSID { get; set; } = string.Empty;
        public string FromBSSID { get; set; } = string.Empty;
        public string ToBSSID { get; set; } = string.Empty;
        public TimeSpan RoamDuration { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class RoamingStatistics
    {
        public string SSID { get; set; } = string.Empty;
        public int TotalRoamingEvents { get; set; }
        public TimeSpan AverageRoamDuration { get; set; }
        public TimeSpan MinRoamDuration { get; set; }
        public TimeSpan MaxRoamDuration { get; set; }
    }
}
