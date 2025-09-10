using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Infrastructure.Performance
{
    /// <summary>
    /// パフォーマンス監視インターフェース
    /// </summary>
    public interface IPerformanceMonitor
    {
        IDisposable StartOperation(string operationName, string category = null);
        void RecordMetric(string name, double value, string unit = null, Dictionary<string, string> tags = null);
        void RecordCounter(string name, long value = 1, Dictionary<string, string> tags = null);
        void RecordGauge(string name, double value, Dictionary<string, string> tags = null);
        void RecordHistogram(string name, double value, Dictionary<string, string> tags = null);
        Task<PerformanceReport> GenerateReportAsync(TimeSpan period);
        List<PerformanceAlert> CheckThresholds();
        void SetThreshold(string metricName, double warningThreshold, double criticalThreshold);
        void StartContinuousMonitoring(TimeSpan interval);
        void StopContinuousMonitoring();
    }

    /// <summary>
    /// パフォーマンス監視の実装
    /// </summary>
    public class PerformanceMonitor : IPerformanceMonitor, IDisposable
    {
        private readonly ConcurrentDictionary<string, PerformanceMetricData> _metrics;
        private readonly ConcurrentDictionary<string, PerformanceThreshold> _thresholds;
        private readonly ConcurrentQueue<PerformanceEvent> _events;
        private readonly Timer _continuousMonitoringTimer;
        private readonly PerformanceCounter _cpuCounter;
        private readonly PerformanceCounter _memoryCounter;
        private readonly Process _currentProcess;
        private bool _continuousMonitoringEnabled;
        private readonly object _lockObject = new object();

        public PerformanceMonitor()
        {
            _metrics = new ConcurrentDictionary<string, PerformanceMetricData>();
            _thresholds = new ConcurrentDictionary<string, PerformanceThreshold>();
            _events = new ConcurrentQueue<PerformanceEvent>();
            _currentProcess = Process.GetCurrentProcess();

            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _memoryCounter = new PerformanceCounter("Memory", "Available MBytes");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Performance counters initialization failed: {ex.Message}");
            }

            _continuousMonitoringTimer = new Timer(ContinuousMonitoringCallback, null, Timeout.Infinite, Timeout.Infinite);
            
            SetDefaultThresholds();
        }

        /// <summary>
        /// 操作の測定を開始
        /// </summary>
        public IDisposable StartOperation(string operationName, string category = null)
        {
            return new OperationMeasurement(this, operationName, category);
        }

        /// <summary>
        /// メトリクスを記録
        /// </summary>
        public void RecordMetric(string name, double value, string unit = null, Dictionary<string, string> tags = null)
        {
            var metricData = _metrics.GetOrAdd(name, _ => new PerformanceMetricData(name, MetricType.Metric));
            
            lock (metricData.Lock)
            {
                metricData.Values.Add(value);
                metricData.LastValue = value;
                metricData.LastUpdated = DateTime.Now;
                metricData.Unit = unit;
                metricData.Tags = tags ?? new Dictionary<string, string>();
            }

            RecordEvent(new PerformanceEvent
            {
                Name = name,
                Type = MetricType.Metric,
                Value = value,
                Unit = unit,
                Tags = tags,
                Timestamp = DateTime.Now
            });
        }

        /// <summary>
        /// カウンターを記録
        /// </summary>
        public void RecordCounter(string name, long value = 1, Dictionary<string, string> tags = null)
        {
            var metricData = _metrics.GetOrAdd(name, _ => new PerformanceMetricData(name, MetricType.Counter));
            
            lock (metricData.Lock)
            {
                metricData.CounterValue += value;
                metricData.LastUpdated = DateTime.Now;
                metricData.Tags = tags ?? new Dictionary<string, string>();
            }

            RecordEvent(new PerformanceEvent
            {
                Name = name,
                Type = MetricType.Counter,
                Value = value,
                Tags = tags,
                Timestamp = DateTime.Now
            });
        }

        /// <summary>
        /// ゲージを記録
        /// </summary>
        public void RecordGauge(string name, double value, Dictionary<string, string> tags = null)
        {
            var metricData = _metrics.GetOrAdd(name, _ => new PerformanceMetricData(name, MetricType.Gauge));
            
            lock (metricData.Lock)
            {
                metricData.LastValue = value;
                metricData.LastUpdated = DateTime.Now;
                metricData.Tags = tags ?? new Dictionary<string, string>();
            }

            RecordEvent(new PerformanceEvent
            {
                Name = name,
                Type = MetricType.Gauge,
                Value = value,
                Tags = tags,
                Timestamp = DateTime.Now
            });
        }

        /// <summary>
        /// ヒストグラムを記録
        /// </summary>
        public void RecordHistogram(string name, double value, Dictionary<string, string> tags = null)
        {
            var metricData = _metrics.GetOrAdd(name, _ => new PerformanceMetricData(name, MetricType.Histogram));
            
            lock (metricData.Lock)
            {
                metricData.Values.Add(value);
                metricData.LastValue = value;
                metricData.LastUpdated = DateTime.Now;
                metricData.Tags = tags ?? new Dictionary<string, string>();
            }

            RecordEvent(new PerformanceEvent
            {
                Name = name,
                Type = MetricType.Histogram,
                Value = value,
                Tags = tags,
                Timestamp = DateTime.Now
            });
        }

        /// <summary>
        /// パフォーマンスレポートを生成
        /// </summary>
        public async Task<PerformanceReport> GenerateReportAsync(TimeSpan period)
        {
            return await Task.Run(() =>
            {
                var endTime = DateTime.Now;
                var startTime = endTime - period;

                var report = new PerformanceReport
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    Period = period,
                    SystemMetrics = GetSystemMetrics(),
                    ApplicationMetrics = GetApplicationMetrics(),
                    Alerts = CheckThresholds()
                };

                // イベントをフィルタリング
                var relevantEvents = new List<PerformanceEvent>();
                foreach (var eventItem in _events)
                {
                    if (eventItem.Timestamp >= startTime && eventItem.Timestamp <= endTime)
                    {
                        relevantEvents.Add(eventItem);
                    }
                }

                report.Events = relevantEvents;
                report.Summary = GenerateReportSummary(report);

                return report;
            });
        }

        /// <summary>
        /// しきい値をチェック
        /// </summary>
        public List<PerformanceAlert> CheckThresholds()
        {
            var alerts = new List<PerformanceAlert>();

            foreach (var metric in _metrics.Values)
            {
                if (_thresholds.TryGetValue(metric.Name, out var threshold))
                {
                    var currentValue = GetCurrentValue(metric);
                    var alertLevel = DetermineAlertLevel(currentValue, threshold);

                    if (alertLevel != AlertLevel.Normal)
                    {
                        alerts.Add(new PerformanceAlert
                        {
                            MetricName = metric.Name,
                            Level = alertLevel,
                            CurrentValue = currentValue,
                            Threshold = alertLevel == AlertLevel.Warning ? threshold.WarningThreshold : threshold.CriticalThreshold,
                            Message = GenerateAlertMessage(metric.Name, alertLevel, currentValue, threshold),
                            Timestamp = DateTime.Now
                        });
                    }
                }
            }

            return alerts;
        }

        /// <summary>
        /// しきい値を設定
        /// </summary>
        public void SetThreshold(string metricName, double warningThreshold, double criticalThreshold)
        {
            _thresholds[metricName] = new PerformanceThreshold
            {
                MetricName = metricName,
                WarningThreshold = warningThreshold,
                CriticalThreshold = criticalThreshold
            };
        }

        /// <summary>
        /// 継続的監視を開始
        /// </summary>
        public void StartContinuousMonitoring(TimeSpan interval)
        {
            _continuousMonitoringEnabled = true;
            _continuousMonitoringTimer.Change(TimeSpan.Zero, interval);
        }

        /// <summary>
        /// 継続的監視を停止
        /// </summary>
        public void StopContinuousMonitoring()
        {
            _continuousMonitoringEnabled = false;
            _continuousMonitoringTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// イベントを記録
        /// </summary>
        private void RecordEvent(PerformanceEvent eventItem)
        {
            _events.Enqueue(eventItem);

            // 古いイベントを削除（メモリ使用量制限）
            while (_events.Count > 10000)
            {
                _events.TryDequeue(out _);
            }
        }

        /// <summary>
        /// システムメトリクスを取得
        /// </summary>
        private SystemMetrics GetSystemMetrics()
        {
            try
            {
                return new SystemMetrics
                {
                    CpuUsage = _cpuCounter?.NextValue() ?? 0,
                    AvailableMemoryMB = _memoryCounter?.NextValue() ?? 0,
                    ProcessMemoryMB = _currentProcess.WorkingSet64 / (1024 * 1024),
                    ThreadCount = _currentProcess.Threads.Count,
                    HandleCount = _currentProcess.HandleCount,
                    Uptime = DateTime.Now - _currentProcess.StartTime
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get system metrics: {ex.Message}");
                return new SystemMetrics();
            }
        }

        /// <summary>
        /// アプリケーションメトリクスを取得
        /// </summary>
        private Dictionary<string, object> GetApplicationMetrics()
        {
            var appMetrics = new Dictionary<string, object>();

            foreach (var metric in _metrics.Values)
            {
                var value = GetCurrentValue(metric);
                appMetrics[metric.Name] = new
                {
                    Value = value,
                    Type = metric.Type.ToString(),
                    Unit = metric.Unit,
                    LastUpdated = metric.LastUpdated,
                    Tags = metric.Tags
                };
            }

            return appMetrics;
        }

        /// <summary>
        /// 現在の値を取得
        /// </summary>
        private double GetCurrentValue(PerformanceMetricData metric)
        {
            lock (metric.Lock)
            {
                return metric.Type switch
                {
                    MetricType.Counter => metric.CounterValue,
                    MetricType.Gauge => metric.LastValue,
                    MetricType.Metric => metric.Values.Count > 0 ? metric.Values.Average() : 0,
                    MetricType.Histogram => metric.Values.Count > 0 ? metric.Values.Average() : 0,
                    _ => metric.LastValue
                };
            }
        }

        /// <summary>
        /// アラートレベルを決定
        /// </summary>
        private AlertLevel DetermineAlertLevel(double currentValue, PerformanceThreshold threshold)
        {
            if (currentValue >= threshold.CriticalThreshold)
                return AlertLevel.Critical;
            if (currentValue >= threshold.WarningThreshold)
                return AlertLevel.Warning;
            return AlertLevel.Normal;
        }

        /// <summary>
        /// アラートメッセージを生成
        /// </summary>
        private string GenerateAlertMessage(string metricName, AlertLevel level, double currentValue, PerformanceThreshold threshold)
        {
            var levelText = level == AlertLevel.Warning ? "警告" : "重要";
            var thresholdValue = level == AlertLevel.Warning ? threshold.WarningThreshold : threshold.CriticalThreshold;
            
            return $"{levelText}: {metricName} の値が {currentValue:F2} でしきい値 {thresholdValue:F2} を超過しました";
        }

        /// <summary>
        /// レポートサマリーを生成
        /// </summary>
        private Dictionary<string, object> GenerateReportSummary(PerformanceReport report)
        {
            var summary = new Dictionary<string, object>
            {
                { "TotalEvents", report.Events.Count },
                { "AlertsCount", report.Alerts.Count },
                { "CriticalAlerts", report.Alerts.Count(a => a.Level == AlertLevel.Critical) },
                { "WarningAlerts", report.Alerts.Count(a => a.Level == AlertLevel.Warning) },
                { "AverageCpuUsage", report.SystemMetrics.CpuUsage },
                { "MemoryUsageMB", report.SystemMetrics.ProcessMemoryMB },
                { "ThreadCount", report.SystemMetrics.ThreadCount }
            };

            return summary;
        }

        /// <summary>
        /// 継続的監視のコールバック
        /// </summary>
        private void ContinuousMonitoringCallback(object state)
        {
            if (!_continuousMonitoringEnabled)
                return;

            try
            {
                var systemMetrics = GetSystemMetrics();
                
                RecordGauge("system.cpu_usage", systemMetrics.CpuUsage);
                RecordGauge("system.memory_available_mb", systemMetrics.AvailableMemoryMB);
                RecordGauge("process.memory_mb", systemMetrics.ProcessMemoryMB);
                RecordGauge("process.thread_count", systemMetrics.ThreadCount);
                RecordGauge("process.handle_count", systemMetrics.HandleCount);

                // GCメトリクス
                RecordGauge("gc.total_memory", GC.GetTotalMemory(false) / (1024 * 1024));
                RecordCounter("gc.gen0_collections", GC.CollectionCount(0));
                RecordCounter("gc.gen1_collections", GC.CollectionCount(1));
                RecordCounter("gc.gen2_collections", GC.CollectionCount(2));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Continuous monitoring failed: {ex.Message}");
            }
        }

        /// <summary>
        /// デフォルトしきい値を設定
        /// </summary>
        private void SetDefaultThresholds()
        {
            SetThreshold("system.cpu_usage", 70, 90);
            SetThreshold("process.memory_mb", 512, 1024);
            SetThreshold("process.thread_count", 50, 100);
            SetThreshold("operation.duration_ms", 1000, 5000);
        }

        public void Dispose()
        {
            StopContinuousMonitoring();
            _continuousMonitoringTimer?.Dispose();
            _cpuCounter?.Dispose();
            _memoryCounter?.Dispose();
            _currentProcess?.Dispose();
        }
    }

    /// <summary>
    /// 操作測定クラス
    /// </summary>
    internal class OperationMeasurement : IDisposable
    {
        private readonly IPerformanceMonitor _monitor;
        private readonly string _operationName;
        private readonly string _category;
        private readonly Stopwatch _stopwatch;
        private readonly DateTime _startTime;

        public OperationMeasurement(IPerformanceMonitor monitor, string operationName, string category)
        {
            _monitor = monitor;
            _operationName = operationName;
            _category = category;
            _startTime = DateTime.Now;
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            
            var tags = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(_category))
            {
                tags["category"] = _category;
            }

            _monitor.RecordMetric($"operation.duration_ms", _stopwatch.ElapsedMilliseconds, "ms", tags);
            _monitor.RecordHistogram($"operation.{_operationName}.duration_ms", _stopwatch.ElapsedMilliseconds, tags);
            _monitor.RecordCounter($"operation.{_operationName}.count", 1, tags);
        }
    }

    /// <summary>
    /// パフォーマンスメトリクスデータ
    /// </summary>
    public class PerformanceMetricData
    {
        public string Name { get; }
        public MetricType Type { get; }
        public List<double> Values { get; }
        public double LastValue { get; set; }
        public long CounterValue { get; set; }
        public DateTime LastUpdated { get; set; }
        public string Unit { get; set; }
        public Dictionary<string, string> Tags { get; set; }
        public object Lock { get; } = new object();

        public PerformanceMetricData(string name, MetricType type)
        {
            Name = name;
            Type = type;
            Values = new List<double>();
            Tags = new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// パフォーマンスイベント
    /// </summary>
    public class PerformanceEvent
    {
        public string Name { get; set; }
        public MetricType Type { get; set; }
        public double Value { get; set; }
        public string Unit { get; set; }
        public Dictionary<string, string> Tags { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// パフォーマンスレポート
    /// </summary>
    public class PerformanceReport
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Period { get; set; }
        public SystemMetrics SystemMetrics { get; set; }
        public Dictionary<string, object> ApplicationMetrics { get; set; }
        public List<PerformanceEvent> Events { get; set; }
        public List<PerformanceAlert> Alerts { get; set; }
        public Dictionary<string, object> Summary { get; set; }
    }

    /// <summary>
    /// システムメトリクス
    /// </summary>
    public class SystemMetrics
    {
        public double CpuUsage { get; set; }
        public double AvailableMemoryMB { get; set; }
        public double ProcessMemoryMB { get; set; }
        public int ThreadCount { get; set; }
        public int HandleCount { get; set; }
        public TimeSpan Uptime { get; set; }
    }

    /// <summary>
    /// パフォーマンスアラート
    /// </summary>
    public class PerformanceAlert
    {
        public string MetricName { get; set; }
        public AlertLevel Level { get; set; }
        public double CurrentValue { get; set; }
        public double Threshold { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// パフォーマンスしきい値
    /// </summary>
    public class PerformanceThreshold
    {
        public string MetricName { get; set; }
        public double WarningThreshold { get; set; }
        public double CriticalThreshold { get; set; }
    }

    /// <summary>
    /// メトリクスタイプ
    /// </summary>
    public enum MetricType
    {
        Counter,
        Gauge,
        Metric,
        Histogram
    }

    /// <summary>
    /// アラートレベル
    /// </summary>
    public enum AlertLevel
    {
        Normal,
        Warning,
        Critical
    }
}