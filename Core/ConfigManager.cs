using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace MurtiWifiConnecter.Core
{
    public static class ConfigManager
    {
        private static readonly string AppDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MurtiWifiConnecter");
        private static readonly string ConfigPath = Path.Combine(Directory.GetCurrentDirectory(), "config.json");
        private static readonly string UserConfigPath = Path.Combine(AppDataDirectory, "user_config.json");
        private static readonly string UserConfigDigestPath = UserConfigPath + ".hmac";
        private static readonly string ConfigQuarantineDirectory = Path.Combine(AppDataDirectory, "quarantine");
        private const string ConfigIntegrityKeyName = "config_integrity";
        private const string ConfigDirectoryName = "MurtiWifiConnecter";

        private static AppConfig _config;
        private static readonly object _configLock = new();
        private static readonly MemoryCache _configCache = new(new MemoryCacheOptions { SizeLimit = 100 });
        private static readonly SemaphoreSlim _configLoadLock = new(1, 1);
        private static DateTime _lastConfigLoad = DateTime.MinValue;
        private static readonly TimeSpan ConfigCacheDuration = TimeSpan.FromSeconds(5);
        private static readonly string[] AllowedLogLevels = { "None", "Error", "Warning", "Info", "Debug" };
        private static readonly string[] AllowedSecurityTypes =
        {
            "Open", "WEP", "WPA2PSK", "WPA2Enterprise", "WPA3SAE", "WPA3Enterprise"
        };
        private static readonly string[] AllowedBillingEditions =
        {
            "Free", "Professional", "Enterprise"
        };
        private const int MaxPreferredNetworks = 100;
        private static byte[] _configIntegrityKey;

        private static readonly Dictionary<string, SettingMetadata> SettingsMetadata = new(StringComparer.OrdinalIgnoreCase)
        {
            ["autoconnect"] = new SettingMetadata(
                key: "autoconnect",
                description: "Automatically connect to preferred networks when available.",
                valueType: "Boolean",
                defaultValue: "true",
                valueAccessor: c => c.AutoConnect,
                allowedValues: "true / false"),
            ["scaninterval"] = new SettingMetadata(
                key: "scaninterval",
                description: "Interval between automatic scans in seconds.",
                valueType: "Integer",
                defaultValue: "30",
                valueAccessor: c => c.ScanInterval,
                range: "5-300"),
            ["connectiontimeout"] = new SettingMetadata(
                key: "connectiontimeout",
                description: "Timeout in seconds for WiFi connection attempts.",
                valueType: "Integer",
                defaultValue: "30",
                valueAccessor: c => c.ConnectionTimeout,
                range: "5-120"),
            ["retryattempts"] = new SettingMetadata(
                key: "retryattempts",
                description: "Number of retries for failed operations.",
                valueType: "Integer",
                defaultValue: "3",
                valueAccessor: c => c.RetryAttempts,
                range: "0-10"),
            ["enablenotifications"] = new SettingMetadata(
                key: "enablenotifications",
                description: "Display notifications for connection events.",
                valueType: "Boolean",
                defaultValue: "true",
                valueAccessor: c => c.EnableNotifications,
                allowedValues: "true / false"),
            ["cacheduration"] = new SettingMetadata(
                key: "cacheduration",
                description: "Duration in seconds to cache scan results.",
                valueType: "Integer",
                defaultValue: "30",
                valueAccessor: c => c.CacheDuration,
                range: "0-3600"),
            ["loglevel"] = new SettingMetadata(
                key: "loglevel",
                description: "Verbosity of application logging.",
                valueType: "String",
                defaultValue: "Info",
                valueAccessor: c => c.LogLevel,
                allowedValues: string.Join(", ", AllowedLogLevels)),
            ["billing.enabled"] = new SettingMetadata(
                key: "billing.enabled",
                description: "Toggle subscription billing enforcement.",
                valueType: "Boolean",
                defaultValue: "false",
                valueAccessor: c => c.Billing?.Enabled ?? false,
                allowedValues: "true / false"),
            ["billing.defaultedition"] = new SettingMetadata(
                key: "billing.defaultedition",
                description: "Edition applied when billing data unavailable.",
                valueType: "String",
                defaultValue: "Free",
                valueAccessor: c => c.Billing?.DefaultEdition ?? "Free",
                allowedValues: string.Join(", ", AllowedBillingEditions)),
            ["billing.graceperioddays"] = new SettingMetadata(
                key: "billing.graceperioddays",
                description: "Number of days to honour grace access after billing failure.",
                valueType: "Integer",
                defaultValue: "7",
                valueAccessor: c => c.Billing?.GracePeriodDays ?? 7,
                range: "0-30"),
            ["billing.cachettlseconds"] = new SettingMetadata(
                key: "billing.cachettlseconds",
                description: "Cache lifetime for billing state before refreshing from Stripe.",
                valueType: "Integer",
                defaultValue: "60",
                valueAccessor: c => c.Billing?.CacheTtlSeconds ?? 60,
                range: "30-3600"),
            ["billing.offlinetolerancehours"] = new SettingMetadata(
                key: "billing.offlinetolerancehours",
                description: "Maximum hours to trust cached billing data when Stripe unreachable.",
                valueType: "Integer",
                defaultValue: "12",
                valueAccessor: c => c.Billing?.OfflineToleranceHours ?? 12,
                range: "1-168"),
            ["defaultsecuritytype"] = new SettingMetadata(
                key: "defaultsecuritytype",
                description: "Default security type for generated profiles.",
                valueType: "String",
                defaultValue: "WPA2PSK",
                valueAccessor: c => c.DefaultSecurityType,
                allowedValues: string.Join(", ", AllowedSecurityTypes)),
            ["showsignalbars"] = new SettingMetadata(
                key: "showsignalbars",
                description: "Show signal strength as bars in status output.",
                valueType: "Boolean",
                defaultValue: "true",
                valueAccessor: c => c.ShowSignalBars,
                allowedValues: "true / false"),
            ["verboseoutput"] = new SettingMetadata(
                key: "verboseoutput",
                description: "Include additional debug information in output.",
                valueType: "Boolean",
                defaultValue: "false",
                valueAccessor: c => c.VerboseOutput,
                allowedValues: "true / false"),
            ["autocleanupinterval"] = new SettingMetadata(
                key: "autocleanupinterval",
                description: "Interval in minutes for cleaning old data.",
                valueType: "Integer",
                defaultValue: "60",
                valueAccessor: c => c.AutoCleanupInterval,
                range: "0-1440"),
            ["maxhistoryentries"] = new SettingMetadata(
                key: "maxhistoryentries",
                description: "Maximum number of history entries retained per network.",
                valueType: "Integer",
                defaultValue: "10",
                valueAccessor: c => c.MaxHistoryEntries,
                range: "0-1000"),
            ["ratelimitcommandmaxattempts"] = new SettingMetadata(
                key: "ratelimitcommandmaxattempts",
                description: "Maximum number of command executions allowed per window before throttling.",
                valueType: "Integer",
                defaultValue: "10",
                valueAccessor: c => c.RateLimitCommandMaxAttempts,
                range: "1-1000"),
            ["ratelimitcommandwindowseconds"] = new SettingMetadata(
                key: "ratelimitcommandwindowseconds",
                description: "Duration of the per-command rate limit window in seconds.",
                valueType: "Integer",
                defaultValue: "60",
                valueAccessor: c => c.RateLimitCommandWindowSeconds,
                range: "1-3600"),
            ["ratelimitglobalmaxattempts"] = new SettingMetadata(
                key: "ratelimitglobalmaxattempts",
                description: "Maximum number of command executions allowed globally per window before throttling.",
                valueType: "Integer",
                defaultValue: "200",
                valueAccessor: c => c.RateLimitGlobalMaxAttempts,
                range: "1-10000"),
            ["ratelimitglobalwindowseconds"] = new SettingMetadata(
                key: "ratelimitglobalwindowseconds",
                description: "Duration of the global rate limit window in seconds.",
                valueType: "Integer",
                defaultValue: "10",
                valueAccessor: c => c.RateLimitGlobalWindowSeconds,
                range: "1-3600")
        };

        public static async Task<AppConfig> LoadConfig()
        {
            const string cacheKey = "app_config";

            // 直接キャッシュチェック - 高速アクセス
            if (_configCache.TryGetValue(cacheKey, out AppConfig cachedConfig) &&
                DateTime.Now - _lastConfigLoad < ConfigCacheDuration)
            {
                return cachedConfig;
            }

            // シンプルなロック - 直接的で高速
            await _configLoadLock.WaitAsync();
            try
            {
                // ダブルチェック - ロック後の再確認
                if (_configCache.TryGetValue(cacheKey, out cachedConfig) &&
                    DateTime.Now - _lastConfigLoad < ConfigCacheDuration)
                {
                    return cachedConfig;
                }

                // 既存設定がある場合は直接返す
                if (_config != null) return _config;

                // 設定読み込み - 直接的でシンプル
                _config = LoadDefaultConfig();

                // ユーザー設定の直接読み込み
                if (File.Exists(UserConfigPath))
                {
                    var integrity = await VerifyUserConfigIntegrity().ConfigureAwait(false);
                    if (integrity.IsValid)
                    {
                        var userConfigJson = await File.ReadAllTextAsync(UserConfigPath).ConfigureAwait(false);
                        var userConfig = JsonSerializer.Deserialize<AppConfig>(userConfigJson, GetJsonOptions());
                        if (userConfig != null)
                        {
                            MergeConfigs(_config, userConfig);
                        }
                    }
                    else
                    {
                        await HandleInvalidUserConfigAsync(integrity.Message).ConfigureAwait(false);
                    }
                }

                // ローカル設定の直接読み込み
                if (File.Exists(ConfigPath))
                {
                    var localConfigJson = await File.ReadAllTextAsync(ConfigPath);
                    var localConfig = JsonSerializer.Deserialize<AppConfig>(localConfigJson, GetJsonOptions());
                    if (localConfig != null)
                    {
                        MergeConfigs(_config, localConfig);
                    }
                }

                // 設定正規化 - 直接適用
                NormalizeConfig(_config, "Loaded configuration corrections:");

                // 検証 - 直接チェック
                if (!TryValidateConfig(_config, out var issues))
                {
                    Console.WriteLine("Warning: Loaded configuration had validation issues. Falling back to defaults.");
                    foreach (var issue in issues)
                    {
                        Console.WriteLine($"  • {issue}");
                    }
                    _config = GetDefaultConfig();
                }

                // タイムスタンプ更新 - 直接設定
                _lastConfigLoad = DateTime.Now;

                // キャッシュ更新 - 直接設定
                var cacheOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ConfigCacheDuration,
                    Size = 1,
                    Priority = CacheItemPriority.High
                };
                _configCache.Set(cacheKey, _config, cacheOptions);

                return _config;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to load config, using defaults: {ex.Message}");
                _config = GetDefaultConfig();

                // デフォルト設定もキャッシュ
                var cacheOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ConfigCacheDuration,
                    Size = 1,
                    Priority = CacheItemPriority.High
                };
                _configCache.Set(cacheKey, _config, cacheOptions);

                return _config;
            }
            finally
            {
                _configLoadLock.Release();
            }
        }

        public static async Task<(bool IsValid, string Message)> VerifyUserConfigIntegrity()
        {
            try
            {
                if (!File.Exists(UserConfigPath))
                {
                    return (false, "User configuration file not found");
                }

                if (!File.Exists(UserConfigDigestPath))
                {
                    return (false, "Digest file missing");
                }

                if (VerifyConfigDigest(UserConfigPath, out var reason))
                {
                    return (true, "User configuration integrity verified");
                }

                return (false, string.IsNullOrEmpty(reason) ? "Integrity verification failed" : reason);
            }
            catch (Exception ex)
            {
                await Logger.LogError("User configuration integrity check error", nameof(ConfigManager), new Dictionary<string, object>
                {
                    ["path"] = UserConfigPath,
                    ["error"] = ex.Message
                }, ex).ConfigureAwait(false);

                await AuditTrail.RecordEventAsync("Configuration", "UserConfigIntegrityCheckError", new Dictionary<string, object>
                {
                    ["path"] = UserConfigPath,
                    ["error"] = ex.Message
                }, "Error").ConfigureAwait(false);

                return (false, ex.Message);
            }
        }

        public static async Task SaveUserConfig(AppConfig config)
        {
            try
            {
                NormalizeConfig(config, "Configuration adjustments before save:");

                if (!TryValidateConfig(config, out var issues))
                {
                    Console.WriteLine("Cannot save configuration due to validation issues:");
                    foreach (var issue in issues)
                    {
                        Console.WriteLine($"  • {issue}");
                    }
                    throw new ArgumentException("Configuration validation failed");
                }

                Directory.CreateDirectory(AppDataDirectory);
                await SecurityManager.EnsureSecureDirectoryAclAsync(AppDataDirectory).ConfigureAwait(false);

                await WriteUserConfigAsync(config).ConfigureAwait(false);

                lock (_configLock)
                {
                    _config = config;
                }

                RefreshConfigCache(config);

                Console.WriteLine($"Configuration saved to {UserConfigPath}");
                await AuditTrail.RecordEventAsync("Configuration", "SaveUserConfig", new Dictionary<string, object>
                {
                    ["path"] = UserConfigPath,
                    ["settings"] = config?.GetHashCode()
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await AuditTrail.RecordEventAsync("Configuration", "SaveUserConfigFailed", new Dictionary<string, object>
                {
                    ["path"] = UserConfigPath,
                    ["reason"] = ex.Message
                }, "Error").ConfigureAwait(false);
                Console.WriteLine($"Failed to save user config: {ex.Message}");
                throw new Exception($"Failed to save user config: {ex.Message}");
            }
        }

        public static async Task ResetToDefaults()
        {
            lock (_configLock)
            {
                _config = GetDefaultConfig();
            }

            try
            {
                if (File.Exists(UserConfigPath))
                {
                    File.Delete(UserConfigPath);
                    DeleteConfigDigest(UserConfigPath);
                }
                Console.WriteLine("Configuration reset to defaults");
                await AuditTrail.RecordEventAsync("Configuration", "ResetToDefaults", new Dictionary<string, object>
                {
                    ["path"] = UserConfigPath
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not delete user config file: {ex.Message}");
                await AuditTrail.RecordEventAsync("Configuration", "ResetToDefaultsWarning", new Dictionary<string, object>
                {
                    ["path"] = UserConfigPath,
                    ["reason"] = ex.Message
                }, "Warning");
            }
        }

        public static async Task<string> ExportConfig(string filePath)
        {
            var config = await LoadConfig();
            var json = JsonSerializer.Serialize(config, GetJsonOptions());
            await File.WriteAllTextAsync(filePath, json);
            await AuditTrail.RecordEventAsync("Configuration", "Export", new Dictionary<string, object>
            {
                ["path"] = filePath
            });
            return filePath;
        }

        public static async Task ImportConfig(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Config file not found: {filePath}");

            var json = await File.ReadAllTextAsync(filePath);
            var importedConfig = JsonSerializer.Deserialize<AppConfig>(json, GetJsonOptions());

            if (importedConfig == null)
                throw new Exception("Invalid configuration file format");

            NormalizeConfig(importedConfig, "Imported configuration adjustments:");

            if (!TryValidateConfig(importedConfig, out var issues))
            {
                Console.WriteLine("Imported configuration has validation issues:");
                foreach (var issue in issues)
                {
                    Console.WriteLine($"  • {issue}");
                }
                throw new ArgumentException("Imported configuration is invalid");
            }

            await SaveUserConfig(importedConfig);
            await AuditTrail.RecordEventAsync("Configuration", "Import", new Dictionary<string, object>
            {
                ["path"] = filePath
            });
        }

        public static async Task<SettingUpdateResult> UpdateSetting<T>(string key, T value)
        {
            var config = await LoadConfig();
            var normalizedKey = key?.ToLower() ?? string.Empty;
            var stringValue = value?.ToString()?.Trim();

            switch (normalizedKey)
            {
                case "autoconnect":
                    if (!TryParseBool(stringValue, out var autoConnect)) return SettingUpdateResult.Failure("Invalid boolean value");
                    config.AutoConnect = autoConnect;
                    break;
                case "scaninterval":
                    if (!TryParseInt(stringValue, 5, 300, "ScanInterval", out var scanInterval)) return SettingUpdateResult.Failure("Value must be between 5 and 300");
                    config.ScanInterval = scanInterval;
                    break;
                case "connectiontimeout":
                    if (!TryParseInt(stringValue, 5, 120, "ConnectionTimeout", out var connectionTimeout)) return SettingUpdateResult.Failure("Value must be between 5 and 120");
                    config.ConnectionTimeout = connectionTimeout;
                    break;
                case "retryattempts":
                    if (!TryParseInt(stringValue, 0, 10, "RetryAttempts", out var retryAttempts)) return SettingUpdateResult.Failure("Value must be between 0 and 10");
                    config.RetryAttempts = retryAttempts;
                    break;
                case "enablenotifications":
                    if (!TryParseBool(stringValue, out var enableNotifications)) return SettingUpdateResult.Failure("Invalid boolean value");
                    config.EnableNotifications = enableNotifications;
                    break;
                case "cacheduration":
                    if (!TryParseInt(stringValue, 0, 3600, "CacheDuration", out var cacheDuration)) return SettingUpdateResult.Failure("Value must be between 0 and 3600");
                    config.CacheDuration = cacheDuration;
                    break;
                case "loglevel":
                    if (string.IsNullOrWhiteSpace(stringValue))
                    {
                        return SettingUpdateResult.Failure("LogLevel cannot be empty");
                    }
                    if (!IsValidLogLevel(stringValue))
                    {
                        return SettingUpdateResult.Failure($"Invalid value. Allowed: {string.Join(", ", AllowedLogLevels)}");
                    }
                    config.LogLevel = NormalizeLogLevel(stringValue);
                    break;
                case "defaultsecuritytype":
                    if (string.IsNullOrWhiteSpace(stringValue))
                    {
                        return SettingUpdateResult.Failure("DefaultSecurityType cannot be empty");
                    }
                    if (!IsValidSecurityType(stringValue))
                    {
                        return SettingUpdateResult.Failure($"Invalid value. Allowed: {string.Join(", ", AllowedSecurityTypes)}");
                    }
                    config.DefaultSecurityType = NormalizeSecurityType(stringValue);
                    break;
                case "showsignalbars":
                    if (!TryParseBool(stringValue, out var showSignalBars)) return SettingUpdateResult.Failure("Invalid boolean value");
                    config.ShowSignalBars = showSignalBars;
                    break;
                case "verboseoutput":
                    if (!TryParseBool(stringValue, out var verboseOutput)) return SettingUpdateResult.Failure("Invalid boolean value");
                    config.VerboseOutput = verboseOutput;
                    break;
                case "autocleanupinterval":
                    if (!TryParseInt(stringValue, 0, 1440, "AutoCleanupInterval", out var cleanupInterval)) return SettingUpdateResult.Failure("Value must be between 0 and 1440");
                    config.AutoCleanupInterval = cleanupInterval;
                    break;
                case "maxhistoryentries":
                    if (!TryParseInt(stringValue, 0, 1000, "MaxHistoryEntries", out var maxHistoryEntries)) return SettingUpdateResult.Failure("Value must be between 0 and 1000");
                    config.MaxHistoryEntries = maxHistoryEntries;
                    break;
                default:
                    throw new ArgumentException($"Unknown setting: {key}");
            }

            try
            {
                await SaveUserConfig(config);
                var metadata = GetSettingMetadata(normalizedKey);
                var currentValue = metadata?.GetCurrentValue(config);
                await AuditTrail.RecordEventAsync("Configuration", "SettingUpdated", new Dictionary<string, object>
                {
                    ["key"] = normalizedKey,
                    ["value"] = currentValue
                });
                return SettingUpdateResult.CreateSuccess(currentValue);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save configuration: {ex.Message}");
                await AuditTrail.RecordEventAsync("Configuration", "SettingUpdateFailed", new Dictionary<string, object>
                {
                    ["key"] = normalizedKey,
                    ["reason"] = ex.Message
                }, "Error");
                return SettingUpdateResult.Failure("Failed to persist configuration");
            }
        }

        public sealed class SettingUpdateResult
        {
            private SettingUpdateResult(bool success, string message, string newValue)
            {
                Success = success;
                Message = message;
                NewValue = newValue;
            }

            public bool Success { get; }
            public string Message { get; }
            public string NewValue { get; }

            public static SettingUpdateResult CreateSuccess(string newValue) => new(true, "", newValue);
            public static SettingUpdateResult Failure(string message) => new(false, message, "");
        }

        public static async Task<T> GetSetting<T>(string key, T defaultValue = default)
        {
            var config = await LoadConfig();

            return key.ToLower() switch
            {
                "autoconnect" => (T)(object)config.AutoConnect,
                "scaninterval" => (T)(object)config.ScanInterval,
                "connectiontimeout" => (T)(object)config.ConnectionTimeout,
                "retryattempts" => (T)(object)config.RetryAttempts,
                "enablenotifications" => (T)(object)config.EnableNotifications,
                "cacheduration" => (T)(object)config.CacheDuration,
                "loglevel" => (T)(object)config.LogLevel,
                "defaultsecuritytype" => (T)(object)config.DefaultSecurityType,
                "showsignalbars" => (T)(object)config.ShowSignalBars,
                "verboseoutput" => (T)(object)config.VerboseOutput,
                "autocleanupinterval" => (T)(object)config.AutoCleanupInterval,
                "maxhistoryentries" => (T)(object)config.MaxHistoryEntries,
                _ => defaultValue
            };
        }

        public static SettingMetadata? GetSettingMetadata(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            SettingsMetadata.TryGetValue(key.Trim(), out var metadata);
            return metadata;
        }

        public static IReadOnlyCollection<SettingMetadata> GetSettingsMetadata()
        {
            return SettingsMetadata.Values.ToList();
        }

        public static async Task<IReadOnlyCollection<SettingDescriptor>> GetSettingsMetadataSnapshot(bool includeCurrentValues = true)
        {
            AppConfig? config = includeCurrentValues ? await LoadConfig() : null;

            return SettingsMetadata.Values
                .OrderBy(s => s.Key)
                .Select(meta => new SettingDescriptor
                {
                    Key = meta.Key,
                    Description = meta.Description,
                    ValueType = meta.ValueType,
                    DefaultValue = meta.DefaultValue,
                    AllowedValues = meta.AllowedValues,
                    Range = meta.Range,
                    Notes = meta.Notes,
                    CurrentValue = includeCurrentValues ? meta.GetCurrentValue(config) : string.Empty
                })
                .ToList();
        }

        public static async Task<string> GetSettingsMetadataJson(bool includeCurrentValues = true)
        {
            var snapshot = await GetSettingsMetadataSnapshot(includeCurrentValues);
            return JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        public static async Task<bool> AddPreferredNetwork(string ssid, int priority = 0)
        {
            var config = await LoadConfig();

            string validSsid;
            try
            {
                validSsid = InputValidator.EnsureValidSsid(ssid);
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"Cannot add preferred network: {ex.Message}");
                return false;
            }

            var sanitizedPriority = Math.Clamp(priority, 0, 500);

            var existing = config.PreferredNetworks.Find(p => p.Ssid.Equals(validSsid, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Ssid = validSsid;
                existing.Priority = sanitizedPriority;
                existing.LastUpdated = DateTime.Now;
            }
            else
            {
                config.PreferredNetworks.Add(new PreferredNetwork
                {
                    Ssid = validSsid,
                    Priority = sanitizedPriority,
                    LastUpdated = DateTime.Now
                });
            }

            config.PreferredNetworks.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            config.PreferredNetworks = config.PreferredNetworks
                .GroupBy(p => p.Ssid, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(p => p.Priority).ThenByDescending(p => p.LastUpdated).First())
                .OrderByDescending(p => p.Priority)
                .ThenByDescending(p => p.LastUpdated)
                .ToList();

            await SaveUserConfig(config);
            return true;
        }

        public static async Task<bool> RemovePreferredNetwork(string ssid)
        {
            var config = await LoadConfig();

            string validSsid;
            try
            {
                validSsid = InputValidator.EnsureValidSsid(ssid);
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"Cannot remove preferred network: {ex.Message}");
                return false;
            }

            var removed = config.PreferredNetworks.RemoveAll(p => p.Ssid.Equals(validSsid, StringComparison.OrdinalIgnoreCase)) > 0;
            if (!removed)
            {
                Console.WriteLine($"Preferred network '{validSsid}' not found");
                return false;
            }

            await SaveUserConfig(config);
            return true;
        }

        public static async Task<List<PreferredNetwork>> GetPreferredNetworks()
        {
            var config = await LoadConfig();
            return config.PreferredNetworks
                .OrderByDescending(p => p.Priority)
                .ThenByDescending(p => p.LastUpdated)
                .ToList();
        }

        public static async Task<bool> ClearPreferredNetworks()
        {
            var config = await LoadConfig();

            if (config.PreferredNetworks?.Count == 0)
            {
                Console.WriteLine("No preferred networks to clear");
                return false;
            }

            config.PreferredNetworks.Clear();
            await SaveUserConfig(config);
            return true;
        }

        public static async Task<AppConfig> GetConfig()
        {
            return await LoadConfig();
        }
        {
            var config = await LoadConfig();

            Console.WriteLine("Current Configuration:");
            Console.WriteLine($"  Auto Connect: {config.AutoConnect}");
            Console.WriteLine($"  Scan Interval: {config.ScanInterval}s");
            Console.WriteLine($"  Connection Timeout: {config.ConnectionTimeout}s");
            Console.WriteLine($"  Retry Attempts: {config.RetryAttempts}");
            Console.WriteLine($"  Enable Notifications: {config.EnableNotifications}");
            Console.WriteLine($"  Cache Duration: {config.CacheDuration}s");
            Console.WriteLine($"  Log Level: {config.LogLevel}");
            Console.WriteLine($"  Default Security: {config.DefaultSecurityType}");
            Console.WriteLine($"  Show Signal Bars: {config.ShowSignalBars}");
            Console.WriteLine($"  Verbose Output: {config.VerboseOutput}");
            Console.WriteLine($"  Auto Cleanup Interval: {config.AutoCleanupInterval}min");
            Console.WriteLine($"  Max History Entries: {config.MaxHistoryEntries}");

            if (config.Billing is not null)
            {
                Console.WriteLine("\nBilling Settings:");
                Console.WriteLine($"  Enabled: {config.Billing.Enabled}");
                Console.WriteLine($"  Default Edition: {config.Billing.DefaultEdition}");
                Console.WriteLine($"  Grace Period Days: {config.Billing.GracePeriodDays}");
                Console.WriteLine($"  Cache TTL Seconds: {config.Billing.CacheTtlSeconds}");
                Console.WriteLine($"  Offline Tolerance Hours: {config.Billing.OfflineToleranceHours}");
                if (config.Billing.Stripe is not null)
                {
                    Console.WriteLine("  Stripe Products:");
                    Console.WriteLine($"    Professional Product: {config.Billing.Stripe.ProductProfessional ?? string.Empty}");
                    Console.WriteLine($"    Enterprise Product: {config.Billing.Stripe.ProductEnterprise ?? string.Empty}");
                    Console.WriteLine("  Stripe Prices:");
                    Console.WriteLine($"    Professional Monthly: {config.Billing.Stripe.PriceProfessionalMonthly ?? string.Empty}");
                    Console.WriteLine($"    Enterprise Monthly: {config.Billing.Stripe.PriceEnterpriseMonthly ?? string.Empty}");
                    if (!string.IsNullOrWhiteSpace(config.Billing.Stripe.SubscriptionId))
                    {
                        Console.WriteLine($"  Subscription Id: {config.Billing.Stripe.SubscriptionId}");
                    }
                }
                if (config.Billing.Webhook is not null)
                {
                    Console.WriteLine("  Webhook Settings:");
                    Console.WriteLine($"    Listen Address: {config.Billing.Webhook.ListenAddress ?? string.Empty}");
                    if (!string.IsNullOrWhiteSpace(config.Billing.Webhook.EndpointSecret))
                    {
                        Console.WriteLine("    Endpoint Secret: (configured)");
                    }
                }
            }

            if (config.PreferredNetworks.Count > 0)
            {
                Console.WriteLine("\nPreferred Networks:");
                foreach (var network in config.PreferredNetworks)
                {
                    Console.WriteLine($"  • {network.Ssid} (Priority: {network.Priority})");
                }
            }
        }

        public static async Task<bool> ValidateConfig(AppConfig config = null)
        {
            config ??= await LoadConfig();

            if (TryValidateConfig(config, out var issues))
            {
                Console.WriteLine("Configuration is valid");
                return true;
            }

            Console.WriteLine("Configuration validation issues:");
            foreach (var issue in issues)
            {
                Console.WriteLine($"  • {issue}");
            }

            return false;
        }

        private static AppConfig LoadDefaultConfig()
        {
            if (File.Exists(ConfigPath))
            {
                try
                {
                    var json = File.ReadAllText(ConfigPath);
                    return JsonSerializer.Deserialize<AppConfig>(json, GetJsonOptions()) ?? GetDefaultConfig();
                }
                catch
                {
                    return GetDefaultConfig();
                }
            }

            return GetDefaultConfig();
        }

        private static AppConfig GetDefaultConfig()
        {
            return new AppConfig
            {
                AutoConnect = true,
                ScanInterval = 30,
                ConnectionTimeout = 30,
                RetryAttempts = 3,
                EnableNotifications = true,
                CacheDuration = 30,
                LogLevel = "Info",
                PreferredNetworks = new List<PreferredNetwork>(),
                DefaultSecurityType = "WPA2PSK",
                ShowSignalBars = true,
                VerboseOutput = false,
                AutoCleanupInterval = 60,
                MaxHistoryEntries = 100,
                Billing = new BillingSettings
                {
                    Enabled = false,
                    DefaultEdition = "Free",
                    GracePeriodDays = 7,
                    CacheTtlSeconds = 60,
                    OfflineToleranceHours = 12,
                    Stripe = new BillingStripeSettings(),
                    Webhook = new BillingWebhookSettings()
                }
            };
        }

        private static async Task SaveUserConfig(AppConfig config)
        {
            await WriteUserConfigAsync(config);
        }

        private static async Task WriteUserConfigAsync(AppConfig config)
        {
            var json = JsonSerializer.Serialize(config, GetJsonOptions());
            await File.WriteAllTextAsync(UserConfigPath, json).ConfigureAwait(false);

            var contentBytes = Encoding.UTF8.GetBytes(json);
            var digest = ComputeDigest(contentBytes);
            await File.WriteAllTextAsync(UserConfigDigestPath, Convert.ToBase64String(digest)).ConfigureAwait(false);
            await SecurityManager.EnsureSecureFileAclAsync(UserConfigPath).ConfigureAwait(false);
            await SecurityManager.EnsureSecureFileAclAsync(UserConfigDigestPath).ConfigureAwait(false);
        }

        private static void RefreshConfigCache(AppConfig config)
        {
            const string cacheKey = "app_config";

            _lastConfigLoad = DateTime.Now;

            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ConfigCacheDuration,
                Size = 1,
                Priority = CacheItemPriority.High
            };

            _configCache.Set(cacheKey, config, cacheOptions);
        }

        private static async Task HandleInvalidUserConfigAsync(string reason)
        {
            try
            {
                await Logger.LogWarning("User configuration failed integrity verification", nameof(ConfigManager), new Dictionary<string, object>
                {
                    ["path"] = UserConfigPath,
                    ["reason"] = reason
                }).ConfigureAwait(false);

                await AuditTrail.RecordEventAsync("Configuration", "UserConfigIntegrityFailure", new Dictionary<string, object>
                {
                    ["path"] = UserConfigPath,
                    ["reason"] = reason
                }, "Warning").ConfigureAwait(false);

                if (!File.Exists(UserConfigPath))
                {
                    return;
                }

                Directory.CreateDirectory(ConfigQuarantineDirectory);
                await SecurityManager.EnsureSecureDirectoryAclAsync(ConfigQuarantineDirectory).ConfigureAwait(false);

                var quarantineName = $"user_config_{DateTime.Now:yyyyMMddHHmmss}.invalid";
                var quarantinePath = Path.Combine(ConfigQuarantineDirectory, quarantineName);

                if (File.Exists(quarantinePath))
                {
                    File.Delete(quarantinePath);
                }

                File.Move(UserConfigPath, quarantinePath);
                DeleteConfigDigest(UserConfigPath);

                await AuditTrail.RecordEventAsync("Configuration", "UserConfigQuarantined", new Dictionary<string, object>
                {
                    ["path"] = UserConfigPath,
                    ["quarantinePath"] = quarantinePath,
                    ["reason"] = reason
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await Logger.LogError("Failed to quarantine invalid user configuration", nameof(ConfigManager), new Dictionary<string, object>
                {
                    ["path"] = UserConfigPath,
                    ["error"] = ex.Message
                }, ex).ConfigureAwait(false);

                await AuditTrail.RecordEventAsync("Configuration", "UserConfigIntegrityHandlingError", new Dictionary<string, object>
                {
                    ["path"] = UserConfigPath,
                    ["error"] = ex.Message
                }, "Error").ConfigureAwait(false);
            }
        }

        private static bool VerifyConfigDigest(string configPath, out string reason)
        {
            reason = null;

            var digestPath = configPath + ".hmac";
            if (!File.Exists(digestPath))
            {
                reason = "Digest file missing";
                return false;
            }

            try
            {
                var expectedText = File.ReadAllText(digestPath).Trim();
                if (string.IsNullOrEmpty(expectedText))
                {
                    reason = "Digest file empty";
                    return false;
                }

                var expected = Convert.FromBase64String(expectedText);
                var content = File.ReadAllBytes(configPath);
                var actual = ComputeDigest(content);

                if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                {
                    reason = "Digest mismatch";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        private static void DeleteConfigDigest(string configPath)
        {
            var digestPath = configPath + ".hmac";
            if (File.Exists(digestPath))
            {
                try
                {
                    File.Delete(digestPath);
                }
                catch
                {
                }
            }
        }

        private static byte[] ComputeDigest(byte[] content)
        {
            EnsureIntegrityKeyLoaded();
            using var hmac = new HMACSHA256(_configIntegrityKey);
            return hmac.ComputeHash(content);
        }

        private static void EnsureIntegrityKeyLoaded()
        {
            if (_configIntegrityKey != null)
            {
                return;
            }

            var key = SecurityManager.GetIntegrityKeyAsync(ConfigIntegrityKeyName).ConfigureAwait(false).GetAwaiter().GetResult();
            Interlocked.CompareExchange(ref _configIntegrityKey, key, null);
        }

        private static void MergeConfigs(AppConfig target, AppConfig source)
        {
            if (source.AutoConnect != target.AutoConnect) target.AutoConnect = source.AutoConnect;
            if (source.ScanInterval != target.ScanInterval) target.ScanInterval = source.ScanInterval;
            if (source.ConnectionTimeout != target.ConnectionTimeout) target.ConnectionTimeout = source.ConnectionTimeout;
            if (source.RetryAttempts != target.RetryAttempts) target.RetryAttempts = source.RetryAttempts;
            if (source.EnableNotifications != target.EnableNotifications) target.EnableNotifications = source.EnableNotifications;
            if (source.CacheDuration != target.CacheDuration) target.CacheDuration = source.CacheDuration;
            if (!string.IsNullOrEmpty(source.LogLevel)) target.LogLevel = source.LogLevel;
            if (!string.IsNullOrEmpty(source.DefaultSecurityType)) target.DefaultSecurityType = source.DefaultSecurityType;
            if (source.ShowSignalBars != target.ShowSignalBars) target.ShowSignalBars = source.ShowSignalBars;
            if (source.VerboseOutput != target.VerboseOutput) target.VerboseOutput = source.VerboseOutput;
            if (source.AutoCleanupInterval != target.AutoCleanupInterval) target.AutoCleanupInterval = source.AutoCleanupInterval;
            if (source.MaxHistoryEntries != target.MaxHistoryEntries) target.MaxHistoryEntries = source.MaxHistoryEntries;
            if (source.RateLimitCommandMaxAttempts != target.RateLimitCommandMaxAttempts) target.RateLimitCommandMaxAttempts = source.RateLimitCommandMaxAttempts;
            if (source.RateLimitCommandWindowSeconds != target.RateLimitCommandWindowSeconds) target.RateLimitCommandWindowSeconds = source.RateLimitCommandWindowSeconds;
            if (source.RateLimitGlobalMaxAttempts != target.RateLimitGlobalMaxAttempts) target.RateLimitGlobalMaxAttempts = source.RateLimitGlobalMaxAttempts;
            if (source.RateLimitGlobalWindowSeconds != target.RateLimitGlobalWindowSeconds) target.RateLimitGlobalWindowSeconds = source.RateLimitGlobalWindowSeconds;

            if (source.PreferredNetworks?.Count > 0)
            {
                target.PreferredNetworks = source.PreferredNetworks;
            }

            if (source.Billing != null)
            {
                target.Billing ??= new BillingSettings();
                if (source.Billing.Enabled != target.Billing.Enabled) target.Billing.Enabled = source.Billing.Enabled;
                if (!string.IsNullOrWhiteSpace(source.Billing.DefaultEdition)) target.Billing.DefaultEdition = source.Billing.DefaultEdition;
                if (source.Billing.GracePeriodDays != target.Billing.GracePeriodDays) target.Billing.GracePeriodDays = source.Billing.GracePeriodDays;
                if (source.Billing.CacheTtlSeconds != target.Billing.CacheTtlSeconds) target.Billing.CacheTtlSeconds = source.Billing.CacheTtlSeconds;
                if (source.Billing.OfflineToleranceHours != target.Billing.OfflineToleranceHours) target.Billing.OfflineToleranceHours = source.Billing.OfflineToleranceHours;

                if (source.Billing.Stripe != null)
                {
                    target.Billing.Stripe ??= new BillingStripeSettings();
                    if (!string.IsNullOrWhiteSpace(source.Billing.Stripe.ProductProfessional)) target.Billing.Stripe.ProductProfessional = source.Billing.Stripe.ProductProfessional;
                    if (!string.IsNullOrWhiteSpace(source.Billing.Stripe.ProductEnterprise)) target.Billing.Stripe.ProductEnterprise = source.Billing.Stripe.ProductEnterprise;
                    if (!string.IsNullOrWhiteSpace(source.Billing.Stripe.PriceProfessionalMonthly)) target.Billing.Stripe.PriceProfessionalMonthly = source.Billing.Stripe.PriceProfessionalMonthly;
                    if (!string.IsNullOrWhiteSpace(source.Billing.Stripe.PriceEnterpriseMonthly)) target.Billing.Stripe.PriceEnterpriseMonthly = source.Billing.Stripe.PriceEnterpriseMonthly;
                    if (!string.IsNullOrWhiteSpace(source.Billing.Stripe.SubscriptionId)) target.Billing.Stripe.SubscriptionId = source.Billing.Stripe.SubscriptionId;
                }

                if (source.Billing.Webhook != null)
                {
                    target.Billing.Webhook ??= new BillingWebhookSettings();
                    if (!string.IsNullOrWhiteSpace(source.Billing.Webhook.EndpointSecret)) target.Billing.Webhook.EndpointSecret = source.Billing.Webhook.EndpointSecret;
                    if (!string.IsNullOrWhiteSpace(source.Billing.Webhook.ListenAddress)) target.Billing.Webhook.ListenAddress = source.Billing.Webhook.ListenAddress;
                }
            }
        }

        private static JsonSerializerOptions GetJsonOptions()
        {
            return new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            };
        }

        private static bool TryValidateConfig(AppConfig config, out List<string> issues)
        {
            issues = new List<string>();

            if (config == null)
            {
                issues.Add("Configuration is null");
                return false;
            }

            if (config.ScanInterval < 5 || config.ScanInterval > 300)
                issues.Add("ScanInterval should be between 5 and 300 seconds");

            if (config.ConnectionTimeout < 5 || config.ConnectionTimeout > 120)
                issues.Add("ConnectionTimeout should be between 5 and 120 seconds");

            if (config.RetryAttempts < 0 || config.RetryAttempts > 10)
                issues.Add("RetryAttempts should be between 0 and 10");

            if (config.CacheDuration < 0 || config.CacheDuration > 3600)
                issues.Add("CacheDuration should be between 0 and 3600 seconds");

            if (config.AutoCleanupInterval < 0 || config.AutoCleanupInterval > 1440)
                issues.Add("AutoCleanupInterval should be between 0 and 1440 minutes");

            if (config.MaxHistoryEntries < 0 || config.MaxHistoryEntries > 1000)
                issues.Add("MaxHistoryEntries should be between 0 and 1000");

            if (config.RateLimitCommandMaxAttempts < 1 || config.RateLimitCommandMaxAttempts > 1000)
                issues.Add("RateLimitCommandMaxAttempts should be between 1 and 1000");

            if (config.RateLimitCommandWindowSeconds < 1 || config.RateLimitCommandWindowSeconds > 3600)
                issues.Add("RateLimitCommandWindowSeconds should be between 1 and 3600 seconds");

            if (config.RateLimitGlobalMaxAttempts < 1 || config.RateLimitGlobalMaxAttempts > 10000)
                issues.Add("RateLimitGlobalMaxAttempts should be between 1 and 10000");

            if (config.RateLimitGlobalWindowSeconds < 1 || config.RateLimitGlobalWindowSeconds > 3600)
                issues.Add("RateLimitGlobalWindowSeconds should be between 1 and 3600 seconds");

            if (!AllowedLogLevels.Any(level => level.Equals(config.LogLevel, StringComparison.OrdinalIgnoreCase)))
                issues.Add($"LogLevel must be one of: {string.Join(", ", AllowedLogLevels)}");

            if (!AllowedSecurityTypes.Any(type => type.Equals(config.DefaultSecurityType, StringComparison.OrdinalIgnoreCase)))
                issues.Add($"DefaultSecurityType must be one of: {string.Join(", ", AllowedSecurityTypes)}");

            if (config.Billing != null)
            {
                if (!AllowedBillingEditions.Any(e => e.Equals(config.Billing.DefaultEdition, StringComparison.OrdinalIgnoreCase)))
                {
                    issues.Add($"Billing.DefaultEdition must be one of: {string.Join(", ", AllowedBillingEditions)}");
                }

                if (config.Billing.GracePeriodDays < 0 || config.Billing.GracePeriodDays > 30)
                {
                    issues.Add("Billing.GracePeriodDays should be between 0 and 30");
                }

                if (config.Billing.CacheTtlSeconds < 30 || config.Billing.CacheTtlSeconds > 3600)
                {
                    issues.Add("Billing.CacheTtlSeconds should be between 30 and 3600 seconds");
                }

                if (config.Billing.OfflineToleranceHours < 1 || config.Billing.OfflineToleranceHours > 168)
                {
                    issues.Add("Billing.OfflineToleranceHours should be between 1 and 168 hours");
                }
            }

            if (config.PreferredNetworks != null)
            {
                foreach (var network in config.PreferredNetworks)
                {
                    if (network == null)
                    {
                        issues.Add("Preferred network entry is null");
                        continue;
                    }

                    try
                    {
                        InputValidator.EnsureValidSsid(network.Ssid);
                    }
                    catch (ArgumentException ex)
                    {
                        issues.Add($"Preferred network '{network.Ssid}' has invalid SSID: {ex.Message}");
                    }

                    if (network.Priority < 0 || network.Priority > 500)
                    {
                        issues.Add($"Preferred network '{network.Ssid}' priority must be between 0 and 500");
                    }
                }
            }

            return issues.Count == 0;
        }

        private static void NormalizeConfig(AppConfig config, string contextMessage = null)
        {
            if (config == null)
            {
                return;
            }

            var adjustments = new List<string>();

            void Clamp(ref int value, int min, int max, string name)
            {
                var original = value;
                value = Math.Clamp(value, min, max);
                if (value != original)
                {
                    adjustments.Add($"{name} adjusted from {original} to {value}");
                }
            }

            var scanInterval = config.ScanInterval;
            Clamp(ref scanInterval, 5, 300, nameof(config.ScanInterval));
            config.ScanInterval = scanInterval;

            var connectionTimeout = config.ConnectionTimeout;
            Clamp(ref connectionTimeout, 5, 120, nameof(config.ConnectionTimeout));
            config.ConnectionTimeout = connectionTimeout;

            var retryAttempts = config.RetryAttempts;
            Clamp(ref retryAttempts, 0, 10, nameof(config.RetryAttempts));
            config.RetryAttempts = retryAttempts;

            var cacheDuration = config.CacheDuration;
            Clamp(ref cacheDuration, 0, 3600, nameof(config.CacheDuration));
            config.CacheDuration = cacheDuration;

            var autoCleanupInterval = config.AutoCleanupInterval;
            Clamp(ref autoCleanupInterval, 0, 1440, nameof(config.AutoCleanupInterval));
            config.AutoCleanupInterval = autoCleanupInterval;

            var maxHistoryEntries = config.MaxHistoryEntries;
            Clamp(ref maxHistoryEntries, 0, 1000, nameof(config.MaxHistoryEntries));
            config.MaxHistoryEntries = maxHistoryEntries;

            var commandMaxAttempts = config.RateLimitCommandMaxAttempts;
            Clamp(ref commandMaxAttempts, 1, 1000, nameof(config.RateLimitCommandMaxAttempts));
            config.RateLimitCommandMaxAttempts = commandMaxAttempts;

            var commandWindowSeconds = config.RateLimitCommandWindowSeconds;
            Clamp(ref commandWindowSeconds, 1, 3600, nameof(config.RateLimitCommandWindowSeconds));
            config.RateLimitCommandWindowSeconds = commandWindowSeconds;

            var globalMaxAttempts = config.RateLimitGlobalMaxAttempts;
            Clamp(ref globalMaxAttempts, 1, 10000, nameof(config.RateLimitGlobalMaxAttempts));
            config.RateLimitGlobalMaxAttempts = globalMaxAttempts;

            var globalWindowSeconds = config.RateLimitGlobalWindowSeconds;
            Clamp(ref globalWindowSeconds, 1, 3600, nameof(config.RateLimitGlobalWindowSeconds));
            config.RateLimitGlobalWindowSeconds = globalWindowSeconds;

            if (string.IsNullOrWhiteSpace(config.LogLevel) || !IsValidLogLevel(config.LogLevel))
            {
                var original = config.LogLevel;
                config.LogLevel = "Info";
                adjustments.Add($"LogLevel reset from '{original}' to 'Info'");
            }
            else
            {
                config.LogLevel = NormalizeLogLevel(config.LogLevel);
            }

            if (string.IsNullOrWhiteSpace(config.DefaultSecurityType) || !IsValidSecurityType(config.DefaultSecurityType))
            {
                var original = config.DefaultSecurityType;
                config.DefaultSecurityType = "WPA2PSK";
                adjustments.Add($"DefaultSecurityType reset from '{original}' to 'WPA2PSK'");
            }
            else
            {
                config.DefaultSecurityType = NormalizeSecurityType(config.DefaultSecurityType);
            }

            config.Billing ??= new BillingSettings();
            config.Billing.Stripe ??= new BillingStripeSettings();
            config.Billing.Webhook ??= new BillingWebhookSettings();

            if (string.IsNullOrWhiteSpace(config.Billing.DefaultEdition) || !AllowedBillingEditions.Any(e => e.Equals(config.Billing.DefaultEdition, StringComparison.OrdinalIgnoreCase)))
            {
                var original = config.Billing.DefaultEdition;
                config.Billing.DefaultEdition = "Free";
                adjustments.Add($"Billing.DefaultEdition reset from '{original}' to 'Free'");
            }
            else
            {
                config.Billing.DefaultEdition = AllowedBillingEditions.First(e => e.Equals(config.Billing.DefaultEdition, StringComparison.OrdinalIgnoreCase));
            }

            Clamp(ref config.Billing.GracePeriodDays, 0, 30, nameof(config.Billing.GracePeriodDays));
            Clamp(ref config.Billing.CacheTtlSeconds, 30, 3600, nameof(config.Billing.CacheTtlSeconds));
            Clamp(ref config.Billing.OfflineToleranceHours, 1, 168, nameof(config.Billing.OfflineToleranceHours));

            config.Billing.Webhook.ListenAddress = string.IsNullOrWhiteSpace(config.Billing.Webhook.ListenAddress)
                ? "http://127.0.0.1:8787/stripe-webhook"
                : config.Billing.Webhook.ListenAddress.Trim();

            config.PreferredNetworks ??= new List<PreferredNetwork>();

            var sanitizedNetworks = new List<PreferredNetwork>();
            foreach (var network in config.PreferredNetworks)
            {
                if (network == null)
                {
                    adjustments.Add("Removed null preferred network entry");
                    continue;
                }

                string sanitizedSsid;
                try
                {
                    sanitizedSsid = InputValidator.EnsureValidSsid(network.Ssid);
                }
                catch (ArgumentException)
                {
                    adjustments.Add($"Removed preferred network with invalid SSID '{network.Ssid}'");
                    continue;
                }

                var sanitizedPriority = Math.Clamp(network.Priority, 0, 500);
                if (sanitizedPriority != network.Priority)
                {
                    adjustments.Add($"Preferred network '{sanitizedSsid}' priority adjusted from {network.Priority} to {sanitizedPriority}");
                }

                var lastUpdated = network.LastUpdated == default ? DateTime.Now : network.LastUpdated;
                sanitizedNetworks.Add(new PreferredNetwork
                {
                    Ssid = sanitizedSsid,
                    Priority = sanitizedPriority,
                    LastUpdated = lastUpdated
                });
            }

            config.PreferredNetworks = sanitizedNetworks
                .GroupBy(p => p.Ssid, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(p => p.Priority).ThenByDescending(p => p.LastUpdated).First())
                .OrderByDescending(p => p.Priority)
                .ThenByDescending(p => p.LastUpdated)
                .Take(MaxPreferredNetworks)
                .ToList();

            if (sanitizedNetworks.Count > MaxPreferredNetworks)
            {
                adjustments.Add($"Preferred networks trimmed to top {MaxPreferredNetworks} entries");
            }

            if (contextMessage != null && adjustments.Count > 0)
            {
                Console.WriteLine(contextMessage);
                foreach (var adjustment in adjustments)
                {
                    Console.WriteLine($"  • {adjustment}");
                }
            }
        }

        public sealed class SettingMetadata
        {
            private readonly Func<AppConfig, object> _valueAccessor;

            public SettingMetadata(string key, string description, string valueType, string defaultValue, Func<AppConfig, object> valueAccessor, string allowedValues = null, string range = null, string notes = null)
            {
                Key = key;
                Description = description;
                ValueType = valueType;
                DefaultValue = defaultValue;
                AllowedValues = allowedValues;
                Range = range;
                Notes = notes;
                _valueAccessor = valueAccessor;
            }

            public string Key { get; }
            public string Description { get; }
            public string ValueType { get; }
            public string DefaultValue { get; }
            public string AllowedValues { get; }
            public string Range { get; }
            public string Notes { get; }

            public string GetCurrentValue(AppConfig config)
            {
                if (config == null)
                {
                    return string.Empty;
                }

                var value = _valueAccessor?.Invoke(config);

                return value switch
                {
                    null => string.Empty,
                    bool b => b.ToString().ToLowerInvariant(),
                    IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                    _ => value.ToString()
                };
            }
        }

        public sealed class SettingDescriptor
        {
            public string Key { get; set; }
            public string Description { get; set; }
            public string ValueType { get; set; }
            public string DefaultValue { get; set; }
            public string AllowedValues { get; set; }
            public string Range { get; set; }
            public string Notes { get; set; }
            public string CurrentValue { get; set; }
        }

        private static bool TryParseBool(string value, out bool result)
        {
            result = false;

            if (string.IsNullOrWhiteSpace(value))
            {
                Console.WriteLine("Boolean value cannot be empty");
                return false;
            }

            if (bool.TryParse(value, out result))
                return true;

            var normalized = value.Trim().ToLowerInvariant();
            if (normalized is "1" or "yes" or "y" or "on")
            {
                result = true;
                return true;
            }

            if (normalized is "0" or "no" or "n" or "off")
            {
                result = false;
                return true;
            }

            Console.WriteLine($"Invalid boolean value '{value}'. Use true/false, yes/no, on/off, or 1/0.");
            return false;
        }

        private static bool TryParseInt(string value, int min, int max, string name, out int result)
        {
            result = default;

            if (string.IsNullOrWhiteSpace(value) || !int.TryParse(value, out result))
            {
                Console.WriteLine($"Invalid {name}. Provide an integer between {min} and {max}.");
                return false;
            }

            if (result < min || result > max)
            {
                Console.WriteLine($"Invalid {name}. Value must be between {min} and {max}.");
                return false;
            }

            return true;
        }

        private static bool IsValidLogLevel(string value)
        {
            return AllowedLogLevels.Any(level => level.Equals(value, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeLogLevel(string value)
        {
            return AllowedLogLevels.First(level => level.Equals(value, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsValidSecurityType(string value)
        {
            return AllowedSecurityTypes.Any(type => type.Equals(value, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeSecurityType(string value)
        {
            return AllowedSecurityTypes.First(type => type.Equals(value, StringComparison.OrdinalIgnoreCase));
        }

        public class AppConfig
        {
            public bool AutoConnect { get; set; } = true;
            public int ScanInterval { get; set; } = 30;
            public int ConnectionTimeout { get; set; } = 30;
            public int RetryAttempts { get; set; } = 3;
            public bool EnableNotifications { get; set; } = true;
            public int CacheDuration { get; set; } = 30;
            public string LogLevel { get; set; } = "Info";
            public List<PreferredNetwork> PreferredNetworks { get; set; } = new();
            public string DefaultSecurityType { get; set; } = "WPA2PSK";
            public bool ShowSignalBars { get; set; } = true;
            public bool VerboseOutput { get; set; } = false;
            public int AutoCleanupInterval { get; set; } = 60;
            public int MaxHistoryEntries { get; set; } = 100;
            public int RateLimitCommandMaxAttempts { get; set; } = 10;
            public int RateLimitCommandWindowSeconds { get; set; } = 60;
            public int RateLimitGlobalMaxAttempts { get; set; } = 200;
            public int RateLimitGlobalWindowSeconds { get; set; } = 10;
            public BillingSettings Billing { get; set; } = new();
        }

        public class PreferredNetwork
        {
            public string Ssid { get; set; }
            public int Priority { get; set; }
            public DateTime LastUpdated { get; set; } = DateTime.Now;
            public bool AutoConnect { get; set; } = true;
        }

        public class BillingSettings
        {
            public bool Enabled { get; set; }
            public string DefaultEdition { get; set; } = "Free";
            public int GracePeriodDays { get; set; } = 7;
            public int CacheTtlSeconds { get; set; } = 60;
            public int OfflineToleranceHours { get; set; } = 12;
            public BillingStripeSettings Stripe { get; set; } = new();
            public BillingWebhookSettings Webhook { get; set; } = new();
        }

        public class BillingStripeSettings
        {
            public string ApiKey { get; set; }
            public string ProductProfessional { get; set; }
            public string ProductEnterprise { get; set; }
            public string PriceProfessionalMonthly { get; set; }
            public string PriceEnterpriseMonthly { get; set; }
            public string SubscriptionId { get; set; }
        }

        public class BillingWebhookSettings
        {
            public string EndpointSecret { get; set; }
            public string ListenAddress { get; set; } = "http://127.0.0.1:8787/stripe-webhook";
        }

        // Extension properties for compatibility with BillingManager
        public static class AppConfigExtensions
        {
            public static string GetStripeApiKey(this AppConfig config)
            {
                return config?.Billing?.Stripe?.ApiKey;
            }

            public static string GetStripeWebhookSecret(this AppConfig config)
            {
                return config?.Billing?.Webhook?.EndpointSecret;
            }

            public static bool GetBillingEnabled(this AppConfig config)
            {
                return config?.Billing?.Enabled ?? false;
            }

            public static string GetDefaultBillingEdition(this AppConfig config)
            {
                return config?.Billing?.DefaultEdition ?? "Free";
            }
        }
    }
}