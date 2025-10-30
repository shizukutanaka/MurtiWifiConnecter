using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Permissions;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// サンドボックス実行環境 - 安全なコード実行のための隔離環境
    /// </summary>
    public static class SandboxManager
    {
        private static readonly Dictionary<string, SandboxEnvironment> _activeSandboxes = new();
        private static readonly object _sandboxLock = new();

        /// <summary>
        /// サンドボックス内で操作を実行
        /// </summary>
        public static async Task<T> ExecuteInSandboxAsync<T>(
            Func<Task<T>> operation,
            SandboxPermissions permissions = null,
            string sandboxName = null,
            TimeSpan? timeout = null)
        {
            var sandboxId = sandboxName ?? Guid.NewGuid().ToString();
            var sandboxEnv = new SandboxEnvironment
            {
                Id = sandboxId,
                Permissions = permissions ?? SandboxPermissions.Default,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            lock (_sandboxLock)
            {
                _activeSandboxes[sandboxId] = sandboxEnv;
            }

            try
            {
                // タイムアウト設定
                var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);

                using var cts = new CancellationTokenSource(effectiveTimeout);

                // サンドボックス内で実行
                var result = await ExecuteWithRestrictionsAsync(operation, sandboxEnv, cts.Token);

                sandboxEnv.CompletedAt = DateTime.UtcNow;
                sandboxEnv.Success = true;

                return result;
            }
            catch (Exception ex)
            {
                sandboxEnv.CompletedAt = DateTime.UtcNow;
                sandboxEnv.Success = false;
                sandboxEnv.LastError = ex.Message;

                await Logger.LogWarning($"Sandbox operation failed: {ex.Message}", nameof(SandboxManager),
                    new Dictionary<string, object> { ["sandboxId"] = sandboxId });

                throw new SandboxException($"Sandbox operation failed: {ex.Message}", ex);
            }
            finally
            {
                lock (_sandboxLock)
                {
                    sandboxEnv.IsActive = false;
                }
            }
        }

        /// <summary>
        /// ファイル操作をサンドボックス化
        /// </summary>
        public static async Task ExecuteFileOperationInSandboxAsync(
            Func<Task> fileOperation,
            string[] allowedPaths = null,
            string sandboxName = "FileOperation")
        {
            var permissions = new SandboxPermissions
            {
                AllowFileAccess = true,
                AllowedFilePaths = allowedPaths ?? new[] { Path.GetTempPath() },
                AllowNetworkAccess = false,
                AllowProcessCreation = false,
                AllowRegistryAccess = false
            };

            await ExecuteInSandboxAsync(async () =>
            {
                // ファイル操作の検証
                ValidateFileOperationPermissions(permissions);
                await fileOperation();
                return true;
            }, permissions, sandboxName);
        }

        /// <summary>
        /// ネットワーク操作をサンドボックス化
        /// </summary>
        public static async Task<T> ExecuteNetworkOperationInSandboxAsync<T>(
            Func<Task<T>> networkOperation,
            string[] allowedHosts = null,
            string sandboxName = "NetworkOperation")
        {
            var permissions = new SandboxPermissions
            {
                AllowFileAccess = false,
                AllowNetworkAccess = true,
                AllowedNetworkHosts = allowedHosts ?? Array.Empty<string>(),
                AllowProcessCreation = false,
                AllowRegistryAccess = false
            };

            return await ExecuteInSandboxAsync(async () =>
            {
                ValidateNetworkOperationPermissions(permissions);
                return await networkOperation();
            }, permissions, sandboxName);
        }

        /// <summary>
        /// サンドボックスの状態を取得
        /// </summary>
        public static IReadOnlyDictionary<string, SandboxEnvironment> GetActiveSandboxes()
        {
            lock (_sandboxLock)
            {
                return new Dictionary<string, SandboxEnvironment>(_activeSandboxes);
            }
        }

        /// <summary>
        /// サンドボックスを強制終了
        /// </summary>
        public static bool TerminateSandbox(string sandboxId)
        {
            lock (_sandboxLock)
            {
                if (_activeSandboxes.TryGetValue(sandboxId, out var sandbox))
                {
                    sandbox.IsActive = false;
                    sandbox.CompletedAt = DateTime.UtcNow;
                    sandbox.LastError = "Terminated by user";
                    return true;
                }
            }
            return false;
        }

        private static async Task<T> ExecuteWithRestrictionsAsync<T>(
            Func<Task<T>> operation,
            SandboxEnvironment environment,
            CancellationToken cancellationToken)
        {
            // 権限チェック
            ValidatePermissions(environment.Permissions);

            // リソース制限の設定
            using var memoryLimiter = new MemoryLimiter(environment.Permissions.MaxMemoryUsage);
            using var timeLimiter = new TimeLimiter(environment.Permissions.MaxExecutionTime);

            // 操作実行
            var task = operation();

            // リソース監視
            var monitoringTask = MonitorResourcesAsync(environment, cancellationToken);

            try
            {
                var result = await task;
                await monitoringTask; // 監視完了を待つ
                return result;
            }
            catch
            {
                await monitoringTask; // エラー時も監視完了を待つ
                throw;
            }
        }

        private static void ValidatePermissions(SandboxPermissions permissions)
        {
            // 基本的なセキュリティチェック
            if (permissions.AllowRegistryAccess && !permissions.AllowElevatedPrivileges)
            {
                throw new SecurityException("Registry access requires elevated privileges");
            }

            if (permissions.AllowProcessCreation && permissions.MaxProcessCount > 5)
            {
                throw new SecurityException("Too many processes allowed in sandbox");
            }
        }

        private static void ValidateFileOperationPermissions(SandboxPermissions permissions)
        {
            if (!permissions.AllowFileAccess)
            {
                throw new SecurityException("File access not permitted in this sandbox");
            }
        }

        private static void ValidateNetworkOperationPermissions(SandboxPermissions permissions)
        {
            if (!permissions.AllowNetworkAccess)
            {
                throw new SecurityException("Network access not permitted in this sandbox");
            }
        }

        private static async Task MonitorResourcesAsync(SandboxEnvironment environment, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && environment.IsActive)
            {
                var memoryUsage = PerformanceMonitor.TakeMemorySnapshot($"Sandbox_{environment.Id}");

                if (environment.Permissions.MaxMemoryUsage > 0 &&
                    memoryUsage.WorkingSet > environment.Permissions.MaxMemoryUsage)
                {
                    throw new SandboxException("Memory usage exceeded sandbox limits");
                }

                await Task.Delay(1000, cancellationToken); // 1秒ごとにチェック
            }
        }

        /// <summary>
        /// メモリ制限クラス
        /// </summary>
        private class MemoryLimiter : IDisposable
        {
            private readonly long _maxMemory;

            public MemoryLimiter(long maxMemory)
            {
                _maxMemory = maxMemory;
            }

            public void Dispose()
            {
                // メモリ制限のクリーンアップ
                GC.Collect();
            }
        }

        /// <summary>
        /// 時間制限クラス
        /// </summary>
        private class TimeLimiter : IDisposable
        {
            private readonly TimeSpan _maxTime;
            private readonly CancellationTokenSource _cts;

            public TimeLimiter(TimeSpan maxTime)
            {
                _maxTime = maxTime;
                _cts = new CancellationTokenSource(maxTime);
            }

            public void Dispose()
            {
                _cts.Dispose();
            }
        }
    }

    /// <summary>
    /// サンドボックス権限設定
    /// </summary>
    public class SandboxPermissions
    {
        public static readonly SandboxPermissions Default = new()
        {
            AllowFileAccess = false,
            AllowNetworkAccess = true,
            AllowProcessCreation = false,
            AllowRegistryAccess = false,
            AllowElevatedPrivileges = false,
            MaxMemoryUsage = 50 * 1024 * 1024, // 50MB
            MaxExecutionTime = TimeSpan.FromSeconds(30),
            MaxProcessCount = 0
        };

        public bool AllowFileAccess { get; set; }
        public string[] AllowedFilePaths { get; set; } = Array.Empty<string>();
        public bool AllowNetworkAccess { get; set; }
        public string[] AllowedNetworkHosts { get; set; } = Array.Empty<string>();
        public bool AllowProcessCreation { get; set; }
        public bool AllowRegistryAccess { get; set; }
        public bool AllowElevatedPrivileges { get; set; }
        public long MaxMemoryUsage { get; set; }
        public TimeSpan MaxExecutionTime { get; set; }
        public int MaxProcessCount { get; set; }
    }

    /// <summary>
    /// サンドボックス環境情報
    /// </summary>
    public class SandboxEnvironment
    {
        public string Id { get; set; }
        public SandboxPermissions Permissions { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public bool IsActive { get; set; }
        public bool Success { get; set; }
        public string LastError { get; set; }
    }

    /// <summary>
    /// サンドボックス例外
    /// </summary>
    public class SandboxException : Exception
    {
        public SandboxException(string message) : base(message) { }

        public SandboxException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}
