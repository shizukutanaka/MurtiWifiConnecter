using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// パフォーマンス監視と最適化ユーティリティ
    /// </summary>
    public static class PerformanceMonitor
    {
        private static readonly Dictionary<string, PerformanceMetrics> _metrics = new();
        private static readonly object _metricsLock = new();
        private static readonly Timer _cleanupTimer;

        static PerformanceMonitor()
        {
            _cleanupTimer = new Timer(CleanupOldMetrics, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        /// <summary>
        /// 処理時間を測定
        /// </summary>
        public static async Task<TimeSpan> MeasureExecutionTimeAsync(Func<Task> action, [CallerMemberName] string operationName = null)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await action();
            }
            finally
            {
                stopwatch.Stop();
                RecordMetric(operationName ?? "AnonymousOperation", stopwatch.Elapsed);
            }
            return stopwatch.Elapsed;
        }

        /// <summary>
        /// 処理時間を測定（同期版）
        /// </summary>
        public static TimeSpan MeasureExecutionTime(Action action, [CallerMemberName] string operationName = null)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                action();
            }
            finally
            {
                stopwatch.Stop();
                RecordMetric(operationName ?? "AnonymousOperation", stopwatch.Elapsed);
            }
            return stopwatch.Elapsed;
        }

        /// <summary>
        /// メモリ使用量を監視
        /// </summary>
        public static MemorySnapshot TakeMemorySnapshot([CallerMemberName] string context = null)
        {
            var process = Process.GetCurrentProcess();
            var snapshot = new MemorySnapshot
            {
                Timestamp = DateTime.UtcNow,
                WorkingSet = process.WorkingSet64,
                PrivateMemory = process.PrivateMemorySize64,
                VirtualMemory = process.VirtualMemorySize64,
                Context = context ?? "General"
            };

            // ガベージコレクションをトリガーして正確な測定
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            snapshot.WorkingSetAfterGC = process.WorkingSet64;
            snapshot.MemoryDelta = snapshot.WorkingSet - snapshot.WorkingSetAfterGC;

            return snapshot;
        }

        /// <summary>
        /// パフォーマンスメトリクスを記録
        /// </summary>
        public static void RecordMetric(string operationName, TimeSpan duration)
        {
            lock (_metricsLock)
            {
                if (!_metrics.TryGetValue(operationName, out var metric))
                {
                    metric = new PerformanceMetrics { OperationName = operationName };
                    _metrics[operationName] = metric;
                }

                metric.TotalExecutions++;
                metric.TotalDuration += duration;
                metric.LastExecutionTime = DateTime.UtcNow;
                metric.AverageDuration = metric.TotalDuration / metric.TotalExecutions;

                if (duration > metric.MaxDuration)
                    metric.MaxDuration = duration;

                if (duration < metric.MinDuration || metric.MinDuration == TimeSpan.Zero)
                    metric.MinDuration = duration;
            }
        }

        /// <summary>
        /// パフォーマンスレポートを取得
        /// </summary>
        public static IReadOnlyDictionary<string, PerformanceMetrics> GetPerformanceReport()
        {
            lock (_metricsLock)
            {
                return new Dictionary<string, PerformanceMetrics>(_metrics);
            }
        }

        /// <summary>
        /// メモリ最適化を実行
        /// </summary>
        public static MemoryOptimizationResult OptimizeMemory()
        {
            var before = TakeMemorySnapshot("BeforeOptimization");

            // 強制ガベージコレクション
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);

            var after = TakeMemorySnapshot("AfterOptimization");

            return new MemoryOptimizationResult
            {
                MemoryBefore = before.WorkingSet,
                MemoryAfter = after.WorkingSet,
                MemorySaved = before.WorkingSet - after.WorkingSet,
                OptimizationTime = after.Timestamp - before.Timestamp
            };
        }

        /// <summary>
        /// CPU使用率を取得
        /// </summary>
        public static double GetCpuUsage()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var startTime = process.TotalProcessorTime;
                var startTimeStamp = Stopwatch.GetTimestamp();

                Thread.Sleep(100); // 100ms待機

                var endTime = process.TotalProcessorTime;
                var endTimeStamp = Stopwatch.GetTimestamp();

                var cpuUsedMs = (endTime - startTime).TotalMilliseconds;
                var totalMsPassed = (endTimeStamp - startTimeStamp) * 1000.0 / Stopwatch.Frequency;

                var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
                return Math.Min(100.0, Math.Max(0.0, cpuUsageTotal * 100.0));
            }
            catch
            {
                return 0.0;
            }
        }

        /// <summary>
        /// パフォーマンス警告をチェック
        /// </summary>
        public static List<PerformanceWarning> CheckPerformanceWarnings()
        {
            var warnings = new List<PerformanceWarning>();
            var report = GetPerformanceReport();

            foreach (var kvp in report)
            {
                var metric = kvp.Value;

                // 平均実行時間が1秒を超える場合
                if (metric.AverageDuration.TotalSeconds > 1.0)
                {
                    warnings.Add(new PerformanceWarning
                    {
                        Type = WarningType.SlowOperation,
                        Operation = metric.OperationName,
                        Message = $"Operation '{metric.OperationName}' is slow (avg: {metric.AverageDuration.TotalSeconds:F2}s)",
                        Severity = WarningSeverity.Medium
                    });
                }

                // メモリ使用量がチェック
                var memorySnapshot = TakeMemorySnapshot($"Check_{metric.OperationName}");
                if (memorySnapshot.WorkingSet > 100 * 1024 * 1024) // 100MB以上
                {
                    warnings.Add(new PerformanceWarning
                    {
                        Type = WarningType.HighMemoryUsage,
                        Operation = metric.OperationName,
                        Message = $"High memory usage detected: {memorySnapshot.WorkingSet / 1024 / 1024}MB",
                        Severity = WarningSeverity.High
                    });
                }
            }

            return warnings;
        }

        private static void CleanupOldMetrics(object state)
        {
            lock (_metricsLock)
            {
                var cutoffTime = DateTime.UtcNow.AddHours(-1); // 1時間以上前のメトリクスを削除
                var keysToRemove = new List<string>();

                foreach (var kvp in _metrics)
                {
                    if (kvp.Value.LastExecutionTime < cutoffTime)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    _metrics.Remove(key);
                }
            }
        }
    }

    /// <summary>
    /// パフォーマンスメトリクス
    /// </summary>
    public class PerformanceMetrics
    {
        public string OperationName { get; set; }
        public int TotalExecutions { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public TimeSpan AverageDuration { get; set; }
        public TimeSpan MaxDuration { get; set; }
        public TimeSpan MinDuration { get; set; }
        public DateTime LastExecutionTime { get; set; }
    }

    /// <summary>
    /// メモリスナップショット
    /// </summary>
    public class MemorySnapshot
    {
        public DateTime Timestamp { get; set; }
        public long WorkingSet { get; set; }
        public long WorkingSetAfterGC { get; set; }
        public long PrivateMemory { get; set; }
        public long VirtualMemory { get; set; }
        public long MemoryDelta { get; set; }
        public string Context { get; set; }
    }

    /// <summary>
    /// メモリ最適化結果
    /// </summary>
    public class MemoryOptimizationResult
    {
        public long MemoryBefore { get; set; }
        public long MemoryAfter { get; set; }
        public long MemorySaved { get; set; }
        public TimeSpan OptimizationTime { get; set; }
    }

    /// <summary>
    /// パフォーマンス警告
    /// </summary>
    public class PerformanceWarning
    {
        public WarningType Type { get; set; }
        public string Operation { get; set; }
        public string Message { get; set; }
        public WarningSeverity Severity { get; set; }
    }

    /// <summary>
    /// 警告タイプ
    /// </summary>
    public enum WarningType
    {
        SlowOperation,
        HighMemoryUsage,
        HighCpuUsage,
        MemoryLeak
    }

    /// <summary>
    /// 警告重要度
    /// </summary>
    public enum WarningSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }
}
