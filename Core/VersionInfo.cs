using System;
using System.IO;
using System.Reflection;

namespace MurtiWifiConnecter.Core
{
    public static class VersionInfo
    {
        public const string Version = "2.0.0";
        public const string ProductName = "MurtiWifiConnecter Pro";
        public const string Description = "Enterprise-Grade WiFi Management Solution";
        public const string Copyright = "© 2024 MurtiWifi Technologies";
        public const string BuildConfiguration = "Release";

        public static readonly DateTime BuildDate = new DateTime(2024, 12, 27);
        public static readonly string BuildNumber = GetBuildNumber();

        private static string GetBuildNumber()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return version?.ToString() ?? "2.0.0.0";
        }

        public static void ShowVersionInfo()
        {
            var headerSeparator = new string('=', 58);
            Console.WriteLine(headerSeparator);
            Console.WriteLine("MurtiWifiConnecter Pro v2.0");
            Console.WriteLine("Enterprise WiFi Management");
            Console.WriteLine(headerSeparator);
            Console.WriteLine($"Version:        {Version}");
            Console.WriteLine($"Build:          {BuildNumber}");
            Console.WriteLine($"Build Date:     {BuildDate:yyyy-MM-dd}");
            Console.WriteLine($"Configuration:  {BuildConfiguration}");
            Console.WriteLine($"Runtime:        .NET 8.0");
            Console.WriteLine($"Platform:       {Environment.OSVersion.Platform}");
            Console.WriteLine($"Architecture:   {Environment.Is64BitProcess} (64-bit)");
            Console.WriteLine();
            Console.WriteLine("Features:");
            Console.WriteLine("  - Advanced Network Analytics");
            Console.WriteLine("  - Real-time Performance Monitoring");
            Console.WriteLine("  - Enterprise Security Management");
            Console.WriteLine("  - Automated Error Recovery");
            Console.WriteLine("  - Comprehensive Backup & Restore");
            Console.WriteLine("  - Health Reporting & Diagnostics");
            Console.WriteLine("  - Rich Terminal UI");
            Console.WriteLine("  - Command-line Automation");
            Console.WriteLine();
            Console.WriteLine($"{Copyright}");
        }

        public static void ShowShortVersion()
        {
            Console.WriteLine($"{ProductName} v{Version}");
            Console.WriteLine($"Build {BuildNumber} ({BuildDate:yyyy-MM-dd})");
            Console.WriteLine("Enterprise WiFi Management Solution");
        }

        public static string GetUserAgent()
        {
            return $"{ProductName}/{Version} (.NET 8.0; Windows NT {Environment.OSVersion.Version})";
        }

        public static bool IsProductionBuild()
        {
            return BuildConfiguration.Equals("Release", StringComparison.OrdinalIgnoreCase);
        }

        public static class Features
        {
            public const bool NetworkAnalytics = true;
            public const bool PerformanceMonitoring = true;
            public const bool SecurityManagement = true;
            public const bool ErrorRecovery = true;
            public const bool BackupRestore = true;
            public const bool HealthReporting = true;
            public const bool RichUI = true;
            public const bool Automation = true;
            public const bool EnterpriseFeatures = true;
            public const bool RealtimeMonitoring = true;
        }

        public static class Limits
        {
            public const int MaxConcurrentConnections = 1;
            public const int MaxHistoryDays = 30;
            public const int MaxMetricsCount = 1000;
            public const int MaxErrorLogEntries = 100;
            public const int MaxBackupSizeMB = 100;
            public const int DefaultTimeoutSeconds = 30;
            public const int MaxRetryAttempts = 3;
        }

        public static class Paths
        {
            public static readonly string AppDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MurtiWifiConnecter");

            public static readonly string ConfigPath = Path.Combine(AppDataPath, "config.json");
            public static readonly string LogsPath = Path.Combine(AppDataPath, "Logs");
            public static readonly string MetricsPath = Path.Combine(AppDataPath, "Metrics");
            public static readonly string BackupsPath = Path.Combine(AppDataPath, "Backups");
            public static readonly string SecurePath = Path.Combine(AppDataPath, "Secure");
            public static readonly string TempPath = Path.Combine(Path.GetTempPath(), "MurtiWifiConnecter");
        }
    }
}