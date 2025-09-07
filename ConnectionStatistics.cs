using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MurtiWifiConnecter
{
    public class ConnectionStatistics
    {
        private const string StatsFileName = "connection_stats.json";
        private readonly string _statsFilePath;
        private ConnectionStatsData _stats;
        
        public ConnectionStatistics()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appDataPath, "MurtiWifiConnecter");
            Directory.CreateDirectory(appFolder);
            _statsFilePath = Path.Combine(appFolder, StatsFileName);
            _stats = LoadStats();
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

            SaveStats();
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

            SaveStats();
        }

        public void RecordSignalStrength(string ssid, int signalStrength)
        {
            if (string.IsNullOrWhiteSpace(ssid)) return;

            var networkStats = GetOrCreateNetworkStats(ssid);
            networkStats.SignalStrengthReadings.Add(new SignalReading 
            { 
                Timestamp = DateTime.Now, 
                Strength = signalStrength 
            });

            // 古い記録を削除（最新100件まで保持）
            if (networkStats.SignalStrengthReadings.Count > 100)
            {
                networkStats.SignalStrengthReadings = networkStats.SignalStrengthReadings
                    .OrderByDescending(r => r.Timestamp)
                    .Take(100)
                    .ToList();
            }

            // 平均信号強度を計算
            networkStats.AverageSignalStrength = (int)networkStats.SignalStrengthReadings.Average(r => r.Strength);
        }

        public NetworkStats? GetNetworkStats(string ssid)
        {
            return _stats.NetworkStatistics.GetValueOrDefault(ssid);
        }

        public List<NetworkStats> GetTopNetworksByUsage(int count = 10)
        {
            return _stats.NetworkStatistics.Values
                .OrderByDescending(s => s.SuccessfulConnections)
                .ThenByDescending(s => s.TotalSessionTime)
                .Take(count)
                .ToList();
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

            SaveStats();
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

        private void SaveStats()
        {
            try
            {
                var json = JsonSerializer.Serialize(_stats, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(_statsFilePath, json);
            }
            catch { }
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
}