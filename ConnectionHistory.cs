using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;

namespace MurtiWifiConnecter
{
    public class ConnectionHistory
    {
        private const string HistoryFileName = "wifi_history.json";
        private readonly string _historyFilePath;
        private List<WifiHistoryEntry> _history;
        private readonly Dictionary<string, WifiHistoryEntry> _historyLookup;
        private readonly object _lockObject = new object();
        private bool _isDirty = false;
        private DateTime _lastSaved = DateTime.MinValue;
        private readonly System.Threading.Timer? _saveTimer;
        private bool _disposed = false;
        
        public ConnectionHistory()
        {
            // アプリケーションデータパスを取得
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MurtiWifiConnecter");
            
            Directory.CreateDirectory(appDataPath);
            _historyFilePath = Path.Combine(appDataPath, HistoryFileName);
            _history = LoadHistory();
            _historyLookup = _history.ToDictionary(h => h.SSID, h => h, StringComparer.OrdinalIgnoreCase);
            _saveTimer = new System.Threading.Timer(SaveTimerCallback, null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2));
        }

        public void AddSuccessfulConnection(string ssid)
        {
            if (string.IsNullOrWhiteSpace(ssid)) return;
            
            lock (_lockObject)
            {
                if (_historyLookup.TryGetValue(ssid, out var existing))
                {
                    existing.LastConnected = DateTime.Now;
                    existing.ConnectionCount++;
                }
                else
                {
                    var newEntry = new WifiHistoryEntry 
                    { 
                        SSID = ssid, 
                        LastConnected = DateTime.Now,
                        ConnectionCount = 1 
                    };
                    _history.Add(newEntry);
                    _historyLookup[ssid] = newEntry;
                }

                // 最大50件まで保持（効率化）
                if (_history.Count > 50)
                {
                    var sortedHistory = _history.OrderByDescending(h => h.LastConnected).ToList();
                    var toRemove = sortedHistory.Skip(50).ToList();
                    
                    foreach (var entry in toRemove)
                    {
                        _history.Remove(entry);
                        _historyLookup.Remove(entry.SSID);
                    }
                }
                
                _isDirty = true;
                SaveHistoryIfNeeded();
            }
        }

        public List<string> GetRecentNetworks(int count = 10)
        {
            return _history.OrderByDescending(h => h.LastConnected)
                          .Take(count)
                          .Select(h => h.SSID)
                          .ToList();
        }

        public bool HasConnectedBefore(string ssid)
        {
            if (string.IsNullOrWhiteSpace(ssid)) return false;
            
            lock (_lockObject)
            {
                return _historyLookup.ContainsKey(ssid);
            }
        }
        
        public bool HasConnectedTo(string ssid) => HasConnectedBefore(ssid);
        
        public List<string> GetAllConnectedNetworks()
        {
            lock (_lockObject)
            {
                return _historyLookup.Keys.ToList();
            }
        }
        
        private void SaveHistoryIfNeeded()
        {
            // 頻繁な保存を抑制（3秒以内は保存しない）
            if (!_isDirty || (DateTime.Now - _lastSaved).TotalSeconds < 3) return;
            
            SaveHistoryInternal();
        }
        
        private void SaveHistoryInternal()
        {
            try
            {
                var json = JsonSerializer.Serialize(_history, new JsonSerializerOptions 
                { 
                    WriteIndented = false // セキュリティ向上のため圧縮
                });
                
                // 履歴の暗号化保存
                var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
                var success = SecurityManager.EncryptFile(_historyFilePath, jsonBytes);
                
                if (!success)
                {
                    // フォールバック: 非暗号化での保存
                    var tempPath = _historyFilePath + ".tmp";
                    File.WriteAllText(tempPath, json);
                    File.Move(tempPath, _historyFilePath, true);
                }
                
                // JSONデータを安全にクリア
                Array.Clear(jsonBytes, 0, jsonBytes.Length);
                
                _lastSaved = DateTime.Now;
                _isDirty = false;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("ConnectionHistory.SaveHistoryInternal", ex);
            }
        }

        private List<WifiHistoryEntry> LoadHistory()
        {
            try
            {
                if (File.Exists(_historyFilePath))
                {
                    var json = File.ReadAllText(_historyFilePath);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var history = JsonSerializer.Deserialize<List<WifiHistoryEntry>>(json);
                        if (history != null)
                        {
                            // 重複を除去し、最新50件を保持
                            return history
                                .GroupBy(h => h.SSID, StringComparer.OrdinalIgnoreCase)
                                .Select(g => g.OrderByDescending(h => h.LastConnected).First())
                                .OrderByDescending(h => h.LastConnected)
                                .Take(50)
                                .ToList();
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                ErrorHandler.LogError("ConnectionHistory.LoadHistory.JsonError", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                ErrorHandler.LogError("ConnectionHistory.LoadHistory.UnauthorizedAccess", ex);
            }
            catch (FileNotFoundException)
            {
                // 初回起動時は正常
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("ConnectionHistory.LoadHistory.General", ex);
            }
            
            return new List<WifiHistoryEntry>(10); // 初期容量設定
        }

        public void ForceSave()
        {
            lock (_lockObject)
            {
                if (_isDirty)
                {
                    _lastSaved = DateTime.MinValue;
                    SaveHistoryInternal();
                }
            }
        }

        public void CleanupOldEntries(int maxAge = 90)
        {
            lock (_lockObject)
            {
                try
                {
                    var cutoffDate = DateTime.Now.AddDays(-maxAge);
                    var originalCount = _history.Count;
                    
                    var toRemove = _history.Where(h => h.LastConnected < cutoffDate).ToList();
                    
                    if (toRemove.Count > 0)
                    {
                        foreach (var entry in toRemove)
                        {
                            _history.Remove(entry);
                            _historyLookup.Remove(entry.SSID);
                        }
                        
                        _isDirty = true;
                        SaveHistoryIfNeeded();
                    }
                }
                catch (Exception ex)
                {
                    ErrorHandler.LogError("ConnectionHistory.CleanupOldEntries", ex);
                }
            }
        }

        public void RemoveNetwork(string ssid)
        {
            if (string.IsNullOrWhiteSpace(ssid)) return;
            
            lock (_lockObject)
            {
                try
                {
                    if (_historyLookup.TryGetValue(ssid, out var entry))
                    {
                        _history.Remove(entry);
                        _historyLookup.Remove(ssid);
                        _isDirty = true;
                        SaveHistoryIfNeeded();
                    }
                }
                catch (Exception ex)
                {
                    ErrorHandler.LogError("ConnectionHistory.RemoveNetwork", ex);
                }
            }
        }
        
        /// <summary>
        /// お気に入りネットワークを自動識別（頻度と最近の使用に基づく）
        /// </summary>
        public List<FavoriteNetwork> GetFavoriteNetworks(int maxCount = 5)
        {
            lock (_lockObject)
            {
                var now = DateTime.Now;
                return _history
                    .Where(h => h.ConnectionCount >= 2) // 最低2回接続
                    .Select(h => new FavoriteNetwork
                    {
                        SSID = h.SSID,
                        ConnectionCount = h.ConnectionCount,
                        LastConnected = h.LastConnected,
                        DaysSinceLastConnection = (int)(now - h.LastConnected).TotalDays,
                        FavoriteScore = CalculateFavoriteScore(h, now)
                    })
                    .OrderByDescending(f => f.FavoriteScore)
                    .Take(maxCount)
                    .ToList();
            }
        }
        
        /// <summary>
        /// 時間帯別接続パターンを分析
        /// </summary>
        public ConnectionTimePattern GetTimePattern(string? ssid = null)
        {
            lock (_lockObject)
            {
                var relevantHistory = string.IsNullOrEmpty(ssid) 
                    ? _history 
                    : _history.Where(h => h.SSID.Equals(ssid, StringComparison.OrdinalIgnoreCase));
                
                var pattern = new ConnectionTimePattern();
                var hourlyStats = new Dictionary<int, int>();
                var dailyStats = new Dictionary<DayOfWeek, int>();
                
                foreach (var entry in relevantHistory)
                {
                    var hour = entry.LastConnected.Hour;
                    var dayOfWeek = entry.LastConnected.DayOfWeek;
                    
                    hourlyStats[hour] = hourlyStats.GetValueOrDefault(hour, 0) + entry.ConnectionCount;
                    dailyStats[dayOfWeek] = dailyStats.GetValueOrDefault(dayOfWeek, 0) + entry.ConnectionCount;
                }
                
                pattern.MostActiveHour = hourlyStats.OrderByDescending(kvp => kvp.Value).FirstOrDefault().Key;
                pattern.MostActiveDay = dailyStats.OrderByDescending(kvp => kvp.Value).FirstOrDefault().Key;
                pattern.HourlyDistribution = hourlyStats;
                pattern.DailyDistribution = dailyStats;
                
                return pattern;
            }
        }
        
        /// <summary>
        /// 詳細使用統計を取得
        /// </summary>
        public ConnectionUsageStatistics GetUsageStatistics()
        {
            lock (_lockObject)
            {
                var now = DateTime.Now;
                var stats = new ConnectionUsageStatistics();
                
                stats.TotalNetworks = _history.Count;
                stats.TotalConnections = _history.Sum(h => h.ConnectionCount);
                stats.MostUsedNetwork = _history.OrderByDescending(h => h.ConnectionCount).FirstOrDefault()?.SSID ?? "";
                stats.MostRecentNetwork = _history.OrderByDescending(h => h.LastConnected).FirstOrDefault()?.SSID ?? "";
                
                // 期間別統計
                var lastWeek = now.AddDays(-7);
                var lastMonth = now.AddDays(-30);
                
                stats.NetworksUsedLastWeek = _history.Count(h => h.LastConnected >= lastWeek);
                stats.NetworksUsedLastMonth = _history.Count(h => h.LastConnected >= lastMonth);
                
                // 使用頻度分析
                stats.HighFrequencyNetworks = _history.Count(h => h.ConnectionCount >= 10);
                stats.MediumFrequencyNetworks = _history.Count(h => h.ConnectionCount >= 3 && h.ConnectionCount < 10);
                stats.LowFrequencyNetworks = _history.Count(h => h.ConnectionCount < 3);
                
                // 平均値計算
                if (stats.TotalNetworks > 0)
                {
                    stats.AverageConnectionsPerNetwork = (double)stats.TotalConnections / stats.TotalNetworks;
                    
                    var daysSinceFirstUse = _history.Min(h => h.LastConnected);
                    var totalDays = Math.Max(1, (now - daysSinceFirstUse).TotalDays);
                    stats.AverageNetworksPerDay = stats.TotalConnections / totalDays;
                }
                
                return stats;
            }
        }
        
        /// <summary>
        /// 接続推奨度を計算
        /// </summary>
        public List<NetworkRecommendation> GetNetworkRecommendations(List<string> availableSSIDs)
        {
            lock (_lockObject)
            {
                var recommendations = new List<NetworkRecommendation>();
                
                foreach (var ssid in availableSSIDs)
                {
                    var historyEntry = _historyLookup.GetValueOrDefault(ssid);
                    var recommendation = new NetworkRecommendation
                    {
                        SSID = ssid,
                        HasHistory = historyEntry != null,
                        RecommendationScore = CalculateRecommendationScore(historyEntry)
                    };
                    
                    if (historyEntry != null)
                    {
                        recommendation.LastConnected = historyEntry.LastConnected;
                        recommendation.ConnectionCount = historyEntry.ConnectionCount;
                        recommendation.RecommendationReason = GenerateRecommendationReason(historyEntry);
                    }
                    
                    recommendations.Add(recommendation);
                }
                
                return recommendations.OrderByDescending(r => r.RecommendationScore).ToList();
            }
        }
        
        private double CalculateFavoriteScore(WifiHistoryEntry entry, DateTime now)
        {
            var daysSinceLastConnection = (now - entry.LastConnected).TotalDays;
            var recencyScore = Math.Max(0, 30 - daysSinceLastConnection) / 30.0; // 30日以内が最高
            var frequencyScore = Math.Min(1.0, entry.ConnectionCount / 20.0); // 20回接続で満点
            
            return (recencyScore * 0.6) + (frequencyScore * 0.4);
        }
        
        private double CalculateRecommendationScore(WifiHistoryEntry? entry)
        {
            if (entry == null) return 0.0;
            
            var now = DateTime.Now;
            var daysSinceLastConnection = (now - entry.LastConnected).TotalDays;
            
            // 基本スコア（接続回数ベース）
            var baseScore = Math.Min(50.0, entry.ConnectionCount * 2.0);
            
            // 最近の使用によるボーナス
            var recencyBonus = daysSinceLastConnection switch
            {
                <= 1 => 30.0,
                <= 7 => 20.0,
                <= 30 => 10.0,
                _ => 0.0
            };
            
            // 時間帯ボーナス（現在時刻と過去の接続時刻の一致度）
            var currentHour = now.Hour;
            var timeBonus = Math.Abs(currentHour - entry.LastConnected.Hour) <= 2 ? 10.0 : 0.0;
            
            return baseScore + recencyBonus + timeBonus;
        }
        
        private string GenerateRecommendationReason(WifiHistoryEntry entry)
        {
            var daysSinceLastConnection = (DateTime.Now - entry.LastConnected).TotalDays;
            
            return (entry.ConnectionCount, daysSinceLastConnection) switch
            {
                (>= 10, <= 1) => "よく使用する最近のネットワーク",
                (>= 10, _) => "頻繁に使用するネットワーク",
                (_, <= 1) => "昨日使用したネットワーク",
                (_, <= 7) => "最近使用したネットワーク",
                _ => "過去に使用したネットワーク"
            };
        }

        public List<WifiHistoryEntry> GetAllEntries()
        {
            return _history.OrderByDescending(h => h.LastConnected).ToList();
        }

        public WifiHistoryEntry? GetEntry(string ssid)
        {
            return _history.FirstOrDefault(h => 
                string.Equals(h.SSID, ssid, StringComparison.OrdinalIgnoreCase));
        }

        public void OptimizeStorage()
        {
            lock (_lockObject)
            {
                try
                {
                    var originalCount = _history.Count;
                    
                    // 重複を削除（辞書の整合性も保つ）
                    var distinct = _history.GroupBy(h => h.SSID, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.OrderByDescending(h => h.LastConnected).First())
                        .ToList();

                    if (distinct.Count != originalCount)
                    {
                        _history = distinct.OrderByDescending(h => h.LastConnected).ToList();
                        _historyLookup.Clear();
                        
                        // 辞書を再構築
                        foreach (var entry in _history)
                        {
                            _historyLookup[entry.SSID] = entry;
                        }
                        
                        _isDirty = true;
                        SaveHistoryIfNeeded();
                    }
                }
                catch (Exception ex)
                {
                    ErrorHandler.LogError("ConnectionHistory.OptimizeStorage", ex);
                }
            }
        }
        
        // 軽量エクスポート機能
        public bool ExportToCSV(string filePath)
        {
            try
            {
                lock (_lockObject)
                {
                    var lines = new List<string>(_history.Count + 1)
                    {
                        "SSID,接続回数,最終接続日時,総使用期間"
                    };
                    
                    var sortedHistory = _history.OrderByDescending(h => h.LastConnected);
                    
                    foreach (var entry in sortedHistory)
                    {
                        var daysUsed = (DateTime.Now - entry.LastConnected).Days;
                        var usage = daysUsed == 0 ? "今日" : 
                                   daysUsed == 1 ? "1日前" : 
                                   daysUsed < 30 ? $"{daysUsed}日前" : 
                                   daysUsed < 365 ? $"{daysUsed / 30}ヶ月前" : 
                                   $"{daysUsed / 365}年前";
                        
                        lines.Add($"\"{entry.SSID}\",{entry.ConnectionCount},{entry.LastConnected:yyyy-MM-dd HH:mm:ss},{usage}");
                    }
                    
                    File.WriteAllLines(filePath, lines);
                    return true;
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("ConnectionHistory.ExportToCSV", ex);
                return false;
            }
        }
        
        public bool ExportToText(string filePath)
        {
            try
            {
                lock (_lockObject)
                {
                    var output = new System.Text.StringBuilder();
                    output.AppendLine("WiFi接続履歴レポート");
                    output.AppendLine($"生成日時: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    output.AppendLine($"総ネットワーク数: {_history.Count}");
                    output.AppendLine(new string('-', 50));
                    output.AppendLine();
                    
                    var sortedHistory = _history.OrderByDescending(h => h.LastConnected);
                    
                    foreach (var entry in sortedHistory)
                    {
                        output.AppendLine($"SSID: {entry.SSID}");
                        output.AppendLine($"  接続回数: {entry.ConnectionCount}回");
                        output.AppendLine($"  最終接続: {entry.LastConnected:yyyy-MM-dd HH:mm:ss}");
                        
                        var daysAgo = (DateTime.Now - entry.LastConnected).Days;
                        var lastUsed = daysAgo == 0 ? "今日" :
                                      daysAgo == 1 ? "1日前" :
                                      daysAgo < 30 ? $"{daysAgo}日前" :
                                      daysAgo < 365 ? $"{daysAgo / 30}ヶ月前" :
                                      $"{daysAgo / 365}年前";
                        
                        output.AppendLine($"  利用期間: {lastUsed}");
                        output.AppendLine();
                    }
                    
                    File.WriteAllText(filePath, output.ToString());
                    return true;
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("ConnectionHistory.ExportToText", ex);
                return false;
            }
        }
        
        public string GenerateQuickSummary()
        {
            lock (_lockObject)
            {
                if (_history.Count == 0)
                    return "接続履歴なし";
                
                var totalConnections = _history.Sum(h => h.ConnectionCount);
                var mostUsed = _history.OrderByDescending(h => h.ConnectionCount).First();
                var recent = _history.Count(h => (DateTime.Now - h.LastConnected).Days <= 7);
                
                return $"総ネットワーク数: {_history.Count}, " +
                       $"総接続回数: {totalConnections}, " +
                       $"最多使用: {mostUsed.SSID} ({mostUsed.ConnectionCount}回), " +
                       $"最近使用: {recent}個";
            }
        }
        
        private void SaveTimerCallback(object state)
        {
            if (_disposed || !_isDirty) return;
            
            lock (_lockObject)
            {
                if ((DateTime.Now - _lastSaved).TotalSeconds > 60) // 1分以上経過した場合のみ保存
                {
                    SaveHistoryInternal();
                }
            }
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _saveTimer?.Dispose();
            
            lock (_lockObject)
            {
                if (_isDirty)
                {
                    SaveHistoryInternal();
                }
            }
            
            GC.SuppressFinalize(this);
        }
    }

    public class WifiHistoryEntry
    {
        public string SSID { get; set; } = string.Empty;
        public DateTime LastConnected { get; set; }
        public int ConnectionCount { get; set; }
    }
}