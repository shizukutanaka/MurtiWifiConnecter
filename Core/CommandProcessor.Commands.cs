using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    public static partial class CommandProcessor
    {
        private sealed record CommandHandlerMetadata(
            string CanonicalName,
            Func<string[], Task<int>> Handler,
            int[]? SensitiveArgumentIndexes = null);

        private static readonly Dictionary<string, CommandHandlerMetadata> CommandMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // Connection commands
            { "connect", new CommandHandlerMetadata("connect", ExecuteConnect, new[] { 2 }) },
            { "c", new CommandHandlerMetadata("connect", ExecuteConnect, new[] { 2 }) },
            { "disconnect", new CommandHandlerMetadata("disconnect", ExecuteDisconnect) },
            { "d", new CommandHandlerMetadata("disconnect", ExecuteDisconnect) },
            { "quick", new CommandHandlerMetadata("quick", ExecuteQuickConnect, new[] { 2 }) },
            { "q", new CommandHandlerMetadata("quick", ExecuteQuickConnect, new[] { 2 }) },

            // Status commands
            { "status", new CommandHandlerMetadata("status", ExecuteStatus) },
            { "s", new CommandHandlerMetadata("status", ExecuteStatus) },
            { "info", new CommandHandlerMetadata("info", ExecuteInfo) },
            { "i", new CommandHandlerMetadata("info", ExecuteInfo) },

            // Network discovery
            { "scan", new CommandHandlerMetadata("scan", ExecuteScan) },
            { "available", new CommandHandlerMetadata("available", ExecuteAvailable) },
            { "a", new CommandHandlerMetadata("available", ExecuteAvailable) },
            { "profiles", new CommandHandlerMetadata("profiles", ExecuteProfiles) },
            { "p", new CommandHandlerMetadata("profiles", ExecuteProfiles) },
            { "adapters", new CommandHandlerMetadata("adapters", ExecuteAdapters) },
            { "adapter", new CommandHandlerMetadata("adapters", ExecuteAdapters) },

            // Management commands
            { "delete", new CommandHandlerMetadata("delete", ExecuteDelete) },
            { "forget", new CommandHandlerMetadata("delete", ExecuteDelete) },
            { "reset", new CommandHandlerMetadata("reset", ExecuteReset) },
            { "r", new CommandHandlerMetadata("reset", ExecuteReset) },
            { "maintenance", new CommandHandlerMetadata("maintenance", ExecuteMaintenance) },
            { "log-purge", new CommandHandlerMetadata("log-purge", ExecuteLogPurge) },
            { "purge-logs", new CommandHandlerMetadata("log-purge", ExecuteLogPurge) },
            { "log-stats", new CommandHandlerMetadata("log-stats", ExecuteLogStats) },
            { "logstats", new CommandHandlerMetadata("log-stats", ExecuteLogStats) },
            { "memory-snapshot", new CommandHandlerMetadata("memory-snapshot", ExecuteMemorySnapshot) },
            { "mem", new CommandHandlerMetadata("memory-snapshot", ExecuteMemorySnapshot) },
            { "history", new CommandHandlerMetadata("history", ExecuteHistory) },
            { "recent", new CommandHandlerMetadata("history", ExecuteHistory) },
            { "history-clear", new CommandHandlerMetadata("history-clear", ExecuteHistoryClear) },
            { "recent-clear", new CommandHandlerMetadata("history-clear", ExecuteHistoryClear) },
            { "history-top", new CommandHandlerMetadata("history-top", ExecuteHistoryTop) },
            { "recent-top", new CommandHandlerMetadata("history-top", ExecuteHistoryTop) },
            { "backup-path-check", new CommandHandlerMetadata("backup-path-check", ExecuteBackupPathCheck) },
            { "backup-path", new CommandHandlerMetadata("backup-path-check", ExecuteBackupPathCheck) },
            { "backup-list", new CommandHandlerMetadata("backup-list", ExecuteBackupList) },
            { "backups", new CommandHandlerMetadata("backup-list", ExecuteBackupList) },

            // Enterprise commands
            { "health", new CommandHandlerMetadata("health", ExecuteHealth) },
            { "prefer-remove", new CommandHandlerMetadata("prefer-remove", ExecutePreferRemove) },
            { "unprefer", new CommandHandlerMetadata("prefer-remove", ExecutePreferRemove) },
            { "prefer-list", new CommandHandlerMetadata("prefer-list", ExecutePreferList) },
            { "preferences", new CommandHandlerMetadata("prefer-list", ExecutePreferList) },
            { "prefer-clear", new CommandHandlerMetadata("prefer-clear", ExecutePreferClear) },
            { "compliance", new CommandHandlerMetadata("compliance", ExecuteCompliance) },
            { "report", new CommandHandlerMetadata("report", ExecuteReport) },
            { "audit-trail", new CommandHandlerMetadata("audit-trail", ExecuteAuditTrail) },
            { "security-scan", new CommandHandlerMetadata("security-scan", ExecuteSecurityScan) },
            { "performance", new CommandHandlerMetadata("performance", ExecutePerformance) },
            { "command-anomalies", new CommandHandlerMetadata("command-anomalies", ExecuteCommandAnomalies) },
            { "cmd-anomalies", new CommandHandlerMetadata("command-anomalies", ExecuteCommandAnomalies) },
            { "command-metrics", new CommandHandlerMetadata("command-metrics", ExecuteCommandMetrics) },
            { "cmd-metrics", new CommandHandlerMetadata("command-metrics", ExecuteCommandMetrics) },

            // Diagnostic commands
            { "diag", new CommandHandlerMetadata("diag", ExecuteDiagnostics) },
            { "test", new CommandHandlerMetadata("test", ExecuteTest) },
            { "speed", new CommandHandlerMetadata("speed", ExecuteSpeedTest) },
            { "stability", new CommandHandlerMetadata("stability", ExecuteStability) },
            { "stab", new CommandHandlerMetadata("stability", ExecuteStability) },
            { "signal", new CommandHandlerMetadata("signal", ExecuteSignalMonitor) },
            { "channels", new CommandHandlerMetadata("channels", ExecuteChannelAnalysis) },
            { "channel", new CommandHandlerMetadata("channels", ExecuteChannelAnalysis) },
            { "dns", new CommandHandlerMetadata("dns", ExecuteDnsOptimization) },
            { "dns-test", new CommandHandlerMetadata("dns", ExecuteDnsOptimization) },
            { "info", new CommandHandlerMetadata("info", ExecuteNetworkInfo) },
            { "network-info", new CommandHandlerMetadata("info", ExecuteNetworkInfo) },
            { "security-scan", new CommandHandlerMetadata("security-scan", ExecuteSecurityScan) },
            { "secscan", new CommandHandlerMetadata("security-scan", ExecuteSecurityScan) },
            { "troubleshoot", new CommandHandlerMetadata("troubleshoot", ExecuteTroubleshootingWizard) },
            { "wizard", new CommandHandlerMetadata("troubleshoot", ExecuteTroubleshootingWizard) },
            { "fix", new CommandHandlerMetadata("troubleshoot", ExecuteTroubleshootingWizard) },
            { "usage", new CommandHandlerMetadata("usage", ExecuteUsageStats) },
            { "stats", new CommandHandlerMetadata("usage", ExecuteUsageStats) },
            { "usage-stats", new CommandHandlerMetadata("usage", ExecuteUsageStats) },
            { "roaming", new CommandHandlerMetadata("roaming", ExecuteRoamingOptimization) },
            { "roam", new CommandHandlerMetadata("roaming", ExecuteRoamingOptimization) },
            { "adapter", new CommandHandlerMetadata("adapter", ExecuteAdapterCapabilities) },
            { "adapters", new CommandHandlerMetadata("adapter", ExecuteAdapterCapabilities) },
            { "overview", new CommandHandlerMetadata("overview", ExecuteHelpOverview) },
            { "help", new CommandHandlerMetadata("overview", ExecuteHelpOverview) },
            { "analytics", new CommandHandlerMetadata("analytics", ExecuteAnalytics) },
            { "trends", new CommandHandlerMetadata("trends", ExecuteTrends) },
            { "security", new CommandHandlerMetadata("security", ExecuteSecurity) },
            { "security-audit", new CommandHandlerMetadata("security-audit", ExecuteSecurityAudit) },
            { "security-metrics", new CommandHandlerMetadata("security-metrics", ExecuteSecurityMetrics) },
            { "metrics", new CommandHandlerMetadata("security-metrics", ExecuteSecurityMetrics) },
            { "backup", new CommandHandlerMetadata("backup", ExecuteBackup) },
            { "restore", new CommandHandlerMetadata("restore", ExecuteRestore) },
            { "backup-list", new CommandHandlerMetadata("backup-list", ExecuteBackupList) },
            { "backup-permissions", new CommandHandlerMetadata("backup-permissions", ExecuteBackupPermissions) },
            { "backup-perms", new CommandHandlerMetadata("backup-permissions", ExecuteBackupPermissions) },
            { "backup-digest-verify", new CommandHandlerMetadata("backup-digest-verify", ExecuteBackupDigestVerify) },
            { "backup-digest", new CommandHandlerMetadata("backup-digest-verify", ExecuteBackupDigestVerify) },
            { "backup-cleanup", new CommandHandlerMetadata("backup-cleanup", ExecuteBackupCleanup) },
            { "backup-prune", new CommandHandlerMetadata("backup-cleanup", ExecuteBackupCleanup) },
            // 新機能コマンド
            { "wifi-analyze", new CommandHandlerMetadata("wifi-analyze", ExecuteWifiAnalyze) },
            { "analyze", new CommandHandlerMetadata("wifi-analyze", ExecuteWifiAnalyze) },
            { "speed-history", new CommandHandlerMetadata("speed-history", ExecuteSpeedHistory) },
            { "speed-stats", new CommandHandlerMetadata("speed-stats", ExecuteSpeedStats) },
            { "report-connection", new CommandHandlerMetadata("report-connection", ExecuteConnectionReport) },
            { "report-security", new CommandHandlerMetadata("report-security", ExecuteSecurityReport) },
            { "report-performance", new CommandHandlerMetadata("report-performance", ExecutePerformanceReport) },
            { "continuous-auth", new CommandHandlerMetadata("continuous-auth", ExecuteContinuousAuth) },

            // Advanced scanning commands
            { "detailed", new CommandHandlerMetadata("detailed", ExecuteDetailedScan) },
            { "monitor", new CommandHandlerMetadata("monitor", ExecuteMonitor) },
            { "predict", new CommandHandlerMetadata("predict", ExecutePredict) },
            { "compare", new CommandHandlerMetadata("compare", ExecuteCompare) },

            // Monitoring commands
            { "realtime", new CommandHandlerMetadata("realtime", ExecuteRealtime) },
            { "monitor-start", new CommandHandlerMetadata("monitor-start", ExecuteMonitorStart) },
            { "monitor-stop", new CommandHandlerMetadata("monitor-stop", ExecuteMonitorStop) },
            { "alerts", new CommandHandlerMetadata("alerts", ExecuteAlerts) },
            { "automation", new CommandHandlerMetadata("automation", ExecuteAutomation) },

            // Utility commands
            { "help", new CommandHandlerMetadata("help", ExecuteHelp) },
            { "h", new CommandHandlerMetadata("help", ExecuteHelp) },
            { "version", new CommandHandlerMetadata("version", ExecuteVersion) },
            { "v", new CommandHandlerMetadata("version", ExecuteVersion) },
            { "clear", new CommandHandlerMetadata("clear", ExecuteClear) },
            { "cls", new CommandHandlerMetadata("clear", ExecuteClear) },
            { "exit", new CommandHandlerMetadata("exit", ExecuteExit) },
            { "quit", new CommandHandlerMetadata("exit", ExecuteExit) },

            // Network isolation commands
            { "isolation", new CommandHandlerMetadata("isolation", ExecuteIsolation) },
            { "isolate", new CommandHandlerMetadata("isolation", ExecuteIsolation) },
            { "network-classify", new CommandHandlerMetadata("network-classify", ExecuteNetworkClassify) },
            { "classify", new CommandHandlerMetadata("network-classify", ExecuteNetworkClassify) },
            { "isolation-recommendations", new CommandHandlerMetadata("isolation-recommendations", ExecuteIsolationRecommendations) },
            { "isolation-recs", new CommandHandlerMetadata("isolation-recommendations", ExecuteIsolationRecommendations) },
            { "isolation-validate", new CommandHandlerMetadata("isolation-validate", ExecuteIsolationValidate) },
            { "bandwidth", new CommandHandlerMetadata("bandwidth", ExecuteBandwidth) },
            { "bandwidth-monitor", new CommandHandlerMetadata("bandwidth-monitor", ExecuteBandwidthMonitor) },
            { "bandwidth-report", new CommandHandlerMetadata("bandwidth-report", ExecuteBandwidthReport) },
            { "bandwidth-stats", new CommandHandlerMetadata("bandwidth-stats", ExecuteBandwidthStats) },
            { "hardware", new CommandHandlerMetadata("hardware", ExecuteHardware) },
            { "hardware-monitor", new CommandHandlerMetadata("hardware-monitor", ExecuteHardwareMonitor) },
            { "hardware-report", new CommandHandlerMetadata("hardware-report", ExecuteHardwareReport) },
            { "hardware-stats", new CommandHandlerMetadata("hardware-stats", ExecuteHardwareStats) },
            { "firmware", new CommandHandlerMetadata("firmware", ExecuteFirmware) },
            { "firmware-scan", new CommandHandlerMetadata("firmware-scan", ExecuteFirmwareScan) },
            { "firmware-update", new CommandHandlerMetadata("firmware-update", ExecuteFirmwareUpdate) },
            { "firmware-report", new CommandHandlerMetadata("firmware-report", ExecuteFirmwareReport) },
            { "firmware-stats", new CommandHandlerMetadata("firmware-stats", ExecuteFirmwareStats) },
            { "ai-optimize", new CommandHandlerMetadata("ai-optimize", ExecuteAIOptimize) },
            { "ai-report", new CommandHandlerMetadata("ai-report", ExecuteAIReport) },
            { "predict", new CommandHandlerMetadata("predict", ExecutePredict) },
        };

        private static readonly IReadOnlyList<string> CommandNames = CommandMap.Keys
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
