using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// Lightweight logger with minimal overhead
    /// </summary>
    public static class Logger
    {
        private static readonly ConcurrentQueue<string> _logQueue = new();
        private static readonly string _logPath;
        private static readonly Timer _flushTimer;
        private static volatile bool _enabled = true;

        static Logger()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var logDir = Path.Combine(appData, "MurtiWifiConnecter");
            Directory.CreateDirectory(logDir);

            _logPath = Path.Combine(logDir, $"log_{DateTime.Now:yyyyMMdd}.txt");

            // Flush logs every 5 seconds
            _flushTimer = new Timer(_ => FlushLogs(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        public static void Enable(bool enabled) => _enabled = enabled;

        public static void Info(string message)
        {
            if (_enabled)
                Log("INFO", message);
        }

        public static void Warning(string message)
        {
            if (_enabled)
                Log("WARN", message);
        }

        public static void Error(string message, Exception? ex = null)
        {
            if (_enabled)
            {
                var errorMsg = ex != null ? $"{message}: {ex.Message}" : message;
                Log("ERROR", errorMsg);
            }
        }

        private static void Log(string level, string message)
        {
            var logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
            _logQueue.Enqueue(logEntry);

            // Auto-flush if queue is getting large
            if (_logQueue.Count > 100)
            {
                Task.Run(() => FlushLogs());
            }
        }

        private static void FlushLogs()
        {
            if (_logQueue.IsEmpty)
                return;

            try
            {
                var logs = new System.Text.StringBuilder();
                while (_logQueue.TryDequeue(out var log))
                {
                    logs.AppendLine(log);
                }

                if (logs.Length > 0)
                {
                    File.AppendAllText(_logPath, logs.ToString());
                }

                // Clean old logs (keep only last 7 days)
                CleanOldLogs();
            }
            catch
            {
                // Ignore logging errors
            }
        }

        private static void CleanOldLogs()
        {
            try
            {
                var logDir = Path.GetDirectoryName(_logPath);
                if (string.IsNullOrEmpty(logDir))
                    return;

                var cutoffDate = DateTime.Now.AddDays(-7);
                foreach (var file in Directory.GetFiles(logDir, "log_*.txt"))
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.CreationTime < cutoffDate)
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        public static void Shutdown()
        {
            _flushTimer?.Dispose();
            FlushLogs();
        }
    }

    // Simplified static logging class for compatibility
    public static class Logging
    {
        public static void LogInfo(string source, string message) => Logger.Info($"[{source}] {message}");
        public static void LogWarning(string source, string message) => Logger.Warning($"[{source}] {message}");
        public static void LogError(string source, string message) => Logger.Error($"[{source}] {message}");
        public static void LogException(string source, Exception ex) => Logger.Error($"[{source}] Exception", ex);
    }
}