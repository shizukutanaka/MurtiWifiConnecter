using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MurtiWifiConnecter
{
    public class AdvancedSettings
    {
        private static readonly string SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MurtiWifiConnector", "advanced_settings.json");

        public ConnectionSettings Connection { get; set; } = new();
        public SecuritySettings Security { get; set; } = new();
        public PerformanceSettings Performance { get; set; } = new();
        public MaintenanceSettings Maintenance { get; set; } = new();

        public static AdvancedSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<AdvancedSettings>(json);
                    return settings ?? CreateDefault();
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("AdvancedSettings.Load", ex);
            }

            return CreateDefault();
        }

        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsFilePath);
                if (directory != null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("AdvancedSettings.Save", ex);
                throw;
            }
        }

        public AdvancedSettings Clone()
        {
            try
            {
                var json = JsonSerializer.Serialize(this);
                return JsonSerializer.Deserialize<AdvancedSettings>(json) ?? CreateDefault();
            }
            catch
            {
                return CreateDefault();
            }
        }

        public static AdvancedSettings CreateDefault()
        {
            return new AdvancedSettings
            {
                Connection = new ConnectionSettings
                {
                    AutoConnectEnabled = false,
                    RememberPasswords = true,
                    TimeoutSeconds = 30,
                    PreferStrongerSignal = true,
                    PreferSecureNetworks = true,
                    AvoidPublicNetworks = false,
                    MinSignalStrength = 30
                },
                Security = new SecuritySettings
                {
                    EnableSecurityAnalysis = true,
                    WarnUnsecureNetworks = true,
                    DetectHotspots = true,
                    BlockSuspiciousNetworks = false,
                    EnforceStrongPasswords = false,
                    MinPasswordLength = 12
                },
                Performance = new PerformanceSettings
                {
                    ScanIntervalSeconds = 15,
                    CacheTimeoutSeconds = 60,
                    AutoMemoryOptimization = true,
                    MemoryThresholdMB = 100,
                    PowerOptimization = false,
                    ReduceBackgroundScanning = false,
                    PowerProfile = "balanced"
                },
                Maintenance = new MaintenanceSettings
                {
                    EnableAutoMaintenance = true,
                    IntervalHours = 4,
                    EnableLogging = true,
                    LogRetentionDays = 30,
                    AutoBackupProfiles = true,
                    BackupRetentionCount = 10
                }
            };
        }
    }

    public class ConnectionSettings
    {
        public bool AutoConnectEnabled { get; set; }
        public bool RememberPasswords { get; set; }
        public int TimeoutSeconds { get; set; }
        public bool PreferStrongerSignal { get; set; }
        public bool PreferSecureNetworks { get; set; }
        public bool AvoidPublicNetworks { get; set; }
        public int MinSignalStrength { get; set; }
    }

    public class SecuritySettings
    {
        public bool EnableSecurityAnalysis { get; set; }
        public bool WarnUnsecureNetworks { get; set; }
        public bool DetectHotspots { get; set; }
        public bool BlockSuspiciousNetworks { get; set; }
        public bool EnforceStrongPasswords { get; set; }
        public int MinPasswordLength { get; set; }
    }

    public class PerformanceSettings
    {
        public int ScanIntervalSeconds { get; set; }
        public int CacheTimeoutSeconds { get; set; }
        public bool AutoMemoryOptimization { get; set; }
        public int MemoryThresholdMB { get; set; }
        public bool PowerOptimization { get; set; }
        public bool ReduceBackgroundScanning { get; set; }
        public string PowerProfile { get; set; } = "balanced";
    }

    public class MaintenanceSettings
    {
        public bool EnableAutoMaintenance { get; set; }
        public int IntervalHours { get; set; }
        public bool EnableLogging { get; set; }
        public int LogRetentionDays { get; set; }
        public bool AutoBackupProfiles { get; set; }
        public int BackupRetentionCount { get; set; }
    }
}