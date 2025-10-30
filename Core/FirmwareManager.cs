using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// Firmware update management system for WiFi adapters and network devices
    /// </summary>
    public static class FirmwareManager
    {
        private static readonly string FirmwareDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MurtiWifiConnecter", "FirmwareData");

        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly object _updateLock = new();
        private static bool _isInitialized = false;

        // Firmware update sources
        private static readonly Dictionary<string, FirmwareUpdateSource> _updateSources = new(StringComparer.OrdinalIgnoreCase);

        // Update history
        private static readonly List<FirmwareUpdateRecord> _updateHistory = new();

        public static async Task InitializeAsync()
        {
            if (_isInitialized) return;

            try
            {
                Directory.CreateDirectory(FirmwareDataPath);

                // Initialize firmware update sources
                await InitializeUpdateSourcesAsync();

                // Load update history
                await LoadUpdateHistoryAsync();

                await Logger.LogInfo("Firmware manager initialized", nameof(FirmwareManager));
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Failed to initialize firmware manager");
            }
        }

        private static async Task InitializeUpdateSourcesAsync()
        {
            // Add common firmware update sources
            _updateSources["WindowsUpdate"] = new FirmwareUpdateSource
            {
                Name = "Windows Update",
                Type = UpdateSourceType.WindowsUpdate,
                Enabled = true,
                CheckInterval = TimeSpan.FromHours(24),
                LastChecked = DateTime.MinValue
            };

            _updateSources["Intel"] = new FirmwareUpdateSource
            {
                Name = "Intel Wireless Drivers",
                Type = UpdateSourceType.Manufacturer,
                Enabled = true,
                CheckInterval = TimeSpan.FromDays(7),
                LastChecked = DateTime.MinValue,
                Url = "https://www.intel.com/content/www/us/en/download-center/home.html"
            };

            _updateSources["Broadcom"] = new FirmwareUpdateSource
            {
                Name = "Broadcom Wireless",
                Type = UpdateSourceType.Manufacturer,
                Enabled = true,
                CheckInterval = TimeSpan.FromDays(7),
                LastChecked = DateTime.MinValue,
                Url = "https://www.broadcom.com/support/download-search"
            };

            _updateSources["Qualcomm"] = new FirmwareUpdateSource
            {
                Name = "Qualcomm Atheros",
                Type = UpdateSourceType.Manufacturer,
                Enabled = true,
                CheckInterval = TimeSpan.FromDays(7),
                LastChecked = DateTime.MinValue,
                Url = "https://www.qualcomm.com/support"
            };

            // Load custom sources from configuration
            await LoadCustomSourcesAsync();
        }

        private static async Task LoadCustomSourcesAsync()
        {
            try
            {
                var configFile = Path.Combine(FirmwareDataPath, "custom_sources.json");
                if (!File.Exists(configFile)) return;

                var json = await File.ReadAllTextAsync(configFile);
                var customSources = JsonSerializer.Deserialize<Dictionary<string, FirmwareUpdateSource>>(json);

                if (customSources != null)
                {
                    foreach (var kvp in customSources)
                    {
                        _updateSources[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Failed to load custom firmware sources");
            }
        }

        private static async Task LoadUpdateHistoryAsync()
        {
            try
            {
                var historyFile = Path.Combine(FirmwareDataPath, "update_history.json");
                if (!File.Exists(historyFile)) return;

                var json = await File.ReadAllTextAsync(historyFile);
                var history = JsonSerializer.Deserialize<List<FirmwareUpdateRecord>>(json);

                if (history != null)
                {
                    _updateHistory.AddRange(history);
                }
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Failed to load firmware update history");
            }
        }

        private static async Task SaveUpdateHistoryAsync()
        {
            try
            {
                var historyFile = Path.Combine(FirmwareDataPath, "update_history.json");
                var json = JsonSerializer.Serialize(_updateHistory, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(historyFile, json);
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Failed to save firmware update history");
            }
        }

        public static async Task<FirmwareScanResult> ScanForUpdatesAsync()
        {
            var result = new FirmwareScanResult
            {
                ScanStartTime = DateTime.Now,
                Devices = new List<FirmwareDeviceInfo>(),
                AvailableUpdates = new List<FirmwareUpdateInfo>()
            };

            try
            {
                // Get all network adapters
                var adapters = await GetNetworkAdaptersAsync();

                foreach (var adapter in adapters)
                {
                    var deviceInfo = new FirmwareDeviceInfo
                    {
                        DeviceId = adapter.DeviceId,
                        Name = adapter.Name,
                        Manufacturer = adapter.Manufacturer,
                        DriverVersion = adapter.DriverVersion,
                        FirmwareVersion = await GetFirmwareVersionAsync(adapter),
                        HardwareId = adapter.HardwareId,
                        LastChecked = DateTime.Now
                    };

                    result.Devices.Add(deviceInfo);

                    // Check for updates from all sources
                    var updates = await CheckForDeviceUpdatesAsync(deviceInfo);
                    result.AvailableUpdates.AddRange(updates);
                }

                result.ScanEndTime = DateTime.Now;
                result.Success = true;

                await Logger.LogInfo("Firmware scan completed", nameof(FirmwareManager),
                    new Dictionary<string, object>
                    {
                        ["devicesFound"] = result.Devices.Count,
                        ["updatesFound"] = result.AvailableUpdates.Count
                    });
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
                await ErrorHandler.LogError(ex, "Firmware scan failed");
            }

            return result;
        }

        private static async Task<List<NetworkAdapterInfo>> GetNetworkAdaptersAsync()
        {
            var adapters = new List<NetworkAdapterInfo>();

            try
            {
                // Use WMI to get network adapter information
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_NetworkAdapter WHERE NetConnectionStatus IS NOT NULL"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var adapter = new NetworkAdapterInfo
                        {
                            DeviceId = obj["DeviceID"]?.ToString(),
                            Name = obj["Name"]?.ToString(),
                            Manufacturer = obj["Manufacturer"]?.ToString(),
                            DriverVersion = obj["DriverVersion"]?.ToString(),
                            HardwareId = obj["HardwareID"] != null ?
                                string.Join(",", (string[])obj["HardwareID"]) : null
                        };

                        adapters.Add(adapter);
                    }
                }
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Failed to enumerate network adapters");
            }

            return adapters;
        }

        private static async Task<string> GetFirmwareVersionAsync(NetworkAdapterInfo adapter)
        {
            try
            {
                // Try to get firmware version using netsh
                if (adapter.Name?.Contains("Wireless", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var output = await ExecuteNetshCommandAsync($"wlan show drivers");
                    var lines = output.Split('\n');

                    foreach (var line in lines)
                    {
                        if (line.Contains("Firmware version", StringComparison.OrdinalIgnoreCase))
                        {
                            return line.Split(':').Last().Trim();
                        }
                    }
                }
            }
            catch
            {
                // Firmware version may not be available
            }

            return "Unknown";
        }

        private static async Task<List<FirmwareUpdateInfo>> CheckForDeviceUpdatesAsync(FirmwareDeviceInfo device)
        {
            var updates = new List<FirmwareUpdateInfo>();

            foreach (var source in _updateSources.Values.Where(s => s.Enabled))
            {
                try
                {
                    // Check if it's time to check this source
                    if (DateTime.Now - source.LastChecked < source.CheckInterval)
                        continue;

                    var sourceUpdates = await CheckSourceForUpdatesAsync(source, device);
                    updates.AddRange(sourceUpdates);

                    source.LastChecked = DateTime.Now;
                }
                catch (Exception ex)
                {
                    await ErrorHandler.LogError(ex, $"Failed to check updates from {source.Name}");
                }
            }

            return updates;
        }

        private static async Task<List<FirmwareUpdateInfo>> CheckSourceForUpdatesAsync(FirmwareUpdateSource source, FirmwareDeviceInfo device)
        {
            var updates = new List<FirmwareUpdateInfo>();

            switch (source.Type)
            {
                case UpdateSourceType.WindowsUpdate:
                    updates.AddRange(await CheckWindowsUpdateAsync(device));
                    break;

                case UpdateSourceType.Manufacturer:
                    updates.AddRange(await CheckManufacturerUpdateAsync(source, device));
                    break;

                case UpdateSourceType.Custom:
                    updates.AddRange(await CheckCustomUpdateAsync(source, device));
                    break;
            }

            return updates;
        }

        private static async Task<List<FirmwareUpdateInfo>> CheckWindowsUpdateAsync(FirmwareDeviceInfo device)
        {
            var updates = new List<FirmwareUpdateInfo>();

            try
            {
                // Use Windows Update API to check for driver/firmware updates
                // This is a simplified implementation
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell",
                        Arguments = "Get-WindowsUpdate -Category 'Drivers' -IsDownloaded $false",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                // Parse output for relevant updates
                if (output.Contains(device.Name ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    updates.Add(new FirmwareUpdateInfo
                    {
                        DeviceId = device.DeviceId,
                        DeviceName = device.Name,
                        CurrentVersion = device.FirmwareVersion,
                        NewVersion = "Available via Windows Update",
                        Source = "Windows Update",
                        Severity = UpdateSeverity.Recommended,
                        DownloadUrl = null,
                        ReleaseNotes = "Check Windows Update for details",
                        DetectedAt = DateTime.Now
                    });
                }
            }
            catch
            {
                // Windows Update check may fail
            }

            return updates;
        }

        private static async Task<List<FirmwareUpdateInfo>> CheckManufacturerUpdateAsync(FirmwareUpdateSource source, FirmwareDeviceInfo device)
        {
            var updates = new List<FirmwareUpdateInfo>();

            // This would normally check manufacturer websites
            // For demonstration, we'll create a placeholder update if the firmware version is old
            if (device.FirmwareVersion != "Unknown" &&
                device.Manufacturer?.Contains(source.Name.Split(' ')[0], StringComparison.OrdinalIgnoreCase) == true)
            {
                updates.Add(new FirmwareUpdateInfo
                {
                    DeviceId = device.DeviceId,
                    DeviceName = device.Name,
                    CurrentVersion = device.FirmwareVersion,
                    NewVersion = "Check manufacturer website",
                    Source = source.Name,
                    Severity = UpdateSeverity.Optional,
                    DownloadUrl = source.Url,
                    ReleaseNotes = $"Visit {source.Url} for the latest firmware updates",
                    DetectedAt = DateTime.Now
                });
            }

            return updates;
        }

        private static async Task<List<FirmwareUpdateInfo>> CheckCustomUpdateAsync(FirmwareUpdateSource source, FirmwareDeviceInfo device)
        {
            var updates = new List<FirmwareUpdateInfo>();

            // Custom update sources would be implemented here
            // This could include internal repositories, third-party services, etc.

            return updates;
        }

        public static async Task<FirmwareUpdateResult> ApplyUpdateAsync(FirmwareUpdateInfo update, bool automatic = false)
        {
            var result = new FirmwareUpdateResult
            {
                UpdateInfo = update,
                StartedAt = DateTime.Now,
                Automatic = automatic
            };

            lock (_updateLock)
            {
                try
                {
                    // Create backup before update
                    result.BackupCreated = CreateFirmwareBackup(update.DeviceId);

                    // Apply the update based on source
                    switch (update.Source.ToLowerInvariant())
                    {
                        case "windows update":
                            result.Success = ApplyWindowsUpdate(update);
                            break;

                        default:
                            // For manufacturer updates, provide instructions
                            result.Success = false;
                            result.Error = "Manual update required. Visit manufacturer website.";
                            break;
                    }

                    result.CompletedAt = DateTime.Now;

                    // Record the update attempt
                    var record = new FirmwareUpdateRecord
                    {
                        DeviceId = update.DeviceId,
                        DeviceName = update.DeviceName,
                        PreviousVersion = update.CurrentVersion,
                        NewVersion = update.NewVersion,
                        Source = update.Source,
                        Automatic = automatic,
                        Success = result.Success,
                        Error = result.Error,
                        Timestamp = DateTime.Now
                    };

                    _updateHistory.Add(record);
                    Task.Run(() => SaveUpdateHistoryAsync());

                    if (result.Success)
                    {
                        await Logger.LogInfo("Firmware update applied successfully", nameof(FirmwareManager),
                            new Dictionary<string, object>
                            {
                                ["device"] = update.DeviceName,
                                ["version"] = update.NewVersion
                            });
                    }
                    else
                    {
                        await Logger.LogWarning("Firmware update failed", nameof(FirmwareManager),
                            new Dictionary<string, object>
                            {
                                ["device"] = update.DeviceName,
                                ["error"] = result.Error
                            });
                    }
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Error = ex.Message;
                    result.CompletedAt = DateTime.Now;

                    await ErrorHandler.LogError(ex, "Firmware update failed");
                }
            }

            return result;
        }

        private static bool CreateFirmwareBackup(string deviceId)
        {
            try
            {
                // Create a backup of current firmware settings
                // This would typically involve saving registry settings, driver files, etc.
                var backupPath = Path.Combine(FirmwareDataPath, "Backups", $"{deviceId}_{DateTime.Now:yyyyMMddHHmmss}");
                Directory.CreateDirectory(backupPath);

                // Save relevant configuration
                // This is a placeholder - actual implementation would depend on the device
                File.WriteAllText(Path.Combine(backupPath, "backup_info.txt"),
                    $"Firmware backup for device {deviceId} created at {DateTime.Now}");

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool ApplyWindowsUpdate(FirmwareUpdateInfo update)
        {
            try
            {
                // Trigger Windows Update for the specific device
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell",
                        Arguments = "Install-WindowsUpdate -Category 'Drivers' -AcceptAll -AutoReboot",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit(300000); // Wait up to 5 minutes

                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<FirmwareReport> GenerateReportAsync(TimeSpan period)
        {
            var cutoff = DateTime.Now - period;

            var report = new FirmwareReport
            {
                GeneratedAt = DateTime.Now,
                Period = period,
                UpdateHistory = _updateHistory.Where(h => h.Timestamp >= cutoff).ToList(),
                ActiveSources = _updateSources.Values.Where(s => s.Enabled).ToList(),
                Recommendations = new List<string>()
            };

            // Generate recommendations
            report.Recommendations = GenerateFirmwareRecommendations(report);

            return report;
        }

        private static List<string> GenerateFirmwareRecommendations(FirmwareReport report)
        {
            var recommendations = new List<string>();

            // Check update frequency
            var recentUpdates = report.UpdateHistory.Where(h => h.Timestamp > DateTime.Now.AddDays(-30)).ToList();
            if (recentUpdates.Count == 0)
            {
                recommendations.Add("ファームウェア更新が30日以上行われていません。セキュリティ更新を確認してください。");
            }

            // Check for failed updates
            var failedUpdates = report.UpdateHistory.Where(h => !h.Success).ToList();
            if (failedUpdates.Count > 0)
            {
                recommendations.Add($"{failedUpdates.Count}件のファームウェア更新が失敗しています。手動での更新を検討してください。");
            }

            // Check update sources
            if (!report.ActiveSources.Any())
            {
                recommendations.Add("有効なファームウェア更新ソースが設定されていません。");
            }

            // Check for outdated devices
            var oldUpdates = report.UpdateHistory.Where(h => h.Timestamp < DateTime.Now.AddMonths(-6)).ToList();
            if (oldUpdates.Count > report.UpdateHistory.Count * 0.7)
            {
                recommendations.Add("多くのデバイスのファームウェアが古くなっています。定期的な更新スケジュールを設定してください。");
            }

            if (!recommendations.Any())
            {
                recommendations.Add("ファームウェア管理状態は良好です。継続的な監視を推奨します。");
            }

            return recommendations;
        }

        public static async Task<FirmwareStatistics> GetStatisticsAsync()
        {
            return new FirmwareStatistics
            {
                TotalUpdatesChecked = _updateHistory.Count,
                SuccessfulUpdates = _updateHistory.Count(h => h.Success),
                FailedUpdates = _updateHistory.Count(h => !h.Success),
                LastScanTime = _updateHistory.Any() ? _updateHistory.Max(h => h.Timestamp) : DateTime.MinValue,
                ActiveSources = _updateSources.Count(s => s.Enabled),
                PendingUpdates = 0 // This would be calculated from scan results
            };
        }

        private static async Task<string> ExecuteNetshCommandAsync(string arguments)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                return output;
            }
            catch
            {
                return string.Empty;
            }
        }

        // Data structures
        public class FirmwareScanResult
        {
            public DateTime ScanStartTime { get; set; }
            public DateTime ScanEndTime { get; set; }
            public bool Success { get; set; }
            public string Error { get; set; }
            public List<FirmwareDeviceInfo> Devices { get; set; } = new();
            public List<FirmwareUpdateInfo> AvailableUpdates { get; set; } = new();
        }

        public class FirmwareDeviceInfo
        {
            public string DeviceId { get; set; }
            public string Name { get; set; }
            public string Manufacturer { get; set; }
            public string DriverVersion { get; set; }
            public string FirmwareVersion { get; set; }
            public string HardwareId { get; set; }
            public DateTime LastChecked { get; set; }
        }

        public class FirmwareUpdateInfo
        {
            public string DeviceId { get; set; }
            public string DeviceName { get; set; }
            public string CurrentVersion { get; set; }
            public string NewVersion { get; set; }
            public string Source { get; set; }
            public UpdateSeverity Severity { get; set; }
            public string DownloadUrl { get; set; }
            public string ReleaseNotes { get; set; }
            public DateTime DetectedAt { get; set; }
        }

        public enum UpdateSeverity
        {
            Critical,
            Important,
            Recommended,
            Optional
        }

        public class FirmwareUpdateSource
        {
            public string Name { get; set; }
            public UpdateSourceType Type { get; set; }
            public bool Enabled { get; set; }
            public TimeSpan CheckInterval { get; set; }
            public DateTime LastChecked { get; set; }
            public string Url { get; set; }
        }

        public enum UpdateSourceType
        {
            WindowsUpdate,
            Manufacturer,
            Custom
        }

        public class FirmwareUpdateRecord
        {
            public string DeviceId { get; set; }
            public string DeviceName { get; set; }
            public string PreviousVersion { get; set; }
            public string NewVersion { get; set; }
            public string Source { get; set; }
            public bool Automatic { get; set; }
            public bool Success { get; set; }
            public string Error { get; set; }
            public DateTime Timestamp { get; set; }
        }

        public class FirmwareUpdateResult
        {
            public FirmwareUpdateInfo UpdateInfo { get; set; }
            public bool Success { get; set; }
            public string Error { get; set; }
            public DateTime StartedAt { get; set; }
            public DateTime CompletedAt { get; set; }
            public bool BackupCreated { get; set; }
            public bool Automatic { get; set; }
        }

        public class FirmwareReport
        {
            public DateTime GeneratedAt { get; set; }
            public TimeSpan Period { get; set; }
            public List<FirmwareUpdateRecord> UpdateHistory { get; set; } = new();
            public List<FirmwareUpdateSource> ActiveSources { get; set; } = new();
            public List<string> Recommendations { get; set; } = new();
        }

        public class FirmwareStatistics
        {
            public int TotalUpdatesChecked { get; set; }
            public int SuccessfulUpdates { get; set; }
            public int FailedUpdates { get; set; }
            public DateTime LastScanTime { get; set; }
            public int ActiveSources { get; set; }
            public int PendingUpdates { get; set; }
        }

        private class NetworkAdapterInfo
        {
            public string DeviceId { get; set; }
            public string Name { get; set; }
            public string Manufacturer { get; set; }
            public string DriverVersion { get; set; }
            public string HardwareId { get; set; }
        }
    }
}
