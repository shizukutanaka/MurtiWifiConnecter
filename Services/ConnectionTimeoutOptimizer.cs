using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Services
{
    /// <summary>
    /// 接続タイムアウト最適化サービス
    /// </summary>
    public static class ConnectionTimeoutOptimizer
    {
        private static readonly Dictionary<string, TimeoutProfile> _operationTimeouts = new()
        {
            // 高速操作 (500-1500ms)
            { "status_check", new TimeoutProfile(500, 1500, 250) },
            { "interface_query", new TimeoutProfile(500, 1500, 250) },
            { "current_ssid", new TimeoutProfile(500, 1500, 250) },
            
            // 中速操作 (1000-3000ms)
            { "wifi_scan", new TimeoutProfile(1000, 3000, 500) },
            { "profile_list", new TimeoutProfile(1000, 2000, 300) },
            { "disconnect", new TimeoutProfile(1000, 2000, 300) },
            
            // 低速操作 (2000-8000ms)
            { "wifi_connect", new TimeoutProfile(2000, 8000, 1000) },
            { "profile_add", new TimeoutProfile(1500, 5000, 500) },
            { "profile_delete", new TimeoutProfile(1000, 3000, 500) },
            
            // 診断操作 (可変)
            { "network_test", new TimeoutProfile(2000, 10000, 1000) },
            { "speed_test", new TimeoutProfile(3000, 15000, 2000) }
        };

        private static readonly Dictionary<string, int> _operationHistory = new();
        private static readonly object _lockObject = new object();
        private static DateTime _lastOptimization = DateTime.MinValue;

        /// <summary>
        /// 操作に最適なタイムアウトを取得
        /// </summary>
        public static int GetOptimalTimeout(string operationType, bool isRetry = false)
        {
            if (!_operationTimeouts.TryGetValue(operationType, out var profile))
            {
                // デフォルトタイムアウト
                return isRetry ? 5000 : 3000;
            }

            // 履歴からの学習
            lock (_lockObject)
            {
                if (_operationHistory.TryGetValue(operationType, out var avgTime))
                {
                    // 平均時間の1.5倍をタイムアウトとする
                    var adaptive = (int)(avgTime * 1.5);
                    return Math.Min(Math.Max(adaptive, profile.MinTimeout), profile.MaxTimeout);
                }
            }

            // リトライ時は長めのタイムアウト
            if (isRetry)
            {
                return (int)(profile.MaxTimeout * 0.8);
            }

            // 初回は中間値
            return (profile.MinTimeout + profile.MaxTimeout) / 2;
        }

        /// <summary>
        /// 実行時間を記録して学習
        /// </summary>
        public static void RecordExecutionTime(string operationType, int milliseconds)
        {
            lock (_lockObject)
            {
                if (!_operationHistory.ContainsKey(operationType))
                {
                    _operationHistory[operationType] = milliseconds;
                }
                else
                {
                    // 移動平均で更新
                    var current = _operationHistory[operationType];
                    _operationHistory[operationType] = (current * 3 + milliseconds) / 4;
                }

                // 定期的にメモリクリーンアップ
                if (DateTime.Now - _lastOptimization > TimeSpan.FromMinutes(30))
                {
                    OptimizeHistory();
                    _lastOptimization = DateTime.Now;
                }
            }
        }

        /// <summary>
        /// 段階的タイムアウト実行
        /// </summary>
        public static async Task<T> ExecuteWithAdaptiveTimeout<T>(
            string operationType,
            Func<int, CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            var profile = _operationTimeouts.GetValueOrDefault(operationType) ?? 
                         new TimeoutProfile(1000, 5000, 500);
            
            var currentTimeout = profile.MinTimeout;
            var attempts = 0;
            var maxAttempts = 3;
            
            while (attempts < maxAttempts)
            {
                attempts++;
                
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(currentTimeout);
                
                var startTime = Environment.TickCount;
                
                try
                {
                    var result = await operation(currentTimeout, cts.Token);
                    
                    // 成功時は実行時間を記録
                    var elapsed = Environment.TickCount - startTime;
                    RecordExecutionTime(operationType, elapsed);
                    
                    return result;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // タイムアウトした場合は次のタイムアウトで再試行
                    currentTimeout = Math.Min(currentTimeout + profile.Increment, profile.MaxTimeout);
                    
                    if (attempts >= maxAttempts)
                        throw new TimeoutException($"Operation {operationType} timed out after {maxAttempts} attempts");
                    
                    // 短い待機後に再試行
                    await Task.Delay(Math.Min(500 * attempts, 2000), cancellationToken);
                }
            }
            
            throw new TimeoutException($"Operation {operationType} failed after {maxAttempts} attempts");
        }

        /// <summary>
        /// 非同期操作を段階的タイムアウトで実行
        /// </summary>
        public static async Task<bool> TryExecuteWithTimeout(
            string operationType,
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await ExecuteWithAdaptiveTimeout(
                    operationType,
                    async (timeout, ct) =>
                    {
                        await operation(ct);
                        return true;
                    },
                    cancellationToken);
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        /// <summary>
        /// 履歴データの最適化
        /// </summary>
        private static void OptimizeHistory()
        {
            lock (_lockObject)
            {
                // 極端な値をリセット
                var keysToUpdate = new List<string>();
                
                foreach (var kvp in _operationHistory)
                {
                    if (_operationTimeouts.TryGetValue(kvp.Key, out var profile))
                    {
                        if (kvp.Value < profile.MinTimeout / 2 || kvp.Value > profile.MaxTimeout * 2)
                        {
                            keysToUpdate.Add(kvp.Key);
                        }
                    }
                }
                
                foreach (var key in keysToUpdate)
                {
                    _operationHistory.Remove(key);
                }
            }
        }

        /// <summary>
        /// 統計情報を取得
        /// </summary>
        public static Dictionary<string, TimeoutStats> GetStatistics()
        {
            var stats = new Dictionary<string, TimeoutStats>();
            
            lock (_lockObject)
            {
                foreach (var kvp in _operationTimeouts)
                {
                    var avgTime = _operationHistory.GetValueOrDefault(kvp.Key, 0);
                    stats[kvp.Key] = new TimeoutStats
                    {
                        OperationType = kvp.Key,
                        MinTimeout = kvp.Value.MinTimeout,
                        MaxTimeout = kvp.Value.MaxTimeout,
                        CurrentAverage = avgTime,
                        OptimalTimeout = GetOptimalTimeout(kvp.Key)
                    };
                }
            }
            
            return stats;
        }

        /// <summary>
        /// 統計をリセット
        /// </summary>
        public static void ResetStatistics()
        {
            lock (_lockObject)
            {
                _operationHistory.Clear();
            }
        }

        private class TimeoutProfile
        {
            public int MinTimeout { get; }
            public int MaxTimeout { get; }
            public int Increment { get; }

            public TimeoutProfile(int min, int max, int increment)
            {
                MinTimeout = min;
                MaxTimeout = max;
                Increment = increment;
            }
        }
    }

    public class TimeoutStats
    {
        public string OperationType { get; set; }
        public int MinTimeout { get; set; }
        public int MaxTimeout { get; set; }
        public int CurrentAverage { get; set; }
        public int OptimalTimeout { get; set; }
    }
}