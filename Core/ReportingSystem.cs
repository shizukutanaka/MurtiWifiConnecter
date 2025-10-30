using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// 包括的なレポートシステムを提供するクラス
    /// 接続履歴、セキュリティレポート、パフォーマンスレポートを生成
    /// </summary>
    public class ReportingSystem : IDisposable
    {
        private readonly Dictionary<string, List<ReportData>> _reportCache = new();
        private readonly object _lockObject = new();

        public event EventHandler<ReportGeneratedEventArgs>? ReportGenerated;

        // 設定
        private const int MaxCacheSize = 10000;
        private const string ReportsDirectory = "Reports";

        public ReportingSystem()
        {
            EnsureReportsDirectory();
        }

        /// <summary>
        /// 接続履歴レポートを生成する
        /// </summary>
        public async Task<ConnectionHistoryReport> GenerateConnectionHistoryReportAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            var report = new ConnectionHistoryReport
            {
                GeneratedAt = DateTime.Now,
                StartDate = startDate ?? DateTime.Now.AddDays(-7),
                EndDate = endDate ?? DateTime.Now
            };

            // 接続履歴データを取得（実際の実装ではデータベースから）
            var historyData = await GetConnectionHistoryDataAsync(report.StartDate, report.EndDate);

            report.TotalConnections = historyData.Count;
            report.SuccessfulConnections = historyData.Count(h => h.Success);
            report.FailedConnections = historyData.Count(h => !h.Success);
            report.SuccessRate = report.TotalConnections > 0 ?
                (double)report.SuccessfulConnections / report.TotalConnections * 100 : 0;

            // SSID別統計
            var ssidStats = historyData
                .GroupBy(h => h.Ssid)
                .Select(g => new SsidConnectionStats
                {
                    Ssid = g.Key,
                    ConnectionCount = g.Count(),
                    SuccessCount = g.Count(h => h.Success),
                    AverageSignalStrength = g.Where(h => h.SignalStrength > 0).Average(h => h.SignalStrength),
                    LastConnected = g.Max(h => h.Timestamp)
                })
                .OrderByDescending(s => s.ConnectionCount)
                .ToList();

            report.SsidStatistics = ssidStats;

            // 日別統計
            var dailyStats = historyData
                .GroupBy(h => h.Timestamp.Date)
                .Select(g => new DailyConnectionStats
                {
                    Date = g.Key,
                    ConnectionCount = g.Count(),
                    SuccessCount = g.Count(h => h.Success),
                    AverageSignalStrength = g.Where(h => h.SignalStrength > 0).Average(h => h.SignalStrength)
                })
                .OrderBy(s => s.Date)
                .ToList();

            report.DailyStatistics = dailyStats;

            // レポートをキャッシュに保存
            await SaveReportAsync("ConnectionHistory", report);

            // イベント発行
            ReportGenerated?.Invoke(this, new ReportGeneratedEventArgs
            {
                ReportType = "ConnectionHistory",
                Report = report
            });

            return report;
        }

        /// <summary>
        /// セキュリティレポートを生成する
        /// </summary>
        public async Task<SecurityReport> GenerateSecurityReportAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            var report = new SecurityReport
            {
                GeneratedAt = DateTime.Now,
                StartDate = startDate ?? DateTime.Now.AddDays(-7),
                EndDate = endDate ?? DateTime.Now
            };

            // セキュリティイベントデータを取得
            var securityData = await GetSecurityEventDataAsync(report.StartDate, report.EndDate);

            report.TotalEvents = securityData.Count;

            // イベントタイプ別統計
            var eventTypeStats = securityData
                .GroupBy(e => e.EventType)
                .Select(g => new EventTypeStats
                {
                    EventType = g.Key,
                    Count = g.Count(),
                    Severity = g.Max(e => e.Severity)
                })
                .ToList();

            report.EventTypeStatistics = eventTypeStats;

            // リスクレベル別統計
            var riskStats = securityData
                .GroupBy(e => e.RiskLevel)
                .Select(g => new RiskLevelStats
                {
                    RiskLevel = g.Key,
                    Count = g.Count(),
                    Percentage = (double)g.Count() / securityData.Count * 100
                })
                .ToList();

            report.RiskLevelStatistics = riskStats;

            // トップリスク要因
            report.TopRiskFactors = securityData
                .Where(e => !string.IsNullOrEmpty(e.RiskFactor))
                .GroupBy(e => e.RiskFactor)
                .Select(g => new RiskFactorStats
                {
                    Factor = g.Key,
                    Count = g.Count()
                })
                .OrderByDescending(r => r.Count)
                .Take(10)
                .ToList();

            // レポートをキャッシュに保存
            await SaveReportAsync("Security", report);

            return report;
        }

        /// <summary>
        /// パフォーマンスレポートを生成する
        /// </summary>
        public async Task<PerformanceReport> GeneratePerformanceReportAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            var report = new PerformanceReport
            {
                GeneratedAt = DateTime.Now,
                StartDate = startDate ?? DateTime.Now.AddDays(-7),
                EndDate = endDate ?? DateTime.Now
            };

            // パフォーマンスデータを取得
            var performanceData = await GetPerformanceDataAsync(report.StartDate, report.EndDate);

            report.TotalMeasurements = performanceData.Count;

            // 平均パフォーマンス指標
            if (performanceData.Any())
            {
                report.AverageDownloadSpeed = performanceData.Average(p => p.DownloadSpeed);
                report.AverageUploadSpeed = performanceData.Average(p => p.UploadSpeed);
                report.AverageLatency = performanceData.Average(p => p.Latency);
                report.AverageSignalStrength = performanceData.Average(p => p.SignalStrength);

                report.MaxDownloadSpeed = performanceData.Max(p => p.DownloadSpeed);
                report.MaxUploadSpeed = performanceData.Max(p => p.UploadSpeed);
                report.MinLatency = performanceData.Min(p => p.Latency);
            }

            // 時間帯別パフォーマンス
            var hourlyStats = performanceData
                .GroupBy(p => p.Timestamp.Hour)
                .Select(g => new HourlyPerformanceStats
                {
                    Hour = g.Key,
                    AverageDownloadSpeed = g.Average(p => p.DownloadSpeed),
                    AverageUploadSpeed = g.Average(p => p.UploadSpeed),
                    AverageLatency = g.Average(p => p.Latency),
                    MeasurementCount = g.Count()
                })
                .OrderBy(s => s.Hour)
                .ToList();

            report.HourlyStatistics = hourlyStats;

            // レポートをキャッシュに保存
            await SaveReportAsync("Performance", report);

            return report;
        }

        /// <summary>
        /// レポートをファイルにエクスポートする
        /// </summary>
        public async Task<string> ExportReportAsync(string reportType, string format = "json")
        {
            var report = GetCachedReport(reportType);
            if (report == null)
                throw new InvalidOperationException($"レポート '{reportType}' が見つかりません。");

            var fileName = $"{reportType}_{DateTime.Now:yyyyMMdd_HHmmss}.{format}";
            var filePath = Path.Combine(ReportsDirectory, fileName);

            switch (format.ToLower())
            {
                case "json":
                    await ExportAsJsonAsync(report, filePath);
                    break;
                case "csv":
                    await ExportAsCsvAsync(report, filePath);
                    break;
                default:
                    throw new NotSupportedException($"フォーマット '{format}' はサポートされていません。");
            }

            return filePath;
        }

        private async Task<List<ConnectionHistoryEntry>> GetConnectionHistoryDataAsync(DateTime startDate, DateTime endDate)
        {
            // 実際の実装ではデータベースから取得
            return new List<ConnectionHistoryEntry>
            {
                new ConnectionHistoryEntry
                {
                    Timestamp = DateTime.Now.AddHours(-2),
                    Ssid = "OfficeWiFi",
                    Success = true,
                    SignalStrength = 85,
                    ConnectionTime = TimeSpan.FromSeconds(5)
                },
                new ConnectionHistoryEntry
                {
                    Timestamp = DateTime.Now.AddHours(-1),
                    Ssid = "HomeNetwork",
                    Success = false,
                    SignalStrength = 45,
                    ErrorMessage = "認証失敗"
                }
            };
        }

        private async Task<List<SecurityEventEntry>> GetSecurityEventDataAsync(DateTime startDate, DateTime endDate)
        {
            // 実際の実装ではデータベースから取得
            return new List<SecurityEventEntry>
            {
                new SecurityEventEntry
                {
                    Timestamp = DateTime.Now.AddHours(-3),
                    EventType = "異常検知",
                    Severity = "高",
                    RiskLevel = "中",
                    RiskFactor = "異常な接続頻度",
                    Description = "短時間に多数の接続試行を検知"
                }
            };
        }

        private async Task<List<PerformanceEntry>> GetPerformanceDataAsync(DateTime startDate, DateTime endDate)
        {
            // 実際の実装ではデータベースから取得
            return new List<PerformanceEntry>
            {
                new PerformanceEntry
                {
                    Timestamp = DateTime.Now.AddHours(-1),
                    DownloadSpeed = 45.5,
                    UploadSpeed = 12.3,
                    Latency = 23,
                    SignalStrength = 78
                }
            };
        }

        private object? GetCachedReport(string reportType)
        {
            lock (_lockObject)
            {
                return _reportCache.ContainsKey(reportType) ?
                    _reportCache[reportType].LastOrDefault() : null;
            }
        }

        private async Task SaveReportAsync(string reportType, object report)
        {
            lock (_lockObject)
            {
                if (!_reportCache.ContainsKey(reportType))
                    _reportCache[reportType] = new List<ReportData>();

                var reportData = new ReportData
                {
                    GeneratedAt = DateTime.Now,
                    ReportType = reportType,
                    Data = report
                };

                _reportCache[reportType].Add(reportData);

                // キャッシュサイズ制限
                if (_reportCache[reportType].Count > MaxCacheSize)
                {
                    _reportCache[reportType].RemoveRange(0,
                        _reportCache[reportType].Count - MaxCacheSize);
                }
            }
        }

        private void EnsureReportsDirectory()
        {
            if (!Directory.Exists(ReportsDirectory))
            {
                Directory.CreateDirectory(ReportsDirectory);
            }
        }

        private async Task ExportAsJsonAsync(object report, string filePath)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(report,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);
        }

        private async Task ExportAsCsvAsync(object report, string filePath)
        {
            // CSVエクスポートの実装（簡易版）
            var csv = "レポートのCSVエクスポート機能は開発中です。";
            await File.WriteAllTextAsync(filePath, csv);
        }

        public void Dispose()
        {
            // クリーンアップ処理
        }
    }

    // データ構造定義
    public class ReportData
    {
        public DateTime GeneratedAt { get; set; }
        public string ReportType { get; set; } = "";
        public object Data { get; set; } = new();
    }

    public class ConnectionHistoryReport
    {
        public DateTime GeneratedAt { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int TotalConnections { get; set; }
        public int SuccessfulConnections { get; set; }
        public int FailedConnections { get; set; }
        public double SuccessRate { get; set; }
        public List<SsidConnectionStats> SsidStatistics { get; set; } = new();
        public List<DailyConnectionStats> DailyStatistics { get; set; } = new();
    }

    public class SecurityReport
    {
        public DateTime GeneratedAt { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int TotalEvents { get; set; }
        public List<EventTypeStats> EventTypeStatistics { get; set; } = new();
        public List<RiskLevelStats> RiskLevelStatistics { get; set; } = new();
        public List<RiskFactorStats> TopRiskFactors { get; set; } = new();
    }

    public class PerformanceReport
    {
        public DateTime GeneratedAt { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int TotalMeasurements { get; set; }
        public double AverageDownloadSpeed { get; set; }
        public double AverageUploadSpeed { get; set; }
        public double AverageLatency { get; set; }
        public double AverageSignalStrength { get; set; }
        public double MaxDownloadSpeed { get; set; }
        public double MaxUploadSpeed { get; set; }
        public double MinLatency { get; set; }
        public List<HourlyPerformanceStats> HourlyStatistics { get; set; } = new();
    }

    public class SsidConnectionStats
    {
        public string Ssid { get; set; } = "";
        public int ConnectionCount { get; set; }
        public int SuccessCount { get; set; }
        public double AverageSignalStrength { get; set; }
        public DateTime LastConnected { get; set; }
    }

    public class DailyConnectionStats
    {
        public DateTime Date { get; set; }
        public int ConnectionCount { get; set; }
        public int SuccessCount { get; set; }
        public double AverageSignalStrength { get; set; }
    }

    public class EventTypeStats
    {
        public string EventType { get; set; } = "";
        public int Count { get; set; }
        public string Severity { get; set; } = "";
    }

    public class RiskLevelStats
    {
        public string RiskLevel { get; set; } = "";
        public int Count { get; set; }
        public double Percentage { get; set; }
    }

    public class RiskFactorStats
    {
        public string Factor { get; set; } = "";
        public int Count { get; set; }
    }

    public class HourlyPerformanceStats
    {
        public int Hour { get; set; }
        public double AverageDownloadSpeed { get; set; }
        public double AverageUploadSpeed { get; set; }
        public double AverageLatency { get; set; }
        public int MeasurementCount { get; set; }
    }

    public class ConnectionHistoryEntry
    {
        public DateTime Timestamp { get; set; }
        public string Ssid { get; set; } = "";
        public bool Success { get; set; }
        public int SignalStrength { get; set; }
        public TimeSpan ConnectionTime { get; set; }
        public string ErrorMessage { get; set; } = "";
    }

    public class SecurityEventEntry
    {
        public DateTime Timestamp { get; set; }
        public string EventType { get; set; } = "";
        public string Severity { get; set; } = "";
        public string RiskLevel { get; set; } = "";
        public string RiskFactor { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public class PerformanceEntry
    {
        public DateTime Timestamp { get; set; }
        public double DownloadSpeed { get; set; }
        public double UploadSpeed { get; set; }
        public double Latency { get; set; }
        public double SignalStrength { get; set; }
    }

    public class ReportGeneratedEventArgs : EventArgs
    {
        public string ReportType { get; set; } = "";
        public object Report { get; set; } = new();
    }
}
