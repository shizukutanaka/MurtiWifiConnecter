namespace MurtiWifiConnecter.Constants
{
    /// <summary>
    /// アプリケーション定数
    /// </summary>
    public static class AppConstants
    {
        /// <summary>
        /// アプリケーション情報
        /// </summary>
        public static class App
        {
            public const string Name = "MurtiWifiConnecter";
            public const string DisplayName = "Murti WiFi Connector";
            public const string Description = "High-performance WiFi connection manager";
        }

        /// <summary>
        /// WiFi接続関連定数
        /// </summary>
        public static class Wifi
        {
            public const int QuickTimeoutMs = 2500;
            public const int NormalTimeoutMs = 8000;
            public const int ExtendedTimeoutMs = 12000;
            public const int ConnectionDelayMs = 400;
            public const int MaxRetryAttempts = 3;
            public const int BaseRetryDelayMs = 1000;
            public const int MaxRetryDelayMs = 15000;
            public const int NetworkResetDelayMs = 800;
            public const int ScanIntervalMs = 10000;
            public const int ConnectionCheckIntervalMs = 30000;
            public const int StartupDelayMs = 500;
        }

        /// <summary>
        /// パフォーマンス関連定数
        /// </summary>
        public static class Performance
        {
            public const int MemoryOptIntervalMinutes = 1;
            public const int SystemMonitoringIntervalMs = 60000;
            public const int CacheCleanupIntervalMinutes = 5;
            public const int MaxCacheSize = 50;
            public const int MaxLogEntries = 1000;
            public const int MaxBackupFiles = 5;
            public const int ProfileCacheValidityMinutes = 3;
            public const int MaxProfileCacheSize = 20;
        }

        /// <summary>
        /// ファイル関連定数
        /// </summary>
        public static class Files
        {
            public const string ProfilesFile = "profiles.json";
            public const string SettingsFile = "settings.json";
            public const string LogFile = "wifi.log";
            public const string StatsFile = "stats.json";
            public const string BackupExtension = ".backup";
            public const int MaxLogFileSizeMB = 5;
            public const int MaxLogFiles = 5;
            public const int LogFlushIntervalMs = 5000;
            public const int MaxProfileBackups = 3;
        }

        /// <summary>
        /// UI関連定数
        /// </summary>
        public static class UI
        {
            public const int RefreshIntervalMs = 5000;
            public const int NotificationTimeoutMs = 5000;
            public const int ToastDurationMs = 3000;
            public const int ProgressUpdateIntervalMs = 100;
            public const int AnimationDurationMs = 300;
        }

        /// <summary>
        /// ネットワーク関連定数
        /// </summary>
        public static class Network
        {
            public const int PingTimeoutMs = 5000;
            public const int HttpTimeoutMs = 10000;
            public const string DefaultTestUrl = "http://www.msftncsi.com/ncsi.txt";
            public const string BackupTestUrl = "http://clients3.google.com/generate_204";
            public const int MinSignalStrength = -90;
            public const int MaxSignalStrength = -30;
        }

        /// <summary>
        /// セキュリティ関連定数
        /// </summary>
        public static class Security
        {
            public const int MinPasswordLength = 8;
            public const int MaxPasswordLength = 63;
            public const int SaltSize = 16;
            public const int KeySize = 32;
            public const int HashIterations = 10000;
        }

        /// <summary>
        /// エラー関連定数
        /// </summary>
        public static class Errors
        {
            public const int MaxErrorRetries = 3;
            public const int ErrorCooldownMs = 1000;
            public const int CircuitBreakerThreshold = 5;
            public const int CircuitBreakerTimeoutMs = 30000;
        }
    }
}