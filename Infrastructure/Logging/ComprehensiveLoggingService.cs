using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Infrastructure.Logging
{
    /// <summary>
    /// 包括的ログサービス
    /// </summary>
    public interface IComprehensiveLoggingService
    {
        void Log(LogLevel level, string message, string category = null, Exception exception = null, Dictionary<string, object> properties = null);
        void LogDebug(string message, string category = null, Dictionary<string, object> properties = null);
        void LogInfo(string message, string category = null, Dictionary<string, object> properties = null);
        void LogWarning(string message, string category = null, Dictionary<string, object> properties = null);
        void LogError(string message, string category = null, Exception exception = null, Dictionary<string, object> properties = null);
        void LogCritical(string message, string category = null, Exception exception = null, Dictionary<string, object> properties = null);
        Task<List<LogEntry>> GetLogsAsync(LogLevel? minimumLevel = null, DateTime? startTime = null, DateTime? endTime = null, string category = null);
        Task ClearLogsAsync();
        void SetLogLevel(LogLevel minimumLevel);
        void AddLogTarget(ILogTarget target);
        void RemoveLogTarget(ILogTarget target);
    }

    /// <summary>
    /// 包括的ログサービスの実装
    /// </summary>
    public class ComprehensiveLoggingService : IComprehensiveLoggingService, IDisposable
    {
        private readonly ConcurrentQueue<LogEntry> _logQueue;
        private readonly List<ILogTarget> _targets;
        private readonly Timer _flushTimer;
        private readonly SemaphoreSlim _flushSemaphore;
        private LogLevel _minimumLogLevel;
        private readonly object _targetsLock = new object();
        private bool _disposed = false;

        public ComprehensiveLoggingService()
        {
            _logQueue = new ConcurrentQueue<LogEntry>();
            _targets = new List<ILogTarget>();
            _flushSemaphore = new SemaphoreSlim(1, 1);
            _minimumLogLevel = LogLevel.Debug;

            // デフォルトターゲットを追加
            AddLogTarget(new FileLogTarget());
            AddLogTarget(new ConsoleLogTarget());

            // 定期的にログをフラッシュ
            _flushTimer = new Timer(async _ => await FlushLogsAsync(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// ログを記録
        /// </summary>
        public void Log(LogLevel level, string message, string category = null, Exception exception = null, Dictionary<string, object> properties = null)
        {
            if (level < _minimumLogLevel)
                return;

            var logEntry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message,
                Category = category ?? "General",
                Exception = exception,
                Properties = properties ?? new Dictionary<string, object>(),
                ThreadId = Thread.CurrentThread.ManagedThreadId,
                ProcessId = System.Diagnostics.Process.GetCurrentProcess().Id
            };

            _logQueue.Enqueue(logEntry);

            // 重要なログは即座にフラッシュ
            if (level >= LogLevel.Error)
            {
                Task.Run(async () => await FlushLogsAsync());
            }
        }

        public void LogDebug(string message, string category = null, Dictionary<string, object> properties = null)
        {
            Log(LogLevel.Debug, message, category, null, properties);
        }

        public void LogInfo(string message, string category = null, Dictionary<string, object> properties = null)
        {
            Log(LogLevel.Information, message, category, null, properties);
        }

        public void LogWarning(string message, string category = null, Dictionary<string, object> properties = null)
        {
            Log(LogLevel.Warning, message, category, null, properties);
        }

        public void LogError(string message, string category = null, Exception exception = null, Dictionary<string, object> properties = null)
        {
            Log(LogLevel.Error, message, category, exception, properties);
        }

        public void LogCritical(string message, string category = null, Exception exception = null, Dictionary<string, object> properties = null)
        {
            Log(LogLevel.Critical, message, category, exception, properties);
        }

        /// <summary>
        /// ログを取得
        /// </summary>
        public async Task<List<LogEntry>> GetLogsAsync(LogLevel? minimumLevel = null, DateTime? startTime = null, DateTime? endTime = null, string category = null)
        {
            var logs = new List<LogEntry>();

            foreach (var target in GetTargets())
            {
                if (target is IQueryableLogTarget queryableTarget)
                {
                    var targetLogs = await queryableTarget.QueryLogsAsync(minimumLevel, startTime, endTime, category);
                    logs.AddRange(targetLogs);
                }
            }

            return logs;
        }

        /// <summary>
        /// ログをクリア
        /// </summary>
        public async Task ClearLogsAsync()
        {
            foreach (var target in GetTargets())
            {
                if (target is IClearableLogTarget clearableTarget)
                {
                    await clearableTarget.ClearLogsAsync();
                }
            }
        }

        /// <summary>
        /// ログレベルを設定
        /// </summary>
        public void SetLogLevel(LogLevel minimumLevel)
        {
            _minimumLogLevel = minimumLevel;
        }

        /// <summary>
        /// ログターゲットを追加
        /// </summary>
        public void AddLogTarget(ILogTarget target)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            lock (_targetsLock)
            {
                if (!_targets.Contains(target))
                {
                    _targets.Add(target);
                }
            }
        }

        /// <summary>
        /// ログターゲットを削除
        /// </summary>
        public void RemoveLogTarget(ILogTarget target)
        {
            if (target == null)
                return;

            lock (_targetsLock)
            {
                _targets.Remove(target);
            }
        }

        /// <summary>
        /// ログをフラッシュ
        /// </summary>
        private async Task FlushLogsAsync()
        {
            if (!await _flushSemaphore.WaitAsync(100))
                return;

            try
            {
                var logsToProcess = new List<LogEntry>();

                // キューからログを取得
                while (_logQueue.TryDequeue(out var logEntry))
                {
                    logsToProcess.Add(logEntry);
                }

                if (logsToProcess.Count == 0)
                    return;

                // 各ターゲットにログを送信
                var targets = GetTargets();
                var tasks = new List<Task>();

                foreach (var target in targets)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            foreach (var log in logsToProcess)
                            {
                                await target.WriteLogAsync(log);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Log target failed: {ex.Message}");
                        }
                    }));
                }

                await Task.WhenAll(tasks);
            }
            finally
            {
                _flushSemaphore.Release();
            }
        }

        /// <summary>
        /// ターゲットリストのコピーを取得
        /// </summary>
        private List<ILogTarget> GetTargets()
        {
            lock (_targetsLock)
            {
                return new List<ILogTarget>(_targets);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _flushTimer?.Dispose();
                FlushLogsAsync().GetAwaiter().GetResult();

                foreach (var target in GetTargets())
                {
                    if (target is IDisposable disposableTarget)
                    {
                        disposableTarget.Dispose();
                    }
                }

                _flushSemaphore?.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// ログターゲットインターフェース
    /// </summary>
    public interface ILogTarget
    {
        Task WriteLogAsync(LogEntry logEntry);
    }

    /// <summary>
    /// 検索可能ログターゲット
    /// </summary>
    public interface IQueryableLogTarget : ILogTarget
    {
        Task<List<LogEntry>> QueryLogsAsync(LogLevel? minimumLevel = null, DateTime? startTime = null, DateTime? endTime = null, string category = null);
    }

    /// <summary>
    /// クリア可能ログターゲット
    /// </summary>
    public interface IClearableLogTarget : ILogTarget
    {
        Task ClearLogsAsync();
    }

    /// <summary>
    /// ファイルログターゲット
    /// </summary>
    public class FileLogTarget : ILogTarget, IQueryableLogTarget, IClearableLogTarget, IDisposable
    {
        private readonly string _logDirectory;
        private readonly string _currentLogFile;
        private readonly SemaphoreSlim _writeSemaphore;
        private readonly long _maxFileSize;
        private readonly int _maxFiles;

        public FileLogTarget(string logDirectory = "Logs", long maxFileSizeMB = 10, int maxFiles = 5)
        {
            _logDirectory = logDirectory;
            _maxFileSize = maxFileSizeMB * 1024 * 1024;
            _maxFiles = maxFiles;
            _writeSemaphore = new SemaphoreSlim(1, 1);

            Directory.CreateDirectory(_logDirectory);
            _currentLogFile = Path.Combine(_logDirectory, $"app_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        }

        public async Task WriteLogAsync(LogEntry logEntry)
        {
            await _writeSemaphore.WaitAsync();
            try
            {
                var logLine = FormatLogEntry(logEntry);
                await File.AppendAllTextAsync(_currentLogFile, logLine + Environment.NewLine);

                // ファイルサイズをチェックしてローテーション
                var fileInfo = new FileInfo(_currentLogFile);
                if (fileInfo.Length > _maxFileSize)
                {
                    await RotateLogFilesAsync();
                }
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        public async Task<List<LogEntry>> QueryLogsAsync(LogLevel? minimumLevel = null, DateTime? startTime = null, DateTime? endTime = null, string category = null)
        {
            var logs = new List<LogEntry>();
            var logFiles = Directory.GetFiles(_logDirectory, "*.log");

            foreach (var file in logFiles)
            {
                try
                {
                    var lines = await File.ReadAllLinesAsync(file);
                    foreach (var line in lines)
                    {
                        if (TryParseLogEntry(line, out var logEntry))
                        {
                            if (MatchesFilter(logEntry, minimumLevel, startTime, endTime, category))
                            {
                                logs.Add(logEntry);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to read log file {file}: {ex.Message}");
                }
            }

            return logs;
        }

        public async Task ClearLogsAsync()
        {
            await _writeSemaphore.WaitAsync();
            try
            {
                var logFiles = Directory.GetFiles(_logDirectory, "*.log");
                foreach (var file in logFiles)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to delete log file {file}: {ex.Message}");
                    }
                }
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        private string FormatLogEntry(LogEntry logEntry)
        {
            var logData = new
            {
                logEntry.Timestamp,
                Level = logEntry.Level.ToString(),
                logEntry.Category,
                logEntry.Message,
                logEntry.ThreadId,
                logEntry.ProcessId,
                Exception = logEntry.Exception?.ToString(),
                logEntry.Properties
            };

            return JsonSerializer.Serialize(logData);
        }

        private bool TryParseLogEntry(string line, out LogEntry logEntry)
        {
            logEntry = null;
            try
            {
                var jsonDoc = JsonDocument.Parse(line);
                var root = jsonDoc.RootElement;

                logEntry = new LogEntry
                {
                    Timestamp = root.GetProperty("Timestamp").GetDateTime(),
                    Level = Enum.Parse<LogLevel>(root.GetProperty("Level").GetString()),
                    Category = root.GetProperty("Category").GetString(),
                    Message = root.GetProperty("Message").GetString(),
                    ThreadId = root.GetProperty("ThreadId").GetInt32(),
                    ProcessId = root.GetProperty("ProcessId").GetInt32(),
                    Properties = new Dictionary<string, object>()
                };

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool MatchesFilter(LogEntry logEntry, LogLevel? minimumLevel, DateTime? startTime, DateTime? endTime, string category)
        {
            if (minimumLevel.HasValue && logEntry.Level < minimumLevel.Value)
                return false;

            if (startTime.HasValue && logEntry.Timestamp < startTime.Value)
                return false;

            if (endTime.HasValue && logEntry.Timestamp > endTime.Value)
                return false;

            if (!string.IsNullOrEmpty(category) && !string.Equals(logEntry.Category, category, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private async Task RotateLogFilesAsync()
        {
            try
            {
                var logFiles = Directory.GetFiles(_logDirectory, "*.log");
                if (logFiles.Length >= _maxFiles)
                {
                    // 古いファイルを削除
                    var filesToDelete = logFiles.Length - _maxFiles + 1;
                    var sortedFiles = Array.Sort(logFiles);
                    for (int i = 0; i < filesToDelete; i++)
                    {
                        File.Delete(logFiles[i]);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Log rotation failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _writeSemaphore?.Dispose();
        }
    }

    /// <summary>
    /// コンソールログターゲット
    /// </summary>
    public class ConsoleLogTarget : ILogTarget
    {
        public Task WriteLogAsync(LogEntry logEntry)
        {
            var color = GetConsoleColor(logEntry.Level);
            var originalColor = Console.ForegroundColor;

            try
            {
                Console.ForegroundColor = color;
                var message = $"[{logEntry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{logEntry.Level}] [{logEntry.Category}] {logEntry.Message}";
                
                if (logEntry.Exception != null)
                {
                    message += $"{Environment.NewLine}Exception: {logEntry.Exception}";
                }

                Console.WriteLine(message);
            }
            finally
            {
                Console.ForegroundColor = originalColor;
            }

            return Task.CompletedTask;
        }

        private ConsoleColor GetConsoleColor(LogLevel level)
        {
            return level switch
            {
                LogLevel.Debug => ConsoleColor.Gray,
                LogLevel.Information => ConsoleColor.White,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Error => ConsoleColor.Red,
                LogLevel.Critical => ConsoleColor.DarkRed,
                _ => ConsoleColor.White
            };
        }
    }

    /// <summary>
    /// メモリログターゲット
    /// </summary>
    public class MemoryLogTarget : ILogTarget, IQueryableLogTarget, IClearableLogTarget
    {
        private readonly ConcurrentQueue<LogEntry> _logs;
        private readonly int _maxEntries;

        public MemoryLogTarget(int maxEntries = 1000)
        {
            _logs = new ConcurrentQueue<LogEntry>();
            _maxEntries = maxEntries;
        }

        public Task WriteLogAsync(LogEntry logEntry)
        {
            _logs.Enqueue(logEntry);

            // 最大エントリ数を超えた場合、古いエントリを削除
            while (_logs.Count > _maxEntries)
            {
                _logs.TryDequeue(out _);
            }

            return Task.CompletedTask;
        }

        public Task<List<LogEntry>> QueryLogsAsync(LogLevel? minimumLevel = null, DateTime? startTime = null, DateTime? endTime = null, string category = null)
        {
            var filteredLogs = new List<LogEntry>();

            foreach (var log in _logs)
            {
                if (MatchesFilter(log, minimumLevel, startTime, endTime, category))
                {
                    filteredLogs.Add(log);
                }
            }

            return Task.FromResult(filteredLogs);
        }

        public Task ClearLogsAsync()
        {
            while (_logs.TryDequeue(out _)) { }
            return Task.CompletedTask;
        }

        private bool MatchesFilter(LogEntry logEntry, LogLevel? minimumLevel, DateTime? startTime, DateTime? endTime, string category)
        {
            if (minimumLevel.HasValue && logEntry.Level < minimumLevel.Value)
                return false;

            if (startTime.HasValue && logEntry.Timestamp < startTime.Value)
                return false;

            if (endTime.HasValue && logEntry.Timestamp > endTime.Value)
                return false;

            if (!string.IsNullOrEmpty(category) && !string.Equals(logEntry.Category, category, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }
    }
}