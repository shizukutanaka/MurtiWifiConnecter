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
        
        public ConnectionHistory()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appDataPath, "MurtiWifiConnecter");
            Directory.CreateDirectory(appFolder);
            _historyFilePath = Path.Combine(appFolder, HistoryFileName);
            _history = LoadHistory();
        }

        public void AddSuccessfulConnection(string ssid)
        {
            if (string.IsNullOrWhiteSpace(ssid)) return;
            
            var existing = _history.FirstOrDefault(h => 
                string.Equals(h.SSID, ssid, StringComparison.OrdinalIgnoreCase));
            
            if (existing != null)
            {
                existing.LastConnected = DateTime.Now;
                existing.ConnectionCount++;
            }
            else
            {
                _history.Add(new WifiHistoryEntry 
                { 
                    SSID = ssid, 
                    LastConnected = DateTime.Now,
                    ConnectionCount = 1 
                });
            }

            // 最大50件まで保持
            _history = _history.OrderByDescending(h => h.LastConnected)
                              .Take(50)
                              .ToList();
            
            SaveHistory();
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
            return _history.Any(h => 
                string.Equals(h.SSID, ssid, StringComparison.OrdinalIgnoreCase));
        }

        private List<WifiHistoryEntry> LoadHistory()
        {
            try
            {
                if (File.Exists(_historyFilePath))
                {
                    var json = File.ReadAllText(_historyFilePath);
                    return JsonSerializer.Deserialize<List<WifiHistoryEntry>>(json) ?? new List<WifiHistoryEntry>();
                }
            }
            catch { }
            
            return new List<WifiHistoryEntry>();
        }

        private void SaveHistory()
        {
            try
            {
                var json = JsonSerializer.Serialize(_history, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(_historyFilePath, json);
            }
            catch { }
        }

        public void CleanupOldEntries(int maxAge = 90)
        {
            try
            {
                var cutoffDate = DateTime.Now.AddDays(-maxAge);
                var originalCount = _history.Count;
                
                _history = _history.Where(h => h.LastConnected >= cutoffDate).ToList();
                
                if (_history.Count != originalCount)
                {
                    SaveHistory();
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("ConnectionHistory.CleanupOldEntries", ex);
            }
        }

        public void RemoveNetwork(string ssid)
        {
            try
            {
                var removed = _history.RemoveAll(h => 
                    string.Equals(h.SSID, ssid, StringComparison.OrdinalIgnoreCase));
                    
                if (removed > 0)
                {
                    SaveHistory();
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("ConnectionHistory.RemoveNetwork", ex);
            }
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
            try
            {
                // 重複を削除
                var distinct = _history.GroupBy(h => h.SSID, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(h => h.LastConnected).First())
                    .ToList();

                if (distinct.Count != _history.Count)
                {
                    _history = distinct.OrderByDescending(h => h.LastConnected).ToList();
                    SaveHistory();
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("ConnectionHistory.OptimizeStorage", ex);
            }
        }
    }

    public class WifiHistoryEntry
    {
        public string SSID { get; set; } = string.Empty;
        public DateTime LastConnected { get; set; }
        public int ConnectionCount { get; set; }
    }
}