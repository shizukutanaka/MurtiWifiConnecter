using System;
using Microsoft.Extensions.Options;

namespace MurtiWifiConnecter.Core.Configuration
{
    /// <summary>
    /// ネットワーク操作の設定を管理するクラス
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
        public TimeSpan PerformanceCheckInterval { get; set; } = TimeSpan.FromMinutes(5);
        public int MemoryThresholdMB { get; set; } = 100;
        public double CacheCompactionPercentage { get; set; } = 0.15;
        public int CacheSizeLimit { get; set; } = 500;
        public TimeSpan CacheExpirationScanFrequency { get; set; } = TimeSpan.FromMinutes(2);
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
        public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(30);
        public int MaxLoginAttempts { get; set; } = 5;
        public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);
    }

    /// <summary>
    /// パフォーマンス設定
    /// </summary>
    public class PerformanceConfig
    {
        public bool EnableMemoryOptimization { get; set; } = true;
        public bool EnableParallelProcessing { get; set; } = true;
        public int MaxParallelTasks { get; set; } = 4;
        public bool AdaptiveCacheEnabled { get; set; } = true;
        public bool CircuitBreakerEnabled { get; set; } = true;
        public int CircuitBreakerFailureThreshold { get; set; } = 5;
        public TimeSpan CircuitBreakerRecoveryTimeout { get; set; } = TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// 設定オプションのラッパークラス
    /// </summary>
    public class NetworkOperationsOptions
    {
        public NetworkOperationsConfig Network { get; set; } = new();
        public SecurityConfig Security { get; set; } = new();
        public PerformanceConfig Performance { get; set; } = new();
    }

    /// <summary>
    /// 設定バリデーション
    /// </summary>
    public static class ConfigurationValidator
    {
        public static bool Validate(NetworkOperationsOptions options, out List<string> errors)
        {
            errors = new List<string>();

            // Network validation
            if (options.Network.MaxRetryAttempts < 1 || options.Network.MaxRetryAttempts > 10)
                errors.Add("MaxRetryAttempts must be between 1 and 10");

            if (options.Network.RetryBaseDelayMs < 100 || options.Network.RetryBaseDelayMs > 10000)
                errors.Add("RetryBaseDelayMs must be between 100 and 10000");

            if (options.Network.MaxConcurrentOperations < 1 || options.Network.MaxConcurrentOperations > 10)
                errors.Add("MaxConcurrentOperations must be between 1 and 10");

            // Security validation
            if (options.Security.MinPasswordLength < 6 || options.Security.MinPasswordLength > 50)
                errors.Add("MinPasswordLength must be between 6 and 50");

            if (options.Security.MaxPasswordLength < options.Security.MinPasswordLength || options.Security.MaxPasswordLength > 128)
                errors.Add("MaxPasswordLength must be between MinPasswordLength and 128");

            // Performance validation
            if (options.Performance.MemoryThresholdMB < 50 || options.Performance.MemoryThresholdMB > 1000)
                errors.Add("MemoryThresholdMB must be between 50 and 1000");

            if (options.Performance.MaxParallelTasks < 1 || options.Performance.MaxParallelTasks > 16)
                errors.Add("MaxParallelTasks must be between 1 and 16");

            return errors.Count == 0;
        }
    }

    /// <summary>
    /// 設定マネージャー
    /// </summary>
    public class ConfigurationManager
    {
        private static readonly SemaphoreSlim _loadLock = new(1, 1);
        private static NetworkOperationsOptions _currentOptions;
        private static DateTime _lastLoadTime = DateTime.MinValue;
        private static readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(1);
        private readonly IOptionsMonitor<NetworkOperationsOptions> _optionsMonitor;

        public ConfigurationManager(IOptionsMonitor<NetworkOperationsOptions> optionsMonitor)
        {
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        }

        public NetworkOperationsOptions GetOptions()
        {
            // キャッシュされた設定を返す（設定の変更時は自動的に更新される）
            return _optionsMonitor.CurrentValue;
        }

        public NetworkOperationsConfig GetNetworkConfig() => GetOptions().Network;
        public SecurityConfig GetSecurityConfig() => GetOptions().Security;
        public PerformanceConfig GetPerformanceConfig() => GetOptions().Performance;
    }
}
