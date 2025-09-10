using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MurtiWifiConnecter.Interfaces;

namespace MurtiWifiConnecter.Services
{
    /// <summary>
    /// 統合ログサービス - シンプルで高速な実装
    /// </summary>
    public class SimpleLoggingService : ILoggingService, IDisposable
    {
        private readonly string _logPath;
        private readonly ConcurrentQueue<string> _logQueue = new();
        private readonly Timer _flushTimer;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private bool _disposed = false;
        
        private const int MaxFileSize = 10 * 1024 * 1024; // 10MB
        private const int MaxBackupFiles = 3;
        private readonly List<LogEntry> _recentLogs = new();
        private readonly object _logsLock = new();

        public event EventHandler<LogEventArgs> LogWritten;

        public SimpleLoggingService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logDir = Path.Combine(appDataPath, "MurtiWifiConnecter", "Logs");
            Directory.CreateDirectory(logDir);
            
            _logPath = Path.Combine(logDir, $"wifi_{DateTime.Now:yyyyMMdd}.log");
            
            // 5秒ごとにフラッシュ
            _flushTimer = new Timer(async _ => await FlushLogsAsync(), null, 
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        public void LogInfo(string message)
        {
            Log("INFO", message);
        }

        public void LogWarning(string message)
        {
            Log("WARN", message);
        }

        public void LogError(string message, Exception ex = null)
        {
            var errorMsg = ex != null ? $"{message}: {ex.Message}" : message;
            Log("ERROR", errorMsg);
        }

        public void LogCritical(string message, Exception ex = null)
        {
            var criticalMsg = ex != null ? $"{message}: {ex.Message}" : message;
            Log("CRITICAL", criticalMsg);
        }

        public void LogDebug(string message)
        {
#if DEBUG
            Log("DEBUG", message);
#endif
        }

        private void Log(string level, string message)
        {
            if (_disposed) return;
            
            var now = DateTime.Now;
            var timestamp = now.ToString("HH:mm:ss.fff");
            var logEntry = $"[{timestamp}] [{level}] {message}";
            
            _logQueue.Enqueue(logEntry);
            
            // 最近のログに追加
            lock (_logsLock)
            {
                _recentLogs.Add(new LogEntry
                {
                    Timestamp = now,
                    Level = ParseLogLevel(level),
                    Message = message
                });
                
                // 最大1000件まで保持
                if (_recentLogs.Count > 1000)
                    _recentLogs.RemoveAt(0);
            }
            
            // イベント発行
            LogWritten?.Invoke(this, new LogEventArgs
            {
                Timestamp = now,
                Level = ParseLogLevel(level),
                Message = message
            });
            
            // コンソール出力（デバッグ時）
#if DEBUG
            Console.WriteLine(logEntry);
#endif
            
            // キューが大きくなりすぎたら即座にフラッシュ
            if (_logQueue.Count > 100)
            {
                _ = Task.Run(async () => await FlushLogsAsync());
            }
        }

        private LogLevel ParseLogLevel(string level)
        {
            return level switch
            {
                "DEBUG" => LogLevel.Debug,
                "INFO" => LogLevel.Info,
                "WARN" => LogLevel.Warning,
                "ERROR" => LogLevel.Error,
                "CRITICAL" => LogLevel.Critical,
                _ => LogLevel.Info
            };
        }

        private async Task FlushLogsAsync()
        {
            if (_disposed || _logQueue.IsEmpty) return;
            
            if (!await _writeLock.WaitAsync(100)) return; // すでに書き込み中なら skip
            
            try
            {
                var logs = new StringBuilder();
                while (_logQueue.TryDequeue(out var log) && logs.Length < 65536) // 64KB バッチ
                {
                    logs.AppendLine(log);
                }
                
                if (logs.Length == 0) return;
                
                // ファイルサイズチェックとローテーション
                await RotateLogFileIfNeededAsync();
                
                // ログ書き込み
                await File.AppendAllTextAsync(_logPath, logs.ToString());
            }
            catch (Exception ex)
            {
                // ログ書き込みエラーは無視（無限ループ防止）
                Console.WriteLine($"Log write error: {ex.Message}");
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private async Task RotateLogFileIfNeededAsync()
        {
            try
            {
                if (!File.Exists(_logPath)) return;
                
                var fileInfo = new FileInfo(_logPath);
                if (fileInfo.Length < MaxFileSize) return;
                
                // ローテーション
                for (int i = MaxBackupFiles - 1; i >= 1; i--)
                {
                    var oldFile = $"{_logPath}.{i}";
                    var newFile = $"{_logPath}.{i + 1}";
                    
                    if (File.Exists(newFile))
                        File.Delete(newFile);
                    
                    if (File.Exists(oldFile))
                        File.Move(oldFile, newFile);
                }
                
                // 現在のファイルを .1 にリネーム
                File.Move(_logPath, $"{_logPath}.1");
            }
            catch
            {
                // ローテーションエラーは無視
            }
        }

        public async Task<List<LogEntry>> GetLogsAsync(DateTime? startTime = null, DateTime? endTime = null, LogLevel? minLevel = null)
        {
            return await Task.Run(() =>
            {
                lock (_logsLock)
                {
                    var query = _recentLogs.AsEnumerable();
                    
                    if (startTime.HasValue)
                        query = query.Where(l => l.Timestamp >= startTime.Value);
                    
                    if (endTime.HasValue)
                        query = query.Where(l => l.Timestamp <= endTime.Value);
                    
                    if (minLevel.HasValue)
                        query = query.Where(l => l.Level >= minLevel.Value);
                    
                    return query.ToList();
                }
            });
        }

        public async Task ClearLogsAsync()
        {
            await Task.Run(() => ClearLogs());
        }

        public async Task<string[]> GetRecentLogsAsync(int lines = 100)
        {
            try
            {
                if (!File.Exists(_logPath))
                    return Array.Empty<string>();
                
                var allLines = await File.ReadAllLinesAsync(_logPath);
                var startIndex = Math.Max(0, allLines.Length - lines);
                var result = new string[Math.Min(lines, allLines.Length)];
                
                Array.Copy(allLines, startIndex, result, 0, result.Length);
                return result;
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        public void ClearLogs()
        {
            try
            {
                while (_logQueue.TryDequeue(out _)) { }
                
                if (File.Exists(_logPath))
                {
                    File.Delete(_logPath);
                }
                
                // バックアップファイルも削除
                for (int i = 1; i <= MaxBackupFiles; i++)
                {
                    var backupFile = $"{_logPath}.{i}";
                    if (File.Exists(backupFile))
                        File.Delete(backupFile);
                }
            }
            catch
            {
                // エラーは無視
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _flushTimer?.Dispose();
            
            // 最終フラッシュ
            FlushLogsAsync().Wait(TimeSpan.FromSeconds(2));
            
            _writeLock?.Dispose();
        }

        // Static methods for compatibility
        public static void LogInfo(string message) => Log.Info(message);
        public static void LogWarning(string message) => Log.Warning(message);
        public static void LogError(string message, Exception ex = null) => Log.Error(message, ex);
        public static void LogDebug(string message) => Log.Debug(message);
    }

    /// <summary>
    /// グローバルログインスタンス
    /// </summary>
    public static class Log
    {
        private static readonly Lazy<SimpleLoggingService> _instance = 
            new Lazy<SimpleLoggingService>(() => new SimpleLoggingService());
        
        public static SimpleLoggingService Instance => _instance.Value;
        
        public static void Info(string message) => Instance.LogInfo(message);
        public static void Warning(string message) => Instance.LogWarning(message);
        public static void Error(string message, Exception ex = null) => Instance.LogError(message, ex);
        public static void Debug(string message) => Instance.LogDebug(message);
    }
}