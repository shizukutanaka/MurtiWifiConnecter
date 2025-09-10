using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Services
{
    /// <summary>
    /// 接続情報キャッシュ - パフォーマンス最適化
    /// </summary>
    public static class ConnectionCache
    {
        private static readonly ConcurrentDictionary<string, CachedConnectionInfo> _cache = new();
        private static readonly SemaphoreSlim _cleanupLock = new(1, 1);
        private static DateTime _lastCleanup = DateTime.MinValue;
        
        // キャッシュ設定
        private const int MaxCacheSize = 50;
        private const int DefaultTtlSeconds = 300; // 5分
        private const int FastTtlSeconds = 30;     // 30秒（高頻度アクセス用）
        private const int SlowTtlSeconds = 900;    // 15分（低頻度アクセス用）

        /// <summary>
        /// 接続情報をキャッシュから取得または作成
        /// </summary>
        public static async Task<T> GetOrSetAsync<T>(
            string key, 
            Func<Task<T>> factory, 
            CacheLevel level = CacheLevel.Normal)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be empty", nameof(key));

            var ttl = GetTtlForLevel(level);
            
            // キャッシュヒット確認
            if (_cache.TryGetValue(key, out var cached) && !cached.IsExpired)
            {
                if (cached.Data is T cachedData)
                {
                    cached.HitCount++;
                    cached.LastAccessed = DateTime.Now;
                    return cachedData;
                }
            }

            // キャッシュミス - 新しいデータを取得
            try
            {
                var data = await factory();
                
                var cacheInfo = new CachedConnectionInfo
                {
                    Key = key,
                    Data = data,
                    CreatedAt = DateTime.Now,
                    LastAccessed = DateTime.Now,
                    ExpiresAt = DateTime.Now.AddSeconds(ttl),
                    Level = level,
                    HitCount = 0
                };

                _cache[key] = cacheInfo;

                // 定期クリーンアップ
                _ = Task.Run(async () => await TryCleanupAsync());

                return data;
            }
            catch (Exception ex)
            {
                SimpleLoggingService.LogError($"Cache factory failed for key: {key}", ex);
                
                // 期限切れキャッシュでもエラー時はそのまま返す
                if (cached?.Data is T fallbackData)
                {
                    SimpleLoggingService.LogWarning($"Using expired cache for key: {key}");
                    return fallbackData;
                }
                
                throw;
            }
        }

        /// <summary>
        /// キャッシュから削除
        /// </summary>
        public static bool Remove(string key)
        {
            return _cache.TryRemove(key, out _);
        }

        /// <summary>
        /// 特定のプレフィックスのキャッシュを削除
        /// </summary>
        public static void RemoveByPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
                return;

            var keysToRemove = new List<string>();
            
            foreach (var kvp in _cache)
            {
                if (kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                _cache.TryRemove(key, out _);
            }

            SimpleLoggingService.LogInfo($"Removed {keysToRemove.Count} cache entries with prefix: {prefix}");
        }

        /// <summary>
        /// キャッシュクリア
        /// </summary>
        public static void Clear()
        {
            var count = _cache.Count;
            _cache.Clear();
            SimpleLoggingService.LogInfo($"Cleared {count} cache entries");
        }

        /// <summary>
        /// キャッシュ統計取得
        /// </summary>
        public static CacheStatistics GetStatistics()
        {
            var stats = new CacheStatistics
            {
                TotalEntries = _cache.Count,
                LastCleanup = _lastCleanup
            };

            foreach (var entry in _cache.Values)
            {
                stats.TotalHits += entry.HitCount;
                
                if (entry.IsExpired)
                    stats.ExpiredEntries++;
                else
                    stats.ValidEntries++;
            }

            if (stats.TotalEntries > 0)
            {
                stats.AverageHits = stats.TotalHits / (double)stats.TotalEntries;
            }

            return stats;
        }

        /// <summary>
        /// 高頻度アクセスキーをファスト化
        /// </summary>
        public static void PromoteToFast(string key)
        {
            if (_cache.TryGetValue(key, out var cached) && cached.Level != CacheLevel.Fast)
            {
                cached.Level = CacheLevel.Fast;
                cached.ExpiresAt = DateTime.Now.AddSeconds(FastTtlSeconds);
            }
        }

        private static int GetTtlForLevel(CacheLevel level)
        {
            return level switch
            {
                CacheLevel.Fast => FastTtlSeconds,
                CacheLevel.Slow => SlowTtlSeconds,
                _ => DefaultTtlSeconds
            };
        }

        private static async Task TryCleanupAsync()
        {
            // 頻繁なクリーンアップを防ぐ
            if ((DateTime.Now - _lastCleanup).TotalMinutes < 2)
                return;

            if (!await _cleanupLock.WaitAsync(100))
                return;

            try
            {
                await CleanupExpiredEntriesAsync();
                _lastCleanup = DateTime.Now;
            }
            finally
            {
                _cleanupLock.Release();
            }
        }

        private static async Task CleanupExpiredEntriesAsync()
        {
            var expiredKeys = new List<string>();
            var now = DateTime.Now;

            // 期限切れエントリを特定
            foreach (var kvp in _cache)
            {
                if (kvp.Value.IsExpired)
                {
                    expiredKeys.Add(kvp.Key);
                }
            }

            // サイズ制限チェック
            if (_cache.Count > MaxCacheSize)
            {
                var entriesToRemove = _cache.Count - MaxCacheSize;
                var leastUsed = _cache.Values
                    .Where(v => !expiredKeys.Contains(v.Key))
                    .OrderBy(v => v.HitCount)
                    .ThenBy(v => v.LastAccessed)
                    .Take(entriesToRemove)
                    .Select(v => v.Key)
                    .ToList();

                expiredKeys.AddRange(leastUsed);
            }

            // 削除実行
            foreach (var key in expiredKeys.Distinct())
            {
                _cache.TryRemove(key, out _);
            }

            if (expiredKeys.Count > 0)
            {
                SimpleLoggingService.LogInfo($"Cache cleanup: removed {expiredKeys.Count} entries");
            }

            await Task.CompletedTask;
        }
    }

    internal class CachedConnectionInfo
    {
        public string Key { get; set; }
        public object Data { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastAccessed { get; set; }
        public DateTime ExpiresAt { get; set; }
        public CacheLevel Level { get; set; }
        public long HitCount { get; set; }

        public bool IsExpired => DateTime.Now > ExpiresAt;
    }

    public enum CacheLevel
    {
        Fast,    // 30秒
        Normal,  // 5分
        Slow     // 15分
    }

    public class CacheStatistics
    {
        public int TotalEntries { get; set; }
        public int ValidEntries { get; set; }
        public int ExpiredEntries { get; set; }
        public long TotalHits { get; set; }
        public double AverageHits { get; set; }
        public DateTime LastCleanup { get; set; }

        public double HitRatio => TotalEntries > 0 ? ValidEntries / (double)TotalEntries : 0;
    }

    /// <summary>
    /// 接続キャッシュのヘルパーメソッド
    /// </summary>
    public static class ConnectionCacheHelper
    {
        public static string CreateKey(string category, params string[] parameters)
        {
            return $"{category}:{string.Join(":", parameters)}";
        }

        public static async Task<string> GetCurrentSSIDCachedAsync()
        {
            return await ConnectionCache.GetOrSetAsync(
                "current_ssid",
                async () => await NetworkUtils.GetCurrentConnectedSSIDAsync(),
                CacheLevel.Fast
            );
        }

        public static async Task<Dictionary<string, int>> GetNetworksCachedAsync()
        {
            return await ConnectionCache.GetOrSetAsync(
                "networks_scan", 
                async () => await NetworkUtils.ScanWifiNetworksAsync(),
                CacheLevel.Normal
            );
        }

        public static void InvalidateNetworkCache()
        {
            ConnectionCache.RemoveByPrefix("networks");
            ConnectionCache.RemoveByPrefix("current_ssid");
        }
    }
}