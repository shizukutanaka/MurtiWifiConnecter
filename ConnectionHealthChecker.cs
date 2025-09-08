using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net.NetworkInformation;

namespace MurtiWifiConnecter
{
    // WiFi接続の健全性を監視・評価する実用クラス
    public class ConnectionHealthChecker : IDisposable
    {
        private readonly Timer _healthCheckTimer;
        private readonly ConnectionLogger _logger;
        private bool _disposed;
        private volatile ConnectionHealthStatus _lastHealthStatus;
        
        public event EventHandler<ConnectionHealthEventArgs>? HealthStatusChanged;
        public event EventHandler<ConnectionHealthEventArgs>? ConnectionDegraded;
        public event EventHandler<ConnectionHealthEventArgs>? ConnectionRecovered;
        
        public ConnectionHealthChecker(ConnectionLogger logger)
        {
            _logger = logger;
            _lastHealthStatus = new ConnectionHealthStatus { Quality = ConnectionQuality.Unknown };
            
            // 30秒間隔でヘルスチェック（軽量）
            _healthCheckTimer = new Timer(CheckConnectionHealth, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }
        
        private async void CheckConnectionHealth(object? state)
        {
            if (_disposed) return;
            
            try
            {
                var health = await PerformHealthCheckAsync();
                
                // 前回の状態と比較
                if (_lastHealthStatus.Quality != health.Quality)
                {
                    HealthStatusChanged?.Invoke(this, new ConnectionHealthEventArgs { Health = health });
                    
                    // 品質の変化を記録
                    if (health.Quality < _lastHealthStatus.Quality)
                    {
                        ConnectionDegraded?.Invoke(this, new ConnectionHealthEventArgs { Health = health });
                        _logger.Log(ConnectionLogger.LogLevel.Warning, "Health", 
                            $"接続品質が低下: {_lastHealthStatus.Quality} → {health.Quality}");
                    }
                    else if (health.Quality > _lastHealthStatus.Quality)
                    {
                        ConnectionRecovered?.Invoke(this, new ConnectionHealthEventArgs { Health = health });
                        _logger.Log(ConnectionLogger.LogLevel.Info, "Health", 
                            $"接続品質が回復: {_lastHealthStatus.Quality} → {health.Quality}");
                    }
                }
                
                _lastHealthStatus = health;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("ConnectionHealthChecker.CheckHealth", ex, _logger);
            }
        }
        
        public async Task<ConnectionHealthStatus> PerformHealthCheckAsync()
        {
            var health = new ConnectionHealthStatus
            {
                Timestamp = DateTime.Now
            };
            
            try
            {
                // 現在接続中のWiFi情報取得
                var currentSSID = await FastWifiConnector.GetCurrentConnectedSSIDAsync();
                if (string.IsNullOrEmpty(currentSSID))
                {
                    health.Quality = ConnectionQuality.Disconnected;
                    health.Issues.Add("WiFiに接続していません");
                    return health;
                }
                
                health.ConnectedSSID = currentSSID;
                
                // 1. 基本的な接続性テスト（軽量）
                var connectivityScore = await TestBasicConnectivity();
                health.ConnectivityScore = connectivityScore;
                
                // 2. レイテンシー測定
                var latency = await MeasureLatency();
                health.Latency = latency;
                
                // 3. 信号強度チェック（現在のネットワーク）
                var signalStrength = await GetCurrentSignalStrength(currentSSID);
                health.SignalStrength = signalStrength;
                
                // 4. 総合品質評価
                health.Quality = CalculateOverallQuality(connectivityScore, latency, signalStrength, health.Issues);
                
                return health;
            }
            catch (Exception ex)
            {
                health.Quality = ConnectionQuality.Error;
                health.Issues.Add($"ヘルスチェックエラー: {ex.Message}");
                return health;
            }
        }
        
        private async Task<int> TestBasicConnectivity()
        {
            var score = 0;
            var testHosts = new[] { "8.8.8.8", "1.1.1.1" }; // Google DNS, Cloudflare DNS
            
            foreach (var host in testHosts)
            {
                try
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(host, 3000);
                    if (reply.Status == IPStatus.Success)
                    {
                        score += 50;
                        if (reply.RoundtripTime < 50) score += 10; // 高速レスポンス
                    }
                }
                catch
                {
                    // ピング失敗は減点しない（軽量評価）
                }
            }
            
            return Math.Min(100, score);
        }
        
        private async Task<long> MeasureLatency()
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync("8.8.8.8", 2000);
                return reply.Status == IPStatus.Success ? reply.RoundtripTime : -1;
            }
            catch
            {
                return -1;
            }
        }
        
        private async Task<int> GetCurrentSignalStrength(string ssid)
        {
            try
            {
                // NetworkUtilsの既存機能を活用
                var networks = await NetworkUtils.ScanWifiNetworksAsync();
                foreach (var network in networks)
                {
                    if (string.Equals(network.Key, ssid, StringComparison.OrdinalIgnoreCase))
                    {
                        return network.Value; // 信号強度
                    }
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }
        
        private static ConnectionQuality CalculateOverallQuality(int connectivityScore, long latency, int signalStrength, System.Collections.Generic.List<string> issues)
        {
            var totalScore = 0;
            
            // 接続性スコア (40%)
            totalScore += (int)(connectivityScore * 0.4);
            
            // レイテンシー評価 (30%)
            if (latency > 0)
            {
                var latencyScore = latency switch
                {
                    < 50 => 100,
                    < 100 => 80,
                    < 200 => 60,
                    < 500 => 40,
                    _ => 20
                };
                totalScore += (int)(latencyScore * 0.3);
            }
            
            // 信号強度評価 (30%)
            if (signalStrength > 0)
            {
                var signalScore = signalStrength switch
                {
                    >= 80 => 100,
                    >= 60 => 80,
                    >= 40 => 60,
                    >= 20 => 40,
                    _ => 20
                };
                totalScore += (int)(signalScore * 0.3);
            }
            
            // 問題がある場合は減点
            if (issues.Count > 0) totalScore -= issues.Count * 10;
            
            return totalScore switch
            {
                >= 85 => ConnectionQuality.Excellent,
                >= 70 => ConnectionQuality.Good,
                >= 50 => ConnectionQuality.Fair,
                >= 30 => ConnectionQuality.Poor,
                _ => ConnectionQuality.Critical
            };
        }
        
        public ConnectionHealthStatus GetLastHealthStatus()
        {
            return _lastHealthStatus;
        }
        
        public void SetCheckInterval(TimeSpan interval)
        {
            if (!_disposed)
            {
                _healthCheckTimer?.Change(interval, interval);
            }
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _healthCheckTimer?.Dispose();
            }
        }
    }
    
    public class ConnectionHealthStatus
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public ConnectionQuality Quality { get; set; } = ConnectionQuality.Unknown;
        public string ConnectedSSID { get; set; } = string.Empty;
        public int ConnectivityScore { get; set; }
        public long Latency { get; set; }
        public int SignalStrength { get; set; }
        public System.Collections.Generic.List<string> Issues { get; set; } = new();
        
        public string GetQualityDescription()
        {
            return Quality switch
            {
                ConnectionQuality.Excellent => "優秀",
                ConnectionQuality.Good => "良好", 
                ConnectionQuality.Fair => "普通",
                ConnectionQuality.Poor => "不良",
                ConnectionQuality.Critical => "危険",
                ConnectionQuality.Disconnected => "未接続",
                ConnectionQuality.Error => "エラー",
                _ => "不明"
            };
        }
        
        public string GetHealthSummary()
        {
            if (Quality == ConnectionQuality.Disconnected)
                return "WiFiに接続していません";
                
            var summary = $"{GetQualityDescription()}";
            if (Latency > 0) summary += $" | 遅延: {Latency}ms";
            if (SignalStrength > 0) summary += $" | 信号: {SignalStrength}%";
            
            return summary;
        }
    }
    
    public class ConnectionHealthEventArgs : EventArgs
    {
        public ConnectionHealthStatus Health { get; set; } = new();
    }
    
    public enum ConnectionQuality
    {
        Unknown,
        Disconnected,
        Critical,
        Poor,
        Fair,
        Good,
        Excellent,
        Error
    }
}