using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MurtiWifiConnecter
{
    public class ConnectionStatistics : IDisposable
    {
        private const string StatsFileName = "connection_stats.json";
        private readonly string _statsFilePath;
        private ConnectionStatsData _stats;
        private readonly object _lockObject = new object();
        private bool _isDirty = false;
        private DateTime _lastSaved = DateTime.MinValue;
        private readonly System.Threading.Timer _saveTimer;
        private bool _disposed = false;
        
        public ConnectionStatistics()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appDataPath, "MurtiWifiConnecter");
            Directory.CreateDirectory(appFolder);
            _statsFilePath = Path.Combine(appFolder, StatsFileName);
            _stats = LoadStats();
            _saveTimer = new System.Threading.Timer(SaveTimerCallback, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        public void RecordConnectionAttempt(string ssid, bool success, TimeSpan? connectionTime = null)
        {
            if (string.IsNullOrWhiteSpace(ssid)) return;

            var networkStats = GetOrCreateNetworkStats(ssid);
            networkStats.ConnectionAttempts++;
            networkStats.LastAttempt = DateTime.Now;

            if (success)
            {
                networkStats.SuccessfulConnections++;
                networkStats.LastSuccessfulConnection = DateTime.Now;
                
                if (connectionTime.HasValue)
                {
                    networkStats.TotalConnectionTime += connectionTime.Value;
                    networkStats.AverageConnectionTime = new TimeSpan(networkStats.TotalConnectionTime.Ticks / networkStats.SuccessfulConnections);
                }
            }
            else
            {
                networkStats.FailedConnections++;
            }

            // グローバル統計更新
            _stats.TotalConnectionAttempts++;
            if (success) _stats.TotalSuccessfulConnections++;

            lock (_lockObject)
            {
                _isDirty = true;
            }
        }

        public void RecordDisconnection(string ssid, TimeSpan? sessionDuration = null)
        {
            if (string.IsNullOrWhiteSpace(ssid)) return;

            var networkStats = GetOrCreateNetworkStats(ssid);
            networkStats.DisconnectionCount++;
            
            if (sessionDuration.HasValue)
            {
                networkStats.TotalSessionTime += sessionDuration.Value;
                if (networkStats.DisconnectionCount > 0)
                {
                    networkStats.AverageSessionDuration = new TimeSpan(networkStats.TotalSessionTime.Ticks / networkStats.DisconnectionCount);
                }
            }

            lock (_lockObject)
            {
                _isDirty = true;
            }
        }

        public void RecordSignalStrength(string ssid, int signalStrength)
        {
            if (string.IsNullOrWhiteSpace(ssid)) return;

            lock (_lockObject)
            {
                var networkStats = GetOrCreateNetworkStats(ssid);
                networkStats.SignalStrengthReadings.Add(new SignalReading 
                { 
                    Timestamp = DateTime.Now, 
                    Strength = signalStrength 
                });

                // 最適化: リストサイズ制限をより効率的に
                if (networkStats.SignalStrengthReadings.Count > 50) // 100から50に削減
                {
                    networkStats.SignalStrengthReadings.RemoveRange(0, networkStats.SignalStrengthReadings.Count - 50);
                }

                // 平均信号強度を軽量計算
                var sum = 0;
                var count = networkStats.SignalStrengthReadings.Count;
                for (int i = 0; i < count; i++)
                {
                    sum += networkStats.SignalStrengthReadings[i].Strength;
                }
                networkStats.AverageSignalStrength = count > 0 ? sum / count : 0;
                
                _isDirty = true;
            }
        }
        
        /// <summary>
        /// バッチで信号強度を記録（パフォーマンス最適化）
        /// </summary>
        public void RecordSignalStrengthBatch(Dictionary<string, int> signalStrengthBatch)
        {
            if (signalStrengthBatch == null || signalStrengthBatch.Count == 0) return;

            lock (_lockObject)
            {
                var currentTime = DateTime.Now;
                
                foreach (var kvp in signalStrengthBatch)
                {
                    var ssid = kvp.Key;
                    var signalStrength = kvp.Value;
                    
                    if (string.IsNullOrWhiteSpace(ssid)) continue;

                    var networkStats = GetOrCreateNetworkStats(ssid);
                    networkStats.SignalStrengthReadings.Add(new SignalReading 
                    { 
                        Timestamp = currentTime, 
                        Strength = signalStrength 
                    });

                    // リストサイズ制限
                    if (networkStats.SignalStrengthReadings.Count > 50)
                    {
                        networkStats.SignalStrengthReadings.RemoveRange(0, networkStats.SignalStrengthReadings.Count - 50);
                    }

                    // 平均信号強度を計算
                    var sum = 0;
                    var count = networkStats.SignalStrengthReadings.Count;
                    for (int i = 0; i < count; i++)
                    {
                        sum += networkStats.SignalStrengthReadings[i].Strength;
                    }
                    networkStats.AverageSignalStrength = count > 0 ? sum / count : 0;
                }
                
                _isDirty = true;
            }
        }

        public NetworkStats? GetNetworkStats(string ssid)
        {
            return _stats.NetworkStatistics.GetValueOrDefault(ssid);
        }

        public List<NetworkStats> GetTopNetworksByUsage(int count = 10)
        {
            lock (_lockObject)
            {
                // メモリ効率化: 配列使用でLINQ回避
                var networks = new List<NetworkStats>(_stats.NetworkStatistics.Values);
                networks.Sort((a, b) => 
                {
                    var result = b.SuccessfulConnections.CompareTo(a.SuccessfulConnections);
                    return result != 0 ? result : b.TotalSessionTime.CompareTo(a.TotalSessionTime);
                });
                
                var result = new List<NetworkStats>(Math.Min(count, networks.Count));
                for (int i = 0; i < Math.Min(count, networks.Count); i++)
                {
                    result.Add(networks[i]);
                }
                return result;
            }
        }

        public List<NetworkStats> GetRecentNetworks(int count = 5)
        {
            return _stats.NetworkStatistics.Values
                .Where(s => s.LastSuccessfulConnection.HasValue)
                .OrderByDescending(s => s.LastSuccessfulConnection)
                .Take(count)
                .ToList();
        }

        public ConnectionSummary GetConnectionSummary()
        {
            var networks = _stats.NetworkStatistics.Values.ToList();
            
            return new ConnectionSummary
            {
                TotalNetworks = networks.Count,
                TotalConnectionAttempts = _stats.TotalConnectionAttempts,
                TotalSuccessfulConnections = _stats.TotalSuccessfulConnections,
                OverallSuccessRate = _stats.TotalConnectionAttempts > 0 ? 
                    (double)_stats.TotalSuccessfulConnections / _stats.TotalConnectionAttempts * 100 : 0,
                MostUsedNetwork = networks.OrderByDescending(n => n.SuccessfulConnections).FirstOrDefault()?.SSID,
                AverageSignalStrength = networks.Where(n => n.AverageSignalStrength > 0).DefaultIfEmpty()
                    .Average(n => n?.AverageSignalStrength ?? 0),
                TotalSessionTime = networks.Sum(n => n.TotalSessionTime.TotalHours)
            };
        }

        public void CleanupOldData(TimeSpan maxAge)
        {
            var cutoffDate = DateTime.Now - maxAge;
            var networksToRemove = _stats.NetworkStatistics
                .Where(kvp => kvp.Value.LastAttempt < cutoffDate)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var ssid in networksToRemove)
            {
                _stats.NetworkStatistics.Remove(ssid);
            }

            // 各ネットワークの古い信号強度記録を削除
            foreach (var networkStats in _stats.NetworkStatistics.Values)
            {
                networkStats.SignalStrengthReadings = networkStats.SignalStrengthReadings
                    .Where(r => r.Timestamp > cutoffDate)
                    .ToList();
            }

            lock (_lockObject)
            {
                _isDirty = true;
            }
        }

        public WifiQualityRecommendation AnalyzeNetworkQuality(string ssid, int signalStrength, bool hasConnectedBefore, int connectionCount = 0)
        {
            var networkStats = GetNetworkStats(ssid);
            var actualConnectionCount = networkStats?.SuccessfulConnections ?? connectionCount;
            
            var recommendation = new WifiQualityRecommendation
            {
                SSID = ssid,
                SignalStrength = signalStrength,
                HasConnectedBefore = hasConnectedBefore,
                RecommendationLevel = GetRecommendationLevel(signalStrength, hasConnectedBefore, actualConnectionCount)
            };
            
            recommendation.RecommendationText = GetRecommendationText(recommendation.RecommendationLevel);
            recommendation.Priority = CalculatePriority(signalStrength, hasConnectedBefore, actualConnectionCount);
            
            return recommendation;
        }

        public List<NetworkStats> GetTopRecommendedNetworks(int count = 5)
        {
            return _stats.NetworkStatistics.Values
                .Where(stats => stats.SuccessRate > 70 && stats.ConnectionAttempts >= 2)
                .OrderByDescending(stats => stats.SuccessRate)
                .ThenBy(stats => stats.AverageConnectionTime.TotalSeconds)
                .Take(count)
                .ToList();
        }

        public WifiAnalysisReport GenerateReport()
        {
            var report = new WifiAnalysisReport
            {
                GeneratedAt = DateTime.Now,
                TotalNetworks = _stats.NetworkStatistics.Count,
                TotalAttempts = _stats.TotalConnectionAttempts,
                TotalSuccesses = _stats.TotalSuccessfulConnections
            };
            
            if (report.TotalAttempts > 0)
            {
                report.OverallSuccessRate = (double)report.TotalSuccesses / report.TotalAttempts * 100;
            }
            
            report.BestPerformingNetworks = GetTopRecommendedNetworks(3);
            
            return report;
        }

        public void OptimizeData()
        {
            var cutoffDate = DateTime.Now.AddDays(-90);
            var keysToRemove = _stats.NetworkStatistics
                .Where(kvp => kvp.Value.LastAttempt < cutoffDate && kvp.Value.ConnectionAttempts < 3)
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var key in keysToRemove)
            {
                _stats.NetworkStatistics.Remove(key);
            }
            
            lock (_lockObject)
            {
                _isDirty = true;
            }
        }
        
        // 軽量レポート生成機能
        public bool ExportDetailedReportToText(string filePath)
        {
            try
            {
                var report = GenerateReport();
                var summary = GetConnectionSummary();
                var output = new System.Text.StringBuilder();
                
                output.AppendLine("WiFi接続統計詳細レポート");
                output.AppendLine($"生成日時: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
                output.AppendLine(new string('=', 50));
                output.AppendLine();
                
                output.AppendLine("## 全体統計");
                output.AppendLine($"総ネットワーク数: {summary.TotalNetworks}");
                output.AppendLine($"総接続試行回数: {summary.TotalConnectionAttempts}");
                output.AppendLine($"成功接続回数: {summary.TotalSuccessfulConnections}");
                output.AppendLine($"全体成功率: {summary.OverallSuccessRate:F1}%");
                output.AppendLine($"最多使用ネットワーク: {summary.MostUsedNetwork ?? "なし"}");
                output.AppendLine($"平均信号強度: {summary.AverageSignalStrength:F1}");
                output.AppendLine($"総セッション時間: {summary.TotalSessionTime:F1}時間");
                output.AppendLine();
                
                output.AppendLine("## トップパフォーマンスネットワーク");
                foreach (var network in report.BestPerformingNetworks)
                {
                    output.AppendLine($"SSID: {network.SSID}");
                    output.AppendLine($"  成功率: {network.SuccessRate:F1}%");
                    output.AppendLine($"  接続回数: {network.SuccessfulConnections}/{network.ConnectionAttempts}");
                    output.AppendLine($"  平均信号強度: {network.AverageSignalStrength}");
                    output.AppendLine($"  最終接続: {network.LastSuccessfulConnection?.ToString("yyyy-MM-dd HH:mm") ?? "なし"}");
                    output.AppendLine($"  信頼性: {(network.IsReliable ? "高" : "低")}");
                    output.AppendLine();
                }
                
                output.AppendLine("## 全ネットワーク詳細");
                var allNetworks = _stats.NetworkStatistics.Values
                    .OrderByDescending(n => n.SuccessfulConnections)
                    .ToList();
                    
                foreach (var network in allNetworks)
                {
                    output.AppendLine($"{network.SSID}: 成功率{network.SuccessRate:F1}% " +
                                     $"({network.SuccessfulConnections}/{network.ConnectionAttempts}回)");
                }
                
                File.WriteAllText(filePath, output.ToString());
                return true;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("ConnectionStatistics.ExportDetailedReport", ex);
                return false;
            }
        }
        
        public string GenerateQuickStatsText()
        {
            try
            {
                var summary = GetConnectionSummary();
                
                if (summary.TotalNetworks == 0)
                    return "統計データなし";
                
                return $"ネットワーク数: {summary.TotalNetworks}, " +
                       $"成功率: {summary.OverallSuccessRate:F1}%, " +
                       $"最多使用: {summary.MostUsedNetwork ?? "なし"}, " +
                       $"平均信号: {summary.AverageSignalStrength:F0}";
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("ConnectionStatistics.GenerateQuickStats", ex);
                return "統計取得エラー";
            }
        }
        
        public Dictionary<string, object> GetKeyMetrics()
        {
            try
            {
                var summary = GetConnectionSummary();
                var bestNetworks = GetTopRecommendedNetworks(3);
                
                return new Dictionary<string, object>
                {
                    ["total_networks"] = summary.TotalNetworks,
                    ["success_rate"] = summary.OverallSuccessRate,
                    ["most_used"] = summary.MostUsedNetwork ?? "なし",
                    ["avg_signal"] = summary.AverageSignalStrength,
                    ["total_hours"] = summary.TotalSessionTime,
                    ["best_networks"] = bestNetworks.Select(n => new { 
                        ssid = n.SSID, 
                        success_rate = n.SuccessRate,
                        connections = n.SuccessfulConnections 
                    }).ToArray()
                };
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("ConnectionStatistics.GetKeyMetrics", ex);
                return new Dictionary<string, object> { ["error"] = "メトリクス取得失敗" };
            }
        }

        private RecommendationLevel GetRecommendationLevel(int signalStrength, bool hasConnectedBefore, int connectionCount)
        {
            if (signalStrength < 20) return RecommendationLevel.Poor;
            if (signalStrength < 40) return RecommendationLevel.Fair;
            if (signalStrength < 60) return RecommendationLevel.Good;
            
            if (hasConnectedBefore && connectionCount > 5)
                return RecommendationLevel.Excellent;
            else if (signalStrength >= 80)
                return RecommendationLevel.VeryGood;
            else
                return RecommendationLevel.Good;
        }
        
        private string GetRecommendationText(RecommendationLevel level)
        {
            return level switch
            {
                RecommendationLevel.Poor => "接続困難",
                RecommendationLevel.Fair => "接続不安定",
                RecommendationLevel.Good => "接続良好",
                RecommendationLevel.VeryGood => "接続快適",
                RecommendationLevel.Excellent => "推奨接続先",
                _ => "評価不明"
            };
        }
        
        private int CalculatePriority(int signalStrength, bool hasConnectedBefore, int connectionCount)
        {
            int priority = signalStrength;
            
            if (hasConnectedBefore) priority += 20;
            if (connectionCount > 5) priority += 10;
            if (connectionCount > 10) priority += 5;
            
            return Math.Min(priority, 100);
        }

        private NetworkStats GetOrCreateNetworkStats(string ssid)
        {
            if (!_stats.NetworkStatistics.TryGetValue(ssid, out var networkStats))
            {
                networkStats = new NetworkStats { SSID = ssid };
                _stats.NetworkStatistics[ssid] = networkStats;
            }
            return networkStats;
        }

        private ConnectionStatsData LoadStats()
        {
            try
            {
                if (File.Exists(_statsFilePath))
                {
                    var json = File.ReadAllText(_statsFilePath);
                    return JsonSerializer.Deserialize<ConnectionStatsData>(json) ?? new ConnectionStatsData();
                }
            }
            catch { }
            
            return new ConnectionStatsData();
        }

        private void SaveTimerCallback(object state)
        {
            if (_disposed) return;
            
            bool shouldSave;
            lock (_lockObject)
            {
                shouldSave = _isDirty && (DateTime.Now - _lastSaved).TotalSeconds > 30;
            }
            
            if (shouldSave)
            {
                SaveStatsInternal();
            }
        }
        
        private void SaveStatsInternal()
        {
            try
            {
                ConnectionStatsData statsToSave;
                lock (_lockObject)
                {
                    if (!_isDirty) return;
                    statsToSave = _stats; // 参照コピー（軽量）
                    _lastSaved = DateTime.Now;
                    _isDirty = false;
                }
                
                var json = JsonSerializer.Serialize(statsToSave, new JsonSerializerOptions 
                { 
                    WriteIndented = false // メモリ節約
                });
                File.WriteAllText(_statsFilePath, json);
            }
            catch { }
        }
        
        public void ForceSave()
        {
            SaveStatsInternal();
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _saveTimer?.Dispose();
            ForceSave();
            
            GC.SuppressFinalize(this);
        }
    }

    public class ConnectionStatsData
    {
        public Dictionary<string, NetworkStats> NetworkStatistics { get; set; } = new();
        public int TotalConnectionAttempts { get; set; }
        public int TotalSuccessfulConnections { get; set; }
    }

    public class NetworkStats
    {
        public string SSID { get; set; } = string.Empty;
        public int ConnectionAttempts { get; set; }
        public int SuccessfulConnections { get; set; }
        public int FailedConnections { get; set; }
        public int DisconnectionCount { get; set; }
        public DateTime LastAttempt { get; set; }
        public DateTime? LastSuccessfulConnection { get; set; }
        public TimeSpan TotalConnectionTime { get; set; }
        public TimeSpan AverageConnectionTime { get; set; }
        public TimeSpan TotalSessionTime { get; set; }
        public TimeSpan AverageSessionDuration { get; set; }
        public int AverageSignalStrength { get; set; }
        public List<SignalReading> SignalStrengthReadings { get; set; } = new();

        public double SuccessRate => ConnectionAttempts > 0 ? (double)SuccessfulConnections / ConnectionAttempts * 100 : 0;
        public bool IsReliable => SuccessRate >= 80 && ConnectionAttempts >= 3;
    }

    public class SignalReading
    {
        public DateTime Timestamp { get; set; }
        public int Strength { get; set; }
    }

    public class ConnectionSummary
    {
        public int TotalNetworks { get; set; }
        public int TotalConnectionAttempts { get; set; }
        public int TotalSuccessfulConnections { get; set; }
        public double OverallSuccessRate { get; set; }
        public string? MostUsedNetwork { get; set; }
        public double AverageSignalStrength { get; set; }
        public double TotalSessionTime { get; set; }
    }

    public class WifiQualityRecommendation
    {
        public string SSID { get; set; } = string.Empty;
        public int SignalStrength { get; set; }
        public bool HasConnectedBefore { get; set; }
        public RecommendationLevel RecommendationLevel { get; set; }
        public string RecommendationText { get; set; } = string.Empty;
        public int Priority { get; set; }
    }

    public class WifiAnalysisReport
    {
        public DateTime GeneratedAt { get; set; }
        public int TotalNetworks { get; set; }
        public int TotalAttempts { get; set; }
        public int TotalSuccesses { get; set; }
        public double OverallSuccessRate { get; set; }
        public List<NetworkStats> BestPerformingNetworks { get; set; } = new();
    }

    public enum RecommendationLevel
    {
        Poor,
        Fair,
        Good,
        VeryGood,
        Excellent
    }
}