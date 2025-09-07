using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace MurtiWifiConnecter
{
    // Martin式の包括的エラー処理システム
    public static class ErrorHandler
    {
        private static readonly ConcurrentDictionary<string, ErrorStats> _errorStats = new();
        private static long _totalErrors = 0;
        private static readonly object _lockObject = new();
        
        // Carmack式の効率的エラー追跡
        public static void LogError(string context, Exception ex, ConnectionLogger? logger = null, 
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
                
                // 構造化ログメッセージ
                var errorDetails = new
                {
                    Context = context,
                    Member = memberName,
                    File = System.IO.Path.GetFileName(filePath),
                    Line = lineNumber,
                    ExceptionType = ex.GetType().Name,
                    Message = ex.Message,
                    StackTrace = ex.StackTrace?.Split('\n').Take(3) // 上位3行のみ
                };
                
                var errorMessage = $"[{DateTime.Now:HH:mm:ss.fff}] ERROR in {context} ({memberName}@{System.IO.Path.GetFileName(filePath)}:{lineNumber}): {ex.Message}";
                
                // ログ出力
                logger?.LogError(context, errorMessage, ex);
                Debug.WriteLine(errorMessage);
                
                // 重要なエラーの場合は詳細出力
                if (IsHighPriorityError(ex))
                {
                    Debug.WriteLine($"[CRITICAL] {ex}");
                }
                
                // 頻発エラーの検出
                if (_errorStats[errorKey].Count > 10)
                {
                    Debug.WriteLine($"[WARNING] Frequent error detected: {errorKey} (count: {_errorStats[errorKey].Count})");
                }
            }
            catch
            {
                // エラーハンドラー内でのエラーは最小限の情報のみ出力
                Debug.WriteLine($"[CRITICAL] Error in ErrorHandler: {ex?.Message}");
            }
        }
        
        private static bool IsHighPriorityError(Exception ex)
        {
            return ex is OutOfMemoryException || 
                   ex is StackOverflowException ||
                   ex is System.Security.SecurityException ||
                   ex is UnauthorizedAccessException;
        }

        public static T SafeExecute<T>(Func<T> action, T defaultValue, string context = "Unknown", ConnectionLogger? logger = null)
        {
            try
            {
                return action();
            }
            catch (Exception ex)
            {
                LogError(context, ex, logger);
                return defaultValue;
            }
        }

        public static void SafeExecute(Action action, string context = "Unknown", ConnectionLogger? logger = null)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                LogError(context, ex, logger);
            }
        }

        public static string GetUserFriendlyErrorMessage(Exception ex)
        {
            return ex switch
            {
                System.Net.NetworkInformation.PingException => "ネットワーク接続に問題があります。",
                System.TimeoutException => "操作がタイムアウトしました。しばらく待ってから再試行してください。",
                System.UnauthorizedAccessException => "管理者権限が必要な操作です。",
                System.ComponentModel.Win32Exception win32Ex when win32Ex.NativeErrorCode == 2 => "必要なシステムコンポーネントが見つかりません。",
                System.ComponentModel.Win32Exception win32Ex when win32Ex.NativeErrorCode == 5 => "アクセスが拒否されました。管理者として実行してください。",
                System.IO.FileNotFoundException => "必要なファイルが見つかりません。",
                System.IO.DirectoryNotFoundException => "指定されたフォルダが見つかりません。",
                System.InvalidOperationException => "現在この操作は実行できません。",
                System.ArgumentException => "入力データに問題があります。",
                _ => "予期しないエラーが発生しました。"
            };
        }

        public static bool IsRetriableError(Exception ex)
        {
            return ex switch
            {
                System.TimeoutException => true,
                System.Net.NetworkInformation.PingException => true,
                System.Net.Sockets.SocketException => true,
                System.ComponentModel.Win32Exception win32Ex when win32Ex.NativeErrorCode == 1460 => true, // Timeout
                _ => false
            };
        }

        public static bool IsUserActionRequired(Exception ex)
        {
            return ex switch
            {
                System.UnauthorizedAccessException => true,
                System.ComponentModel.Win32Exception win32Ex when win32Ex.NativeErrorCode == 5 => true,
                System.IO.FileNotFoundException => true,
                System.ArgumentException => true,
                _ => false
            };
        }
        
        // エラー統計の取得（高品質コード品質保証）
        public static ErrorSummary GetErrorSummary()
        {
            lock (_lockObject)
            {
                return new ErrorSummary
                {
                    TotalErrors = _totalErrors,
                    UniqueErrorTypes = _errorStats.Count,
                    MostFrequentErrors = _errorStats
                        .OrderByDescending(kvp => kvp.Value.Count)
                        .Take(5)
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                    RecentErrors = _errorStats.Values
                        .Where(stat => stat.LastOccurrence > DateTime.Now.AddHours(-1))
                        .Count()
                };
            }
        }
        
        public static void ClearErrorStats()
        {
            lock (_lockObject)
            {
                _errorStats.Clear();
                Interlocked.Exchange(ref _totalErrors, 0);
            }
        }
        
        public static SystemStability GetSystemStability()
        {
            var recentErrors = _errorStats.Values
                .Where(stat => stat.LastOccurrence > DateTime.Now.AddMinutes(-30))
                .Sum(stat => stat.Count);
            
            var stabilityScore = Math.Max(0, 100 - (recentErrors * 5));
            
            return new SystemStability
            {
                Score = stabilityScore,
                RecentErrorCount = recentErrors,
                IsStable = stabilityScore >= 80,
                Recommendation = stabilityScore switch
                {
                    >= 90 => "システムは安定しています",
                    >= 80 => "軽微な問題があります。監視を継続してください",
                    >= 60 => "不安定な状態です。再起動を検討してください",
                    >= 40 => "深刻な問題があります。ログを確認してください",
                    _ => "システムが不安定です。即座に対処が必要です"
                }
            };
        }
    }
    
    public class ErrorStats
    {
        public int Count { get; set; }
        public DateTime LastOccurrence { get; set; }
        public string Context { get; set; } = string.Empty;
    }
    
    public class ErrorSummary
    {
        public long TotalErrors { get; set; }
        public int UniqueErrorTypes { get; set; }
        public Dictionary<string, ErrorStats> MostFrequentErrors { get; set; } = new();
        public int RecentErrors { get; set; }
        
        public override string ToString()
        {
            return $"Total: {TotalErrors}, Types: {UniqueErrorTypes}, Recent: {RecentErrors}";
        }
    }
    
    public class SystemStability
    {
        public int Score { get; set; }
        public int RecentErrorCount { get; set; }
        public bool IsStable { get; set; }
        public string Recommendation { get; set; } = string.Empty;
        
        public string GetStabilityIcon()
        {
            return Score switch
            {
                >= 90 => "🟢",
                >= 80 => "🟡",
                >= 60 => "🟠",
                _ => "🔴"
            };
        }
    }
}