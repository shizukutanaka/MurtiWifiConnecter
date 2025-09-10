using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using MurtiWifiConnecter.Infrastructure.Performance;

namespace MurtiWifiConnecter.Monitoring
{
    /// <summary>
    /// システムヘルスモニターインターフェース
    /// </summary>
    public interface ISystemHealthMonitor
    {
        Task<SystemHealthReport> GetSystemHealthAsync();
        Task<List<HealthAlert>> CheckHealthAlertsAsync();
        Task StartContinuousMonitoringAsync(TimeSpan interval, CancellationToken cancellationToken = default);
        void StopContinuousMonitoring();
        Task<SystemDiagnosticsReport> RunDiagnosticsAsync();
        Task<ComponentHealthReport> CheckComponentHealthAsync(SystemComponent component);
        event Action<HealthAlert> HealthAlertRaised;
        event Action<SystemHealthReport> HealthReportGenerated;
        void SetHealthThreshold(string metricName, double warningThreshold, double criticalThreshold);
        Task<SystemOptimizationReport> AnalyzeOptimizationOpportunitiesAsync();
    }

    /// <summary>
    /// システムヘルスモニターの実装
    /// </summary>
    public class SystemHealthMonitor : ISystemHealthMonitor, IDisposable
    {
        private readonly IPerformanceMonitor _performanceMonitor;
        private readonly Dictionary<string, HealthThreshold> _healthThresholds;
        private readonly Timer _monitoringTimer;
        private bool _isMonitoring;
        private CancellationTokenSource _cancellationTokenSource;

        public event Action<HealthAlert> HealthAlertRaised;
        public event Action<SystemHealthReport> HealthReportGenerated;

        public SystemHealthMonitor(IPerformanceMonitor performanceMonitor)
        {
            _performanceMonitor = performanceMonitor;
            _healthThresholds = new Dictionary<string, HealthThreshold>();
            _monitoringTimer = new Timer(MonitoringCallback, null, Timeout.Infinite, Timeout.Infinite);
            
            InitializeDefaultThresholds();
        }

        /// <summary>
        /// システムヘルス情報を取得
        /// </summary>
        public async Task<SystemHealthReport> GetSystemHealthAsync()
        {
            var report = new SystemHealthReport
            {
                Timestamp = DateTime.Now,
                ComponentHealths = new List<ComponentHealthReport>(),
                SystemMetrics = new SystemMetrics(),
                OverallHealth = HealthStatus.Healthy
            };

            try
            {
                // システムメトリクスを収集
                report.SystemMetrics = await CollectSystemMetricsAsync();

                // 各コンポーネントの健全性をチェック
                var components = Enum.GetValues<SystemComponent>();
                foreach (var component in components)
                {
                    var componentHealth = await CheckComponentHealthAsync(component);
                    report.ComponentHealths.Add(componentHealth);
                }

                // 全体的な健全性を計算
                report.OverallHealth = CalculateOverallHealth(report.ComponentHealths);
                report.HealthScore = CalculateHealthScore(report);

                // アラートをチェック
                report.Alerts = await CheckHealthAlertsAsync();

                HealthReportGenerated?.Invoke(report);
            }
            catch (Exception ex)
            {
                report.Errors.Add($"Health monitoring error: {ex.Message}");
                report.OverallHealth = HealthStatus.Critical;
            }

            return report;
        }

        /// <summary>
        /// ヘルスアラートをチェック
        /// </summary>
        public async Task<List<HealthAlert>> CheckHealthAlertsAsync()
        {
            var alerts = new List<HealthAlert>();

            try
            {
                // CPU使用率チェック
                var cpuUsage = await GetCpuUsageAsync();
                CheckThresholdAndAddAlert(alerts, "CPU_Usage", cpuUsage, "CPU使用率");

                // メモリ使用率チェック
                var memoryUsage = await GetMemoryUsagePercentageAsync();
                CheckThresholdAndAddAlert(alerts, "Memory_Usage", memoryUsage, "メモリ使用率");

                // ディスク使用率チェック
                var diskUsage = await GetDiskUsageAsync();
                CheckThresholdAndAddAlert(alerts, "Disk_Usage", diskUsage, "ディスク使用率");

                // ネットワーク健全性チェック
                var networkHealth = await CheckNetworkHealthAsync();
                if (!networkHealth.IsHealthy)
                {
                    alerts.Add(new HealthAlert
                    {
                        AlertType = "Network_Health",
                        Severity = AlertSeverity.Warning,
                        Message = "ネットワーク接続に問題があります",
                        Details = networkHealth.Issues,
                        Timestamp = DateTime.Now
                    });
                }

                // WiFiアダプター健全性チェック
                var wifiHealth = await CheckWiFiAdapterHealthAsync();
                if (!wifiHealth.IsHealthy)
                {
                    alerts.Add(new HealthAlert
                    {
                        AlertType = "WiFi_Adapter",
                        Severity = AlertSeverity.High,
                        Message = "WiFiアダプターに問題があります",
                        Details = wifiHealth.Issues,
                        Timestamp = DateTime.Now
                    });
                }

                // プロセス健全性チェック
                var processAlerts = await CheckProcessHealthAsync();
                alerts.AddRange(processAlerts);

                // サービス健全性チェック
                var serviceAlerts = await CheckServiceHealthAsync();
                alerts.AddRange(serviceAlerts);

                // 各アラートを発生させる
                foreach (var alert in alerts.Where(a => a.Severity >= AlertSeverity.Warning))
                {
                    HealthAlertRaised?.Invoke(alert);
                }
            }
            catch (Exception ex)
            {
                alerts.Add(new HealthAlert
                {
                    AlertType = "System_Error",
                    Severity = AlertSeverity.Critical,
                    Message = $"ヘルスチェック中にエラーが発生しました: {ex.Message}",
                    Timestamp = DateTime.Now
                });
            }

            return alerts;
        }

        /// <summary>
        /// 継続的監視を開始
        /// </summary>
        public async Task StartContinuousMonitoringAsync(TimeSpan interval, CancellationToken cancellationToken = default)
        {
            _isMonitoring = true;
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            
            _monitoringTimer.Change(TimeSpan.Zero, interval);

            try
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    await Task.Delay(interval, _cancellationTokenSource.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常な終了
            }
        }

        /// <summary>
        /// 継続的監視を停止
        /// </summary>
        public void StopContinuousMonitoring()
        {
            _isMonitoring = false;
            _monitoringTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _cancellationTokenSource?.Cancel();
        }

        /// <summary>
        /// システム診断を実行
        /// </summary>
        public async Task<SystemDiagnosticsReport> RunDiagnosticsAsync()
        {
            var report = new SystemDiagnosticsReport
            {
                DiagnosticsDate = DateTime.Now,
                Tests = new List<DiagnosticTestResult>()
            };

            // ハードウェア診断
            report.Tests.AddRange(await RunHardwareDiagnosticsAsync());

            // ネットワーク診断
            report.Tests.AddRange(await RunNetworkDiagnosticsAsync());

            // WiFi診断
            report.Tests.AddRange(await RunWiFiDiagnosticsAsync());

            // パフォーマンス診断
            report.Tests.AddRange(await RunPerformanceDiagnosticsAsync());

            // セキュリティ診断
            report.Tests.AddRange(await RunSecurityDiagnosticsAsync());

            // 全体的な診断結果を計算
            report.OverallResult = CalculateOverallDiagnosticResult(report.Tests);

            return report;
        }

        /// <summary>
        /// コンポーネントの健全性をチェック
        /// </summary>
        public async Task<ComponentHealthReport> CheckComponentHealthAsync(SystemComponent component)
        {
            var report = new ComponentHealthReport
            {
                Component = component,
                CheckDate = DateTime.Now,
                Status = HealthStatus.Healthy,
                Issues = new List<string>(),
                Metrics = new Dictionary<string, double>()
            };

            try
            {
                switch (component)
                {
                    case SystemComponent.CPU:
                        await CheckCpuHealthAsync(report);
                        break;
                    case SystemComponent.Memory:
                        await CheckMemoryHealthAsync(report);
                        break;
                    case SystemComponent.Disk:
                        await CheckDiskHealthAsync(report);
                        break;
                    case SystemComponent.Network:
                        await CheckNetworkComponentHealthAsync(report);
                        break;
                    case SystemComponent.WiFiAdapter:
                        await CheckWiFiAdapterComponentHealthAsync(report);
                        break;
                    case SystemComponent.Process:
                        await CheckProcessComponentHealthAsync(report);
                        break;
                    case SystemComponent.Service:
                        await CheckServiceComponentHealthAsync(report);
                        break;
                }
            }
            catch (Exception ex)
            {
                report.Status = HealthStatus.Critical;
                report.Issues.Add($"Component health check failed: {ex.Message}");
            }

            return report;
        }

        /// <summary>
        /// ヘルスしきい値を設定
        /// </summary>
        public void SetHealthThreshold(string metricName, double warningThreshold, double criticalThreshold)
        {
            _healthThresholds[metricName] = new HealthThreshold
            {
                MetricName = metricName,
                WarningThreshold = warningThreshold,
                CriticalThreshold = criticalThreshold
            };
        }

        /// <summary>
        /// 最適化機会を分析
        /// </summary>
        public async Task<SystemOptimizationReport> AnalyzeOptimizationOpportunitiesAsync()
        {
            var report = new SystemOptimizationReport
            {
                AnalysisDate = DateTime.Now,
                Opportunities = new List<OptimizationOpportunity>()
            };

            // CPU最適化機会
            var cpuOpportunities = await AnalyzeCpuOptimizationAsync();
            report.Opportunities.AddRange(cpuOpportunities);

            // メモリ最適化機会
            var memoryOpportunities = await AnalyzeMemoryOptimizationAsync();
            report.Opportunities.AddRange(memoryOpportunities);

            // ディスク最適化機会
            var diskOpportunities = await AnalyzeDiskOptimizationAsync();
            report.Opportunities.AddRange(diskOpportunities);

            // ネットワーク最適化機会
            var networkOpportunities = await AnalyzeNetworkOptimizationAsync();
            report.Opportunities.AddRange(networkOpportunities);

            // 優先度でソート
            report.Opportunities = report.Opportunities
                .OrderByDescending(o => o.Priority)
                .ThenByDescending(o => o.ImpactScore)
                .ToList();

            return report;
        }

        #region Private Helper Methods

        private async Task<SystemMetrics> CollectSystemMetricsAsync()
        {
            var metrics = new SystemMetrics
            {
                CpuUsage = await GetCpuUsageAsync(),
                MemoryUsage = await GetMemoryUsageAsync(),
                MemoryUsagePercentage = await GetMemoryUsagePercentageAsync(),
                DiskUsage = await GetDiskUsageAsync(),
                NetworkBytesReceived = await GetNetworkBytesReceivedAsync(),
                NetworkBytesSent = await GetNetworkBytesSentAsync(),
                ProcessCount = Process.GetProcesses().Length,
                ThreadCount = await GetTotalThreadCountAsync(),
                HandleCount = await GetTotalHandleCountAsync(),
                Uptime = GetSystemUptime()
            };

            return metrics;
        }

        private async Task<double> GetCpuUsageAsync()
        {
            try
            {
                using var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                cpuCounter.NextValue(); // 最初の呼び出しは無視
                await Task.Delay(100);
                return cpuCounter.NextValue();
            }
            catch
            {
                return 0;
            }
        }

        private async Task<long> GetMemoryUsageAsync()
        {
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                return currentProcess.WorkingSet64;
            }
            catch
            {
                return 0;
            }
        }

        private async Task<double> GetMemoryUsagePercentageAsync()
        {
            try
            {
                using var memoryCounter = new PerformanceCounter("Memory", "Available MBytes");
                var availableMemory = memoryCounter.NextValue();
                
                // 総メモリ量を取得
                var totalMemory = GetTotalPhysicalMemory();
                var usedMemory = (totalMemory / 1024 / 1024) - availableMemory;
                
                return (usedMemory / (totalMemory / 1024 / 1024)) * 100;
            }
            catch
            {
                return 0;
            }
        }

        private async Task<double> GetDiskUsageAsync()
        {
            try
            {
                var systemDrive = DriveInfo.GetDrives().FirstOrDefault(d => d.Name == "C:\\");
                if (systemDrive != null)
                {
                    var usedSpace = systemDrive.TotalSize - systemDrive.AvailableFreeSpace;
                    return (double)usedSpace / systemDrive.TotalSize * 100;
                }
            }
            catch
            {
                // エラーの場合は0を返す
            }
            return 0;
        }

        private async Task<long> GetNetworkBytesReceivedAsync()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                return interfaces.Sum(ni => ni.GetIPv4Statistics().BytesReceived);
            }
            catch
            {
                return 0;
            }
        }

        private async Task<long> GetNetworkBytesSentAsync()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                return interfaces.Sum(ni => ni.GetIPv4Statistics().BytesSent);
            }
            catch
            {
                return 0;
            }
        }

        private async Task<int> GetTotalThreadCountAsync()
        {
            try
            {
                return Process.GetProcesses().Sum(p =>
                {
                    try { return p.Threads.Count; }
                    catch { return 0; }
                });
            }
            catch
            {
                return 0;
            }
        }

        private async Task<int> GetTotalHandleCountAsync()
        {
            try
            {
                return Process.GetProcesses().Sum(p =>
                {
                    try { return p.HandleCount; }
                    catch { return 0; }
                });
            }
            catch
            {
                return 0;
            }
        }

        private TimeSpan GetSystemUptime()
        {
            try
            {
                return TimeSpan.FromMilliseconds(Environment.TickCount64);
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }

        private long GetTotalPhysicalMemory()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (ManagementObject obj in searcher.Get())
                {
                    return Convert.ToInt64(obj["TotalPhysicalMemory"]);
                }
            }
            catch
            {
                // フォールバック
            }
            return 8L * 1024 * 1024 * 1024; // 8GB as default
        }

        private HealthStatus CalculateOverallHealth(List<ComponentHealthReport> componentHealths)
        {
            if (componentHealths.Any(c => c.Status == HealthStatus.Critical))
                return HealthStatus.Critical;
            if (componentHealths.Any(c => c.Status == HealthStatus.Unhealthy))
                return HealthStatus.Unhealthy;
            if (componentHealths.Any(c => c.Status == HealthStatus.Degraded))
                return HealthStatus.Degraded;
            return HealthStatus.Healthy;
        }

        private double CalculateHealthScore(SystemHealthReport report)
        {
            var baseScore = 100.0;
            
            foreach (var component in report.ComponentHealths)
            {
                var penalty = component.Status switch
                {
                    HealthStatus.Degraded => 10,
                    HealthStatus.Unhealthy => 25,
                    HealthStatus.Critical => 50,
                    _ => 0
                };
                baseScore -= penalty;
            }

            return Math.Max(0, baseScore);
        }

        private void CheckThresholdAndAddAlert(List<HealthAlert> alerts, string metricName, double currentValue, string displayName)
        {
            if (_healthThresholds.TryGetValue(metricName, out var threshold))
            {
                AlertSeverity? severity = null;
                
                if (currentValue >= threshold.CriticalThreshold)
                    severity = AlertSeverity.Critical;
                else if (currentValue >= threshold.WarningThreshold)
                    severity = AlertSeverity.Warning;

                if (severity.HasValue)
                {
                    alerts.Add(new HealthAlert
                    {
                        AlertType = metricName,
                        Severity = severity.Value,
                        Message = $"{displayName}が高い値を示しています: {currentValue:F1}%",
                        CurrentValue = currentValue,
                        Threshold = severity == AlertSeverity.Critical ? threshold.CriticalThreshold : threshold.WarningThreshold,
                        Timestamp = DateTime.Now
                    });
                }
            }
        }

        private async Task<NetworkHealthResult> CheckNetworkHealthAsync()
        {
            var result = new NetworkHealthResult { IsHealthy = true, Issues = new List<string>() };

            try
            {
                // インターネット接続テスト
                using var ping = new Ping();
                var reply = await ping.SendPingAsync("8.8.8.8", 5000);
                if (reply.Status != IPStatus.Success)
                {
                    result.IsHealthy = false;
                    result.Issues.Add("インターネット接続が不安定です");
                }

                // ネットワークインターフェースチェック
                var activeInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up && 
                                ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .ToList();

                if (!activeInterfaces.Any())
                {
                    result.IsHealthy = false;
                    result.Issues.Add("アクティブなネットワークインターフェースがありません");
                }
            }
            catch (Exception ex)
            {
                result.IsHealthy = false;
                result.Issues.Add($"ネットワークチェックエラー: {ex.Message}");
            }

            return result;
        }

        private async Task<WiFiHealthResult> CheckWiFiAdapterHealthAsync()
        {
            var result = new WiFiHealthResult { IsHealthy = true, Issues = new List<string>() };

            try
            {
                // WiFiアダプターの存在確認
                var wifiInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    .ToList();

                if (!wifiInterfaces.Any())
                {
                    result.IsHealthy = false;
                    result.Issues.Add("WiFiアダプターが見つかりません");
                }
                else
                {
                    foreach (var wifiInterface in wifiInterfaces)
                    {
                        if (wifiInterface.OperationalStatus != OperationalStatus.Up)
                        {
                            result.IsHealthy = false;
                            result.Issues.Add($"WiFiアダプター '{wifiInterface.Name}' が無効です");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.IsHealthy = false;
                result.Issues.Add($"WiFiアダプターチェックエラー: {ex.Message}");
            }

            return result;
        }

        private async Task<List<HealthAlert>> CheckProcessHealthAsync()
        {
            var alerts = new List<HealthAlert>();

            try
            {
                var processes = Process.GetProcesses();
                
                // CPU使用率の高いプロセスをチェック
                foreach (var process in processes.Take(10))
                {
                    try
                    {
                        if (process.TotalProcessorTime.TotalMilliseconds > 0)
                        {
                            // CPU時間が異常に高いプロセスを検出
                            // 実際の実装では、より詳細な分析が必要
                        }
                    }
                    catch
                    {
                        // プロセスにアクセスできない場合は無視
                    }
                }

                // メモリ使用量の高いプロセスをチェック
                var highMemoryProcesses = processes
                    .Where(p => 
                    {
                        try { return p.WorkingSet64 > 500 * 1024 * 1024; } // 500MB以上
                        catch { return false; }
                    })
                    .Take(5);

                foreach (var process in highMemoryProcesses)
                {
                    try
                    {
                        alerts.Add(new HealthAlert
                        {
                            AlertType = "High_Memory_Process",
                            Severity = AlertSeverity.Info,
                            Message = $"プロセス '{process.ProcessName}' が大量のメモリを使用しています",
                            Details = new List<string> { $"メモリ使用量: {process.WorkingSet64 / 1024 / 1024:F0} MB" },
                            Timestamp = DateTime.Now
                        });
                    }
                    catch
                    {
                        // プロセス情報にアクセスできない場合は無視
                    }
                }
            }
            catch (Exception ex)
            {
                alerts.Add(new HealthAlert
                {
                    AlertType = "Process_Check_Error",
                    Severity = AlertSeverity.Warning,
                    Message = $"プロセスチェックエラー: {ex.Message}",
                    Timestamp = DateTime.Now
                });
            }

            return alerts;
        }

        private async Task<List<HealthAlert>> CheckServiceHealthAsync()
        {
            var alerts = new List<HealthAlert>();

            try
            {
                // 重要なWindowsサービスの状態をチェック
                var criticalServices = new[]
                {
                    "WLAN AutoConfig", // WiFi関連
                    "Network List Service",
                    "Network Location Awareness",
                    "DNS Client"
                };

                foreach (var serviceName in criticalServices)
                {
                    try
                    {
                        using var service = new System.ServiceProcess.ServiceController(serviceName);
                        if (service.Status != System.ServiceProcess.ServiceControllerStatus.Running)
                        {
                            alerts.Add(new HealthAlert
                            {
                                AlertType = "Service_Not_Running",
                                Severity = AlertSeverity.High,
                                Message = $"重要なサービス '{serviceName}' が実行されていません",
                                Details = new List<string> { $"ステータス: {service.Status}" },
                                Timestamp = DateTime.Now
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        alerts.Add(new HealthAlert
                        {
                            AlertType = "Service_Check_Error",
                            Severity = AlertSeverity.Warning,
                            Message = $"サービス '{serviceName}' のチェックに失敗しました: {ex.Message}",
                            Timestamp = DateTime.Now
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                alerts.Add(new HealthAlert
                {
                    AlertType = "Service_Check_Error",
                    Severity = AlertSeverity.Warning,
                    Message = $"サービスチェックエラー: {ex.Message}",
                    Timestamp = DateTime.Now
                });
            }

            return alerts;
        }

        private void InitializeDefaultThresholds()
        {
            SetHealthThreshold("CPU_Usage", 70, 90);
            SetHealthThreshold("Memory_Usage", 80, 95);
            SetHealthThreshold("Disk_Usage", 85, 95);
        }

        private void MonitoringCallback(object state)
        {
            if (!_isMonitoring) return;

            Task.Run(async () =>
            {
                try
                {
                    var healthReport = await GetSystemHealthAsync();
                    var alerts = await CheckHealthAlertsAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Monitoring callback error: {ex.Message}");
                }
            });
        }

        // 診断関連のメソッドの実装は省略（実際の実装では各種診断を実行）
        private async Task<List<DiagnosticTestResult>> RunHardwareDiagnosticsAsync() => new();
        private async Task<List<DiagnosticTestResult>> RunNetworkDiagnosticsAsync() => new();
        private async Task<List<DiagnosticTestResult>> RunWiFiDiagnosticsAsync() => new();
        private async Task<List<DiagnosticTestResult>> RunPerformanceDiagnosticsAsync() => new();
        private async Task<List<DiagnosticTestResult>> RunSecurityDiagnosticsAsync() => new();

        private DiagnosticResult CalculateOverallDiagnosticResult(List<DiagnosticTestResult> tests)
        {
            if (tests.Any(t => t.Result == DiagnosticResult.Critical))
                return DiagnosticResult.Critical;
            if (tests.Any(t => t.Result == DiagnosticResult.Warning))
                return DiagnosticResult.Warning;
            return DiagnosticResult.Passed;
        }

        // コンポーネント健全性チェックメソッドの実装は省略
        private async Task CheckCpuHealthAsync(ComponentHealthReport report) { }
        private async Task CheckMemoryHealthAsync(ComponentHealthReport report) { }
        private async Task CheckDiskHealthAsync(ComponentHealthReport report) { }
        private async Task CheckNetworkComponentHealthAsync(ComponentHealthReport report) { }
        private async Task CheckWiFiAdapterComponentHealthAsync(ComponentHealthReport report) { }
        private async Task CheckProcessComponentHealthAsync(ComponentHealthReport report) { }
        private async Task CheckServiceComponentHealthAsync(ComponentHealthReport report) { }

        // 最適化分析メソッドの実装は省略
        private async Task<List<OptimizationOpportunity>> AnalyzeCpuOptimizationAsync() => new();
        private async Task<List<OptimizationOpportunity>> AnalyzeMemoryOptimizationAsync() => new();
        private async Task<List<OptimizationOpportunity>> AnalyzeDiskOptimizationAsync() => new();
        private async Task<List<OptimizationOpportunity>> AnalyzeNetworkOptimizationAsync() => new();

        #endregion

        public void Dispose()
        {
            StopContinuousMonitoring();
            _monitoringTimer?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
    }

    #region Data Models

    public class SystemHealthReport
    {
        public DateTime Timestamp { get; set; }
        public HealthStatus OverallHealth { get; set; }
        public double HealthScore { get; set; }
        public List<ComponentHealthReport> ComponentHealths { get; set; } = new();
        public SystemMetrics SystemMetrics { get; set; }
        public List<HealthAlert> Alerts { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }

    public class ComponentHealthReport
    {
        public SystemComponent Component { get; set; }
        public HealthStatus Status { get; set; }
        public DateTime CheckDate { get; set; }
        public List<string> Issues { get; set; } = new();
        public Dictionary<string, double> Metrics { get; set; } = new();
    }

    public class HealthAlert
    {
        public string AlertType { get; set; }
        public AlertSeverity Severity { get; set; }
        public string Message { get; set; }
        public List<string> Details { get; set; } = new();
        public double? CurrentValue { get; set; }
        public double? Threshold { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class HealthThreshold
    {
        public string MetricName { get; set; }
        public double WarningThreshold { get; set; }
        public double CriticalThreshold { get; set; }
    }

    public class SystemDiagnosticsReport
    {
        public DateTime DiagnosticsDate { get; set; }
        public List<DiagnosticTestResult> Tests { get; set; } = new();
        public DiagnosticResult OverallResult { get; set; }
    }

    public class DiagnosticTestResult
    {
        public string TestName { get; set; }
        public string Category { get; set; }
        public DiagnosticResult Result { get; set; }
        public string Message { get; set; }
        public List<string> Details { get; set; } = new();
    }

    public class SystemOptimizationReport
    {
        public DateTime AnalysisDate { get; set; }
        public List<OptimizationOpportunity> Opportunities { get; set; } = new();
    }

    public class OptimizationOpportunity
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public OptimizationPriority Priority { get; set; }
        public double ImpactScore { get; set; }
        public string Implementation { get; set; }
        public TimeSpan EstimatedTimeToImplement { get; set; }
    }

    private class NetworkHealthResult
    {
        public bool IsHealthy { get; set; }
        public List<string> Issues { get; set; } = new();
    }

    private class WiFiHealthResult
    {
        public bool IsHealthy { get; set; }
        public List<string> Issues { get; set; } = new();
    }

    public enum SystemComponent
    {
        CPU,
        Memory,
        Disk,
        Network,
        WiFiAdapter,
        Process,
        Service
    }

    public enum HealthStatus
    {
        Healthy,
        Degraded,
        Unhealthy,
        Critical
    }

    public enum AlertSeverity
    {
        Info,
        Warning,
        High,
        Critical
    }

    public enum DiagnosticResult
    {
        Passed,
        Warning,
        Critical
    }

    public enum OptimizationPriority
    {
        Low,
        Medium,
        High,
        Critical
    }

    #endregion
}