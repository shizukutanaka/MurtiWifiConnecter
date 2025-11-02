using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MurtiWifiConnecter.Core.Configuration
{
    /// <summary>
    /// ネットワーク操作の設定
    /// </summary>
    public class NetworkOperationsConfig
    {
        public TimeSpan ScanCacheDuration { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(10);
        public int MaxRetryAttempts { get; set; } = 3;
        public int RetryBaseDelayMs { get; set; } = 1000;
        public double RetryBackoffMultiplier { get; set; } = 1.5;
        public int MaxRetryDelaySeconds { get; set; } = 6;
        public int RateLimitWindowSeconds { get; set; } = 60;
        public int RateLimitMaxAttempts { get; set; } = 10;
        public bool EnableParallelScanning { get; set; } = true;
        public int MaxConcurrentOperations { get; set; } = 3;
        public bool EnableEnhancedLogging { get; set; } = false;
        public string LogLevel { get; set; } = "Information";
    }

    /// <summary>
    /// セキュリティ設定
    /// </summary>
    public class SecurityConfig
    {
        public bool EnableEnhancedValidation { get; set; } = true;
        public bool EnableAuditLogging { get; set; } = true;
        public int CredentialRotationDays { get; set; } = 90;
        public int MinPasswordLength { get; set; } = 8;
        public int MaxPasswordLength { get; set; } = 63;
        public bool EnableQuantumResistantEncryption { get; set; } = false;
        public bool RequireStrongPasswords { get; set; } = true;
        public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(30);
        public int MaxLoginAttempts { get; set; } = 5;
        public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);
    }

    /// <summary>
    /// パフォーマンス設定
    /// </summary>
    public class PerformanceConfig
    {
        public double CacheCompactionPercentage { get; set; } = 0.15;
        public int MemoryThresholdMB { get; set; } = 100;
        public TimeSpan PerformanceCheckInterval { get; set; } = TimeSpan.FromMinutes(5);
        public bool EnableMemoryOptimization { get; set; } = true;
        public int MaxCacheEntries { get; set; } = 500;
        public TimeSpan CacheExpirationScanFrequency { get; set; } = TimeSpan.FromMinutes(2);
        public bool EnableParallelProcessing { get; set; } = true;
        public int MaxParallelTasks { get; set; } = 4;
    }

    /// <summary>
    /// アプリケーション全体の設定
    /// </summary>
    public class AppConfig
    {
        public NetworkOperationsConfig Network { get; set; } = new();
        public SecurityConfig Security { get; set; } = new();
        public PerformanceConfig Performance { get; set; } = new();
        public LoggingConfig Logging { get; set; } = new();
        public FeatureFlags Features { get; set; } = new();

        public static AppConfig Default => new();

        public static async Task<AppConfig> LoadAsync(string configPath = null)
        {
            configPath ??= GetDefaultConfigPath();

            if (!File.Exists(configPath))
            {
                // デフォルト設定をファイルに保存
                var defaultConfig = Default;
                await SaveAsync(configPath, defaultConfig);
                return defaultConfig;
            }

            try
            {
                var json = await File.ReadAllTextAsync(configPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });

                return config ?? Default;
            }
            catch (Exception ex)
            {
                // 設定ファイルの読み込みに失敗した場合はデフォルト設定を使用
                Console.WriteLine($"Warning: Failed to load configuration from {configPath}: {ex.Message}");
                return Default;
            }
        }

        public static async Task SaveAsync(string configPath, AppConfig config)
        {
            try
            {
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });

                var directory = Path.GetDirectoryName(configPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(configPath, json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save configuration to {configPath}", ex);
            }
        }

        private static string GetDefaultConfigPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MurtiWifiConnecter",
                "appsettings.json");
        }
    }

    /// <summary>
    /// ログ設定
    /// </summary>
    public class LoggingConfig
    {
        public string LogLevel { get; set; } = "Information";
        public string LogFormat { get; set; } = "Structured";
        public bool EnableConsoleLogging { get; set; } = true;
        public bool EnableFileLogging { get; set; } = true;
        public string LogDirectory { get; set; } = "Logs";
        public int MaxLogFiles { get; set; } = 10;
        public int MaxLogFileSizeMB { get; set; } = 10;
        public bool LogSensitiveData { get; set; } = false;
    }

    /// <summary>
    /// 機能フラグ
    /// </summary>
    public class FeatureFlags
    {
        public bool EnableExperimentalFeatures { get; set; } = false;
        public bool EnableDiagnostics { get; set; } = true;
        public bool EnableMetrics { get; set; } = true;
        public bool EnableAutoUpdate { get; set; } = false;
        public bool EnableCloudBackup { get; set; } = false;
        public bool EnableRemoteManagement { get; set; } = false;
    }

    /// <summary>
    /// 設定マネージャー
    /// </summary>
    public class ConfigurationManager
    {
        private static readonly SemaphoreSlim _loadLock = new(1, 1);
        private static AppConfig _currentConfig;
        private static DateTime _lastLoadTime = DateTime.MinValue;
        private static readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(1);
        private readonly ILogger<ConfigurationManager> _logger;
        private readonly string _configPath;

        public ConfigurationManager(ILogger<ConfigurationManager> logger, string configPath = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configPath = configPath ?? GetDefaultConfigPath();
        }

        public async Task<AppConfig> GetConfigAsync()
        {
            await _loadLock.WaitAsync();

            try
            {
                if (_currentConfig != null && DateTime.UtcNow - _lastLoadTime < _cacheDuration)
                {
                    return _currentConfig;
                }

                _currentConfig = await AppConfig.LoadAsync(_configPath);
                _lastLoadTime = DateTime.UtcNow;

                _logger.LogInformation("Configuration loaded successfully");
                return _currentConfig;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load configuration, using defaults");
                _currentConfig = AppConfig.Default;
                return _currentConfig;
            }
            finally
            {
                _loadLock.Release();
            }
        }

        public async Task UpdateConfigAsync(Action<AppConfig> updateAction)
        {
            var config = await GetConfigAsync();
            var originalConfig = JsonSerializer.Serialize(config);

            updateAction(config);

            // 設定の検証
            var validationResult = ValidateConfig(config);
            if (!validationResult.IsValid)
            {
                throw new ArgumentException($"Invalid configuration: {validationResult.Message}");
            }

            await AppConfig.SaveAsync(_configPath, config);

            _logger.LogInformation("Configuration updated", new
            {
                OriginalConfig = originalConfig,
                UpdatedConfig = JsonSerializer.Serialize(config)
            });
        }

        private ValidationResult ValidateConfig(AppConfig config)
        {
            var errors = new List<string>();

            if (config.Network.MaxRetryAttempts < 1 || config.Network.MaxRetryAttempts > 10)
                errors.Add("MaxRetryAttempts must be between 1 and 10");

            if (config.Network.RetryBaseDelayMs < 100 || config.Network.RetryBaseDelayMs > 10000)
                errors.Add("RetryBaseDelayMs must be between 100 and 10000");

            if (config.Security.MinPasswordLength < 6 || config.Security.MinPasswordLength > 50)
                errors.Add("MinPasswordLength must be between 6 and 50");

            if (config.Security.MaxPasswordLength < config.Security.MinPasswordLength || config.Security.MaxPasswordLength > 128)
                errors.Add("MaxPasswordLength must be between MinPasswordLength and 128");

            if (config.Performance.MemoryThresholdMB < 50 || config.Performance.MemoryThresholdMB > 1000)
                errors.Add("MemoryThresholdMB must be between 50 and 1000");

            if (errors.Count > 0)
            {
                return ValidationResult.Failure(string.Join("; ", errors));
            }

            return ValidationResult.Success();
        }

        private static string GetDefaultConfigPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MurtiWifiConnecter",
                "appsettings.json");
        }
    }
}
