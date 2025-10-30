using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// CPUキャッシュ最適化マネージャー
    /// </summary>
    public static class CpuCacheOptimizer
    {
        private static readonly int ProcessorCount = Environment.ProcessorCount;
        private static readonly int CacheLineSize = GetCacheLineSize();

        /// <summary>
        /// キャッシュラインサイズを取得
        /// </summary>
        private static int GetCacheLineSize()
        {
            try
            {
                // Windowsでは一般的に64バイト
                // 実際の環境に合わせて調整可能
                return 64;
            }
            catch
            {
                return 64; // デフォルト値
            }
        }

        /// <summary>
        /// キャッシュフレンドリーなデータ構造を最適化
        /// </summary>
        public static T[] OptimizeForCache<T>(T[] data, int elementSize = 0)
        {
            if (data == null || data.Length == 0)
                return data;

            var estimatedElementSize = elementSize > 0 ? elementSize : EstimateElementSize<T>();
            var optimalChunkSize = Math.Max(1, CacheLineSize / estimatedElementSize);

            // データの再配置（キャッシュ効率を考慮）
            return ReorganizeForCacheLocality(data, optimalChunkSize);
        }

        /// <summary>
        /// 要素サイズを推定
        /// </summary>
        private static int EstimateElementSize<T>()
        {
            try
            {
                return Marshal.SizeOf<T>();
            }
            catch
            {
                // ジェネリック型の場合はデフォルトサイズを使用
                return Unsafe.SizeOf<T>();
            }
        }

        /// <summary>
        /// キャッシュ局所性を考慮したデータ再配置
        /// </summary>
        private static T[] ReorganizeForCacheLocality<T>(T[] data, int chunkSize)
        {
            var result = new T[data.Length];
            var chunks = new List<List<T>>();

            // データをチャンクに分割
            for (int i = 0; i < data.Length; i += chunkSize)
            {
                var chunk = new List<T>();
                for (int j = 0; j < chunkSize && i + j < data.Length; j++)
                {
                    chunk.Add(data[i + j]);
                }
                chunks.Add(chunk);
            }

            // チャンクを再配置（キャッシュ効率を向上）
            int resultIndex = 0;
            foreach (var chunk in chunks)
            {
                foreach (var item in chunk)
                {
                    result[resultIndex++] = item;
                }
            }

            return result;
        }

        /// <summary>
        /// 並行処理のキャッシュ効率を最適化
        /// </summary>
        public static async Task ProcessWithCacheOptimization<TInput, TOutput>(
            IEnumerable<TInput> input,
            Func<TInput, Task<TOutput>> processor,
            int maxConcurrency = 0)
        {
            if (maxConcurrency <= 0)
            {
                maxConcurrency = Math.Min(ProcessorCount, 4); // CPU数または4の小さい方
            }

            var semaphore = new SemaphoreSlim(maxConcurrency);
            var tasks = new List<Task<TOutput>>();

            foreach (var item in input)
            {
                await semaphore.WaitAsync();
                var task = Task.Run(async () =>
                {
                    try
                    {
                        return await processor(item);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });
                tasks.Add(task);
            }

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// メモリアクセスパターンを最適化
        /// </summary>
        public static void OptimizeMemoryAccessPattern<T>(T[] array) where T : struct
        {
            // キャッシュラインに合わせてプリフェッチ
            int prefetchDistance = CacheLineSize / Unsafe.SizeOf<T>();

            for (int i = 0; i < array.Length; i += prefetchDistance)
            {
                // プリフェッチヒント（実際のハードウェアプリフェッチに依存）
                var temp = array[Math.Min(i + prefetchDistance, array.Length - 1)];
            }
        }

        /// <summary>
        /// CPUアフィニティを最適化
        /// </summary>
        public static void OptimizeThreadAffinity()
        {
            var currentProcess = Process.GetCurrentProcess();

            // CPUアフィニティを設定（利用可能な全CPUを使用）
            try
            {
                currentProcess.ProcessorAffinity = (IntPtr)((1 << ProcessorCount) - 1);
            }
            catch
            {
                // アフィニティ設定に失敗した場合は無視
            }

            // スレッド優先度を最適化
            Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
        }
    }

    /// <summary>
    /// パフォーマンスプロファイリングマネージャー
    /// </summary>
    public static class PerformanceProfiler
    {
        private static readonly Dictionary<string, MethodProfile> _methodProfiles = new();
        private static readonly object _profileLock = new();
        private static readonly Timer _profileReportingTimer;

        static PerformanceProfiler()
        {
            _profileReportingTimer = new Timer(GenerateProfileReport, null,
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10));
        }

        /// <summary>
        /// メソッドの実行をプロファイリング
        /// </summary>
        public static async Task<T> ProfileMethodAsync<T>(Func<Task<T>> method, string methodName = null)
        {
            var profile = StartProfiling(methodName ?? method.Method.Name);
            try
            {
                var result = await method();
                EndProfiling(profile);
                return result;
            }
            catch (Exception ex)
            {
                EndProfiling(profile, ex);
                throw;
            }
        }

        /// <summary>
        /// 同期メソッドの実行をプロファイリング
        /// </summary>
        public static T ProfileMethod<T>(Func<T> method, string methodName = null)
        {
            var profile = StartProfiling(methodName ?? method.Method.Name);
            try
            {
                var result = method();
                EndProfiling(profile);
                return result;
            }
            catch (Exception ex)
            {
                EndProfiling(profile, ex);
                throw;
            }
        }

        /// <summary>
        /// メモリ割り当てを追跡しながらメソッドを実行
        /// </summary>
        public static async Task<T> ProfileMemoryAsync<T>(Func<Task<T>> method, string methodName = null)
        {
            var startMemory = GC.GetTotalMemory(false);
            var profile = StartProfiling(methodName ?? method.Method.Name);

            try
            {
                var result = await method();
                var endMemory = GC.GetTotalMemory(false);
                profile.MemoryAllocated = endMemory - startMemory;
                EndProfiling(profile);
                return result;
            }
            catch (Exception ex)
            {
                EndProfiling(profile, ex);
                throw;
            }
        }

        /// <summary>
        /// CPU使用率を監視しながらメソッドを実行
        /// </summary>
        public static async Task<T> ProfileCpuAsync<T>(Func<Task<T>> method, string methodName = null)
        {
            var process = Process.GetCurrentProcess();
            var startCpuTime = process.TotalProcessorTime;
            var profile = StartProfiling(methodName ?? method.Method.Name);

            try
            {
                var result = await method();
                var endCpuTime = process.TotalProcessorTime;
                profile.CpuTime = endCpuTime - startCpuTime;
                EndProfiling(profile);
                return result;
            }
            catch (Exception ex)
            {
                EndProfiling(profile, ex);
                throw;
            }
        }

        /// <summary>
        /// プロファイリングを開始
        /// </summary>
        private static MethodProfile StartProfiling(string methodName)
        {
            var profile = new MethodProfile
            {
                MethodName = methodName,
                StartTime = DateTime.UtcNow,
                ThreadId = Thread.CurrentThread.ManagedThreadId
            };

            lock (_profileLock)
            {
                if (!_methodProfiles.TryGetValue(methodName, out var existingProfile))
                {
                    existingProfile = new MethodProfile { MethodName = methodName };
                    _methodProfiles[methodName] = existingProfile;
                }
                existingProfile.CallCount++;
            }

            return profile;
        }

        /// <summary>
        /// プロファイリングを終了
        /// </summary>
        private static void EndProfiling(MethodProfile profile, Exception exception = null)
        {
            profile.EndTime = DateTime.UtcNow;
            profile.Duration = profile.EndTime - profile.StartTime;
            profile.Exception = exception;

            lock (_profileLock)
            {
                if (_methodProfiles.TryGetValue(profile.MethodName, out var existingProfile))
                {
                    existingProfile.TotalDuration += profile.Duration;
                    existingProfile.TotalMemoryAllocated += profile.MemoryAllocated;
                    existingProfile.TotalCpuTime += profile.CpuTime;

                    if (exception != null)
                    {
                        existingProfile.ExceptionCount++;
                    }

                    // パフォーマンス警告のチェック
                    if (profile.Duration.TotalSeconds > 5) // 5秒以上かかる場合
                    {
                        Logger.LogWarning($"Method {profile.MethodName} took {profile.Duration.TotalSeconds:F2}s to execute",
                            nameof(PerformanceProfiler));
                    }

                    if (profile.MemoryAllocated > 10 * 1024 * 1024) // 10MB以上割り当てた場合
                    {
                        Logger.LogWarning($"Method {profile.MethodName} allocated {profile.MemoryAllocated / 1024 / 1024}MB",
                            nameof(PerformanceProfiler));
                    }
                }
            }
        }

        /// <summary>
        /// プロファイルレポートを生成
        /// </summary>
        private static void GenerateProfileReport(object state)
        {
            try
            {
                lock (_profileLock)
                {
                    foreach (var kvp in _methodProfiles)
                    {
                        var profile = kvp.Value;
                        if (profile.CallCount > 0)
                        {
                            var avgDuration = profile.TotalDuration / profile.CallCount;
                            var avgMemory = profile.TotalMemoryAllocated / profile.CallCount;

                            if (avgDuration.TotalMilliseconds > 1000 || avgMemory > 1024 * 1024) // 1秒 or 1MB以上
                            {
                                Logger.LogInfo($"Performance Report - {profile.MethodName}: " +
                                    $"Calls: {profile.CallCount}, Avg Duration: {avgDuration.TotalMilliseconds:F2}ms, " +
                                    $"Avg Memory: {avgMemory / 1024:F2}KB, Exceptions: {profile.ExceptionCount}",
                                    nameof(PerformanceProfiler));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Profile report generation failed: {ex.Message}", nameof(PerformanceProfiler), null, ex);
            }
        }

        /// <summary>
        /// パフォーマンス統計を取得
        /// </summary>
        public static Dictionary<string, MethodProfile> GetPerformanceStatistics()
        {
            lock (_profileLock)
            {
                return new Dictionary<string, MethodProfile>(_methodProfiles);
            }
        }

        /// <summary>
        /// プロファイルをクリア
        /// </summary>
        public static void ClearProfiles()
        {
            lock (_profileLock)
            {
                _methodProfiles.Clear();
            }
        }
    }

    /// <summary>
    /// メモリリーク検出マネージャー
    /// </summary>
    public static class MemoryLeakDetector
    {
        private static readonly Dictionary<string, WeakReference> _objectRegistry = new();
        private static readonly Timer _leakDetectionTimer;
        private static readonly object _registryLock = new();

        static MemoryLeakDetector()
        {
            _leakDetectionTimer = new Timer(DetectLeaks, null,
                TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30));
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
        /// メモリリークレポートを取得
        /// </summary>
        public static MemoryLeakReport GetLeakReport()
        {
            lock (_registryLock)
            {
                return new MemoryLeakReport
                {
                    RegisteredObjects = _objectRegistry.Count,
                    Timestamp = DateTime.UtcNow
                };
            }
        }
    }

    // サポートクラス
    public class GCStatistics
    {
        public int Gen0Collections { get; set; }
        public int Gen1Collections { get; set; }
        public int Gen2Collections { get; set; }
        public long TotalMemory { get; set; }
        public long LastGCCollection { get; set; }
    }

    public class PrioritizedTask
    {
        public TaskPriority Priority { get; set; }
        public Func<Task> Execute { get; set; }
    }

    public enum TaskPriority
    {
        Low = 1,
        Normal = 2,
        High = 3,
        Critical = 4
    }

    public class HybridOperation<T>
    {
        public bool IsCpuBound { get; set; }
        public Func<Task<T>> Execute { get; set; }
    }

    public class NetworkMetrics
    {
        public long BytesSent { get; set; }
        public long BytesReceived { get; set; }
        public int ConnectionsCreated { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class NetworkOptimizationResult
    {
        public string OperationType { get; set; }
        public TimeSpan Duration { get; set; }
        public long BytesSent { get; set; }
        public long BytesReceived { get; set; }
        public int ConnectionsCreated { get; set; }
        public List<string> Recommendations { get; set; } = new();
    }

    public class MethodProfile
    {
        public string MethodName { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public int ThreadId { get; set; }
        public long CallCount { get; set; }
        public long TotalDurationTicks { get; set; }
        public TimeSpan TotalDuration => TimeSpan.FromTicks(TotalDurationTicks);
        public long TotalMemoryAllocated { get; set; }
        public TimeSpan TotalCpuTime { get; set; }
        public int ExceptionCount { get; set; }
        public Exception Exception { get; set; }
        public long MemoryAllocated { get; set; }
        public TimeSpan CpuTime { get; set; }
    }

    public class MemoryLeakReport
    {
        public int RegisteredObjects { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
