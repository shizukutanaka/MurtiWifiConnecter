using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MurtiWifiConnecter.Infrastructure.Resilience;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// 統合エラー処理システム
    /// </summary>
    public static class ErrorHandler
    {
        private static readonly ConcurrentDictionary<string, ErrorStats> _errorStats = new();
        private static long _totalErrors = 0;
        private static readonly ConcurrentDictionary<string, RetryPolicy> _retryPolicies = new();
        private static readonly ConcurrentDictionary<string, CircuitBreakerState> _circuitBreakers = new();
        private static readonly Timer _cleanupTimer;
        private static readonly Timer _circuitBreakerTimer;
        
        static ErrorHandler()
        {
            InitializeDefaultRetryPolicies();
            // 1時間ごとに古いエラー統計をクリーンアップ
            _cleanupTimer = new Timer(CleanupOldStats, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
            // サーキットブレーカーの定期チェック
            _circuitBreakerTimer = new Timer(UpdateCircuitBreakers, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }
        
        /// <summary>
        /// エラーをログに記録
        /// </summary>
        public static void LogError(string context, Exception ex,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            try
            {
                Interlocked.Increment(ref _totalErrors);
                
                // エラー統計を更新
                var errorKey = $"{context}:{ex.GetType().Name}";
                _errorStats.AddOrUpdate(errorKey,
                    new ErrorStats { Count = 1, LastOccurrence = DateTime.Now, Context = context },
                    (key, existing) => { existing.Count++; existing.LastOccurrence = DateTime.Now; return existing; });
                
                // 統合ログサービスにログ出力
                var message = $"[{context}] {ex.GetType().Name}: {ex.Message} at {System.IO.Path.GetFileName(filePath)}:{lineNumber} in {memberName}";
                Services.Log.Error(message, ex);
                
                // デバッグ出力
                Debug.WriteLine($"[ERROR] {context}: {ex.Message}");
                
                // 自動回復の試行
                TryAutoRecovery(context, ex);
            }
            catch
            {
                // エラーハンドラ内でのエラーは無視
            }
        }
        
        /// <summary>
        /// リトライポリシーに基づいた操作の実行
        /// </summary>
        public static async Task<T> ExecuteWithRetryAsync<T>(
            string operation,
            Func<CancellationToken, Task<T>> func,
            RetryPolicy? customPolicy = null,
            CancellationToken cancellationToken = default)
        {
            var policy = customPolicy ?? GetRetryPolicy(operation);
            var attempts = 0;
            var lastException = null as Exception;
            
            while (attempts < policy.MaxAttempts && !cancellationToken.IsCancellationRequested)
            {
                attempts++;
                
                try
                {
                    return await func(cancellationToken);
                }
                catch (Exception ex) when (attempts < policy.MaxAttempts && policy.ShouldRetry(ex))
                {
                    lastException = ex;
                    var delay = policy.GetDelay(attempts);
                    
                    LogError($"{operation}.Retry", ex);
                    
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, cancellationToken);
                    }
                }
            }
            
            throw lastException ?? new InvalidOperationException($"操作 {operation} が {attempts} 回の試行後に失敗しました");
        }
        
        /// <summary>
        /// エラー回復の試行
        /// </summary>
        public static async Task<RecoveryResult> AttemptRecoveryAsync(
            string context,
            Exception exception,
            Func<Task<bool>> recoveryAction,
            CancellationToken cancellationToken = default)
        {
            var result = new RecoveryResult
            {
                Context = context,
                StartTime = DateTime.Now
            };
            
            try
            {
                LogError($"{context}.RecoveryAttempt", exception);
                
                result.Success = await recoveryAction();
                result.Duration = DateTime.Now - result.StartTime;
                
                if (result.Success)
                {
                    Debug.WriteLine($"[RECOVERY] {context}: 回復成功");
                }
                else
                {
                    Debug.WriteLine($"[RECOVERY] {context}: 回復失敗");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.Duration = DateTime.Now - result.StartTime;
                LogError($"{context}.RecoveryFailed", ex);
                return result;
            }
        }
        
        /// <summary>
        /// サーキットブレーカー付きで操作を実行
        /// </summary>
        public static async Task<T> ExecuteWithCircuitBreakerAsync<T>(
            string operation,
            Func<Task<T>> func,
            int failureThreshold = 5,
            TimeSpan? breakDuration = null,
            T? defaultValue = default)
        {
            var circuitBreaker = GetOrCreateCircuitBreaker(operation, failureThreshold, breakDuration ?? TimeSpan.FromMinutes(2));
            
            // サーキットがオープンの場合
            if (circuitBreaker.State == CircuitBreakerStateEnum.Open)
            {
                if (DateTime.Now - circuitBreaker.LastFailure < circuitBreaker.BreakDuration)
                {
                    LogError($"{operation}.CircuitOpen", new InvalidOperationException("Circuit breaker is open"));
                    return defaultValue;
                }
                else
                {
                    // ハーフオープン状態に移行
                    circuitBreaker.State = CircuitBreakerStateEnum.HalfOpen;
                }
            }
            
            try
            {
                var result = await func();
                
                // 成功時の処理
                if (circuitBreaker.State == CircuitBreakerStateEnum.HalfOpen)
                {
                    circuitBreaker.State = CircuitBreakerStateEnum.Closed;
                    circuitBreaker.FailureCount = 0;
                }
                
                return result;
            }
            catch (Exception ex)
            {
                circuitBreaker.FailureCount++;
                circuitBreaker.LastFailure = DateTime.Now;
                
                // 閾値を超えた場合はサーキットをオープン
                if (circuitBreaker.FailureCount >= circuitBreaker.FailureThreshold)
                {
                    circuitBreaker.State = CircuitBreakerStateEnum.Open;
                }
                
                LogError($"{operation}.CircuitBreaker", ex);
                throw;
            }
        }
        
        /// <summary>
        /// エラーの種類を判定
        /// </summary>
        public static ErrorCategory CategorizeError(Exception ex)
        {
            return ex switch
            {
                UnauthorizedAccessException => ErrorCategory.Security,
                System.Net.NetworkInformation.NetworkInformationException => ErrorCategory.Network,
                TimeoutException => ErrorCategory.Timeout,
                IOException => ErrorCategory.IO,
                OutOfMemoryException => ErrorCategory.Resource,
                OperationCanceledException => ErrorCategory.Cancellation,
                ArgumentException => ErrorCategory.Validation,
                System.Management.ManagementException => ErrorCategory.System,
                _ => ErrorCategory.Unknown
            };
        }
        
        /// <summary>
        /// ユーザーフレンドリーなエラーメッセージを生成
        /// </summary>
        public static string GetUserFriendlyErrorMessage(Exception ex)
        {
            var category = CategorizeError(ex);
            
            return category switch
            {
                ErrorCategory.Security => "管理者権限が必要です。アプリケーションを管理者として実行してください。",
                ErrorCategory.Network => "ネットワーク接続に問題があります。WiFiアダプターの状態を確認してください。",
                ErrorCategory.Timeout => "接続がタイムアウトしました。しばらく待ってから再試行してください。",
                ErrorCategory.IO => "ファイルの読み書きに失敗しました。ディスク容量やアクセス権限を確認してください。",
                ErrorCategory.Resource => "メモリ不足です。他のアプリケーションを終了してから再試行してください。",
                ErrorCategory.Cancellation => "操作がキャンセルされました。",
                ErrorCategory.Validation => "入力データに問題があります。SSIDやパスワードを確認してください。",
                ErrorCategory.System => "システムエラーが発生しました。WindowsのWiFiサービスを確認してください。",
                _ => $"予期しないエラーが発生しました: {ex.Message}"
            };
        }
        
        /// <summary>
        /// エラー統計を取得
        /// </summary>
        public static ErrorStatsSummary GetErrorStatistics()
        {
            return new ErrorStatsSummary
            {
                TotalErrors = _totalErrors,
                ErrorsByContext = _errorStats.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (kvp.Value.Count, kvp.Value.LastOccurrence)),
                TopErrors = _errorStats
                    .OrderByDescending(kvp => kvp.Value.Count)
                    .Take(10)
                    .Select(kvp => new ErrorSummaryItem
                    {
                        Context = kvp.Value.Context,
                        ErrorType = kvp.Key.Split(':')[1],
                        Count = kvp.Value.Count,
                        LastOccurrence = kvp.Value.LastOccurrence
                    })
                    .ToList()
            };
        }
        
        private static void InitializeDefaultRetryPolicies()
        {
            _retryPolicies["NetworkConnection"] = new RetryPolicy
            {
                MaxAttempts = 3,
                InitialDelay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromSeconds(10),
                BackoffMultiplier = 2,
                ShouldRetry = ex => ex is TimeoutException || ex.Message.Contains("ネットワーク")
            };
            
            _retryPolicies["FileOperation"] = new RetryPolicy
            {
                MaxAttempts = 2,
                InitialDelay = TimeSpan.FromMilliseconds(500),
                MaxDelay = TimeSpan.FromSeconds(2),
                BackoffMultiplier = 1.5,
                ShouldRetry = ex => ex is System.IO.IOException
            };
            
            _retryPolicies["Default"] = new RetryPolicy
            {
                MaxAttempts = 2,
                InitialDelay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromSeconds(5),
                BackoffMultiplier = 2,
                ShouldRetry = ex => !(ex is ArgumentException || ex is InvalidOperationException)
            };
        }
        
        private static RetryPolicy GetRetryPolicy(string operation)
        {
            return _retryPolicies.GetValueOrDefault(operation) ?? _retryPolicies["Default"];
        }
        
        private static CircuitBreakerState GetOrCreateCircuitBreaker(string operation, int failureThreshold, TimeSpan breakDuration)
        {
            return _circuitBreakers.GetOrAdd(operation, _ => new CircuitBreakerState
            {
                Operation = operation,
                FailureThreshold = failureThreshold,
                BreakDuration = breakDuration,
                State = CircuitBreakerStateEnum.Closed
            });
        }
        
        private static void UpdateCircuitBreakers(object? state)
        {
            try
            {
                var now = DateTime.Now;
                foreach (var breaker in _circuitBreakers.Values)
                {
                    // オープン状態で十分な時間が経過した場合、ハーフオープンに移行
                    if (breaker.State == CircuitBreakerStateEnum.Open &&
                        now - breaker.LastFailure > breaker.BreakDuration)
                    {
                        breaker.State = CircuitBreakerStateEnum.HalfOpen;
                    }
                }
            }
            catch
            {
                // サーキットブレーカー更新エラーは無視
            }
        }
        
        private static void CleanupOldStats(object? state)
        {
            try
            {
                var cutoffTime = DateTime.Now.AddHours(-24);
                var keysToRemove = _errorStats
                    .Where(kvp => kvp.Value.LastOccurrence < cutoffTime)
                    .Select(kvp => kvp.Key)
                    .ToList();
                
                foreach (var key in keysToRemove)
                {
                    _errorStats.TryRemove(key, out _);
                }
                
                // 古いサーキットブレーカーもクリーンアップ
                var breakersToRemove = _circuitBreakers
                    .Where(kvp => kvp.Value.LastFailure < cutoffTime)
                    .Select(kvp => kvp.Key)
                    .ToList();
                    
                foreach (var key in breakersToRemove)
                {
                    _circuitBreakers.TryRemove(key, out _);
                }
            }
            catch
            {
                // クリーンアップエラーは無視
            }
        }
        
        // 内部クラス
        private class ErrorStats
        {
            public int Count { get; set; }
            public DateTime LastOccurrence { get; set; }
            public string Context { get; set; } = string.Empty;
        }
    }
    
    /// <summary>
    /// リトライポリシー
    /// </summary>
    public class RetryPolicy
    {
        public int MaxAttempts { get; set; } = 3;
        public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);
        public double BackoffMultiplier { get; set; } = 2.0;
        public Func<Exception, bool> ShouldRetry { get; set; } = ex => true;
        
        public TimeSpan GetDelay(int attemptNumber)
        {
            var delay = TimeSpan.FromMilliseconds(InitialDelay.TotalMilliseconds * Math.Pow(BackoffMultiplier, attemptNumber - 1));
            return delay > MaxDelay ? MaxDelay : delay;
        }
    }
    
    /// <summary>
    /// 回復結果
    /// </summary>
    public class RecoveryResult
    {
        public bool Success { get; set; }
        public string Context { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public DateTime StartTime { get; set; }
        public TimeSpan Duration { get; set; }
        /// <summary>
        /// 自動回復を試行
        /// </summary>
        private static void TryAutoRecovery(string context, Exception ex)
        {
            try
            {
                // メモリ不足エラー
                if (ex is OutOfMemoryException)
                {
                    Task.Run(() => SystemManager.OptimizeMemory());
                    return;
                }
                
                // ファイルアクセスエラー
                if (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // 少し待ってから処理を続行
                    Task.Delay(1000);
                    return;
                }
                
                // ネットワークエラー
                if (ex is System.Net.NetworkInformation.PingException || 
                    ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase))
                {
                    // ネットワーク診断を実行
                    Task.Run(async () => 
                    {
                        try
                        {
                            await NetworkDiagnostics.RunBasicDiagnosticsAsync();
                        }
                        catch { }
                    });
                    return;
                }
                
                // WiFi接続エラー
                if (context.Contains("WiFi", StringComparison.OrdinalIgnoreCase) ||
                    context.Contains("Connection", StringComparison.OrdinalIgnoreCase))
                {
                    // 最適化されたスキャナーは削除されました
                    return;
                }
            }
            catch
            {
                // 自動回復中のエラーは無視
            }
        }
    }
    
    /// <summary>
    /// エラー統計サマリー
    /// </summary>
    public class ErrorStatsSummary
    {
        public long TotalErrors { get; set; }
        public Dictionary<string, (int Count, DateTime LastOccurrence)> ErrorsByContext { get; set; } = new();
        public List<ErrorSummaryItem> TopErrors { get; set; } = new();
    }
    
    /// <summary>
    /// エラーサマリー項目
    /// </summary>
    public class ErrorSummaryItem
    {
        public string Context { get; set; } = string.Empty;
        public string ErrorType { get; set; } = string.Empty;
        public int Count { get; set; }
        public DateTime LastOccurrence { get; set; }
    }
    
    /// <summary>
    /// サーキットブレーナーの状態
    /// </summary>
    public class CircuitBreakerState
    {
        public string Operation { get; set; } = string.Empty;
        public CircuitBreakerStateEnum State { get; set; } = CircuitBreakerStateEnum.Closed;
        public int FailureCount { get; set; }
        public int FailureThreshold { get; set; } = 5;
        public DateTime LastFailure { get; set; } = DateTime.MinValue;
        public TimeSpan BreakDuration { get; set; } = TimeSpan.FromMinutes(2);
    }
    
    /// <summary>
    /// サーキットブレーナーの状態列挙
    /// </summary>
    public enum CircuitBreakerStateEnum
    {
        Closed,    // 正常状態
        Open,      // エラーが多く、リクエストをブロック
        HalfOpen   // テスト的にリクエストを許可
    }
    
    /// <summary>
    /// エラーのカテゴリー
    /// </summary>
    public enum ErrorCategory
    {
        Unknown,
        Security,      // セキュリティ関連
        Network,       // ネットワーク関連
        Timeout,       // タイムアウト
        IO,            // ファイルI/O
        Resource,      // リソース不足
        Cancellation,  // キャンセル
        Validation,    // 入力検証
        System         // システムエラー
    }

    /// <summary>
    /// 最近のエラーを取得
    /// </summary>
    public static List<ErrorInfo> GetRecentErrors(int maxCount = 50)
    {
        try
        {
            var recentErrors = new List<ErrorInfo>();
            var cutoffTime = DateTime.Now.AddHours(-24);

            foreach (var kvp in _errorStats)
            {
                if (kvp.Value.LastOccurrence >= cutoffTime)
                {
                    recentErrors.Add(new ErrorInfo
                    {
                        Timestamp = kvp.Value.LastOccurrence,
                        Category = kvp.Value.Context,
                        Message = $"エラー発生回数: {kvp.Value.Count}回",
                        Details = $"エラーキー: {kvp.Key}"
                    });
                }
            }

            return recentErrors.OrderByDescending(e => e.Timestamp)
                              .Take(maxCount)
                              .ToList();
        }
        catch
        {
            return new List<ErrorInfo>();
        }
    }
}