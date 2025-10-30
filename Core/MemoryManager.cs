using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// メモリ管理と最適化ユーティリティ（強化版）
    /// </summary>
    public static class MemoryManager
    {
        private static readonly Timer _memoryMaintenanceTimer;
        private static long _totalAllocatedObjects;
        private static long _totalDeallocatedObjects;
        private static readonly ConcurrentDictionary<string, MemoryPool> _dynamicPools = new();
        private static readonly MemoryPressureMonitor _pressureMonitor = new();

        static MemoryManager()
        {
            _memoryMaintenanceTimer = new Timer(MemoryMaintenanceCallback, null,
                TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        }

        /// <summary>
        /// メモリ使用量を最適化
        /// </summary>
        public static MemoryOptimizationResult OptimizeMemoryUsage()
        {
            var before = PerformanceMonitor.TakeMemorySnapshot("BeforeOptimization");

            // 強制ガベージコレクション（ジェネレーション0-2）
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // LOH (Large Object Heap) の最適化
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);

            var after = PerformanceMonitor.TakeMemorySnapshot("AfterOptimization");

            return new MemoryOptimizationResult
            {
                MemoryBefore = before.WorkingSet,
                MemoryAfter = after.WorkingSet,
                MemorySaved = before.WorkingSet - after.WorkingSet,
                OptimizationTime = after.Timestamp - before.Timestamp
            };
        }

        /// <summary>
        /// 定期的なメモリメンテナンス
        /// </summary>
        private static void MemoryMaintenanceCallback(object state)
        {
            try
            {
                var warnings = PerformanceMonitor.CheckPerformanceWarnings();

                foreach (var warning in warnings)
                {
                    if (warning.Type == WarningType.HighMemoryUsage)
                    {
                        // メモリ使用量が高い場合、最適化を実行
                        var result = OptimizeMemoryUsage();
                        if (result.MemorySaved > 10 * 1024 * 1024) // 10MB以上削減された場合
                        {
                            Logger.LogInfo($"Memory optimized: {result.MemorySaved / 1024 / 1024}MB saved",
                                nameof(MemoryManager));
                        }
                    }
                }

                // 定期的なGC（ジェネレーション0のみ）
                GC.Collect(0);

                // 動的プールのメンテナンス
                MaintainDynamicPools();

                // メモリ圧力の監視
                _pressureMonitor.CheckMemoryPressure();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Memory maintenance failed: {ex.Message}", nameof(MemoryManager), null, ex);
            }
        }

        /// <summary>
        /// メモリ使用量を監視し、閾値を超えたら警告
        /// </summary>
        public static bool CheckMemoryThreshold(long thresholdBytes = 100_000_000) // 100MB
        {
            var snapshot = PerformanceMonitor.TakeMemorySnapshot("ThresholdCheck");
            return snapshot.WorkingSet > thresholdBytes;
        }

        /// <summary>
        /// 動的メモリプールの作成
        /// </summary>
        public static MemoryPool CreateDynamicPool<T>(string poolName, int initialSize = 10, int maxSize = 100) where T : class, new()
        {
            var pool = new MemoryPool(typeof(T), poolName, initialSize, maxSize);
            _dynamicPools[poolName] = pool;
            Logger.LogInfo($"Dynamic memory pool created: {poolName} for type {typeof(T).Name}", nameof(MemoryManager));
            return pool;
        }

        /// <summary>
        /// 動的プールからオブジェクトを取得
        /// </summary>
        public static T GetFromPool<T>(string poolName) where T : class, new()
        {
            if (_dynamicPools.TryGetValue(poolName, out var pool))
            {
                return pool.Get() as T ?? new T();
            }

            // プールが存在しない場合、動的に作成
            var newPool = CreateDynamicPool<T>(poolName);
            return newPool.Get() as T ?? new T();
        }

        /// <summary>
        /// 動的プールにオブジェクトを返却
        /// </summary>
        public static void ReturnToPool<T>(string poolName, T obj) where T : class
        {
            if (_dynamicPools.TryGetValue(poolName, out var pool))
            {
                pool.Return(obj);
            }
        }

        /// <summary>
        /// 動的プールのメンテナンス
        /// </summary>
        private static void MaintainDynamicPools()
        {
            foreach (var kvp in _dynamicPools)
            {
                kvp.Value.Maintain();
            }

            // 使用されていないプールのクリーンアップ
            var poolsToRemove = new List<string>();
            foreach (var kvp in _dynamicPools)
            {
                if (kvp.Value.IsUnused && kvp.Value.Age.TotalHours > 1)
                {
                    poolsToRemove.Add(kvp.Key);
                }
            }

            foreach (var poolName in poolsToRemove)
            {
                _dynamicPools.TryRemove(poolName, out _);
                Logger.LogInfo($"Unused memory pool removed: {poolName}", nameof(MemoryManager));
            }
        }

        /// <summary>
        /// メモリ使用量の予測
        /// </summary>
        public static MemoryPrediction PredictMemoryUsage(int timeWindowMinutes = 30)
        {
            var currentMemory = PerformanceMonitor.TakeMemorySnapshot("Prediction");
            var prediction = new MemoryPrediction
            {
                CurrentUsage = currentMemory.WorkingSet,
                PredictedUsage = currentMemory.WorkingSet,
                TimeWindow = TimeSpan.FromMinutes(timeWindowMinutes),
                Confidence = 0.5 // 基本的な予測のため中程度の信頼性
            };

            // 簡易的な予測ロジック（実際のアプリケーションではより複雑なアルゴリズムを使用）
            var memoryTrend = CalculateMemoryTrend(timeWindowMinutes);
            prediction.PredictedUsage += (long)(memoryTrend * timeWindowMinutes * 60 * 1024); // 1KB/min trend

            if (prediction.PredictedUsage > 500 * 1024 * 1024) // 500MB以上予測される場合
            {
                prediction.Recommendations.Add("Consider implementing memory optimization measures");
                prediction.ShouldOptimize = true;
            }

            return prediction;
        }

        /// <summary>
        /// メモリ使用量のトレンドを計算
        /// </summary>
        private static double CalculateMemoryTrend(int minutes)
        {
            // 簡易実装：実際のアプリケーションでは履歴データを分析
            var process = Process.GetCurrentProcess();
            var privateMemory = process.PrivateMemorySize64;

            // メモリ増加率を計算（簡易版）
            return privateMemory > 100 * 1024 * 1024 ? 0.1 : -0.05; // 100MB以上なら増加傾向
        }

        /// <summary>
        /// メモリ断片化を監視
        /// </summary>
        public static FragmentationAnalysis AnalyzeFragmentation()
        {
            var analysis = new FragmentationAnalysis
            {
                Timestamp = DateTime.UtcNow,
                TotalMemory = GC.GetTotalMemory(false),
                Generation0Collections = GC.CollectionCount(0),
                Generation1Collections = GC.CollectionCount(1),
                Generation2Collections = GC.CollectionCount(2)
            };

            // 断片化の推定（LOHのサイズをチェック）
            analysis.LargeObjectHeapSize = GetLargeObjectHeapSize();
            analysis.FragmentationRatio = CalculateFragmentationRatio();

            if (analysis.FragmentationRatio > 0.3) // 30%以上の断片化
            {
                analysis.Recommendations.Add("High memory fragmentation detected. Consider compacting LOH.");
                analysis.ShouldCompact = true;
            }

            return analysis;
        }

        /// <summary>
        /// Large Object Heapサイズを取得
        /// </summary>
        private static long GetLargeObjectHeapSize()
        {
            // LOHサイズの推定（簡易実装）
            return GC.GetTotalMemory(false) / 4; // 概算
        }

        /// <summary>
        /// 断片化率を計算
        /// </summary>
        private static double CalculateFragmentationRatio()
        {
            // 断片化率の計算（簡易実装）
            var totalMemory = GC.GetTotalMemory(false);
            var gen2Collections = GC.CollectionCount(2);

            // Gen2コレクションが多いほど断片化の可能性が高い
            return Math.Min(gen2Collections / 100.0, 1.0);
        }

        /// <summary>
        /// メモリリークの検出を開始
        /// </summary>
        public static void StartLeakDetection()
        {
            MemoryLeakDetector.RegisterLeakDetectionTimer();
        }

        /// <summary>
        /// メモリリーク検出を停止
        /// </summary>
        public static void StopLeakDetection()
        {
            MemoryLeakDetector.UnregisterLeakDetectionTimer();
        }

        /// <summary>
        /// メモリ使用量のレポートを生成
        /// </summary>
        public static MemoryReport GenerateMemoryReport()
        {
            var report = new MemoryReport
            {
                Timestamp = DateTime.UtcNow,
                TotalAllocatedObjects = _totalAllocatedObjects,
                TotalDeallocatedObjects = _totalDeallocatedObjects,
                ActivePools = _dynamicPools.Count,
                MemoryPressure = _pressureMonitor.GetCurrentPressure(),
                FragmentationAnalysis = AnalyzeFragmentation(),
                MemoryPrediction = PredictMemoryUsage()
            };

            // プール統計の収集
            foreach (var kvp in _dynamicPools)
            {
                var poolStats = kvp.Value.GetStatistics();
                report.PoolStatistics[kvp.Key] = poolStats;
            }

            return report;
        }
    }

    /// <summary>
    /// 動的メモリプール
    /// </summary>
    public class MemoryPool
    {
        private readonly ConcurrentBag<object> _pool = new();
        private readonly Type _objectType;
        private readonly string _poolName;
        private readonly int _maxSize;
        private readonly Func<object> _factory;
        private readonly Action<object> _resetAction;
        private int _createdCount;
        private DateTime _lastAccessTime = DateTime.UtcNow;
        private int _accessCount;

        public MemoryPool(Type objectType, string poolName, int initialSize = 10, int maxSize = 100,
                         Func<object> factory = null, Action<object> resetAction = null)
        {
            _objectType = objectType;
            _poolName = poolName;
            _maxSize = maxSize;
            _factory = factory ?? (() => Activator.CreateInstance(objectType));
            _resetAction = resetAction ?? (_ => { });

            // 初期プールを準備
            for (int i = 0; i < initialSize; i++)
            {
                _pool.Add(_factory());
                _createdCount++;
            }
        }

        /// <summary>
        /// オブジェクトを取得
        /// </summary>
        public object Get()
        {
            _lastAccessTime = DateTime.UtcNow;
            _accessCount++;

            if (_pool.TryTake(out var item))
            {
                return item;
            }

            // プールが空の場合、新規作成
            if (_createdCount < _maxSize)
            {
                Interlocked.Increment(ref _createdCount);
                return _factory();
            }

            // 最大サイズに達した場合、新規作成（制限なし）
            return _factory();
        }

        /// <summary>
        /// オブジェクトを返却
        /// </summary>
        public void Return(object item)
        {
            if (item == null) return;

            // リセット処理
            _resetAction(item);

            // プールに戻す（サイズ制限以内）
            if (_pool.Count < _maxSize)
            {
                _pool.Add(item);
            }
        }

        /// <summary>
        /// プールのメンテナンス
        /// </summary>
        public void Maintain()
        {
            // 古いオブジェクトのクリーンアップ（必要に応じて）
            var currentCount = _pool.Count;
            if (currentCount > _maxSize / 2 && Age.TotalMinutes > 30)
            {
                // プールの半分をクリーンアップ
                var itemsToRemove = new List<object>();
                for (int i = 0; i < currentCount / 2 && _pool.TryTake(out var item); i++)
                {
                    itemsToRemove.Add(item);
                }

                Logger.LogDebug($"Pool {_poolName} maintained: removed {itemsToRemove.Count} items",
                    nameof(MemoryPool));
            }
        }

        /// <summary>
        /// 使用されていないかを確認
        /// </summary>
        public bool IsUnused => Age.TotalMinutes > 10 && _accessCount == 0;

        /// <summary>
        /// 最後のアクセスからの経過時間
        /// </summary>
        public TimeSpan Age => DateTime.UtcNow - _lastAccessTime;

        /// <summary>
        /// プールの統計情報を取得
        /// </summary>
        public PoolStatistics GetStatistics()
        {
            return new PoolStatistics
            {
                PoolName = _poolName,
                ObjectType = _objectType.Name,
                AvailableCount = _pool.Count,
                CreatedCount = _createdCount,
                MaxSize = _maxSize,
                AccessCount = _accessCount,
                LastAccessTime = _lastAccessTime,
                Age = Age
            };
        }
    }

    /// <summary>
    /// メモリ圧力監視
    /// </summary>
    public class MemoryPressureMonitor
    {
        private long _lastMemoryUsage;
        private DateTime _lastCheck = DateTime.UtcNow;
        private readonly Queue<long> _memoryHistory = new Queue<long>();
        private const int HistorySize = 10;

        public void CheckMemoryPressure()
        {
            var currentMemory = PerformanceMonitor.TakeMemorySnapshot("PressureCheck").WorkingSet;
            var now = DateTime.UtcNow;

            // 履歴に追加
            _memoryHistory.Enqueue(currentMemory);
            if (_memoryHistory.Count > HistorySize)
            {
                _memoryHistory.Dequeue();
            }

            // メモリ圧力を計算
            var pressure = CalculateMemoryPressure(currentMemory);

            if (pressure > 0.8) // 80%以上の圧力
            {
                Logger.LogWarning($"High memory pressure detected: {pressure:P0}", nameof(MemoryPressureMonitor));
                TriggerMemoryOptimization();
            }

            _lastMemoryUsage = currentMemory;
            _lastCheck = now;
        }

        private double CalculateMemoryPressure(long currentMemory)
        {
            // 利用可能なメモリに基づいて圧力を計算
            var totalMemory = GC.GetTotalMemory(false);
            return (double)currentMemory / (currentMemory + totalMemory);
        }

        private void TriggerMemoryOptimization()
        {
            // メモリ最適化をトリガー
            var result = MemoryManager.OptimizeMemoryUsage();
            if (result.MemorySaved > 1024 * 1024) // 1MB以上削減
            {
                Logger.LogInfo($"Memory optimization triggered: {result.MemorySaved / 1024 / 1024}MB saved",
                    nameof(MemoryPressureMonitor));
            }
        }

        public double GetCurrentPressure()
        {
            var currentMemory = PerformanceMonitor.TakeMemorySnapshot("PressureQuery").WorkingSet;
            return CalculateMemoryPressure(currentMemory);
        }
    }

    /// <summary>
    /// メモリリーク検出
    /// </summary>
    public static class MemoryLeakDetector
    {
        private static readonly Dictionary<string, WeakReference> _objectRegistry = new();
        private static Timer _leakDetectionTimer;
        private static readonly object _registryLock = new();

        public static void RegisterLeakDetectionTimer()
        {
            if (_leakDetectionTimer == null)
            {
                _leakDetectionTimer = new Timer(DetectLeaks, null,
                    TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30));
            }
        }

        public static void UnregisterLeakDetectionTimer()
        {
            _leakDetectionTimer?.Dispose();
            _leakDetectionTimer = null;
        }

        /// <summary>
        /// メモリリークを検出
        /// </summary>
        private static void DetectLeaks(object state)
        {
            try
            {
                var leakedObjects = new List<string>();

                lock (_registryLock)
                {
                    foreach (var kvp in _objectRegistry)
                    {
                        if (!kvp.Value.IsAlive)
                        {
                            leakedObjects.Add(kvp.Key);
                        }
                    }

                    // ガベージコレクション済みのオブジェクトをクリーンアップ
                    foreach (var leaked in leakedObjects)
                    {
                        _objectRegistry.Remove(leaked);
                    }
                }

                if (leakedObjects.Count > 0)
                {
                    Logger.LogWarning($"Potential memory leaks detected: {string.Join(", ", leakedObjects)}",
                        nameof(MemoryLeakDetector));
                }

                // 全体的なメモリ使用量をチェック
                var memoryInfo = PerformanceMonitor.TakeMemorySnapshot("LeakDetection");
                if (memoryInfo.WorkingSet > 500 * 1024 * 1024) // 500MB以上
                {
                    Logger.LogWarning($"High memory usage detected: {memoryInfo.WorkingSet / 1024 / 1024}MB",
                        nameof(MemoryLeakDetector));
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Memory leak detection failed: {ex.Message}", nameof(MemoryLeakDetector), null, ex);
            }
        }

        /// <summary>
        /// オブジェクトを監視対象に登録
        /// </summary>
        public static void RegisterObject(object obj, string objectId)
        {
            lock (_registryLock)
            {
                _objectRegistry[objectId] = new WeakReference(obj);
            }
        }

        /// <summary>
        /// オブジェクトの監視を解除
        /// </summary>
        public static void UnregisterObject(string objectId)
        {
            lock (_registryLock)
            {
                _objectRegistry.Remove(objectId);
            }
        }
    }

    /// <summary>
    /// メモリ予測
    /// </summary>
    public class MemoryPrediction
    {
        public long CurrentUsage { get; set; }
        public long PredictedUsage { get; set; }
        public TimeSpan TimeWindow { get; set; }
        public double Confidence { get; set; }
        public bool ShouldOptimize { get; set; }
        public List<string> Recommendations { get; set; } = new();
    }

    /// <summary>
    /// 断片化分析
    /// </summary>
    public class FragmentationAnalysis
    {
        public DateTime Timestamp { get; set; }
        public long TotalMemory { get; set; }
        public int Generation0Collections { get; set; }
        public int Generation1Collections { get; set; }
        public int Generation2Collections { get; set; }
        public long LargeObjectHeapSize { get; set; }
        public double FragmentationRatio { get; set; }
        public bool ShouldCompact { get; set; }
        public List<string> Recommendations { get; set; } = new();
    }

    /// <summary>
    /// メモリレポート
    /// </summary>
    public class MemoryReport
    {
        public DateTime Timestamp { get; set; }
        public long TotalAllocatedObjects { get; set; }
        public long TotalDeallocatedObjects { get; set; }
        public int ActivePools { get; set; }
        public double MemoryPressure { get; set; }
        public FragmentationAnalysis FragmentationAnalysis { get; set; }
        public MemoryPrediction MemoryPrediction { get; set; }
        public Dictionary<string, PoolStatistics> PoolStatistics { get; set; } = new();
    }

    /// <summary>
    /// プール統計
    /// </summary>
    public class PoolStatistics
    {
        public string PoolName { get; set; }
        public string ObjectType { get; set; }
        public int AvailableCount { get; set; }
        public int CreatedCount { get; set; }
        public int MaxSize { get; set; }
        public int AccessCount { get; set; }
        public DateTime LastAccessTime { get; set; }
        public TimeSpan Age { get; set; }
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
}
