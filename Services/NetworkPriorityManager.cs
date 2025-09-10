using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Services
{
    /// <summary>
    /// ネットワーク優先度管理サービス
    /// </summary>
    public class NetworkPriorityManager : IDisposable
    {
        private readonly string _configPath;
        private readonly SemaphoreSlim _configLock = new(1, 1);
        private readonly Dictionary<string, NetworkPriorityConfig> _priorities;
        private readonly System.Threading.Timer _autoSaveTimer;
        private bool _isDirty = false;
        private bool _disposed = false;

        public event EventHandler<NetworkPriorityChangedEventArgs> PriorityChanged;

        public NetworkPriorityManager()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MurtiWifiConnecter"
            );
            Directory.CreateDirectory(appDataPath);
            
            _configPath = Path.Combine(appDataPath, "network_priorities.json");
            _priorities = new Dictionary<string, NetworkPriorityConfig>(StringComparer.OrdinalIgnoreCase);
            
            // 設定を読み込み
            LoadPriorities();
            
            // 自動保存タイマー（5分ごと）
            _autoSaveTimer = new System.Threading.Timer(
                AutoSaveCallback, 
                null, 
                TimeSpan.FromMinutes(5), 
                TimeSpan.FromMinutes(5)
            );
        }

        /// <summary>
        /// ネットワークの優先度を設定
        /// </summary>
        public async Task SetPriorityAsync(string ssid, int priority, bool autoConnect = true)
        {
            if (string.IsNullOrWhiteSpace(ssid))
                throw new ArgumentException("SSID cannot be empty", nameof(ssid));
            
            if (priority < 0 || priority > 100)
                throw new ArgumentException("Priority must be between 0 and 100", nameof(priority));
            
            await _configLock.WaitAsync();
            try
            {
                var existing = _priorities.ContainsKey(ssid);
                
                _priorities[ssid] = new NetworkPriorityConfig
                {
                    SSID = ssid,
                    Priority = priority,
                    AutoConnect = autoConnect,
                    LastModified = DateTime.Now,
                    ConnectionCount = existing ? _priorities[ssid].ConnectionCount : 0
                };
                
                _isDirty = true;
                
                // イベント発火
                PriorityChanged?.Invoke(this, new NetworkPriorityChangedEventArgs
                {
                    SSID = ssid,
                    Priority = priority,
                    AutoConnect = autoConnect
                });
            }
            finally
            {
                _configLock.Release();
            }
        }

        /// <summary>
        /// 優先度を取得
        /// </summary>
        public async Task<int> GetPriorityAsync(string ssid)
        {
            if (string.IsNullOrWhiteSpace(ssid))
                return 0;
            
            await _configLock.WaitAsync();
            try
            {
                return _priorities.TryGetValue(ssid, out var config) ? config.Priority : 0;
            }
            finally
            {
                _configLock.Release();
            }
        }

        /// <summary>
        /// 利用可能なネットワークから最優先のものを選択
        /// </summary>
        public async Task<string> GetBestNetworkAsync(List<WifiNetwork> availableNetworks)
        {
            if (availableNetworks == null || availableNetworks.Count == 0)
                return null;
            
            await _configLock.WaitAsync();
            try
            {
                var prioritizedNetworks = new List<(WifiNetwork network, int priority)>();
                
                foreach (var network in availableNetworks)
                {
                    if (_priorities.TryGetValue(network.SSID, out var config) && config.AutoConnect)
                    {
                        // 優先度と信号強度を組み合わせたスコア
                        var effectivePriority = config.Priority * 100 + network.SignalStrength;
                        prioritizedNetworks.Add((network, effectivePriority));
                    }
                    else if (network.HasConnectedBefore)
                    {
                        // 過去に接続したことがあるネットワークは低優先度で追加
                        prioritizedNetworks.Add((network, network.SignalStrength));
                    }
                }
                
                if (prioritizedNetworks.Count == 0)
                    return null;
                
                // 最高優先度のネットワークを返す
                var best = prioritizedNetworks.OrderByDescending(p => p.priority).First();
                
                // 接続回数を増やす
                if (_priorities.TryGetValue(best.network.SSID, out var bestConfig))
                {
                    bestConfig.ConnectionCount++;
                    bestConfig.LastConnected = DateTime.Now;
                    _isDirty = true;
                }
                
                return best.network.SSID;
            }
            finally
            {
                _configLock.Release();
            }
        }

        /// <summary>
        /// ネットワークの優先度リストを取得
        /// </summary>
        public async Task<List<NetworkPriorityInfo>> GetPriorityListAsync()
        {
            await _configLock.WaitAsync();
            try
            {
                return _priorities.Values
                    .OrderByDescending(p => p.Priority)
                    .ThenBy(p => p.SSID)
                    .Select(p => new NetworkPriorityInfo
                    {
                        SSID = p.SSID,
                        Priority = p.Priority,
                        AutoConnect = p.AutoConnect,
                        ConnectionCount = p.ConnectionCount,
                        LastConnected = p.LastConnected
                    })
                    .ToList();
            }
            finally
            {
                _configLock.Release();
            }
        }

        /// <summary>
        /// 優先度を削除
        /// </summary>
        public async Task<bool> RemovePriorityAsync(string ssid)
        {
            if (string.IsNullOrWhiteSpace(ssid))
                return false;
            
            await _configLock.WaitAsync();
            try
            {
                if (_priorities.Remove(ssid))
                {
                    _isDirty = true;
                    return true;
                }
                return false;
            }
            finally
            {
                _configLock.Release();
            }
        }

        /// <summary>
        /// 自動接続を有効/無効化
        /// </summary>
        public async Task SetAutoConnectAsync(string ssid, bool autoConnect)
        {
            await _configLock.WaitAsync();
            try
            {
                if (_priorities.TryGetValue(ssid, out var config))
                {
                    config.AutoConnect = autoConnect;
                    config.LastModified = DateTime.Now;
                    _isDirty = true;
                }
            }
            finally
            {
                _configLock.Release();
            }
        }

        /// <summary>
        /// すべての優先度設定をクリア
        /// </summary>
        public async Task ClearAllPrioritiesAsync()
        {
            await _configLock.WaitAsync();
            try
            {
                _priorities.Clear();
                _isDirty = true;
            }
            finally
            {
                _configLock.Release();
            }
        }

        /// <summary>
        /// 接続履歴に基づいて優先度を自動調整
        /// </summary>
        public async Task OptimizePrioritiesAsync()
        {
            await _configLock.WaitAsync();
            try
            {
                var sortedByUsage = _priorities.Values
                    .OrderByDescending(p => p.ConnectionCount)
                    .ThenByDescending(p => p.LastConnected ?? DateTime.MinValue)
                    .ToList();
                
                // 使用頻度に基づいて優先度を再割り当て
                var priority = 100;
                var step = Math.Max(1, 90 / Math.Max(1, sortedByUsage.Count - 1));
                
                foreach (var config in sortedByUsage)
                {
                    config.Priority = priority;
                    priority = Math.Max(10, priority - step);
                }
                
                _isDirty = true;
            }
            finally
            {
                _configLock.Release();
            }
        }

        /// <summary>
        /// 設定を保存
        /// </summary>
        public async Task SavePrioritiesAsync()
        {
            if (!_isDirty)
                return;
            
            await _configLock.WaitAsync();
            try
            {
                var json = JsonSerializer.Serialize(_priorities, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                await File.WriteAllTextAsync(_configPath, json);
                _isDirty = false;
            }
            catch (Exception ex)
            {
                SimpleLoggingService.LogError($"Failed to save network priorities: {ex.Message}");
            }
            finally
            {
                _configLock.Release();
            }
        }

        /// <summary>
        /// 設定を読み込み
        /// </summary>
        private void LoadPriorities()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, NetworkPriorityConfig>>(json);
                    
                    if (loaded != null)
                    {
                        foreach (var kvp in loaded)
                        {
                            _priorities[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SimpleLoggingService.LogError($"Failed to load network priorities: {ex.Message}");
            }
        }

        private void AutoSaveCallback(object state)
        {
            if (_disposed || !_isDirty)
                return;
            
            Task.Run(async () => await SavePrioritiesAsync());
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            
            _disposed = true;
            
            // 最終保存
            if (_isDirty)
            {
                SavePrioritiesAsync().Wait(5000);
            }
            
            _autoSaveTimer?.Dispose();
            _configLock?.Dispose();
        }
    }

    /// <summary>
    /// 優先度設定
    /// </summary>
    internal class NetworkPriorityConfig
    {
        public string SSID { get; set; }
        public int Priority { get; set; }
        public bool AutoConnect { get; set; }
        public int ConnectionCount { get; set; }
        public DateTime? LastConnected { get; set; }
        public DateTime LastModified { get; set; }
    }

    /// <summary>
    /// 優先度情報（公開用）
    /// </summary>
    public class NetworkPriorityInfo
    {
        public string SSID { get; set; }
        public int Priority { get; set; }
        public bool AutoConnect { get; set; }
        public int ConnectionCount { get; set; }
        public DateTime? LastConnected { get; set; }
    }

    /// <summary>
    /// 優先度変更イベント
    /// </summary>
    public class NetworkPriorityChangedEventArgs : EventArgs
    {
        public string SSID { get; set; }
        public int Priority { get; set; }
        public bool AutoConnect { get; set; }
    }
}