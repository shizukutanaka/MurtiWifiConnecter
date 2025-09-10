using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Infrastructure.Telemetry
{
    #region Core Data Structures

    /// <summary>
    /// 構造化ログエントリ
    /// </summary>
    public class LogEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public JsonElement Data { get; set; }
        public string? TraceId { get; set; }
        public string? SpanId { get; set; }
        public int ThreadId { get; set; }
        public int ProcessId { get; set; }
        public string MachineName { get; set; } = string.Empty;
        public CallerInfo CallerInfo { get; set; } = new();
        public Dictionary<string, object> Properties { get; set; } = new();
        public Exception? Exception { get; set; }
    }

    /// <summary>
    /// 呼び出し元情報
    /// </summary>
    public class CallerInfo
    {
        public string MemberName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
    }

    /// <summary>
    /// ログレベル定義
    /// </summary>
    public enum LogLevel
    {
        Trace = 0,
        Debug = 1,
        Information = 2,
        Warning = 3,
        Error = 4,
        Critical = 5
    }

    /// <summary>
    /// セキュリティレベル定義
    /// </summary>
    public enum SecurityLevel
    {
        Low = 1,
        Medium = 2,
        High = 3,
        Critical = 4
    }

    #endregion

    #region Configuration

    /// <summary>
    /// ロガー設定
    /// </summary>
    public class LoggerConfiguration
    {
        public LogLevel MinimumLevel { get; set; } = LogLevel.Information;
        public bool EnableConsoleOutput { get; set; } = true;
        public bool EnableFileOutput { get; set; } = true;
        public bool EnableStructuredOutput { get; set; } = true;
        public string LogDirectory { get; set; } = "Logs";
        public long MaxFileSize { get; set; } = 50 * 1024 * 1024; // 50MB
        public int MaxFiles { get; set; } = 20;
        public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(5);
        public bool EnableMetrics { get; set; } = true;
        public bool EnableTracing { get; set; } = true;
        public bool EnableCompression { get; set; } = false;
        public Dictionary<string, object> GlobalProperties { get; set; } = new();
    }

    #endregion

    #region Sink Interfaces and Implementations

    /// <summary>
    /// ログシンクインターフェース
    /// </summary>
    public interface ILogSink : IDisposable
    {
        Task WriteAsync(IReadOnlyList<LogEntry> entries);
        bool IsHealthy();
        Task FlushAsync();
    }

    /// <summary>
    /// コンソールシンク
    /// </summary>
    public class ConsoleSink : ILogSink
    {
        private readonly LoggerConfiguration _config;
        private volatile bool _disposed = false;

        public ConsoleSink(LoggerConfiguration config)
        {
            _config = config;
        }

        public async Task WriteAsync(IReadOnlyList<LogEntry> entries)
        {
            if (_disposed) return;

            foreach (var entry in entries)
            {
                var color = GetConsoleColor(entry.Level);
                var originalColor = Console.ForegroundColor;
                
                try
                {
                    Console.ForegroundColor = color;
                    var message = FormatConsoleMessage(entry);
                    await Console.Out.WriteLineAsync(message).ConfigureAwait(false);
                }
                finally
                {
                    Console.ForegroundColor = originalColor;
                }
            }
        }

        public bool IsHealthy() => !_disposed;

        public Task FlushAsync() => Task.CompletedTask;

        private ConsoleColor GetConsoleColor(LogLevel level)
        {
            return level switch
            {
                LogLevel.Critical => ConsoleColor.Magenta,
                LogLevel.Error => ConsoleColor.Red,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Information => ConsoleColor.White,
                LogLevel.Debug => ConsoleColor.Gray,
                LogLevel.Trace => ConsoleColor.DarkGray,
                _ => ConsoleColor.White
            };
        }

        private string FormatConsoleMessage(LogEntry entry)
        {
            var levelStr = entry.Level switch
            {
                LogLevel.Critical => "CRT",
                LogLevel.Error => "ERR",
                LogLevel.Warning => "WRN",
                LogLevel.Information => "INF",
                LogLevel.Debug => "DBG",
                LogLevel.Trace => "TRC",
                _ => "UNK"
            };

            return $"[{entry.Timestamp:HH:mm:ss.fff}] [{levelStr}] [{entry.Category}] {entry.Message}";
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }

    /// <summary>
    /// ファイルシンク
    /// </summary>
    public class FileSink : ILogSink
    {
        private readonly LoggerConfiguration _config;
        private readonly SemaphoreSlim _writeSemaphore = new(1, 1);
        private volatile bool _disposed = false;

        public FileSink(LoggerConfiguration config)
        {
            _config = config;
            Directory.CreateDirectory(_config.LogDirectory);
        }

        public async Task WriteAsync(IReadOnlyList<LogEntry> entries)
        {
            if (_disposed || !entries.Any()) return;

            await _writeSemaphore.WaitAsync().ConfigureAwait(false);
            
            try
            {
                var filePath = GetCurrentLogFilePath();
                var content = string.Join(Environment.NewLine, entries.Select(FormatLogEntry)) + Environment.NewLine;
                
                await File.AppendAllTextAsync(filePath, content).ConfigureAwait(false);
                
                // ファイルローテーション確認
                await CheckFileRotationAsync(filePath).ConfigureAwait(false);
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        public bool IsHealthy() => !_disposed && Directory.Exists(_config.LogDirectory);

        public async Task FlushAsync()
        {
            // ファイルシステムへのフラッシュは自動で行われる
            await Task.CompletedTask;
        }

        private string GetCurrentLogFilePath()
        {
            var fileName = $"app_{DateTime.Now:yyyyMMdd}.log";
            return Path.Combine(_config.LogDirectory, fileName);
        }

        private string FormatLogEntry(LogEntry entry)
        {
            var levelStr = entry.Level.ToString().ToUpper().Substring(0, 3);
            
            var message = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{levelStr}] [{entry.Category}] [{entry.ThreadId}] {entry.Message}";
            
            if (entry.Properties.Any())
            {
                var props = string.Join(", ", entry.Properties.Select(p => $"{p.Key}={p.Value}"));
                message += $" | Properties: {props}";
            }
            
            if (entry.Exception != null)
            {
                message += $"{Environment.NewLine}Exception: {entry.Exception}";
            }
            
            return message;
        }

        private async Task CheckFileRotationAsync(string filePath)
        {
            if (!File.Exists(filePath)) return;
            
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > _config.MaxFileSize)
            {
                await RotateLogsAsync().ConfigureAwait(false);
            }
        }

        private async Task RotateLogsAsync()
        {
            try
            {
                var logFiles = Directory.GetFiles(_config.LogDirectory, "app_*.log")
                    .OrderByDescending(f => new FileInfo(f).CreationTime)
                    .ToList();

                // 古いファイルを削除
                if (logFiles.Count >= _config.MaxFiles)
                {
                    foreach (var oldFile in logFiles.Skip(_config.MaxFiles - 1))
                    {
                        try { File.Delete(oldFile); } catch { }
                    }
                }

                // 現在のファイルをアーカイブ
                var currentLog = logFiles.FirstOrDefault();
                if (currentLog != null && File.Exists(currentLog))
                {
                    var archiveName = Path.GetFileNameWithoutExtension(currentLog) +
                                     $"_{DateTime.Now:HHmmss}.log";
                    var archivePath = Path.Combine(_config.LogDirectory, archiveName);
                    File.Move(currentLog, archivePath);
                }
            }
            catch
            {
                // ローテーションエラーは無視
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _writeSemaphore?.Dispose();
        }
    }

    /// <summary>
    /// 構造化ファイルシンク（JSON形式）
    /// </summary>
    public class StructuredFileSink : ILogSink
    {
        private readonly LoggerConfiguration _config;
        private readonly SemaphoreSlim _writeSemaphore = new(1, 1);
        private volatile bool _disposed = false;

        public StructuredFileSink(LoggerConfiguration config)
        {
            _config = config;
            Directory.CreateDirectory(_config.LogDirectory);
        }

        public async Task WriteAsync(IReadOnlyList<LogEntry> entries)
        {
            if (_disposed || !entries.Any()) return;

            await _writeSemaphore.WaitAsync().ConfigureAwait(false);
            
            try
            {
                var filePath = GetCurrentStructuredLogFilePath();
                var jsonLines = entries.Select(SerializeLogEntry);
                var content = string.Join(Environment.NewLine, jsonLines) + Environment.NewLine;
                
                await File.AppendAllTextAsync(filePath, content).ConfigureAwait(false);
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        public bool IsHealthy() => !_disposed && Directory.Exists(_config.LogDirectory);

        public async Task FlushAsync()
        {
            await Task.CompletedTask;
        }

        /// <summary>
        /// ログクエリ実行
        /// </summary>
        public async Task<LogQueryResult> QueryAsync(LogQuery query, CancellationToken cancellationToken)
        {
            try
            {
                var files = Directory.GetFiles(_config.LogDirectory, "structured_*.jsonl")
                    .Where(f => IsFileInDateRange(f, query.StartDate, query.EndDate))
                    .OrderByDescending(f => new FileInfo(f).CreationTime);

                var results = new List<LogEntry>();
                var totalScanned = 0;

                foreach (var file in files)
                {
                    if (cancellationToken.IsCancellationRequested || results.Count >= query.MaxResults)
                        break;

                    var fileResults = await QueryFileAsync(file, query, cancellationToken).ConfigureAwait(false);
                    results.AddRange(fileResults.Take(query.MaxResults - results.Count));
                    totalScanned += fileResults.Count;
                }

                return new LogQueryResult
                {
                    Success = true,
                    Results = results,
                    TotalScanned = totalScanned,
                    ExecutionTime = TimeSpan.FromMilliseconds(100) // 実装簡略化
                };
            }
            catch (Exception ex)
            {
                return new LogQueryResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private string GetCurrentStructuredLogFilePath()
        {
            var fileName = $"structured_{DateTime.Now:yyyyMMdd}.jsonl";
            return Path.Combine(_config.LogDirectory, fileName);
        }

        private string SerializeLogEntry(LogEntry entry)
        {
            var logObject = new
            {
                entry.Id,
                entry.Timestamp,
                Level = entry.Level.ToString(),
                entry.Category,
                entry.Message,
                Data = entry.Data.ValueKind != JsonValueKind.Undefined ? entry.Data : default(JsonElement?),
                entry.TraceId,
                entry.SpanId,
                entry.ThreadId,
                entry.ProcessId,
                entry.MachineName,
                entry.CallerInfo,
                entry.Properties,
                Exception = entry.Exception?.ToString()
            };

            return JsonSerializer.Serialize(logObject, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }

        private bool IsFileInDateRange(string filePath, DateTime? startDate, DateTime? endDate)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var dateStr = fileName.Split('_').LastOrDefault();
            
            if (DateTime.TryParseExact(dateStr, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var fileDate))
            {
                return (startDate == null || fileDate >= startDate) && 
                       (endDate == null || fileDate <= endDate);
            }
            
            return true; // 日付が解析できない場合は含める
        }

        private async Task<List<LogEntry>> QueryFileAsync(string filePath, LogQuery query, CancellationToken cancellationToken)
        {
            var results = new List<LogEntry>();
            
            using var reader = new StreamReader(filePath);
            string line;
            
            while ((line = await reader.ReadLineAsync()) != null && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var logEntry = JsonSerializer.Deserialize<LogEntry>(line);
                    if (logEntry != null && MatchesQuery(logEntry, query))
                    {
                        results.Add(logEntry);
                    }
                }
                catch
                {
                    // 解析エラーは無視
                }
            }
            
            return results;
        }

        private bool MatchesQuery(LogEntry entry, LogQuery query)
        {
            if (query.MinLevel.HasValue && entry.Level < query.MinLevel.Value)
                return false;
            
            if (!string.IsNullOrEmpty(query.Category) && !entry.Category.Contains(query.Category, StringComparison.OrdinalIgnoreCase))
                return false;
            
            if (!string.IsNullOrEmpty(query.MessageFilter) && !entry.Message.Contains(query.MessageFilter, StringComparison.OrdinalIgnoreCase))
                return false;
            
            if (query.StartDate.HasValue && entry.Timestamp < query.StartDate.Value)
                return false;
            
            if (query.EndDate.HasValue && entry.Timestamp > query.EndDate.Value)
                return false;
            
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _writeSemaphore?.Dispose();
        }
    }

    /// <summary>
    /// メトリクスシンク
    /// </summary>
    public class MetricsSink : ILogSink
    {
        private readonly MetricsCollector _metricsCollector;
        private volatile bool _disposed = false;

        public MetricsSink(MetricsCollector metricsCollector)
        {
            _metricsCollector = metricsCollector;
        }

        public async Task WriteAsync(IReadOnlyList<LogEntry> entries)
        {
            if (_disposed) return;

            foreach (var entry in entries)
            {
                _metricsCollector.RecordLogEvent(entry.Level.ToString(), entry.Category);
                
                if (entry.Level >= LogLevel.Warning)
                {
                    _metricsCollector.RecordErrorEvent(entry.Category, entry.Level.ToString());
                }
            }

            await Task.CompletedTask;
        }

        public bool IsHealthy() => !_disposed;

        public Task FlushAsync() => Task.CompletedTask;

        public void Dispose()
        {
            _disposed = true;
        }
    }

    #endregion

    #region Query System

    /// <summary>
    /// ログクエリ
    /// </summary>
    public class LogQuery
    {
        public LogLevel? MinLevel { get; set; }
        public string? Category { get; set; }
        public string? MessageFilter { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int MaxResults { get; set; } = 1000;
        public string? TraceId { get; set; }
        public Dictionary<string, object> PropertyFilters { get; set; } = new();
    }

    /// <summary>
    /// ログクエリ結果
    /// </summary>
    public class LogQueryResult
    {
        public bool Success { get; set; }
        public List<LogEntry> Results { get; set; } = new();
        public int TotalScanned { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// ログストリームオプション
    /// </summary>
    public class LogStreamOptions
    {
        public LogLevel MinLevel { get; set; } = LogLevel.Information;
        public string? CategoryFilter { get; set; }
        public TimeSpan BufferTime { get; set; } = TimeSpan.FromSeconds(1);
        public int MaxBufferSize { get; set; } = 100;
    }

    #endregion

    #region Statistics and Monitoring

    /// <summary>
    /// ロギング統計
    /// </summary>
    public class LoggingStatistics
    {
        public long TotalLogsProcessed { get; set; }
        public long TotalBytesWritten { get; set; }
        public int ActiveSinks { get; set; }
        public int TotalSinks { get; set; }
        public bool IsHealthy { get; set; }
        public TimeSpan Uptime { get; set; }
        public double AverageLogSize { get; set; }
        public Dictionary<string, long> LogsByLevel { get; set; } = new();
        public Dictionary<string, long> LogsByCategory { get; set; } = new();
    }

    /// <summary>
    /// メトリクス収集システム
    /// </summary>
    public class MetricsCollector : IDisposable
    {
        private readonly ConcurrentDictionary<string, long> _counters = new();
        private readonly ConcurrentDictionary<string, double> _gauges = new();
        private readonly ConcurrentDictionary<string, List<double>> _histograms = new();
        private volatile bool _disposed = false;

        public void RecordMetric(string name, double value, long memoryUsed, Dictionary<string, double>? customMetrics)
        {
            if (_disposed) return;

            _gauges[name] = value;
            _gauges[$"{name}_memory"] = memoryUsed;
            
            if (customMetrics != null)
            {
                foreach (var metric in customMetrics)
                {
                    _gauges[$"{name}_{metric.Key}"] = metric.Value;
                }
            }
        }

        public void RecordLogEvent(string level, string category)
        {
            if (_disposed) return;

            _counters[$"logs_{level}"] = _counters.GetValueOrDefault($"logs_{level}") + 1;
            _counters[$"logs_category_{category}"] = _counters.GetValueOrDefault($"logs_category_{category}") + 1;
        }

        public void RecordErrorEvent(string category, string level)
        {
            if (_disposed) return;

            _counters[$"errors_{category}"] = _counters.GetValueOrDefault($"errors_{category}") + 1;
            _counters[$"errors_level_{level}"] = _counters.GetValueOrDefault($"errors_level_{level}") + 1;
        }

        public MetricsSnapshot GetSnapshot()
        {
            return new MetricsSnapshot
            {
                Counters = new Dictionary<string, long>(_counters),
                Gauges = new Dictionary<string, double>(_gauges),
                Timestamp = DateTimeOffset.UtcNow
            };
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }

    /// <summary>
    /// メトリクススナップショット
    /// </summary>
    public class MetricsSnapshot
    {
        public Dictionary<string, long> Counters { get; set; } = new();
        public Dictionary<string, double> Gauges { get; set; } = new();
        public DateTimeOffset Timestamp { get; set; }
    }

    /// <summary>
    /// トレースコンテキスト
    /// </summary>
    public class TraceContext
    {
        private readonly ThreadLocal<string> _currentTraceId = new();
        private readonly ThreadLocal<string> _currentSpanId = new();

        public string? CurrentTraceId 
        { 
            get => _currentTraceId.Value; 
            set => _currentTraceId.Value = value; 
        }

        public string? CurrentSpanId 
        { 
            get => _currentSpanId.Value; 
            set => _currentSpanId.Value = value; 
        }

        public IDisposable StartTrace(string traceId, string spanId)
        {
            return new TraceScope(this, traceId, spanId);
        }

        private class TraceScope : IDisposable
        {
            private readonly TraceContext _context;
            private readonly string _previousTraceId;
            private readonly string _previousSpanId;

            public TraceScope(TraceContext context, string traceId, string spanId)
            {
                _context = context;
                _previousTraceId = context.CurrentTraceId;
                _previousSpanId = context.CurrentSpanId;
                
                context.CurrentTraceId = traceId;
                context.CurrentSpanId = spanId;
            }

            public void Dispose()
            {
                _context.CurrentTraceId = _previousTraceId;
                _context.CurrentSpanId = _previousSpanId;
            }
        }
    }

    #endregion
}