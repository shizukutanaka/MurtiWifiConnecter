using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// 非同期操作マネージャー - 並行処理の最適化と効率化
    /// </summary>
    public static class AsyncOperationManager
    {
        private static readonly SemaphoreSlim _concurrencyLimiter = new SemaphoreSlim(Environment.ProcessorCount * 2);
        private static readonly ConcurrentDictionary<string, OperationMetrics> _operationMetrics = new();
        private static readonly BufferBlock<AsyncOperation> _operationQueue = new BufferBlock<AsyncOperation>();
        private static readonly ActionBlock<AsyncOperation> _operationProcessor;

        static AsyncOperationManager()
        {
            // 操作処理ブロックの設定
            _operationProcessor = new ActionBlock<AsyncOperation>(
                async operation => await ProcessOperationAsync(operation),
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount,
                    BoundedCapacity = 1000,
                    EnsureOrdered = false // 順序保証なしでパフォーマンス優先
                });

            // キューとプロセッサを接続
            _operationQueue.LinkTo(_operationProcessor);

            // 定期的なメトリクスクリーンアップ
            var cleanupTimer = new Timer(_ => CleanupMetrics(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        /// <summary>
        /// 非同期操作をキューに追加
        /// </summary>
        public static async Task<T> EnqueueOperationAsync<T>(Func<Task<T>> operation, string operationName = null)
        {
            var asyncOp = new AsyncOperation
            {
                Id = Guid.NewGuid().ToString(),
                Name = operationName ?? "AnonymousOperation",
                OperationTask = async () =>
                {
                    // 並行処理制限
                    await _concurrencyLimiter.WaitAsync();
                    try
                    {
                        using (var scope = PerformanceMonitor.MeasureExecutionTimeAsync(async () =>
                        {
                            var result = await operation();
                            return result;
                        }, operationName))
                        {
                            var result = await operation();
                            return result;
                        }
                    }
                    finally
                    {
                        _concurrencyLimiter.Release();
                    }
                },
                StartTime = DateTime.UtcNow
            };

            await _operationQueue.SendAsync(asyncOp);

            // 操作完了を待つ
            await asyncOp.Completion.Task;

            if (asyncOp.Exception != null)
                throw asyncOp.Exception;

            return (T)asyncOp.Result;
        }

        /// <summary>
        /// バッチ操作を最適化して実行
        /// </summary>
        public static async Task<IEnumerable<T>> ExecuteBatchAsync<T>(
            IEnumerable<Func<Task<T>>> operations,
            int maxConcurrency = 0,
            string batchName = null)
        {
            if (maxConcurrency <= 0)
                maxConcurrency = Environment.ProcessorCount * 2;

            var semaphore = new SemaphoreSlim(maxConcurrency);
            var tasks = new List<Task<T>>();

            foreach (var operation in operations)
            {
                await semaphore.WaitAsync();
                var task = Task.Run(async () =>
                {
                    try
                    {
                        return await operation();
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });
                tasks.Add(task);
            }

            var results = await Task.WhenAll(tasks);
            RecordBatchMetrics(batchName ?? "BatchOperation", results.Length, DateTime.UtcNow);

            return results;
        }

        /// <summary>
        /// タイムアウト付き非同期操作
        /// </summary>
        public static async Task<T> ExecuteWithTimeoutAsync<T>(
            Func<Task<T>> operation,
            TimeSpan timeout,
            string operationName = null)
        {
            using var cts = new CancellationTokenSource(timeout);

            try
            {
                var result = await EnqueueOperationAsync(async () =>
                {
                    cts.Token.ThrowIfCancellationRequested();
                    return await operation();
                }, operationName ?? "TimeoutOperation");

                return result;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                throw new TimeoutException($"Operation '{operationName}' timed out after {timeout.TotalSeconds} seconds");
            }
        }

        /// <summary>
        /// 操作メトリクスを取得
        /// </summary>
        public static IReadOnlyDictionary<string, OperationMetrics> GetOperationMetrics()
        {
            return new Dictionary<string, OperationMetrics>(_operationMetrics);
        }

        /// <summary>
        /// 操作の優先度を設定
        /// </summary>
        public static async Task<T> ExecuteWithPriorityAsync<T>(
            Func<Task<T>> operation,
            OperationPriority priority,
            string operationName = null)
        {
            // 優先度に基づいて処理（現在は全て同じ処理）
            // 将来的に優先度キューを実装可能
            return await EnqueueOperationAsync(operation, operationName ?? "PriorityOperation");
        }

        /// <summary>
        /// リソース使用量を監視しながら操作を実行
        /// </summary>
        public static async Task<T> ExecuteWithResourceMonitoringAsync<T>(
            Func<Task<T>> operation,
            string operationName = null)
        {
            var beforeMemory = PerformanceMonitor.TakeMemorySnapshot("BeforeOperation");
            var beforeCpu = PerformanceMonitor.GetCpuUsage();

            try
            {
                var result = await operation();

                var afterMemory = PerformanceMonitor.TakeMemorySnapshot("AfterOperation");
                var afterCpu = PerformanceMonitor.GetCpuUsage();

                // リソース使用量を記録
                RecordResourceUsage(operationName ?? "MonitoredOperation",
                    beforeMemory, afterMemory, beforeCpu, afterCpu);

                return result;
            }
            catch
            {
                // エラー時もリソース使用量を記録
                var afterMemory = PerformanceMonitor.TakeMemorySnapshot("AfterOperation_Error");
                var afterCpu = PerformanceMonitor.GetCpuUsage();

                RecordResourceUsage(operationName ?? "MonitoredOperation_Error",
                    beforeMemory, afterMemory, beforeCpu, afterCpu);

                throw;
            }
        }

        private static async Task ProcessOperationAsync(AsyncOperation operation)
        {
            try
            {
                operation.Result = await operation.OperationTask();
                RecordOperationMetrics(operation.Name, DateTime.UtcNow - operation.StartTime, true);
            }
            catch (Exception ex)
            {
                operation.Exception = ex;
                RecordOperationMetrics(operation.Name, DateTime.UtcNow - operation.StartTime, false);
            }
            finally
            {
                operation.Completion.SetResult(true);
            }
        }

        private static void RecordOperationMetrics(string operationName, TimeSpan duration, bool success)
        {
            var metrics = _operationMetrics.GetOrAdd(operationName, _ => new OperationMetrics { OperationName = operationName });

            Interlocked.Increment(ref metrics.TotalExecutions);
            metrics.TotalDuration += duration;

            if (success)
                Interlocked.Increment(ref metrics.SuccessCount);
            else
                Interlocked.Increment(ref metrics.FailureCount);

            metrics.LastExecutionTime = DateTime.UtcNow;
            metrics.AverageDuration = metrics.TotalDuration / metrics.TotalExecutions;
        }

        private static void RecordBatchMetrics(string batchName, int operationCount, DateTime completionTime)
        {
            var metrics = _operationMetrics.GetOrAdd(batchName, _ => new OperationMetrics { OperationName = batchName });
            Interlocked.Add(ref metrics.TotalExecutions, operationCount);
            metrics.LastExecutionTime = completionTime;
        }

        private static void RecordResourceUsage(string operationName, MemorySnapshot before, MemorySnapshot after, double beforeCpu, double afterCpu)
        {
            var metrics = _operationMetrics.GetOrAdd(operationName, _ => new OperationMetrics { OperationName = operationName });

            metrics.MemoryDelta = after.WorkingSet - before.WorkingSet;
            metrics.CpuUsageDelta = afterCpu - beforeCpu;
            metrics.LastResourceCheck = DateTime.UtcNow;
        }

        private static void CleanupMetrics()
        {
            var cutoffTime = DateTime.UtcNow.AddHours(-1);

            foreach (var key in _operationMetrics.Keys)
            {
                if (_operationMetrics.TryGetValue(key, out var metrics))
                {
                    if (metrics.LastExecutionTime < cutoffTime)
                    {
                        _operationMetrics.TryRemove(key, out _);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 非同期操作
    /// </summary>
    internal class AsyncOperation
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public Func<Task<object>> OperationTask { get; set; }
        public DateTime StartTime { get; set; }
        public object Result { get; set; }
        public Exception Exception { get; set; }
        public TaskCompletionSource<bool> Completion { get; } = new TaskCompletionSource<bool>();
    }

    /// <summary>
    /// 操作優先度
    /// </summary>
    public enum OperationPriority
    {
        Low,
        Normal,
        High,
        Critical
    }

    /// <summary>
    /// 操作メトリクス
    /// </summary>
    public class OperationMetrics
    {
        public string OperationName { get; set; }
        public long TotalExecutions { get; set; }
        public long SuccessCount { get; set; }
        public long FailureCount { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public TimeSpan AverageDuration { get; set; }
        public DateTime LastExecutionTime { get; set; }
        public long MemoryDelta { get; set; }
        public double CpuUsageDelta { get; set; }
        public DateTime LastResourceCheck { get; set; }

        public double SuccessRate => TotalExecutions > 0 ? (double)SuccessCount / TotalExecutions : 0;
    }
}
