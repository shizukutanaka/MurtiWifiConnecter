using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// 高性能な統合ログシステム - John Carmackスタイルのシンプルで効率的な設計
    /// 構造化ログと基本ログを統合し、メモリ効率とパフォーマンスを最適化
    /// </summary>
    public static class Logger
    {
        // 軽量な内部状態管理 - 過度な抽象化を避ける
        private static readonly object _syncLock = new();
        private static string _logDirectory = string.Empty;
        private static bool _initialized;
        private static byte[] _integrityKey;
        private static readonly SemaphoreSlim _writeLock = new(1, 1);

        // 設定の直接管理 - キャッシュ不要のシンプル設計
        private static LogLevel _currentLevel = LogLevel.Info;
        private static DateTime _currentDate = DateTime.MinValue;
        private static string _currentLogFile = string.Empty;

        // 統計情報の直接管理 - 軽量なカウンタ
        private static int _totalLogs, _errorLogs, _warningLogs, _infoLogs, _debugLogs, _securityLogs;

        // ログ出力の直接制御 - シンプルな配列管理
        private const int MaxLogFiles = 30;
        private static readonly string[] _recentMessages = new string[100];
        private static int _messageIndex = 0;

        /// <summary>
        /// ログシステムを初期化 - 直接的で高速な初期化
        /// </summary>
        public static async Task InitializeAsync()
        {
            if (_initialized) return;

            lock (_syncLock)
            {
                if (_initialized) return;

                // 直接的なディレクトリ作成 - 不要なチェックを避ける
                _logDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MurtiWifiConnecter", "logs");

                Directory.CreateDirectory(_logDirectory);

                try
                {
                    SecurityManager.EnsureSecureDirectoryAclAsync(_logDirectory).GetAwaiter().GetResult();
                }
                catch (Exception aclEx)
                {
                    Console.WriteLine($"Warning: Failed to apply secure ACLs to log directory: {aclEx.Message}");
                    _ = LoggerFallbackAudit("LogDirectoryAclFailure", new Dictionary<string, object>
                    {
                        ["directory"] = _logDirectory,
                        ["error"] = aclEx.Message
                    });
                }

                // 整合性キーの直接生成
                if (_integrityKey == null)
                {
                    _integrityKey = GenerateIntegrityKey();
                }

                _initialized = true;
                await LogInfo("Logger initialized", "Logger", new Dictionary<string, object>
                {
                    ["LogDirectory"] = _logDirectory,
                    ["IntegrityKeyLength"] = _integrityKey?.Length ?? 0
                });
            }
        }

        private static async Task LoggerFallbackAudit(string eventName, Dictionary<string, object> payload)
        {
            try
            {
                await AuditTrail.RecordEventAsync("Logging", eventName, payload, "Warning").ConfigureAwait(false);
            }
            catch
            {
                // Swallow audit failures to avoid recursion during logging issues
            }
        }

        /// <summary>
        /// 情報ログ - 直接的な出力
        /// </summary>
        public static async Task LogInfo(string message, string? source = null, Dictionary<string, object>? properties = null)</parameter
        {
            await WriteLog(LogLevel.Info, message, source, properties);
        }

        /// <summary>
        /// 警告ログ - 直接的な出力
        /// </summary>
        public static async Task LogWarning(string message, string? source = null, Dictionary<string, object>? properties = null)
        {
            await WriteLog(LogLevel.Warning, message, source, properties);
        }

        /// <summary>
        /// エラーログ - 直接的な出力
        /// </summary>
        public static async Task LogError(string message, string? source = null, Dictionary<string, object>? properties = null, Exception? exception = null)
        {
            await WriteLog(LogLevel.Error, message, source, properties, exception);
        }

        /// <summary>
        /// デバッグログ - 直接的な出力
        /// </summary>
        public static async Task LogDebug(string message, string? source = null, Dictionary<string, object>? properties = null)
        {
            await WriteLog(LogLevel.Debug, message, source, properties);
        }

        /// <summary>
        /// セキュリティログ - 直接的な出力
        /// </summary>
        public static async Task LogSecurity(string message, string category, Dictionary<string, object>? properties = null)
        {
            var securityProps = new Dictionary<string, object>(properties ?? new Dictionary<string, object>())
            {
                ["SecurityCategory"] = category,
                ["SecurityEvent"] = true
            };

            await WriteLog(LogLevel.Warning, message, "Security", securityProps);
        }

        /// <summary>
        /// パフォーマンスログ - 直接的な出力
        /// </summary>
        public static async Task LogPerformance(string operation, double durationMs, Dictionary<string, object>? metadata = null)
        {
            var perfProps = new Dictionary<string, object>(metadata ?? new Dictionary<string, object>())
            {
                ["Operation"] = operation,
                ["DurationMs"] = durationMs,
                ["PerformanceMetric"] = true
            };

            await WriteLog(LogLevel.Info, $"Performance: {operation} in {durationMs:F2}ms", "Performance", perfProps);
        }

        /// <summary>
        /// ログ出力の中心 - 直接的で高速な実装
        /// </summary>
        private static async Task WriteLog(LogLevel level, string message, string? source, Dictionary<string, object>? properties, Exception? exception = null)
        {
            if (!_initialized)
            {
                // 初期化前に直接出力
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{level}] {message}");
                return;
            }

            // ログレベルの直接チェック - 高速判定
            if (!ShouldLog(level)) return;

            // ログエントリの直接構築 - 文字列連結の最適化
            var timestamp = DateTime.Now;
            var logEntry = BuildLogEntry(timestamp, level, message, source, properties, exception);

            // 統計情報の直接更新 - ロックフリーなカウンタ
            UpdateCounters(level);

            // 最近のメッセージを直接保存 - リングバッファ
            StoreRecentMessage(logEntry);

            // ファイル出力 - 直接的で効率的な書き込み
            await WriteToFileAsync(logEntry);

            // コンソール出力 - 開発時のみ
            if (IsDevelopment())
            {
                Console.WriteLine(logEntry);
            }
        }

        /// <summary>
        /// ログエントリの直接構築 - シンプルな文字列処理
        /// </summary>
        private static string BuildLogEntry(DateTime timestamp, LogLevel level, string message, string source, Dictionary<string, object> properties, Exception exception)
        {
            var sb = new StringBuilder(256); // 初期容量を直接指定

            // タイムスタンプ - 高速フォーマット
            sb.Append('[').Append(timestamp.ToString("HH:mm:ss.fff")).Append("] ");

            // ログレベル - 直接文字列
            sb.Append('[').Append(GetLevelString(level)).Append("] ");

            // メッセージ - 直接追加
            if (!string.IsNullOrEmpty(source))
            {
                sb.Append('[').Append(source).Append("] ");
            }

            sb.Append(message);

            // プロパティ - シンプルなJSON形式
            if (properties != null && properties.Count > 0)
            {
                sb.Append(" {");
                var first = true;
                foreach (var prop in properties)
                {
                    if (!first) sb.Append(", ");
                    sb.Append('"').Append(prop.Key).Append("\":");
                    sb.Append(JsonSerializer.Serialize(prop.Value));
                    first = false;
                }
                sb.Append('}');
            }

            // 例外 - 直接追加
            if (exception != null)
            {
                sb.Append(" | Exception: ").Append(exception.Message);
            }

            return sb.ToString();
        }

        /// <summary>
        /// ファイル出力 - 直接的で効率的な書き込み
        /// </summary>
        private static async Task WriteToFileAsync(string logEntry)
        {
            var currentDate = DateTime.Now.Date;
            var currentFile = Path.Combine(_logDirectory, $"murtiwifi-{currentDate:yyyy-MM-dd}.log");

            try
            {
                await _writeLock.WaitAsync();

                // ファイルが変更された場合のみ再オープン
                if (currentFile != _currentLogFile)
                {
                    _currentLogFile = currentFile;
                    _currentDate = currentDate;

                    try
                    {
                        if (!File.Exists(_currentLogFile))
                        {
                            using (File.Create(_currentLogFile))
                            {
                                // ensure file exists before ACL enforcement
                            }
                        }

                        await SecurityManager.EnsureSecureFileAclAsync(_currentLogFile).ConfigureAwait(false);
                    }
                    catch (Exception aclEx)
                    {
                        await LogWarning("Failed to apply secure ACLs to log file", nameof(Logger), new Dictionary<string, object>
                        {
                            ["file"] = _currentLogFile,
                            ["error"] = aclEx.Message
                        }).ConfigureAwait(false);

                        await LoggerFallbackAudit("LogFileAclFailure", new Dictionary<string, object>
                        {
                            ["file"] = _currentLogFile,
                            ["error"] = aclEx.Message
                        }).ConfigureAwait(false);
                    }
                }

                // 直接ファイル書き込み - 効率的なI/O
                await File.AppendAllTextAsync(currentFile, logEntry + Environment.NewLine);

                // ファイルサイズチェック - 直接判定
                var fileInfo = new FileInfo(currentFile);
                if (fileInfo.Length > 10 * 1024 * 1024) // 10MB
                {
                    // ログローテーション - 直接実装
                    await RotateLogFilesAsync();
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// ログローテーション - シンプルな実装
        /// </summary>
        private static async Task RotateLogFilesAsync()
        {
            var logFiles = Directory.GetFiles(_logDirectory, "murtiwifi-*.log")
                .OrderByDescending(File.GetLastWriteTime)
                .ToArray();

            // 古いファイルを直接削除
            for (int i = MaxLogFiles; i < logFiles.Length; i++)
            {
                try { File.Delete(logFiles[i]); } catch { }
            }
        }

        public static async Task<int> PurgeLogsAsync(int retentionDays = 30, bool secureDelete = true)
        {
            await InitializeAsync().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(_logDirectory) || !Directory.Exists(_logDirectory))
            {
                return 0;
            }

            var cutoff = DateTime.Now.AddDays(-Math.Max(retentionDays, 0));
            var removed = 0;

            var files = Directory.GetFiles(_logDirectory, "murtiwifi-*.log", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTime < cutoff)
                    {
                        if (secureDelete)
                        {
                            await SecurityManager.SecureDeleteFileAsync(file).ConfigureAwait(false);
                        }
                        else
                        {
                            info.Delete();
                        }

                        removed++;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Logger] Failed to purge log '{file}': {ex.Message}");
                }
            }

            if (removed > 0)
            {
                await LogInfo("Log files purged", nameof(Logger), new Dictionary<string, object>
                {
                    ["removed"] = removed,
                    ["retentionDays"] = retentionDays,
                    ["secureDeletion"] = secureDelete
                }).ConfigureAwait(false);
            }

            return removed;
        }

        /// <summary>
        /// ログレベル判定 - 直接比較
        /// </summary>
        private static bool ShouldLog(LogLevel level)
        {
            return (int)level >= (int)_currentLevel;
        }

        /// <summary>
        /// カウンタ更新 - ロックフリーな直接更新
        /// </summary>
        private static void UpdateCounters(LogLevel level)
        {
            Interlocked.Increment(ref _totalLogs);

            switch (level)
            {
                case LogLevel.Error: Interlocked.Increment(ref _errorLogs); break;
                case LogLevel.Warning: Interlocked.Increment(ref _warningLogs); break;
                case LogLevel.Info: Interlocked.Increment(ref _infoLogs); break;
                case LogLevel.Debug: Interlocked.Increment(ref _debugLogs); break;
            }
        }

        /// <summary>
        /// 最近のメッセージ保存 - リングバッファ
        /// </summary>
        private static void StoreRecentMessage(string message)
        {
            var index = Interlocked.Increment(ref _messageIndex) % _recentMessages.Length;
            _recentMessages[index] = message;
        }

        /// <summary>
        /// ログ統計取得 - 直接カウンタ読み取り
        /// </summary>
        public static LogStatistics GetStatistics()
        {
            return new LogStatistics
            {
                TotalLogEntries = _totalLogs,
                SecurityEvents = _securityLogs,
                ErrorEvents = _errorLogs,
                WarningEvents = _warningLogs,
                InfoEvents = _infoLogs,
                DebugEvents = _debugLogs,
                LastUpdate = DateTime.Now,
                RecentMessages = GetRecentMessages()
            };
        }

        /// <summary>
        /// 最近のメッセージ取得 - 直接配列コピー
        /// </summary>
        private static string[] GetRecentMessages()
        {
            var messages = new string[Math.Min(_recentMessages.Length, _messageIndex + 1)];
            var startIndex = (_messageIndex + 1) % _recentMessages.Length;

            for (int i = 0; i < messages.Length; i++)
            {
                messages[i] = _recentMessages[(startIndex + i) % _recentMessages.Length];
            }

            return messages;
        }

        /// <summary>
        /// ログレベル文字列 - 直接マッピング
        /// </summary>
        private static string GetLevelString(LogLevel level)
        {
            return level switch
            {
                LogLevel.Debug => "DBG",
                LogLevel.Info => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                _ => "UNK"
            };
        }

        /// <summary>
        /// 開発環境判定 - 直接チェック
        /// </summary>
        private static bool IsDevelopment()
        {
            return Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
        }

        /// <summary>
        /// 整合性キー生成 - 直接実装
        /// </summary>
        private static byte[] GenerateIntegrityKey()
        {
            var key = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(key);
            return key;
        }

        /// <summary>
        /// ログ設定更新 - 直接設定
        /// </summary>
        public static void SetLogLevel(LogLevel level)
        {
            _currentLevel = level;
        }

        /// <summary>
        /// ログ出力テスト - 直接テスト
        /// </summary>
        public static async Task<bool> TestLoggingAsync()
        {
            try
            {
                await LogInfo("Log system test", "Logger", new Dictionary<string, object>
                {
                    ["Test"] = true,
                    ["Timestamp"] = DateTime.Now
                });
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// ログレベル定義 - シンプルな列挙
    /// </summary>
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3
    }

    /// <summary>
    /// ログ統計 - 軽量な構造体
    /// </summary>
    public struct LogStatistics
    {
        public int TotalLogEntries { get; set; }
        public int SecurityEvents { get; set; }
        public int ErrorEvents { get; set; }
        public int WarningEvents { get; set; }
        public int InfoEvents { get; set; }
        public int DebugEvents { get; set; }
        public DateTime LastUpdate { get; set; }
        public string[] RecentMessages { get; set; }
    }

    /// <summary>
    /// ログレベル定義 - シンプルな列挙
    /// </summary>
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3
    }
}
