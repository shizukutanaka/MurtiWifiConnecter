using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// WiFi接続ログ管理の強化クラス
    /// 効率的なログ分析と自動メンテナンス機能を提供
    /// </summary>
    public class WifiLogManager
    {
        private readonly ILogger<WifiLogManager> _logger;
        private readonly string _logDirectory;
        private readonly Timer? _cleanupTimer;

        public WifiLogManager(ILogger<WifiLogManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MurtiWifiConnecter", "Logs");

            // ログディレクトリが存在しない場合は作成
            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }

            // 定期的なログクリーンアップを設定（24時間ごと）
            _cleanupTimer = new Timer(
                CleanupOldLogs,
                null,
                TimeSpan.FromHours(24),
                TimeSpan.FromHours(24));
        }

        /// <summary>
        /// ログファイルを分析して接続パターンを抽出
        /// </summary>
        public async Task<WifiConnectionAnalysis> AnalyzeConnectionLogsAsync(int daysBack = 7)
        {
            try
            {
                var analysis = new WifiConnectionAnalysis();
                var cutoffDate = DateTime.UtcNow.AddDays(-daysBack);

                // ログファイルを収集
                var logFiles = GetLogFilesInDateRange(cutoffDate, DateTime.UtcNow);

                foreach (var logFile in logFiles)
                {
                    await AnalyzeLogFileAsync(logFile, analysis, cutoffDate);
                }

                // 分析結果を計算
                analysis.TotalAnalysisPeriod = daysBack;
                analysis.AnalysisTimestamp = DateTime.UtcNow;

                await _logger.LogInformation("ログ分析を完了しました", new Dictionary<string, object>
                {
                    ["analyzedFiles"] = logFiles.Count,
                    ["periodDays"] = daysBack,
                    ["totalConnections"] = analysis.TotalConnectionAttempts,
                    ["successRate"] = analysis.SuccessRate
                });

                return analysis;
            }
            catch (Exception ex)
            {
                await _logger.LogError("ログ分析中にエラーが発生しました", ex);

                return new WifiConnectionAnalysis
                {
                    AnalysisTimestamp = DateTime.UtcNow,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// ログファイルから特定のネットワークの接続履歴を取得
        /// </summary>
        public async Task<List<ConnectionLogEntry>> GetNetworkConnectionHistoryAsync(string ssid, int maxEntries = 100)
        {
            try
            {
                var entries = new List<ConnectionLogEntry>();
                var logFiles = GetAllLogFiles().OrderByDescending(f => f.LastWriteTime);

                foreach (var logFile in logFiles)
                {
                    var fileEntries = await ParseLogFileForNetworkAsync(logFile.FullName, ssid);

                    // 最新のエントリから追加
                    foreach (var entry in fileEntries.OrderByDescending(e => e.Timestamp))
                    {
                        if (entries.Count >= maxEntries)
                            break;

                        entries.Add(entry);
                    }

                    if (entries.Count >= maxEntries)
                        break;
                }

                return entries
                    .OrderByDescending(e => e.Timestamp)
                    .Take(maxEntries)
                    .ToList();
            }
            catch (Exception ex)
            {
                await _logger.LogError($"ネットワーク '{ssid}' の接続履歴取得中にエラーが発生しました", ex);
                return new List<ConnectionLogEntry>();
            }
        }

        /// <summary>
        /// ログファイルのサイズを最適化（古いエントリの圧縮）
        /// </summary>
        public async Task OptimizeLogFilesAsync()
        {
            try
            {
                var logFiles = GetAllLogFiles()
                    .Where(f => f.Length > 10 * 1024 * 1024) // 10MB以上のファイル
                    .OrderByDescending(f => f.LastWriteTime)
                    .ToList();

                foreach (var logFile in logFiles)
                {
                    await CompressOldLogEntriesAsync(logFile.FullName);
                }

                await _logger.LogInformation($"ログファイル最適化を完了しました", new Dictionary<string, object>
                {
                    ["processedFiles"] = logFiles.Count
                });
            }
            catch (Exception ex)
            {
                await _logger.LogError("ログファイル最適化中にエラーが発生しました", ex);
            }
        }

        /// <summary>
        /// 指定された日付範囲のログファイルを取得
        /// </summary>
        private List<FileInfo> GetLogFilesInDateRange(DateTime startDate, DateTime endDate)
        {
            return GetAllLogFiles()
                .Where(f =>
                    f.LastWriteTime >= startDate &&
                    f.LastWriteTime <= endDate)
                .ToList();
        }

        /// <summary>
        /// 全てのログファイルを取得
        /// </summary>
        private List<FileInfo> GetAllLogFiles()
        {
            return Directory.Exists(_logDirectory)
                ? new DirectoryInfo(_logDirectory)
                    .GetFiles("*.log", SearchOption.TopDirectoryOnly)
                    .Where(f => f.Name.StartsWith("MurtiWifiConnecter"))
                    .ToList()
                : new List<FileInfo>();
        }

        /// <summary>
        /// ログファイルを分析して統計情報を収集
        /// </summary>
        private async Task AnalyzeLogFileAsync(string logFilePath, WifiConnectionAnalysis analysis, DateTime cutoffDate)
        {
            try
            {
                using var reader = new StreamReader(logFilePath);
                string? line;

                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    // タイムスタンプを抽出（ログ形式に基づく）
                    var timestamp = ExtractTimestampFromLogLine(line);
                    if (timestamp < cutoffDate)
                        continue;

                    // 接続試行を検出
                    if (line.Contains("接続試行") || line.Contains("Connecting to"))
                    {
                        analysis.TotalConnectionAttempts++;

                        if (line.Contains("成功") || line.Contains("success"))
                        {
                            analysis.SuccessfulConnections++;
                        }
                        else if (line.Contains("失敗") || line.Contains("failed"))
                        {
                            analysis.FailedConnections++;
                        }
                    }

                    // エラーログを検出
                    if (line.Contains("エラー") || line.Contains("Error") || line.Contains("Exception"))
                    {
                        analysis.ErrorCount++;
                    }

                    // セキュリティイベントを検出
                    if (line.Contains("セキュリティ") || line.Contains("Security") || line.Contains("認証"))
                    {
                        analysis.SecurityEvents++;
                    }
                }
            }
            catch (Exception ex)
            {
                await _logger.LogWarning($"ログファイル '{Path.GetFileName(logFilePath)}' の分析に失敗しました", ex.Message);
            }
        }

        /// <summary>
        /// 指定されたネットワークのログエントリを抽出
        /// </summary>
        private async Task<List<ConnectionLogEntry>> ParseLogFileForNetworkAsync(string logFilePath, string ssid)
        {
            var entries = new List<ConnectionLogEntry>();

            try
            {
                using var reader = new StreamReader(logFilePath);
                string? line;

                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line) || !line.Contains(ssid))
                        continue;

                    var entry = ParseConnectionLogEntry(line, ssid);
                    if (entry != null)
                    {
                        entries.Add(entry);
                    }
                }
            }
            catch (Exception ex)
            {
                await _logger.LogWarning($"ネットワーク '{ssid}' のログ解析中にエラーが発生しました", ex.Message);
            }

            return entries;
        }

        /// <summary>
        /// ログ行からタイムスタンプを抽出
        /// </summary>
        private DateTime ExtractTimestampFromLogLine(string logLine)
        {
            // ログ形式に基づいてタイムスタンプを抽出
            var match = Regex.Match(logLine, @"\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\]");
            if (match.Success && DateTime.TryParse(match.Groups[1].Value, out var timestamp))
            {
                return timestamp;
            }

            return DateTime.MinValue;
        }

        /// <summary>
        /// ログ行から接続ログエントリを解析
        /// </summary>
        private ConnectionLogEntry? ParseConnectionLogEntry(string logLine, string ssid)
        {
            try
            {
                var timestamp = ExtractTimestampFromLogLine(logLine);
                if (timestamp == DateTime.MinValue)
                    return null;

                var isSuccess = logLine.Contains("成功") || logLine.Contains("success");
                var isError = logLine.Contains("失敗") || logLine.Contains("failed") || logLine.Contains("Error");

                return new ConnectionLogEntry
                {
                    Timestamp = timestamp,
                    SSID = ssid,
                    IsSuccessful = isSuccess,
                    IsError = isError,
                    LogMessage = logLine.Trim()
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 古いログエントリを圧縮
        /// </summary>
        private async Task CompressOldLogEntriesAsync(string logFilePath)
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(logFilePath);
                var cutoffDate = DateTime.UtcNow.AddDays(-30); // 30日より古いエントリを圧縮

                var recentLines = lines
                    .Where(line => ExtractTimestampFromLogLine(line) >= cutoffDate)
                    .ToList();

                if (recentLines.Count < lines.Length)
                {
                    // 圧縮版を書き戻す
                    await File.WriteAllLinesAsync(logFilePath, recentLines);

                    await _logger.LogInformation($"ログファイルを圧縮しました", new Dictionary<string, object>
                    {
                        ["file"] = Path.GetFileName(logFilePath),
                        ["originalLines"] = lines.Length,
                        ["compressedLines"] = recentLines.Count
                    });
                }
            }
            catch (Exception ex)
            {
                await _logger.LogError($"ログファイル圧縮中にエラーが発生しました: {Path.GetFileName(logFilePath)}", ex);
            }
        }

        /// <summary>
        /// 古いログファイルをクリーンアップ
        /// </summary>
        private void CleanupOldLogs(object? state)
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-90); // 90日より古いファイルを削除

                var oldFiles = GetAllLogFiles()
                    .Where(f => f.LastWriteTime < cutoffDate)
                    .ToList();

                foreach (var oldFile in oldFiles)
                {
                    try
                    {
                        oldFile.Delete();
                        _logger.LogInformation($"古いログファイルを削除しました: {oldFile.Name}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"ログファイル削除に失敗しました: {oldFile.Name}", ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("ログクリーンアップ中にエラーが発生しました", ex);
            }
        }
    }

    /// <summary>
    /// WiFi接続分析結果
    /// </summary>
    public class WifiConnectionAnalysis
    {
        public DateTime AnalysisTimestamp { get; set; }
        public int TotalAnalysisPeriod { get; set; }
        public int TotalConnectionAttempts { get; set; }
        public int SuccessfulConnections { get; set; }
        public int FailedConnections { get; set; }
        public int ErrorCount { get; set; }
        public int SecurityEvents { get; set; }
        public double SuccessRate => TotalConnectionAttempts > 0 ? (double)SuccessfulConnections / TotalConnectionAttempts * 100 : 0;
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// 接続ログエントリ
    /// </summary>
    public class ConnectionLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string SSID { get; set; } = "";
        public bool IsSuccessful { get; set; }
        public bool IsError { get; set; }
        public string LogMessage { get; set; } = "";
    }
}
