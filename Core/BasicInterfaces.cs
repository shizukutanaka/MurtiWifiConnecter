using System;
using System.Collections.Generic;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// Basic WiFi service interface
    /// </summary>
    public interface IWifiService
    {
        Task<Result<WifiConnectionResult>> ConnectAsync(string ssid, string password, CancellationToken ct = default);
        Task<Result<WifiConnectionResult>> ConnectSecureAsync(string ssid, SecureString password, CancellationToken ct = default);
        Task<Result<bool>> DisconnectAsync(CancellationToken ct = default);
        Task<Result<string>> GetCurrentSSIDAsync(CancellationToken ct = default);
        Task<Result<NetworkInfo>> GetCurrentNetworkInfoAsync(CancellationToken ct = default);
        Task<Result<bool>> ValidateConnectionAsync(string ssid, CancellationToken ct = default);
        Task<Result<IReadOnlyList<WifiAdapterInfo>>> GetAvailableAdaptersAsync(CancellationToken ct = default);
        void SetPreferredAdapter(string? adapterName);
        string? GetPreferredAdapter();

        event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;
        event EventHandler<WifiErrorEventArgs>? ErrorOccurred;
    }

    /// <summary>
    /// Basic validation utilities
    /// </summary>
    public static class Validation
    {
        public static Result<string> SanitizeSSID(string ssid)
        {
            if (string.IsNullOrWhiteSpace(ssid))
                return Result<string>.Failure("SSID cannot be empty");

            ssid = ssid.Trim();

            if (ssid.Length > 32)
                return Result<string>.Failure("SSID cannot exceed 32 characters");

            // Remove potentially dangerous characters
            ssid = System.Text.RegularExpressions.Regex.Replace(ssid, @"[\""|&;$`<>()]", "");

            return Result<string>.Success(ssid);
        }

        public static bool IsValidPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
                return false;

            if (password.Length < 8 || password.Length > 63)
                return false;

            return true;
        }
    }

    /// <summary>
    /// Basic performance utilities
    /// </summary>
    public static class PerformanceOptimizations
    {
        private static string? _cachedSSID;
        private static DateTime _cacheTime = DateTime.MinValue;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(5);

        public static bool ShouldAttemptConnection(string ssid, string password)
        {
            if (string.IsNullOrWhiteSpace(ssid) || string.IsNullOrWhiteSpace(password))
                return false;

            if (ssid.Length > 32 || password.Length < 8 || password.Length > 63)
                return false;

            return true;
        }

        public static bool IsConnectionCheckFresh(string ssid)
        {
            if (_cachedSSID == null || !_cachedSSID.Equals(ssid, StringComparison.OrdinalIgnoreCase))
                return false;

            return DateTime.UtcNow - _cacheTime < CacheDuration;
        }

        public static void UpdateConnectionCache(string ssid)
        {
            _cachedSSID = ssid;
            _cacheTime = DateTime.UtcNow;
        }

        public static bool SSIDEquals(string ssid1, string ssid2)
        {
            return string.Equals(ssid1, ssid2, StringComparison.OrdinalIgnoreCase);
        }

        public static int GetOptimalTimeout(int attempt, bool isRetry)
        {
            if (isRetry)
                return 8000 + (attempt * 2000); // 8s base + 2s per attempt

            return attempt == 0 ? 5000 : 8000; // 5s first attempt, 8s retry
        }

        public static void ScheduleBackgroundTask(Func<Task> task)
        {
            // Simple fire-and-forget background task
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000); // Small delay to not interfere with current operation
                try
                {
                    await task();
                }
                catch
                {
                    // Ignore background task errors
                }
            });
        }
    }

    /// <summary>
    /// Simple memory optimizer
    /// </summary>
    public static class MemoryOptimizer
    {
        private static DateTime _lastOptimization = DateTime.MinValue;
        private static readonly TimeSpan OptimizationInterval = TimeSpan.FromMinutes(5);

        public static async Task OptimizeIfNeededAsync()
        {
            if (DateTime.UtcNow - _lastOptimization < OptimizationInterval)
                return;

            await Task.Run(() =>
            {
                try
                {
                    GC.Collect(0, GCCollectionMode.Optimized, false);
                    _lastOptimization = DateTime.UtcNow;
                }
                catch
                {
                    // Ignore GC errors
                }
            });
        }
    }
}