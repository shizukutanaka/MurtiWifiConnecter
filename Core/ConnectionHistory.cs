using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// Connection history and statistics manager
    /// </summary>
    public class ConnectionHistory
    {
        private readonly string _historyFilePath;
        private List<ConnectionRecord> _history;
        private readonly object _lock = new();
        private const int MaxHistoryEntries = 1000;

        public ConnectionHistory()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dataDir = Path.Combine(appData, "MurtiWifiConnecter");
            Directory.CreateDirectory(dataDir);

            _historyFilePath = Path.Combine(dataDir, "connection_history.json");
            _history = new List<ConnectionRecord>();

            LoadHistory();
        }

        /// <summary>
        /// Record a connection event
        /// </summary>
        public void RecordConnection(string ssid, bool success, string? message = null)
        {
            lock (_lock)
            {
                var record = new ConnectionRecord
                {
                    SSID = ssid,
                    Timestamp = DateTime.UtcNow,
                    Success = success,
                    Message = message ?? (success ? "Connected successfully" : "Connection failed"),
                    ConnectionType = ConnectionEventType.Connect
                };

                _history.Add(record);

                // Trim history if too large
                if (_history.Count > MaxHistoryEntries)
                {
                    _history = _history.OrderByDescending(r => r.Timestamp)
                        .Take(MaxHistoryEntries)
                        .ToList();
                }

                SaveHistoryAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Record a disconnection event
        /// </summary>
        public void RecordDisconnection(string? ssid = null, string? reason = null)
        {
            lock (_lock)
            {
                var record = new ConnectionRecord
                {
                    SSID = ssid ?? "Unknown",
                    Timestamp = DateTime.UtcNow,
                    Success = true,
                    Message = reason ?? "Disconnected",
                    ConnectionType = ConnectionEventType.Disconnect
                };

                _history.Add(record);
                SaveHistoryAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Record connection duration
        /// </summary>
        public void UpdateConnectionDuration(string ssid, TimeSpan duration)
        {
            lock (_lock)
            {
                var lastConnection = _history
                    .Where(r => r.SSID == ssid && r.ConnectionType == ConnectionEventType.Connect && r.Success)
                    .OrderByDescending(r => r.Timestamp)
                    .FirstOrDefault();

                if (lastConnection != null)
                {
                    lastConnection.Duration = duration;
                    SaveHistoryAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Get connection statistics
        /// </summary>
        public ConnectionStatistics GetStatistics(string? ssid = null, int daysBack = 30)
        {
            lock (_lock)
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-daysBack);
                var relevantHistory = _history.Where(r => r.Timestamp >= cutoffDate);

                if (!string.IsNullOrEmpty(ssid))
                {
                    relevantHistory = relevantHistory.Where(r => r.SSID == ssid);
                }

                var historyList = relevantHistory.ToList();

                var stats = new ConnectionStatistics
                {
                    TotalConnections = historyList.Count(r => r.ConnectionType == ConnectionEventType.Connect),
                    SuccessfulConnections = historyList.Count(r => r.ConnectionType == ConnectionEventType.Connect && r.Success),
                    FailedConnections = historyList.Count(r => r.ConnectionType == ConnectionEventType.Connect && !r.Success),
                    TotalDisconnections = historyList.Count(r => r.ConnectionType == ConnectionEventType.Disconnect)
                };

                // Calculate success rate
                if (stats.TotalConnections > 0)
                {
                    stats.SuccessRate = (double)stats.SuccessfulConnections / stats.TotalConnections * 100;
                }

                // Calculate average connection duration
                var durationsInSeconds = historyList
                    .Where(r => r.Duration.HasValue)
                    .Select(r => r.Duration!.Value.TotalSeconds)
                    .ToList();

                if (durationsInSeconds.Any())
                {
                    stats.AverageConnectionDuration = TimeSpan.FromSeconds(durationsInSeconds.Average());
                }

                // Find most used networks
                stats.MostUsedNetworks = historyList
                    .Where(r => r.ConnectionType == ConnectionEventType.Connect && r.Success)
                    .GroupBy(r => r.SSID)
                    .Select(g => new NetworkUsage
                    {
                        SSID = g.Key,
                        ConnectionCount = g.Count(),
                        LastConnected = g.Max(r => r.Timestamp)
                    })
                    .OrderByDescending(n => n.ConnectionCount)
                    .Take(10)
                    .ToList();

                // Recent failures
                stats.RecentFailures = historyList
                    .Where(r => r.ConnectionType == ConnectionEventType.Connect && !r.Success)
                    .OrderByDescending(r => r.Timestamp)
                    .Take(5)
                    .ToList();

                return stats;
            }
        }

        /// <summary>
        /// Get connection history
        /// </summary>
        public List<ConnectionRecord> GetHistory(int maxEntries = 100)
        {
            lock (_lock)
            {
                return _history
                    .OrderByDescending(r => r.Timestamp)
                    .Take(maxEntries)
                    .ToList();
            }
        }

        /// <summary>
        /// Clear history
        /// </summary>
        public void ClearHistory()
        {
            lock (_lock)
            {
                _history.Clear();
                SaveHistoryAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Export history to CSV
        /// </summary>
        public async Task ExportToCsvAsync(string filePath)
        {
            var lines = new List<string>
            {
                "Timestamp,SSID,Event,Success,Duration,Message"
            };

            lock (_lock)
            {
                foreach (var record in _history.OrderBy(r => r.Timestamp))
                {
                    var duration = record.Duration?.ToString(@"hh\:mm\:ss") ?? "";
                    lines.Add($"{record.Timestamp:yyyy-MM-dd HH:mm:ss},{record.SSID},{record.ConnectionType},{record.Success},{duration},{record.Message}");
                }
            }

            await File.WriteAllLinesAsync(filePath, lines);
        }

        private void LoadHistory()
        {
            try
            {
                if (File.Exists(_historyFilePath))
                {
                    var json = File.ReadAllText(_historyFilePath);
                    _history = JsonSerializer.Deserialize<List<ConnectionRecord>>(json) ?? new List<ConnectionRecord>();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load connection history: {ex.Message}", ex);
                _history = new List<ConnectionRecord>();
            }
        }

        private async Task SaveHistoryAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(_history, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(_historyFilePath, json);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save connection history: {ex.Message}", ex);
            }
        }
    }

    public class ConnectionRecord
    {
        public string SSID { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public ConnectionEventType ConnectionType { get; set; }
        public TimeSpan? Duration { get; set; }
    }

    public enum ConnectionEventType
    {
        Connect,
        Disconnect,
        Reconnect
    }

    public class ConnectionStatistics
    {
        public int TotalConnections { get; set; }
        public int SuccessfulConnections { get; set; }
        public int FailedConnections { get; set; }
        public int TotalDisconnections { get; set; }
        public double SuccessRate { get; set; }
        public TimeSpan AverageConnectionDuration { get; set; }
        public List<NetworkUsage> MostUsedNetworks { get; set; } = new();
        public List<ConnectionRecord> RecentFailures { get; set; } = new();

        public string GetSummary()
        {
            return $"Total: {TotalConnections} | Success Rate: {SuccessRate:F1}% | " +
                   $"Avg Duration: {AverageConnectionDuration:hh\\:mm\\:ss}";
        }
    }

    public class NetworkUsage
    {
        public string SSID { get; set; } = string.Empty;
        public int ConnectionCount { get; set; }
        public DateTime LastConnected { get; set; }
    }
}