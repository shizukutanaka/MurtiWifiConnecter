using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// シンプルなシステム管理クラス
    /// </summary>
    public static class SystemManager
    {
        private static readonly object _memoryLock = new();
        private static DateTime _lastMemoryOptimization = DateTime.MinValue;
        
        /// <summary>
        /// メモリ最適化（統合版）
        /// </summary>
        public static void OptimizeMemory()
        {
            lock (_memoryLock)
            {
                // 30秒に1回以上は実行しない
                if (DateTime.Now - _lastMemoryOptimization < TimeSpan.FromSeconds(30))
                    return;
                
                _lastMemoryOptimization = DateTime.Now;
                
                try
                {
                    // メモリ使用量をチェック
                    var workingSet = GC.GetTotalMemory(false);
                    var isHighPressure = workingSet > 100_000_000; // 100MB以上で高負荷
                    
                    if (isHighPressure)
                    {
                        // フル最適化
                        GC.Collect(0, GCCollectionMode.Optimized);
                        GC.Collect(1, GCCollectionMode.Optimized);
                        GC.Collect(2, GCCollectionMode.Optimized);
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                    }
                    else
                    {
                        // 軽量最適化
                        GC.Collect(0, GCCollectionMode.Optimized);
                    }
                    
                    // プロセス優先度を調整
                    var currentProcess = Process.GetCurrentProcess();
                    if (isHighPressure && currentProcess.PriorityClass != ProcessPriorityClass.BelowNormal)
                    {
                        currentProcess.PriorityClass = ProcessPriorityClass.BelowNormal;
                    }
                    else if (!isHighPressure && currentProcess.PriorityClass == ProcessPriorityClass.BelowNormal)
                    {
                        currentProcess.PriorityClass = ProcessPriorityClass.Normal;
                    }
                }
                catch (Exception ex)
                {
                    ErrorHandler.LogError("SystemManager.OptimizeMemory", ex);
                }
            }
        }
        
        /// <summary>
        /// 起動時の最適化
        /// </summary>
        public static async Task OptimizeStartupAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    // 起動時のプロセス優先度設定
                    var currentProcess = Process.GetCurrentProcess();
                    currentProcess.PriorityClass = ProcessPriorityClass.Normal;
                    
                    // 初期メモリ最適化
                    OptimizeMemory();
                }
                catch
                {
                    // 最適化エラーは無視
                }
            });
        }
        
        /// <summary>
        /// 定期的な最適化
        /// </summary>
        public static async Task RunPeriodicOptimizationAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(10), cancellationToken);
                
                if (!cancellationToken.IsCancellationRequested)
                {
                    OptimizeMemory();
                }
            }
        }
        
        /// <summary>
        /// 現在のヘルス状態を取得
        /// </summary>
        public static SystemHealth GetCurrentHealth()
        {
            var process = Process.GetCurrentProcess();
            var workingSet = process.WorkingSet64;
            
            // 簡単なヘルスチェック（200MB以上でWarning）
            var status = workingSet > 200_000_000 ? HealthStatus.Warning : HealthStatus.Good;
            
            return new SystemHealth
            {
                Status = status,
                MemoryUsageMB = workingSet / (1024 * 1024)
            };
        }
        
        /// <summary>
        /// ネットワークスキャンの記録（統計用）
        /// </summary>
        public static void RecordNetworkScan(int networksFound, TimeSpan duration)
        {
            // 統計記録（現在は空実装）
            Debug.WriteLine($"Network scan completed: {networksFound} networks in {duration.TotalMilliseconds}ms");
        }
    }
    
    /// <summary>
    /// システムヘルス状態
    /// </summary>
    public class SystemHealth
    {
        public HealthStatus Status { get; set; } = HealthStatus.Good;
        public long MemoryUsageMB { get; set; }
    }
    
    /// <summary>
    /// ヘルスステータス
    /// </summary>
    public enum HealthStatus
    {
        Good,
        Warning,
        Critical
    }
}