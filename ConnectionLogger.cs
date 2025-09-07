using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    public class ConnectionLogger : IDisposable
    {
        private readonly string _logDirectory;
        private readonly SemaphoreSlim _writeSemaphore = new(1, 1);
        private readonly Queue<LogEntry> _logQueue = new();
        private readonly Timer _flushTimer;
        private bool _disposed;
        private const int MaxLogFileSize = 5 * 1024 * 1024; // 5MB
        private const int MaxLogFiles = 5;
        private const int FlushIntervalMs = 5000; // 5秒ごとにフラッシュ

        public enum LogLevel
        {
            Debug,
            Info,
            Warning,
            Error
        }

        private class LogEntry
        {
            public DateTime Timestamp { get; set; }
            public LogLevel Level { get; set; }
            public string Category { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public string? Details { get; set; }
        }

        public ConnectionLogger()
        {
            _logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MurtiWifiConnecter",
                "Logs");
                
            if (!Directory.Exists(_logDirectory))
                Directory.CreateDirectory(_logDirectory);
                
            _flushTimer = new Timer(FlushLogs, null, FlushIntervalMs, FlushIntervalMs);
        }

        public void LogConnection(string ssid, bool success, int signalStrength, string? errorMessage = null)
        {
            var message = $"Connection to '{ssid}' - Signal: {signalStrength}%";
            var details = success ? "Success" : $"Failed: {errorMessage ?? "Unknown error"}";
            
            Log(success ? LogLevel.Info : LogLevel.Warning, "Connection", message, details);
        }

        public void LogDisconnection(string ssid, string reason)
        {
            Log(LogLevel.Info, "Disconnection", $"Disconnected from '{ssid}'", reason);
        }

        public void LogNetworkScan(int networksFound, long scanTimeMs)
        {
            Log(LogLevel.Debug, "Scan", $"Found {networksFound} networks in {scanTimeMs}ms");
        }

        public void LogAutoSwitch(string fromSSID, string toSSID, bool success)
        {
            var message = $"Auto-switch from '{fromSSID}' to '{toSSID}'";
            Log(success ? LogLevel.Info : LogLevel.Warning, "AutoSwitch", message, 
                success ? "Completed" : "Failed");
        }

        public void LogError(string category, string message, Exception? exception = null)
        {
            Log(LogLevel.Error, category, message, exception?.ToString());
        }

        public void Log(LogLevel level, string category, string message, string? details = null)
        {
            if (_disposed) return;
            
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Category = category,
                Message = message,
                Details = details
            };
            
            lock (_logQueue)
            {
                _logQueue.Enqueue(entry);
                
                // キューが大きくなりすぎたら即座にフラッシュ
                if (_logQueue.Count > 100)
                {
                    Task.Run(() => FlushLogs(null));
                }
            }
        }

        private async void FlushLogs(object? state)
        {
            if (_disposed || !await _writeSemaphore.WaitAsync(100))
                return;
                
            try
            {
                List<LogEntry> entries;
                lock (_logQueue)
                {
                    if (_logQueue.Count == 0) return;
                    entries = _logQueue.ToList();
                    _logQueue.Clear();
                }
                
                await WriteLogsToFileAsync(entries);
            }
            catch { }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        private async Task WriteLogsToFileAsync(List<LogEntry> entries)
        {
            var logFileName = GetCurrentLogFileName();
            var logFilePath = Path.Combine(_logDirectory, logFileName);
            
            // ファイルサイズチェックとローテーション
            if (File.Exists(logFilePath))
            {
                var fileInfo = new FileInfo(logFilePath);
                if (fileInfo.Length > MaxLogFileSize)
                {
                    await RotateLogsAsync();
                    logFilePath = Path.Combine(_logDirectory, GetCurrentLogFileName());
                }
            }
            
            // ログエントリを書き込み
            var sb = new StringBuilder();
            foreach (var entry in entries)
            {
                sb.AppendLine(FormatLogEntry(entry));
            }
            
            await File.AppendAllTextAsync(logFilePath, sb.ToString());
        }

        private string FormatLogEntry(LogEntry entry)
        {
            var level = entry.Level switch
            {
                LogLevel.Debug => "DBG",
                LogLevel.Info => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                _ => "UNK"
            };
            
            var formatted = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [{entry.Category}] {entry.Message}";
            
            if (!string.IsNullOrEmpty(entry.Details))
            {
                formatted += $"\n    Details: {entry.Details}";
            }
            
            return formatted;
        }

        private string GetCurrentLogFileName()
        {
            return $"connection_{DateTime.Now:yyyyMMdd}.log";
        }

        private async Task RotateLogsAsync()
        {
            try
            {
                var logFiles = Directory.GetFiles(_logDirectory, "connection_*.log")
                    .OrderByDescending(f => new FileInfo(f).CreationTime)
                    .ToList();
                    
                // 古いログファイルを削除
                if (logFiles.Count >= MaxLogFiles)
                {
                    foreach (var oldFile in logFiles.Skip(MaxLogFiles - 1))
                    {
                        try { File.Delete(oldFile); } catch { }
                    }
                }
                
                // 現在のログファイルをアーカイブ
                var currentLog = logFiles.FirstOrDefault();
                if (currentLog != null && File.Exists(currentLog))
                {
                    var archiveName = Path.GetFileNameWithoutExtension(currentLog) + 
                                     $"_{DateTime.Now:HHmmss}.log";
                    var archivePath = Path.Combine(_logDirectory, archiveName);
                    File.Move(currentLog, archivePath);
                }
            }
            catch { }
        }

        public async Task<List<string>> GetRecentLogsAsync(int lines = 100)
        {
            var result = new List<string>();
            
            try
            {
                var logFiles = Directory.GetFiles(_logDirectory, "connection_*.log")
                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                    .Take(2) // 最新2ファイルから取得
                    .ToList();
                    
                foreach (var logFile in logFiles)
                {
                    if (result.Count >= lines) break;
                    
                    var fileLines = await File.ReadAllLinesAsync(logFile);
                    var relevantLines = fileLines
                        .Reverse()
                        .Take(lines - result.Count);
                        
                    result.AddRange(relevantLines);
                }
                
                result.Reverse(); // 時系列順に戻す
            }
            catch { }
            
            return result;
        }

        public async Task<Dictionary<string, int>> GetConnectionStatisticsAsync(DateTime since)
        {
            var stats = new Dictionary<string, int>();
            
            try
            {
                var logFiles = Directory.GetFiles(_logDirectory, "connection_*.log")
                    .Where(f => new FileInfo(f).LastWriteTime >= since)
                    .ToList();
                    
                foreach (var logFile in logFiles)
                {
                    var lines = await File.ReadAllLinesAsync(logFile);
                    foreach (var line in lines)
                    {
                        if (line.Contains("[Connection]") && line.Contains("Success"))
                        {
                            // SSIDを抽出
                            var startIdx = line.IndexOf("'");
                            var endIdx = line.IndexOf("'", startIdx + 1);
                            if (startIdx >= 0 && endIdx > startIdx)
                            {
                                var ssid = line.Substring(startIdx + 1, endIdx - startIdx - 1);
                                stats[ssid] = stats.ContainsKey(ssid) ? stats[ssid] + 1 : 1;
                            }
                        }
                    }
                }
            }
            catch { }
            
            return stats;
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            
            // 残りのログをフラッシュ
            FlushLogs(null);
            Thread.Sleep(100); // フラッシュ完了を待つ
            
            _flushTimer?.Dispose();
            _writeSemaphore?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}