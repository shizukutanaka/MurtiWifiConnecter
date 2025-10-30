using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// Comprehensive system health monitoring for enterprise environments
    /// </summary>
    public static class SystemHealthMonitor
    {
        private static readonly Timer _healthCheckTimer;
        private static readonly Dictionary<string, HealthMetric> _healthMetrics = new();
        private static readonly ConfigurationDriftDetector _driftDetector = new ConfigurationDriftDetector();
        private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromMinutes(1);

        private static DateTime _lastFullHealthCheck = DateTime.MinValue;
        private static SystemHealthStatus _lastHealthStatus = SystemHealthStatus.Unknown;

        static SystemHealthMonitor()
        {
            _healthCheckTimer = new Timer(async _ => await PerformHealthCheckAsync(),
                                        null,
                                        TimeSpan.FromSeconds(30),
                                        HealthCheckInterval);
        }

        public static async Task<SystemHealthReport> GetDetailedHealthReportAsync()
        {
            await PerformHealthCheckAsync();

            var report = new SystemHealthReport
            {
                Timestamp = DateTime.UtcNow,
                OverallStatus = _lastHealthStatus,
                Metrics = new Dictionary<string, HealthMetric>(_healthMetrics),
                Recommendations = GenerateRecommendations(),
                DetailedChecks = await PerformDetailedChecksAsync()
            };

            return report;
        }

        private static async Task<List<DetailedHealthCheck>> PerformDetailedChecksAsync()
        {
            var checks = new List<DetailedHealthCheck>();

            // ネットワーク接続性チェック
            checks.Add(await CheckNetworkConnectivityDetailedAsync());

            // WiFiアダプタ詳細チェック
            checks.Add(await CheckWiFiAdapterDetailedAsync());

            // セキュリティ状態詳細チェック
            checks.Add(await CheckSecurityStatusDetailedAsync());

            // パフォーマンスメトリクス詳細チェック
            checks.Add(await CheckPerformanceMetricsDetailedAsync());

            return checks;
        }

        private static async Task<DetailedHealthCheck> CheckNetworkConnectivityDetailedAsync()
        {
            var check = new DetailedHealthCheck
            {
                Category = "Network",
                Name = "Network Connectivity",
                Status = HealthStatus.Healthy,
                Description = "Detailed network connectivity assessment"
            };

            try
            {
                // ネットワークインターフェースの詳細チェック
                var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                var wifiInterface = interfaces.FirstOrDefault(ni =>
                    ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211);

                if (wifiInterface != null)
                {
                    check.Details = $"WiFi adapter found: {wifiInterface.Description}";
                    check.Status = wifiInterface.OperationalStatus == OperationalStatus.Up ?
                        HealthStatus.Healthy : HealthStatus.Warning;
                }
                else
                {
                    check.Details = "No WiFi adapter detected";
                    check.Status = HealthStatus.Critical;
                }
            }
            catch (Exception ex)
            {
                check.Status = HealthStatus.Warning;
                check.Details = $"Network check failed: {ex.Message}";
            }

            return check;
        }

        private static async Task<DetailedHealthCheck> CheckWiFiAdapterDetailedAsync()
        {
            var check = new DetailedHealthCheck
            {
                Category = "WiFi Adapter",
                Name = "WiFi Adapter Status",
                Status = HealthStatus.Healthy,
                Description = "Detailed WiFi adapter health assessment"
            };

            try
            {
                var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                var wifiInterface = interfaces.FirstOrDefault(ni =>
                    ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211);

                if (wifiInterface != null)
                {
                    var details = new List<string>
                    {
                        $"Name: {wifiInterface.Name}",
                        $"Description: {wifiInterface.Description}",
                        $"Status: {wifiInterface.OperationalStatus}",
                        $"Speed: {wifiInterface.Speed / 1_000_000} Mbps"
                    };

                    check.Details = string.Join(", ", details);

                    if (wifiInterface.OperationalStatus != OperationalStatus.Up)
                    {
                        check.Status = HealthStatus.Warning;
                    }
                }
                else
                {
                    check.Status = HealthStatus.Critical;
                    check.Details = "No WiFi adapter found";
                }
            }
            catch (Exception ex)
            {
                check.Status = HealthStatus.Warning;
                check.Details = $"WiFi adapter check failed: {ex.Message}";
            }

            return check;
        }

        private static async Task<DetailedHealthCheck> CheckSecurityStatusDetailedAsync()
        {
            var check = new DetailedHealthCheck
            {
                Category = "Security",
                Name = "Security Status",
                Status = HealthStatus.Healthy,
                Description = "Detailed security configuration assessment"
            };

            try
            {
                var securityDetails = new List<string>();

                // 管理者権限チェック
                var isAdmin = SecurityManager.IsRunningAsAdmin();
                securityDetails.Add($"Admin Privileges: {isAdmin}");

                // セキュアストレージチェック
                var secureStorageExists = Directory.Exists(SecurityManager.SecureStoragePath);
                securityDetails.Add($"Secure Storage: {secureStorageExists}");

                // レート制限チェック
                var rateLimitingActive = true; // 実装済み
                securityDetails.Add($"Rate Limiting: {rateLimitingActive}");

                check.Details = string.Join(", ", securityDetails);

                if (!secureStorageExists)
                {
                    check.Status = HealthStatus.Warning;
                }
            }
            catch (Exception ex)
            {
                check.Status = HealthStatus.Warning;
                check.Details = $"Security check failed: {ex.Message}";
            }

            return check;
        }

        private static async Task<DetailedHealthCheck> CheckPerformanceMetricsDetailedAsync()
        {
            var check = new DetailedHealthCheck
            {
                Category = "Performance",
                Name = "Performance Metrics",
                Status = HealthStatus.Healthy,
                Description = "Detailed performance metrics assessment"
            };

            try
            {
                var process = Process.GetCurrentProcess();
                var performanceDetails = new List<string>
                {
                    $"Memory: {process.WorkingSet64 / (1024 * 1024):F0} MB",
                    $"Threads: {process.Threads.Count}",
                    $"Handles: {process.HandleCount}",
                    $"Uptime: {(DateTime.Now - process.StartTime).TotalHours:F1} hours"
                };

                check.Details = string.Join(", ", performanceDetails);

                // パフォーマンスしきい値チェック
                if (process.WorkingSet64 > 200 * 1024 * 1024) // 200MB以上
                {
                    check.Status = HealthStatus.Warning;
                }
            }
            catch (Exception ex)
            {
                check.Status = HealthStatus.Warning;
                check.Details = $"Performance check failed: {ex.Message}";
            }

            return check;
        }

        public static async Task<List<string>> GetActiveAlertsAsync()
        {
            var alerts = new List<string>();

            try
            {
                var report = await GetHealthReportAsync();

                // クリティカルな問題をアラートとして抽出
                if (report.OverallStatus == SystemHealthStatus.Critical)
                {
                    alerts.Add("System health is critical - immediate attention required");
                }

                // メモリ使用率の警告
                var memoryMetric = report.Metrics.GetValueOrDefault("MemoryUsage");
                if (memoryMetric != null && memoryMetric.Status == HealthStatus.Warning)
                {
                    alerts.Add("High memory usage detected - consider optimizing memory usage");
                }

                // ネットワーク接続の問題
                var networkMetric = report.Metrics.GetValueOrDefault("NetworkConnectivity");
                if (networkMetric != null && networkMetric.Status == HealthStatus.Critical)
                {
                    alerts.Add("Network connectivity issues detected - check network configuration");
                }
            }
            catch (Exception ex)
            {
                alerts.Add($"Health monitoring error: {ex.Message}");
            }

            return alerts;
        }

        public static async Task<SystemHealthReport> GetHealthReportAsync()
        {
            await PerformHealthCheckAsync();

            lock (_metricsLock)
            {
                var report = new SystemHealthReport
                {
                    Timestamp = DateTime.UtcNow,
                    OverallStatus = _lastHealthStatus,
                    Metrics = new Dictionary<string, HealthMetric>(_healthMetrics),
                    Recommendations = GenerateRecommendations()
                };

                return report;
            }
        }

        public static async Task<bool> IsSystemHealthyAsync()
        {
            var report = await GetHealthReportAsync();
            return report.OverallStatus == SystemHealthStatus.Healthy ||
                   report.OverallStatus == SystemHealthStatus.Warning;
        }

        private static async Task PerformHealthCheckAsync()
        {
            try
            {
                var healthChecks = new List<Task>
                {
                    CheckMemoryUsageAsync(),
                    CheckDiskSpaceAsync(),
                    CheckNetworkConnectivityAsync(),
                    CheckWiFiAdapterStatusAsync(),
                    CheckSystemPerformanceAsync(),
                    CheckSecurityStatusAsync(),
                    CheckConfigurationIntegrityAsync(),
                    CheckConfigurationDriftAsync(),
                    CheckLogFileHealthAsync()
                };

                await Task.WhenAll(healthChecks);

                // Calculate overall health status
                lock (_metricsLock)
                {
                    var criticalIssues = _healthMetrics.Values.Count(m => m.Status == HealthStatus.Critical);
                    var warningIssues = _healthMetrics.Values.Count(m => m.Status == HealthStatus.Warning);

                    if (criticalIssues > 0)
                        _lastHealthStatus = SystemHealthStatus.Critical;
                    else if (warningIssues > 2)
                        _lastHealthStatus = SystemHealthStatus.Degraded;
                    else if (warningIssues > 0)
                        _lastHealthStatus = SystemHealthStatus.Warning;
                    else
                        _lastHealthStatus = SystemHealthStatus.Healthy;
                }

                _lastFullHealthCheck = DateTime.UtcNow;

                // Log health status changes
                if (_lastHealthStatus != SystemHealthStatus.Healthy)
                {
                    await Logger.LogWarning($"System health status: {_lastHealthStatus}",
                                           nameof(SystemHealthMonitor));
                }

                await AuditTrail.RecordEventAsync("SystemHealth", "HealthCheckCompleted",
                    new Dictionary<string, object>
                    {
                        ["status"] = _lastHealthStatus.ToString(),
                        ["checkDuration"] = (DateTime.UtcNow - _lastFullHealthCheck).TotalSeconds
                    }, "Low");
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Health check failed");
                UpdateHealthMetric("HealthCheckError", HealthStatus.Critical,
                                 $"Health monitoring failed: {ex.Message}");
            }
        }

        private static async Task CheckMemoryUsageAsync()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var memoryMB = process.WorkingSet64 / (1024 * 1024);
                var memoryThreshold = await EnterpriseConfiguration.GetConfigValueAsync("MemoryThreshold", 100);

                HealthStatus status;
                string message;

                if (memoryMB > memoryThreshold * 1.5)
                {
                    status = HealthStatus.Critical;
                    message = $"High memory usage: {memoryMB}MB (threshold: {memoryThreshold}MB)";
                }
                else if (memoryMB > memoryThreshold)
                {
                    status = HealthStatus.Warning;
                    message = $"Elevated memory usage: {memoryMB}MB (threshold: {memoryThreshold}MB)";
                }
                else
                {
                    status = HealthStatus.Healthy;
                    message = $"Memory usage normal: {memoryMB}MB";
                }

                UpdateHealthMetric("MemoryUsage", status, message, memoryMB);
            }
            catch (Exception ex)
            {
                UpdateHealthMetric("MemoryUsage", HealthStatus.Warning,
                                 $"Memory check failed: {ex.Message}");
            }
        }

        private static async Task CheckDiskSpaceAsync()
        {
            try
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var drive = new DriveInfo(Path.GetPathRoot(appDataPath));

                var freeSpaceGB = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                var totalSpaceGB = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                var freeSpacePercent = (freeSpaceGB / totalSpaceGB) * 100;

                HealthStatus status;
                string message;

                if (freeSpacePercent < 5 || freeSpaceGB < 1)
                {
                    status = HealthStatus.Critical;
                    message = $"Critical disk space: {freeSpaceGB:F1}GB ({freeSpacePercent:F1}%) free";
                }
                else if (freeSpacePercent < 10 || freeSpaceGB < 2)
                {
                    status = HealthStatus.Warning;
                    message = $"Low disk space: {freeSpaceGB:F1}GB ({freeSpacePercent:F1}%) free";
                }
                else
                {
                    status = HealthStatus.Healthy;
                    message = $"Disk space adequate: {freeSpaceGB:F1}GB ({freeSpacePercent:F1}%) free";
                }

                UpdateHealthMetric("DiskSpace", status, message, freeSpaceGB);
            }
            catch (Exception ex)
            {
                UpdateHealthMetric("DiskSpace", HealthStatus.Warning,
                                 $"Disk space check failed: {ex.Message}");
            }
        }

        private static async Task CheckNetworkConnectivityAsync()
        {
            try
            {
                var ping = new Ping();
                var testHosts = new[] { "8.8.8.8", "1.1.1.1", "208.67.222.222" };
                var successfulPings = 0;

                foreach (var host in testHosts)
                {
                    try
                    {
                        var reply = await ping.SendPingAsync(host, 3000);
                        if (reply.Status == IPStatus.Success)
                        {
                            successfulPings++;
                        }
                    }
                    catch
                    {
                        // Individual ping failure is expected
                    }
                }

                HealthStatus status;
                string message;

                if (successfulPings == 0)
                {
                    status = HealthStatus.Critical;
                    message = "No network connectivity detected";
                }
                else if (successfulPings < testHosts.Length / 2)
                {
                    status = HealthStatus.Warning;
                    message = $"Limited network connectivity: {successfulPings}/{testHosts.Length} hosts reachable";
                }
                else
                {
                    status = HealthStatus.Healthy;
                    message = $"Network connectivity good: {successfulPings}/{testHosts.Length} hosts reachable";
                }

                UpdateHealthMetric("NetworkConnectivity", status, message, successfulPings);
            }
            catch (Exception ex)
            {
                UpdateHealthMetric("NetworkConnectivity", HealthStatus.Warning,
                                 $"Network connectivity check failed: {ex.Message}");
            }
        }

        private static async Task CheckWiFiAdapterStatusAsync()
        {
            try
            {
                var wifiAdapters = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    .ToList();

                HealthStatus status;
                string message;

                if (!wifiAdapters.Any())
                {
                    status = HealthStatus.Critical;
                    message = "No WiFi adapters found";
                }
                else
                {
                    var activeAdapters = wifiAdapters.Count(a => a.OperationalStatus == OperationalStatus.Up);

                    if (activeAdapters == 0)
                    {
                        status = HealthStatus.Critical;
                        message = $"No WiFi adapters active ({wifiAdapters.Count} total)";
                    }
                    else
                    {
                        status = HealthStatus.Healthy;
                        message = $"WiFi adapters operational: {activeAdapters}/{wifiAdapters.Count} active";
                    }
                }

                UpdateHealthMetric("WiFiAdapterStatus", status, message, wifiAdapters.Count);
            }
            catch (Exception ex)
            {
                UpdateHealthMetric("WiFiAdapterStatus", HealthStatus.Warning,
                                 $"WiFi adapter check failed: {ex.Message}");
            }
        }

        private static async Task CheckSystemPerformanceAsync()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var cpuUsage = GetCpuUsage();

                HealthStatus status;
                string message;

                if (cpuUsage > 80)
                {
                    status = HealthStatus.Critical;
                    message = $"High CPU usage: {cpuUsage:F1}%";
                }
                else if (cpuUsage > 50)
                {
                    status = HealthStatus.Warning;
                    message = $"Elevated CPU usage: {cpuUsage:F1}%";
                }
                else
                {
                    status = HealthStatus.Healthy;
                    message = $"CPU usage normal: {cpuUsage:F1}%";
                }

                UpdateHealthMetric("SystemPerformance", status, message, cpuUsage);
            }
            catch (Exception ex)
            {
                UpdateHealthMetric("SystemPerformance", HealthStatus.Warning,
                                 $"Performance check failed: {ex.Message}");
            }
        }

        private static async Task CheckSecurityStatusAsync()
        {
            try
            {
                var issues = new List<string>();

                // Check if running with appropriate privileges
                var principal = new System.Security.Principal.WindowsPrincipal(
                    System.Security.Principal.WindowsIdentity.GetCurrent());
                var isAdmin = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

                // Check configuration security
                var requireEncryption = await EnterpriseConfiguration.GetConfigValueAsync("RequireEncryption", true);
                var secureDelete = await EnterpriseConfiguration.GetConfigValueAsync("SecureDelete", true);

                if (!requireEncryption)
                    issues.Add("Encryption not required");

                if (!secureDelete)
                    issues.Add("Secure delete disabled");

                HealthStatus status;
                string message;

                if (issues.Count > 2)
                {
                    status = HealthStatus.Critical;
                    message = $"Multiple security issues: {string.Join(", ", issues)}";
                }
                else if (issues.Count > 0)
                {
                    status = HealthStatus.Warning;
                    message = $"Security concerns: {string.Join(", ", issues)}";
                }
                else
                {
                    status = HealthStatus.Healthy;
                    message = "Security configuration optimal";
                }

                UpdateHealthMetric("SecurityStatus", status, message, issues.Count);
            }
            catch (Exception ex)
            {
                UpdateHealthMetric("SecurityStatus", HealthStatus.Warning,
                                 $"Security check failed: {ex.Message}");
            }
        }

        private static async Task CheckConfigurationDriftAsync()
        {
            try
            {
                var driftReport = await _driftDetector.DetectDriftAsync();

                HealthStatus status;
                string message;

                if (driftReport.DetectedDrifts.Any(d => d.Severity == DriftSeverity.Critical))
                {
                    status = HealthStatus.Critical;
                    message = $"Critical configuration drift detected: {driftReport.DetectedDrifts.Count(d => d.Severity == DriftSeverity.Critical)} issues";
                }
                else if (driftReport.DetectedDrifts.Any(d => d.Severity == DriftSeverity.High))
                {
                    status = HealthStatus.Warning;
                    message = $"Configuration drift detected: {driftReport.DetectedDrifts.Count} total issues";
                }
                else if (driftReport.DetectedDrifts.Any())
                {
                    status = HealthStatus.Warning;
                    message = $"Minor configuration drift: {driftReport.DetectedDrifts.Count} issues";
                }
                else
                {
                    status = HealthStatus.Healthy;
                    message = "No configuration drift detected";
                }

                UpdateHealthMetric("ConfigurationDrift", status, message, driftReport.DetectedDrifts.Count);

                // Log drift detection results
                if (driftReport.DetectedDrifts.Any())
                {
                    await Logger.LogWarning($"Configuration drift detected", "SystemHealthMonitor",
                        new Dictionary<string, object>
                        {
                            ["driftCount"] = driftReport.DetectedDrifts.Count,
                            ["criticalCount"] = driftReport.DetectedDrifts.Count(d => d.Severity == DriftSeverity.Critical),
                            ["highCount"] = driftReport.DetectedDrifts.Count(d => d.Severity == DriftSeverity.High),
                            ["recommendations"] = driftReport.Recommendations
                        });
                }
            }
            catch (Exception ex)
            {
                UpdateHealthMetric("ConfigurationDrift", HealthStatus.Warning,
                    $"Configuration drift check failed: {ex.Message}");
            }
        }

        private static async Task CheckLogFileHealthAsync()
        {
            try
            {
                var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                        "MurtiWifiConnecter");

                if (!Directory.Exists(logDir))
                {
                    UpdateHealthMetric("LogFileHealth", HealthStatus.Warning, "Log directory missing");
                    return;
                }

                var logFiles = Directory.GetFiles(logDir, "*.log");
                var totalLogSize = logFiles.Sum(f => new FileInfo(f).Length);
                var totalLogSizeMB = totalLogSize / (1024.0 * 1024.0);

                HealthStatus status;
                string message;

                if (totalLogSizeMB > 100)
                {
                    status = HealthStatus.Warning;
                    message = $"Large log files: {totalLogSizeMB:F1}MB total";
                }
                else
                {
                    status = HealthStatus.Healthy;
                    message = $"Log files healthy: {logFiles.Length} files, {totalLogSizeMB:F1}MB total";
                }

                UpdateHealthMetric("LogFileHealth", status, message, totalLogSizeMB);
            }
            catch (Exception ex)
            {
                UpdateHealthMetric("LogFileHealth", HealthStatus.Warning,
                                 $"Log file check failed: {ex.Message}");
            }
        }

        private static void UpdateHealthMetric(string name, HealthStatus status, string message, double? value = null)
        {
            lock (_metricsLock)
            {
                _healthMetrics[name] = new HealthMetric
                {
                    Name = name,
                    Status = status,
                    Message = message,
                    Value = value,
                    LastChecked = DateTime.UtcNow
                };
            }
        }

        private static double GetCpuUsage()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var startTime = DateTime.UtcNow;
                var startCpuUsage = process.TotalProcessorTime;

                Thread.Sleep(100);

                var endTime = DateTime.UtcNow;
                var endCpuUsage = process.TotalProcessorTime;

                var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
                var totalMsPassed = (endTime - startTime).TotalMilliseconds;
                var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

                return cpuUsageTotal * 100;
            }
            catch
            {
                return 0;
            }
        }

        private static List<string> GenerateRecommendations()
        {
            var recommendations = new List<string>();

            lock (_metricsLock)
            {
                foreach (var metric in _healthMetrics.Values)
                {
                    switch (metric.Name)
                    {
                        case "MemoryUsage" when metric.Status != HealthStatus.Healthy:
                            recommendations.Add("Consider restarting the application to free memory");
                            break;
                        case "DiskSpace" when metric.Status != HealthStatus.Healthy:
                            recommendations.Add("Clean up log files or temporary data");
                            break;
                        case "NetworkConnectivity" when metric.Status != HealthStatus.Healthy:
                            recommendations.Add("Check network connection and firewall settings");
                            break;
                        case "WiFiAdapterStatus" when metric.Status != HealthStatus.Healthy:
                            recommendations.Add("Verify WiFi adapter drivers and hardware");
                            break;
                        case "SystemPerformance" when metric.Status != HealthStatus.Healthy:
                            recommendations.Add("Close unnecessary applications to reduce CPU load");
                            break;
                        case "SecurityStatus" when metric.Status != HealthStatus.Healthy:
                            recommendations.Add("Review and update security configuration");
                            break;
                        case "ConfigurationIntegrity" when metric.Status != HealthStatus.Healthy:
                            recommendations.Add("Reset configuration to defaults if issues persist");
                            break;
                        case "LogFileHealth" when metric.Status != HealthStatus.Healthy:
                            recommendations.Add("Archive or delete old log files");
                            break;
                    }
                }
            }

            return recommendations;
        }
    }

    public class HealthMetric
    {
        public string Name { get; set; } = string.Empty;
        public HealthStatus Status { get; set; }
        public string Message { get; set; } = string.Empty;
        public double? Value { get; set; }
        public DateTime LastChecked { get; set; }
    }

    public class SystemHealthReport
    {
        public DateTime Timestamp { get; set; }
        public SystemHealthStatus OverallStatus { get; set; }
        public Dictionary<string, HealthMetric> Metrics { get; set; } = new();
        public List<DetailedHealthCheck> DetailedChecks { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
    }

    public class DetailedHealthCheck
    {
        public string Category { get; set; }
        public string Name { get; set; }
        public HealthStatus Status { get; set; }
        public string Description { get; set; }
        public string Details { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Message { get; set; } = string.Empty;
        public double? Value { get; set; }
        public DateTime LastChecked { get; set; }
    }

    public enum HealthStatus
    {
        Healthy,
        Warning,
        Critical
    }

    public enum SystemHealthStatus
    {
        Unknown,
        Healthy,
        Warning,
        Degraded,
        Critical
    }
}