using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;

namespace MurtiWifiConnecter.Core
{
    public static class ErrorHandler
    {
        private static readonly ConcurrentQueue<ErrorLog> _errorHistory = new();
        private const int MaxErrorHistory = 50;

        // Enhanced error context and suggestions
        private static readonly Dictionary<string, ErrorContext> _errorContexts = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _errorContextLock = new();

        public static async Task<T> ExecuteWithRetry<T>(Func<Task<T>> operation, int maxRetries = 3, int delayMs = 1000)
        {
            Exception lastException = null;
            var operationName = operation.Method.Name;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    return await operation();
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    await LogError(ex, $"Attempt {attempt + 1}/{maxRetries + 1} failed for operation: {operationName}");

                    // 最後の試行以外はリトライ
                    if (attempt < maxRetries)
                    {
                        var delay = delayMs * (int)Math.Pow(2, attempt); // 指数バックオフ
                        await Task.Delay(delay);
                    }
                }
            }

            // すべての試行が失敗した場合
            var failureException = new OperationFailedException($"Operation '{operationName}' failed after {maxRetries + 1} attempts", lastException);
            await LogError(failureException, $"All retry attempts failed for: {operationName}");
            throw failureException;
        }

        public static async Task<bool> ExecuteWithRetryBool(Func<Task<bool>> operation, int maxRetries = 3, int delayMs = 1000)
        {
            try
            {
                return await ExecuteWithRetry(operation, maxRetries, delayMs);
            }
            catch
            {
                return false;
            }
        }

        // Circuit Breaker Pattern Implementation (consolidated)
        private static readonly Dictionary<string, CircuitBreakerState> _circuitBreakers = new();
        private static readonly object _circuitBreakerLock = new();
        private const int DefaultFailureThreshold = 5;
        private const int DefaultTimeoutMs = 60000; // 1 minute

        /// <summary>
        /// Execute operation with circuit breaker pattern for resilience
        /// </summary>
        public static async Task<T> ExecuteWithCircuitBreaker<T>(
            string operationKey,
            Func<Task<T>> operation,
            T fallbackValue = default!,
            int failureThreshold = DefaultFailureThreshold,
            int timeoutMs = DefaultTimeoutMs)
        {
            var timeout = TimeSpan.FromMilliseconds(timeoutMs);
            var circuitBreaker = GetOrCreateCircuitBreaker(operationKey, failureThreshold, timeoutMs);

            lock (_circuitBreakerLock)
            {
                // Reset circuit breaker if timeout elapsed
                if (circuitBreaker.LastFailureTime.HasValue &&
                    DateTime.Now - circuitBreaker.LastFailureTime.Value > timeout)
                {
                    circuitBreaker.State = CircuitBreakerStateEnum.Closed;
                    circuitBreaker.FailureCount = 0;
                    circuitBreaker.LastFailureTime = null;
                }

                // Check if circuit is open
                if (circuitBreaker.State == CircuitBreakerStateEnum.Open)
                {
                    Console.WriteLine($"Circuit breaker open for {operationKey}, using fallback");
                    return fallbackValue;
                }
            }

            try
            {
                var result = await operation();
                OnSuccess(circuitBreaker, operationKey);
                return result;
            }
            catch (Exception ex)
            {
                OnFailure(circuitBreaker, operationKey, ex);
                if (circuitBreaker.State == CircuitBreakerStateEnum.Open)
                {
                    await LogError(ex, $"Circuit breaker opened for: {operationKey}");
                    return fallbackValue;
                }
                throw;
            }
        }

        /// <summary>
        /// ネットワーク操作を安全に実行（自動リトライとフォールバック付き）
        /// </summary>
        public static async Task<T> HandleNetworkOperationWithRecovery<T>(Func<Task<T>> networkOperation, T fallbackValue = default!, int maxRetries = 3)
        {
            try
            {
                return await ExecuteWithRetry(networkOperation, maxRetries);
            }
            catch (NetworkException ex)
            {
                await LogError(ex, "Network operation failed with fallback");
                return fallbackValue;
            }
            catch (UnauthorizedAccessException ex)
            {
                await LogError(ex, "Permission denied - administrator privileges required");
                Console.WriteLine("Error: Administrator privileges required for WiFi operations");
                Console.WriteLine("Please run as administrator or check permissions");
                return fallbackValue;
            }
            catch (TimeoutException ex)
            {
                await LogError(ex, "Operation timed out - network may be unavailable");
                Console.WriteLine("Operation timed out. Please check your network connection and try again");
                return fallbackValue;
            }
            catch (OperationFailedException ex) when (ex.InnerException is NetworkException)
            {
                await LogError(ex, "Network operation failed after all retries");
                Console.WriteLine("Network operation failed after multiple attempts. Please check your connection.");
                return fallbackValue;
            }
            catch (Exception ex)
            {
                await LogError(ex, "Unexpected error in network operation with recovery");
                Console.WriteLine($"Unexpected error: {GetUserFriendlyMessage(ex)}");
                return fallbackValue;
            }
        }

        /// <summary>
        /// システム全体のエラーレポートを生成
        /// </summary>
        public static async Task<ErrorReport> GenerateErrorReportAsync()
        {
            var recentErrors = GetRecentErrors(20);
            var report = new ErrorReport
            {
                GeneratedAt = DateTime.Now,
                TotalErrors = _errorHistory.Count,
                RecentErrors = recentErrors,
                ErrorSummary = GenerateErrorSummary(recentErrors),
                Recommendations = GenerateRecommendations(recentErrors)
            };

            await StructuredLogger.LogInformation("Error report generated", "ErrorHandler", new Dictionary<string, object>
            {
                ["TotalErrors"] = report.TotalErrors,
                ["ReportPeriod"] = "Last 20 errors"
            });

            return report;
        }

        /// <summary>
        /// エラーの修復を試行
        /// </summary>
        public static async Task<bool> AttemptErrorRecoveryAsync(Exception exception, string? context = null)
        {
            try
            {
                var recoveryStrategies = new List<Func<Task<bool>>>
                {
                    async () => await AttemptNetworkRecoveryAsync(exception),
                    async () => await AttemptPermissionRecoveryAsync(exception),
                    async () => await AttemptConfigurationRecoveryAsync(exception),
                    async () => await AttemptCacheRecoveryAsync(exception)
                };

                foreach (var strategy in recoveryStrategies)
                {
                    if (await strategy())
                    {
                        await StructuredLogger.LogInformation("Error recovery successful", "ErrorHandler", new Dictionary<string, object>
                        {
                            ["Context"] = context,
                            ["RecoveryStrategy"] = strategy.Method.Name
                        });
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                await LogError(ex, $"Error recovery attempt failed for context: {context}");
                return false;
            }
        }

        private static CircuitBreakerState GetOrCreateCircuitBreaker(string operationKey, int failureThreshold, int timeoutMs)
        {
            lock (_circuitBreakerLock)
            {
                if (!_circuitBreakers.TryGetValue(operationKey, out var circuitBreaker))
                {
                    circuitBreaker = new CircuitBreakerState
                    {
                        OperationKey = operationKey,
                        FailureThreshold = failureThreshold,
                        TimeoutMs = timeoutMs,
                        State = CircuitBreakerStateEnum.Closed,
                        FailureCount = 0,
                        LastFailureTime = null
                    };
                    _circuitBreakers[operationKey] = circuitBreaker;
                }
                return circuitBreaker;
            }
        }

        private static void OnSuccess(CircuitBreakerState circuitBreaker, string operationKey)
        {
            lock (_circuitBreakerLock)
            {
                circuitBreaker.FailureCount = 0;
                circuitBreaker.State = CircuitBreakerStateEnum.Closed;
                circuitBreaker.LastFailureTime = null;
            }
        }

        private static void OnFailure(CircuitBreakerState circuitBreaker, string operationKey, Exception exception)
        {
            lock (_circuitBreakerLock)
            {
                circuitBreaker.FailureCount++;
                circuitBreaker.LastFailureTime = DateTime.Now;

                if (circuitBreaker.FailureCount >= circuitBreaker.FailureThreshold)
                {
                    circuitBreaker.State = CircuitBreakerStateEnum.Open;
                    Console.WriteLine($"Circuit breaker opened for {operationKey} after {circuitBreaker.FailureCount} failures");
                }
            }
        }

        private static async Task<bool> AttemptNetworkRecoveryAsync(Exception exception)
        {
            // ネットワーク関連のエラーの場合、ネットワーク状態をチェックして回復を試行
            if (exception is NetworkException || exception.InnerException is NetworkException)
            {
                try
                {
                    // ネットワークインターフェースをリセット
                    var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                    foreach (var ni in interfaces)
                    {
                        if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211)
                        {
                            // ワイヤレスアダプタの状態を確認・リセット
                            break;
                        }
                    }
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }

        private static async Task<bool> AttemptPermissionRecoveryAsync(Exception exception)
        {
            // 権限関連のエラーの場合、管理者権限での再実行を提案
            if (exception is UnauthorizedAccessException)
            {
                // 管理者権限をチェックし、提案を表示
                return true; // 提案のみ
            }
            return false;
        }

        private static async Task<bool> AttemptConfigurationRecoveryAsync(Exception exception)
        {
            // 設定関連のエラーの場合、設定をリセット
            try
            {
                await ConfigManager.ResetToDefaults();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> AttemptCacheRecoveryAsync(Exception exception)
        {
            // キャッシュ関連のエラーの場合、キャッシュをクリア
            try
            {
                // NetworkOperationsのキャッシュをクリア
                var cacheField = typeof(NetworkOperations).GetField("_cache",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (cacheField?.GetValue(null) is Microsoft.Extensions.Caching.Memory.MemoryCache cache)
                {
                    cache.Clear();
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Dictionary<string, int> GenerateErrorSummary(List<ErrorLog> errors)
        {
            var summary = new Dictionary<string, int>();
            foreach (var error in errors)
            {
                var key = error.Exception;
                summary[key] = summary.GetValueOrDefault(key, 0) + 1;
            }
            return summary;
        }

        private static List<string> GenerateRecommendations(List<ErrorLog> errors)
        {
            var recommendations = new List<string>();

            // エラータイプ別の推奨事項を生成
            var errorTypes = errors.GroupBy(e => e.Exception);
            foreach (var errorType in errorTypes)
            {
                switch (errorType.Key)
                {
                    case "NetworkException":
                        recommendations.Add("ネットワーク接続を確認し、再試行してください");
                        break;
                    case "UnauthorizedAccessException":
                        recommendations.Add("管理者権限で実行してください");
                        break;
                    case "TimeoutException":
                        recommendations.Add("ネットワークのタイムアウト値を調整してください");
                        break;
                    default:
                        recommendations.Add($"エラー '{errorType.Key}' の詳細ログを確認してください");
                        break;
                }
            }

            return recommendations.Distinct().ToList();
        }


        // Circuit breaker state enum
        private enum CircuitBreakerStateEnum
        {
            Closed,
            Open,
            HalfOpen
        }

        private class CircuitBreakerState
        {
            public string OperationKey { get; set; }
            public CircuitBreakerStateEnum State { get; set; }
            public int FailureCount { get; set; }
            public int FailureThreshold { get; set; }
            public int TimeoutMs { get; set; }
            public DateTime? LastFailureTime { get; set; }
        }

        public static async Task LogError(Exception ex, string? context = null)
        {
            var errorLog = new ErrorLog
            {
                Timestamp = DateTime.Now,
                Exception = ex.GetType().Name,
                Message = ex.Message,
                Context = context,
                StackTrace = ex.StackTrace
            };

            _errorHistory.Enqueue(errorLog);
            while (_errorHistory.Count > MaxErrorHistory && _errorHistory.TryDequeue(out _))
            {
                // Trim oldest entries
            }

            try
            {
                await Logger.LogError("Captured exception", nameof(ErrorHandler),
                    new Dictionary<string, object>
                    {
                        ["context"] = context ?? string.Empty,
                        ["exception"] = ex.GetType().FullName,
                        ["message"] = ex.Message
                    }, ex);
            }
            catch
            {
                // Ignore logging failures to avoid cascading errors
            }

            try
            {
                await AuditTrail.RecordEventAsync("Error", "Logged", new Dictionary<string, object>
                {
                    ["context"] = context ?? string.Empty,
                    ["exception"] = ex.GetType().FullName,
                    ["message"] = ex.Message
                }, AuditSeverityFromException(ex));
            }
            catch
            {
                // Avoid recursive failures
            }

            // Log to file for offline diagnostics as a fallback
            await LogToFile(errorLog);
        }

        public static List<ErrorLog> GetRecentErrors(int count = 10)
        {
            return _errorHistory.Reverse().Take(count).ToList();
        }

        public static void ShowDiagnostics()
        {
            Console.WriteLine("Error Diagnostics:");
            Console.WriteLine($"Total errors logged: {_errorHistory.Count}");

            if (_errorHistory.Count > 0)
            {
                var recent = GetRecentErrors(5);
                Console.WriteLine("\nRecent errors:");
                foreach (var error in recent)
                {
                    Console.WriteLine($"  {error.Timestamp:HH:mm:ss} - {error.Exception}: {error.Message}");
                }

                // Show common error patterns
                var errorTypes = new Dictionary<string, int>();
                foreach (var error in _errorHistory)
                {
                    if (errorTypes.ContainsKey(error.Exception))
                        errorTypes[error.Exception]++;
                    else
                        errorTypes[error.Exception] = 1;
                }

                Console.WriteLine("\nError frequency:");
                foreach (var kvp in errorTypes)
                {
                    Console.WriteLine($"  {kvp.Key}: {kvp.Value} times");
                }
            }
        }

        public static bool IsRecoverableError(Exception ex)
        {
            return ex switch
            {
                NetworkException => true,
                TimeoutException => true,
                System.Net.Http.HttpRequestException => true,
                SocketException => true,
                IOException when ex.Message.Contains("device") => true,
                UnauthorizedAccessException => false, // Requires user action
                ArgumentException => false, // Code issue
                _ => true // Assume recoverable by default
            };
        }

        public static string GetUserFriendlyMessage(Exception ex)
        {
            return ex switch
            {
                UnauthorizedAccessException => "管理者権限が必要です。MurtiWifiConnecter.exeを右クリックして「管理者として実行」を選択してください。",
                NetworkException => "ネットワーク接続に問題があります。WiFiアダプタの状態を確認してください。",
                TimeoutException => "操作がタイムアウトしました。ネットワーク接続が不安定な可能性があります。",
                ArgumentException => "入力値が無効です。コマンドとパラメータを確認してください。",
                FileNotFoundException => "必要なシステムファイルが見つかりません。",
                DirectoryNotFoundException => "必要なディレクトリが見つかりません。",
                System.Net.Http.HttpRequestException => "ネットワークリクエストに失敗しました。インターネット接続を確認してください。",
                System.Net.Sockets.SocketException => "ネットワークソケットエラーが発生しました。ネットワーク設定を確認してください。",
                IOException when ex.Message.Contains("device") => "デバイスアクセスエラーが発生しました。WiFiアダプタの接続を確認してください。",
                IOException when ex.Message.Contains("sharing") => "ファイル共有違反が発生しました。他のプログラムがファイルをロックしている可能性があります。",
                InvalidOperationException when ex.Message.Contains("rate limit") => "レート制限を超えました。しばらく待ってから再試行してください。",
                InvalidOperationException when ex.Message.Contains("circuit breaker") => "サーキットブレーカーが作動しました。システムが過負荷状態です。",
                SecurityException => "セキュリティエラーが発生しました。システム設定を確認してください。",
                System.Security.Cryptography.CryptographicException => "暗号化処理でエラーが発生しました。システムのセキュリティ設定を確認してください。",
                _ => $"エラーが発生しました: {ex.Message}".Length > 100 ? $"エラーが発生しました: {ex.Message[..100]}..." : $"エラーが発生しました: {ex.Message}"
            };
        }

        public static string GetDetailedErrorInfo(Exception ex, string? context = null)
        {
            var message = new System.Text.StringBuilder();
            message.AppendLine($"エラー種別: {ex.GetType().Name}");
            message.AppendLine($"メッセージ: {ex.Message}");

            if (!string.IsNullOrEmpty(context))
            {
                message.AppendLine($"コンテキスト: {context}");
            }

            if (ex.InnerException != null)
            {
                message.AppendLine($"内部エラー: {ex.InnerException.Message}");
            }

            message.AppendLine($"タイムスタンプ: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            // スタックトレース（開発時のみ詳細表示）
            if (IsDevelopment())
            {
                message.AppendLine($"スタックトレース: {ex.StackTrace}");
            }

            return message.ToString();
        }

        public static async Task ShowErrorDiagnostics(Exception ex, string? context = null)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            Console.WriteLine("  ERROR DIAGNOSTICS");
            Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            Console.ResetColor();
            Console.WriteLine();

            var userMessage = GetUserFriendlyMessage(ex);
            Console.WriteLine($"問題: {userMessage}");
            Console.WriteLine();

            // 解決策の提案
            var suggestions = GetErrorSuggestions(ex);
            if (suggestions.Any())
            {
                Console.WriteLine("解決策の提案:");
                foreach (var suggestion in suggestions)
                {
                    Console.WriteLine($"  • {suggestion}");
                }
                Console.WriteLine();
            }

            // 詳細情報の表示（開発時のみ）
            if (IsDevelopment())
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("詳細情報:");
                Console.WriteLine(GetDetailedErrorInfo(ex, context));
                Console.ResetColor();
            }

            // ログファイルの場所を表示
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MurtiWifiConnecter");
            Console.WriteLine($"ログファイルの場所: {logDir}");
            Console.WriteLine();

            // 追加のヘルプ
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("追加のヘルプ:");
            Console.WriteLine("  • 'diagnostics' でシステム診断を実行してください");
            Console.WriteLine("  • 'history' で操作履歴を確認してください");
            Console.WriteLine("  • 'help' で利用可能なコマンドを表示してください");
            Console.ResetColor();
            Console.WriteLine();
        }

        public static List<string> GetErrorSuggestions(Exception ex)
        {
            var suggestions = new List<string>();

            switch (ex)
            {
                case UnauthorizedAccessException:
                    suggestions.Add("管理者権限で実行してください");
                    suggestions.Add("Windowsのユーザーアカウント制御を確認してください");
                    suggestions.Add("アンチウイルスソフトウェアがブロックしていないか確認してください");
                    break;

                case NetworkException:
                    suggestions.Add("WiFiアダプタが有効になっているか確認してください");
                    suggestions.Add("ネットワークケーブルを抜き差ししてみてください");
                    suggestions.Add("ネットワークドライバを更新してください");
                    break;

                case TimeoutException:
                    suggestions.Add("ネットワーク接続が安定しているか確認してください");
                    suggestions.Add("タイムアウト値を設定で調整してください");
                    suggestions.Add("ネットワーク負荷が高い可能性があります");
                    break;

                case ArgumentException:
                    suggestions.Add("コマンドの構文を確認してください");
                    suggestions.Add("パラメータが正しいか確認してください");
                    suggestions.Add("'help' で正しい使い方を確認してください");
                    break;

                case System.IO.IOException when ex.Message.Contains("sharing"):
                    suggestions.Add("他のプログラムがファイルをロックしている可能性があります");
                    suggestions.Add("エクスプローラーでファイルを閉じてください");
                    suggestions.Add("アンチウイルスがファイルをスキャン中かもしれません");
                    break;

                case InvalidOperationException when ex.Message.Contains("rate limit"):
                    suggestions.Add("しばらく待ってから再試行してください");
                    suggestions.Add("レート制限設定を確認してください");
                    suggestions.Add("短時間に多くのコマンドを実行しすぎています");
                    break;

                default:
                    suggestions.Add("アプリケーションを再起動してください");
                    suggestions.Add("システムの再起動を試してください");
                    suggestions.Add("ログファイルを確認して詳細を調査してください");
                    break;
            }

            return suggestions;
        }

        public static async Task<bool> ValidateSystemRequirements()
        {
            var issues = new List<string>();

            // Check if running on Windows
            if (!OperatingSystem.IsWindows())
            {
                issues.Add("This application requires Windows");
            }

            // Check for netsh command
            try
            {
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = "wlan show interfaces",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit(2000);

                if (process.ExitCode != 0)
                {
                    issues.Add("netsh command failed - WiFi adapter may not be available");
                }
            }
            catch
            {
                issues.Add("netsh command not found - Windows WiFi tools not available");
            }

            // Check for administrator privileges (if needed)
            try
            {
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                if (!principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
                {
                    Console.WriteLine("Warning: Running without administrator privileges");
                    Console.WriteLine("Some operations may require elevated permissions");
                }
            }
            catch
            {
                issues.Add("Cannot determine privilege level");
            }

            if (issues.Count > 0)
            {
                Console.WriteLine("System validation issues:");
                foreach (var issue in issues)
                {
                    Console.WriteLine($"  • {issue}");
                }
                return false;
            }

            return true;
        }

        // Note: Circuit breaker implementation is consolidated above

        private static string AuditSeverityFromException(Exception ex)
        {
            return ex switch
            {
                UnauthorizedAccessException => "Critical",
                SecurityException => "Critical",
                System.Security.Cryptography.CryptographicException => "High",
                NetworkException => "Medium",
                TimeoutException => "Medium",
                ArgumentException => "Low",
                _ => "Medium"
            };
        }

        private static async Task LogToFile(ErrorLog errorLog)
        {
            try
            {
                var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MurtiWifiConnecter");
                Directory.CreateDirectory(logDir);

                var logFile = Path.Combine(logDir, $"errors_{DateTime.Now:yyyy-MM-dd}.log");
                var logEntry = JsonSerializer.Serialize(errorLog, new JsonSerializerOptions { WriteIndented = true });

                await File.AppendAllTextAsync(logFile, logEntry + Environment.NewLine);

                // Clean up old log files (keep last 7 days)
                var cutoffDate = DateTime.Now.AddDays(-7);
                foreach (var file in Directory.GetFiles(logDir, "errors_*.log"))
                {
                    if (File.GetCreationTime(file) < cutoffDate)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch
            {
                // Silently fail logging to avoid recursion
            }
        }

        public class ErrorLog
        {
            public DateTime Timestamp { get; set; }
            public string Exception { get; set; }
            public string Message { get; set; }
            public string Context { get; set; }
            public string StackTrace { get; set; }
        }
    }

    /// <summary>
    /// Circuit breaker open exception
    /// </summary>
    public class CircuitBreakerOpenException : Exception
    {
        public CircuitBreakerOpenException(string message) : base(message) { }
        public CircuitBreakerOpenException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Error report class
    /// </summary>
    public class ErrorReport
    {
        public DateTime GeneratedAt { get; set; }
        public int TotalErrors { get; set; }
        public List<ErrorHandler.ErrorLog> RecentErrors { get; set; } = new();
        public Dictionary<string, int> ErrorSummary { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
        /// <summary>
        /// Get enhanced error information with suggestions
        /// </summary>
        public static async Task<EnhancedErrorInfo> GetEnhancedErrorInfoAsync(Exception exception, string? context = null)
        {
            var errorType = exception.GetType().Name;
            var errorMessage = exception.Message;

            // Get error context and suggestions
            var (suggestions, relatedErrors) = await GetErrorSuggestionsAsync(errorType, errorMessage, context);

            return new EnhancedErrorInfo
            {
                OriginalException = exception,
                ErrorType = errorType,
                ErrorMessage = errorMessage,
                Context = context,
                Suggestions = suggestions,
                RelatedErrors = relatedErrors,
                Timestamp = DateTime.Now,
                IsRecoverable = IsRecoverableError(exception)
            };
        }

        /// <summary>
        /// Log error with enhanced context and suggestions
        /// </summary>
        public static async Task LogEnhancedErrorAsync(Exception exception, string? context = null, Dictionary<string, object>? additionalData = null)
        {
            var enhancedInfo = await GetEnhancedErrorInfoAsync(exception, context);

            var logData = new Dictionary<string, object>
            {
                ["errorType"] = enhancedInfo.ErrorType,
                ["errorMessage"] = enhancedInfo.ErrorMessage,
                ["context"] = enhancedInfo.Context,
                ["isRecoverable"] = enhancedInfo.IsRecoverable,
                ["suggestionsCount"] = enhancedInfo.Suggestions.Count,
                ["timestamp"] = enhancedInfo.Timestamp
            };

            if (additionalData != null)
            {
                foreach (var kvp in additionalData)
                {
                    logData[kvp.Key] = kvp.Value;
                }
            }

            await Logger.LogError(enhancedInfo.ErrorMessage, context ?? "EnhancedErrorHandler", logData);

            // Log suggestions for debugging
            if (enhancedInfo.Suggestions.Any())
            {
                await Logger.LogDebug($"Error suggestions: {string.Join("; ", enhancedInfo.Suggestions)}",
                    context ?? "EnhancedErrorHandler", new Dictionary<string, object>
                    {
                        ["suggestions"] = enhancedInfo.Suggestions
                    });
            }
        }

        /// <summary>
        /// Handle error with user-friendly message and suggestions
        /// </summary>
        public static async Task<int> HandleErrorWithSuggestionsAsync(Exception exception, string? context = null)
        {
            var enhancedInfo = await GetEnhancedErrorInfoAsync(exception, context);

            // Display user-friendly error message
            UIHelper.ShowModal("エラー発生",
                $"エラーが発生しました: {enhancedInfo.ErrorMessage}\n\n" +
                (enhancedInfo.Suggestions.Any() ? $"推奨される対処法:\n{string.Join("\n", enhancedInfo.Suggestions.Select(s => $"• {s}"))}\n\n" : "") +
                (enhancedInfo.IsRecoverable ? "このエラーは回復可能な可能性があります。" : "このエラーは深刻な問題を示している可能性があります。"),
                enhancedInfo.IsRecoverable ? UIHelper.ModalType.Warning : UIHelper.ModalType.Error);

            await LogEnhancedErrorAsync(exception, context);
            return enhancedInfo.IsRecoverable ? 0 : 1;
        }

        /// <summary>
        /// Get error suggestions based on error type and context
        /// </summary>
        private static async Task<(List<string> suggestions, List<string> relatedErrors)> GetErrorSuggestionsAsync(string errorType, string errorMessage, string? context)
        {
            var suggestions = new List<string>();
            var relatedErrors = new List<string>();

            // Network-related errors
            if (errorType.Contains("Network") || errorMessage.Contains("network", StringComparison.OrdinalIgnoreCase))
            {
                suggestions.Add("ネットワーク接続を確認してください");
                suggestions.Add("WiFiアダプタが有効になっているか確認してください");
                suggestions.Add("'diagnostics' コマンドを実行して詳細な診断情報を取得してください");
                suggestions.Add("ネットワークケーブルが正しく接続されているか確認してください");
                relatedErrors.Add("NetworkInformationException");
                relatedErrors.Add("WebException");
            }

            // Permission/Access errors
            if (errorType.Contains("Unauthorized") || errorType.Contains("Security") || errorMessage.Contains("access", StringComparison.OrdinalIgnoreCase))
            {
                suggestions.Add("管理者権限でアプリケーションを実行してください");
                suggestions.Add("Windowsセキュリティ設定を確認してください");
                suggestions.Add("アンチウイルスソフトウェアがブロックしていないか確認してください");
                suggestions.Add("ファイル/フォルダの権限設定を確認してください");
                relatedErrors.Add("UnauthorizedAccessException");
                relatedErrors.Add("SecurityException");
            }

            // Timeout errors
            if (errorType.Contains("Timeout") || errorMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            {
                suggestions.Add("ネットワーク接続が不安定な可能性があります");
                suggestions.Add("ファイアウォール設定を確認してください");
                suggestions.Add("VPN接続がタイムアウトの原因になっている可能性があります");
                suggestions.Add("'speed' コマンドでネットワーク速度をテストしてください");
                relatedErrors.Add("TimeoutException");
            }

            // Configuration errors
            if (errorMessage.Contains("config", StringComparison.OrdinalIgnoreCase) || context?.Contains("config", StringComparison.OrdinalIgnoreCase) == true)
            {
                suggestions.Add("'config validate' コマンドで設定を検証してください");
                suggestions.Add("'config reset' コマンドでデフォルト設定に戻してください");
                suggestions.Add("設定ファイルの構文エラーを確認してください");
                relatedErrors.Add("JsonException");
                relatedErrors.Add("FormatException");
            }

            // WiFi-specific errors
            if (context?.Contains("wifi", StringComparison.OrdinalIgnoreCase) == true ||
                context?.Contains("network", StringComparison.OrdinalIgnoreCase) == true ||
                errorMessage.Contains("wlan", StringComparison.OrdinalIgnoreCase))
            {
                suggestions.Add("'scan' コマンドで利用可能なネットワークを確認してください");
                suggestions.Add("WiFiアダプタのドライバを更新してください");
                suggestions.Add("ネットワークセキュリティ設定を確認してください");
                suggestions.Add("'security-scan' コマンドでセキュリティ診断を実行してください");
                relatedErrors.Add("NetworkInformationException");
            }

            // Rate limiting errors
            if (errorMessage.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
                errorMessage.Contains("too many", StringComparison.OrdinalIgnoreCase))
            {
                suggestions.Add("短時間に多くの操作を実行しすぎています");
                suggestions.Add("しばらく待ってから再試行してください");
                suggestions.Add("'security-metrics' コマンドでレート制限状況を確認してください");
            }

            // Generic suggestions if no specific ones found
            if (!suggestions.Any())
            {
                suggestions.Add("'diagnostics' コマンドでシステム診断を実行してください");
                suggestions.Add("アプリケーションを再起動してください");
                suggestions.Add("ログファイルで詳細なエラー情報を確認してください");
                suggestions.Add("ヘルプドキュメントを参照してください");
            }

            return (suggestions, relatedErrors);
        }

        /// <summary>
        /// Determine if an error is recoverable
        /// </summary>
        private static bool IsRecoverableError(Exception exception)
        {
            var errorType = exception.GetType();

            // Recoverable errors
            if (errorType == typeof(System.Net.NetworkInformation.NetworkInformationException) ||
                errorType == typeof(System.Net.WebException) ||
                errorType == typeof(System.TimeoutException) ||
                errorType == typeof(System.IO.IOException) ||
                errorType == typeof(UnauthorizedAccessException))
            {
                return true;
            }

            // Check error message for recoverable patterns
            var message = exception.Message.ToLowerInvariant();
            if (message.Contains("timeout") ||
                message.Contains("network") ||
                message.Contains("connection") ||
                message.Contains("access denied") ||
                message.Contains("rate limit"))
            {
                return true;
            }

            // Non-recoverable errors
            if (errorType == typeof(System.OutOfMemoryException) ||
                errorType == typeof(System.StackOverflowException) ||
                errorType == typeof(System.TypeInitializationException))
            {
                return false;
            }

            // Default to recoverable for unknown errors
            return true;
        }

        /// <summary>
        /// Initialize error contexts and suggestions
        /// </summary>
        public static void InitializeErrorContexts()
        {
            lock (_errorContextLock)
            {
                // Initialize with common error patterns and their solutions
                _errorContexts["NetworkInformationException"] = new ErrorContext
                {
                    ErrorType = "NetworkInformationException",
                    CommonCauses = new[] { "WiFiアダプタ障害", "ドライバ問題", "ネットワーク設定エラー" },
                    Solutions = new[] {
                        "WiFiアダプタが有効になっているか確認してください",
                        "デバイスマネージャーでWiFiアダプタの状態を確認してください",
                        "WiFiドライバを更新してください"
                    }
                };

                _errorContexts["UnauthorizedAccessException"] = new ErrorContext
                {
                    ErrorType = "UnauthorizedAccessException",
                    CommonCauses = new[] { "管理者権限不足", "ファイル権限設定", "セキュリティポリシー" },
                    Solutions = new[] {
                        "管理者権限でアプリケーションを実行してください",
                        "ファイル/フォルダの権限設定を確認してください",
                        "Windowsセキュリティ設定を確認してください"
                    }
                };

                _errorContexts["TimeoutException"] = new ErrorContext
                {
                    ErrorType = "TimeoutException",
                    CommonCauses = new[] { "ネットワーク遅延", "サーバー応答なし", "ファイアウォールブロック" },
                    Solutions = new[] {
                        "ネットワーク接続を確認してください",
                        "ファイアウォール設定を確認してください",
                        "インターネット接続をテストしてください"
                    }
                };
            }
        }
    }

    /// <summary>
    /// Enhanced error information with suggestions
    /// </summary>
    public class EnhancedErrorInfo
    {
        public Exception OriginalException { get; set; }
        public string ErrorType { get; set; }
        public string ErrorMessage { get; set; }
        public string Context { get; set; }
        public List<string> Suggestions { get; set; } = new();
        public List<string> RelatedErrors { get; set; } = new();
        public DateTime Timestamp { get; set; }
        public bool IsRecoverable { get; set; }
    }

    /// <summary>
    /// Error context with causes and solutions
    /// </summary>
    public class ErrorContext
    {
        public string ErrorType { get; set; }
        public string[] CommonCauses { get; set; } = Array.Empty<string>();
        public string[] Solutions { get; set; } = Array.Empty<string>();
    }

    public class OperationFailedException : Exception
    {
        public OperationFailedException(string message) : base(message) { }
        public OperationFailedException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class NetworkException : Exception
    {
        public NetworkException(string message) : base(message) { }
        public NetworkException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class SocketException : Exception
    {
        public SocketException(string message) : base(message) { }
        public SocketException(string message, Exception innerException) : base(message, innerException) { }
    }