using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// メモリ最適化マネージャー
    /// </summary>
    public static class MemoryOptimizer
    {
        private static readonly Timer _optimizationTimer;
        private static readonly object _optimizationLock = new();
        private static DateTime _lastFullOptimization = DateTime.MinValue;
        private static long _peakMemoryUsage = 0;
        
        static MemoryOptimizer()
        {
            // 定期的な最適化（5分間隔）
            _optimizationTimer = new Timer(PerformRoutineOptimization, null, 
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }
        
        /// <summary>
        /// フルメモリ最適化を実行
        /// </summary>
        public static void OptimizeMemoryFull()
        {
            lock (_optimizationLock)
            {
                try
                {
                    var beforeMemory = GC.GetTotalMemory(false);
                    
                    // 世代別ガベージコレクション
                    GC.Collect(0, GCCollectionMode.Optimized);
                    GC.Collect(1, GCCollectionMode.Optimized);
                    GC.Collect(2, GCCollectionMode.Optimized);
                    
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    
                    // ワーキングセットの最小化
                    var process = Process.GetCurrentProcess();
                    if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                    {
                        try
                        {
                            process.MinWorkingSet = (IntPtr)(process.WorkingSet64 / 2);
                        }
                        catch
                        {
                            // ワーキングセット最小化が失敗しても続行
                        }
                    }
                    
                    var afterMemory = GC.GetTotalMemory(false);
                    var freedMemory = beforeMemory - afterMemory;
                    
                    _lastFullOptimization = DateTime.Now;
                    
                    Debug.WriteLine($"[MemoryOptimizer] Full optimization: {freedMemory / 1024}KB freed");
                }
                catch (Exception ex)
                {
                    ErrorHandler.LogError("MemoryOptimizer.OptimizeMemoryFull", ex);
                }
            }
        }
        
        /// <summary>
        /// 軽量なメモリ最適化
        /// </summary>
        public static void OptimizeMemoryLight()
        {
            try
            {
                // 第0世代のみガベージコレクション
                GC.Collect(0, GCCollectionMode.Optimized);
                
                // メモリ使用量の監視
                var currentMemory = GC.GetTotalMemory(false);
                if (currentMemory > _peakMemoryUsage)
                {
                    _peakMemoryUsage = currentMemory;
                }
                
                // 100MB以上使用している場合は詳細最適化
                if (currentMemory > 100 * 1024 * 1024)
                {
                    OptimizeCollections();
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("MemoryOptimizer.OptimizeMemoryLight", ex);
            }
        }
        
        /// <summary>
        /// コレクションクラスの最適化
        /// </summary>
        public static void OptimizeCollections()
        {
            try
            {
                // FastWifiConnectorのキャッシュクリーンアップ
                var cacheSize = FastWifiConnector.GetCachedNetworkCount();
                if (cacheSize > 100)
                {
                    FastWifiConnector.ClearCache();
                }
                
                // ConnectionLoggerのログ最適化
                ConnectionLogger.OptimizeLogBuffer();
                
                // 軽量ガベージコレクション
                GC.Collect(1, GCCollectionMode.Optimized);
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("MemoryOptimizer.OptimizeCollections", ex);
            }
        }
        
        /// <summary>
        /// メモリプレッシャーをチェック
        /// </summary>
        public static bool IsMemoryPressureHigh()
        {
            try
            {
                var currentMemory = GC.GetTotalMemory(false);
                var process = Process.GetCurrentProcess();
                var workingSet = process.WorkingSet64;
                
                // 200MB以上のワーキングセットまたは150MB以上のマネージドヒープ
                return workingSet > 200 * 1024 * 1024 || currentMemory > 150 * 1024 * 1024;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// メモリ統計を取得
        /// </summary>
        public static MemoryStats GetMemoryStats()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                return new MemoryStats
                {
                    ManagedMemory = GC.GetTotalMemory(false),
                    WorkingSet = process.WorkingSet64,
                    PeakWorkingSet = process.PeakWorkingSet64,
                    PeakManagedMemory = _peakMemoryUsage,
                    Gen0Collections = GC.CollectionCount(0),
                    Gen1Collections = GC.CollectionCount(1),
                    Gen2Collections = GC.CollectionCount(2)
                };
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("MemoryOptimizer.GetMemoryStats", ex);
                return new MemoryStats();
            }
        }
        
        /// <summary>
        /// アプリケーション終了時のクリーンアップ
        /// </summary>
        public static void Shutdown()
        {
            try
            {
                _optimizationTimer?.Dispose();
                
                // 最終クリーンアップ
                FastWifiConnector.ClearCache();
                OptimizeMemoryFull();
            }
            catch
            {
                // シャットダウン時のエラーは無視
            }
        }
        
        /// <summary>
        /// 定期的な最適化処理
        /// </summary>
        private static void PerformRoutineOptimization(object? state)
        {
            try
            {
                if (IsMemoryPressureHigh())
                {
                    OptimizeMemoryFull();
                }
                else
                {
                    OptimizeMemoryLight();
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("MemoryOptimizer.PerformRoutineOptimization", ex);
            }
        }
        
        /// <summary>
        /// 緊急メモリ解放
        /// </summary>
        public static void EmergencyMemoryRelease()
        {
            try
            {
                // 全キャッシュをクリア
                FastWifiConnector.ClearCache();
                
                // 強制ガベージコレクション
                GC.Collect(2, GCCollectionMode.Forced);
                GC.WaitForPendingFinalizers();
                GC.Collect();
                
                // 可能な限りワーキングセットを縮小
                var process = Process.GetCurrentProcess();
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    try
                    {
                        process.MinWorkingSet = IntPtr.Zero;
                    }
                    catch
                    {
                        // 失敗しても続行
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("MemoryOptimizer.EmergencyMemoryRelease", ex);
            }
        }
    }
    
    /// <summary>
    /// メモリ統計情報
    /// </summary>
    public class MemoryStats
    {
        public long ManagedMemory { get; set; }
        public long WorkingSet { get; set; }
        public long PeakWorkingSet { get; set; }
        public long PeakManagedMemory { get; set; }
        public int Gen0Collections { get; set; }
        public int Gen1Collections { get; set; }
        public int Gen2Collections { get; set; }
        
        public double ManagedMemoryMB => ManagedMemory / (1024.0 * 1024.0);
        public double WorkingSetMB => WorkingSet / (1024.0 * 1024.0);
    }
}