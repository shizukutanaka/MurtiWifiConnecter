using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// 軽量ネットワーク性能追跡システム
    /// </summary>
    public class NetworkPerformanceTracker : IDisposable
    {
        private readonly ConnectionLogger _logger;
        private readonly Timer _performanceTimer;
        private readonly Queue<PerformanceSnapshot> _performanceHistory = new();
        private readonly Dictionary<string, NetworkPerformanceProfile> _networkProfiles = new();
        
        private bool _disposed = false;
        private PerformanceCounters _counters = new();
        
        public event EventHandler<PerformanceChangedEventArgs>? PerformanceChanged;
        
        public NetworkPerformanceData CurrentPerformance { get; private set; } = new();
        public bool IsTracking { get; private set; } = false;
        
        private const int TrackingIntervalMs = 10000; // 10秒間隔
        private const int MaxHistoryItems = 180; // 30分間の履歴
        
        public NetworkPerformanceTracker(ConnectionLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // 10秒間隔でのパフォーマンス測定
            _performanceTimer = new Timer(TrackPerformance, null, TrackingIntervalMs, TrackingIntervalMs);
        }
        
        public void StartTracking()
        {
            IsTracking = true;
            _logger.Log(ConnectionLogger.LogLevel.Info, "PerformanceTracker", "ネットワーク性能追跡を開始しました");
        }
        
        public void StopTracking()
        {
            IsTracking = false;
            _logger.Log(ConnectionLogger.LogLevel.Info, "PerformanceTracker", "ネットワーク性能追跡を停止しました");
        }
        
        private async void TrackPerformance(object? state)
        {
            if (_disposed || !IsTracking) return;
            
            try
            {
                var performance = await MeasureCurrentPerformanceAsync();
                UpdatePerformanceData(performance);
                UpdatePerformanceHistory(performance);
                
                // パフォーマンス変化の通知
                OnPerformanceChanged(new PerformanceChangedEventArgs { Performance = performance });
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("NetworkPerformanceTracker.TrackPerformance", ex, _logger);
            }
        }
        
        private async Task<PerformanceSnapshot> MeasureCurrentPerformanceAsync()
        {
            var snapshot = new PerformanceSnapshot
            {
                Timestamp = DateTime.Now
            };
            
            try
            {
                // 現在の接続SSID取得
                var currentSSID = await FastWifiConnector.GetCurrentConnectedSSIDAsync();
                snapshot.SSID = currentSSID ?? "";
                
                if (!string.IsNullOrEmpty(currentSSID))
                {
                    // レイテンシ測定（軽量版）
                    snapshot.Latency = await MeasureLatencyAsync();
                    
                    // データ使用量測定
                    var dataUsage = MeasureDataUsage();
                    snapshot.BytesSent = dataUsage.BytesSent;
                    snapshot.BytesReceived = dataUsage.BytesReceived;
                    
                    // 接続安定性評価
                    snapshot.ConnectionStability = EvaluateConnectionStability();
                    
                    // 信号強度取得
                    snapshot.SignalStrength = NetworkUtils.GetSignalStrength(currentSSID);
                }
                else
                {
                    snapshot.IsConnected = false;
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("NetworkPerformanceTracker.MeasureCurrentPerformanceAsync", ex, _logger);
                snapshot.HasError = true;
                snapshot.ErrorMessage = ex.Message;
            }
            
            return snapshot;
        }
        
        private async Task<TimeSpan> MeasureLatencyAsync()
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync("8.8.8.8", 3000);
                
                return reply.Status == IPStatus.Success 
                    ? TimeSpan.FromMilliseconds(reply.RoundtripTime)
                    : TimeSpan.FromMilliseconds(9999); // タイムアウト値
            }
            catch
            {
                return TimeSpan.FromMilliseconds(9999);
            }
        }
        
        private DataUsageSnapshot MeasureDataUsage()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                var wifiInterface = interfaces.FirstOrDefault(i => 
                    i.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                    i.OperationalStatus == OperationalStatus.Up);
                
                if (wifiInterface?.GetIPv4Statistics() is IPv4InterfaceStatistics stats)
                {
                    var newCounters = new PerformanceCounters
                    {
                        BytesSent = stats.BytesSent,
                        BytesReceived = stats.BytesReceived,
                        Timestamp = DateTime.Now
                    };
                    
                    // 前回測定からの差分を計算
                    var result = new DataUsageSnapshot();
                    if (_counters.Timestamp != DateTime.MinValue)
                    {
                        var timeDiff = (newCounters.Timestamp - _counters.Timestamp).TotalSeconds;
                        if (timeDiff > 0)
                        {
                            result.BytesSent = (long)Math.Max(0, (newCounters.BytesSent - _counters.BytesSent) / timeDiff);
                            result.BytesReceived = (long)Math.Max(0, (newCounters.BytesReceived - _counters.BytesReceived) / timeDiff);
                        }
                    }
                    
                    _counters = newCounters;
                    return result;
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("NetworkPerformanceTracker.MeasureDataUsage", ex, _logger);
            }
            
            return new DataUsageSnapshot();
        }
        
        private double EvaluateConnectionStability()
        {
            if (_performanceHistory.Count < 3) return 1.0;
            
            var recent = _performanceHistory.TakeLast(6).ToList();
            var disconnections = recent.Count(p => !p.IsConnected);
            var highLatency = recent.Count(p => p.Latency.TotalMilliseconds > 200);
            
            var stabilityScore = 1.0 - ((disconnections * 0.3) + (highLatency * 0.1));
            return Math.Max(0.0, Math.Min(1.0, stabilityScore));
        }
        
        private void UpdatePerformanceData(PerformanceSnapshot snapshot)
        {
            CurrentPerformance.SSID = snapshot.SSID;
            CurrentPerformance.IsConnected = snapshot.IsConnected;
            CurrentPerformance.Latency = snapshot.Latency;
            CurrentPerformance.SignalStrength = snapshot.SignalStrength;
            CurrentPerformance.ConnectionStability = snapshot.ConnectionStability;
            CurrentPerformance.DataRateKbps = CalculateDataRate(snapshot);
            CurrentPerformance.LastUpdated = snapshot.Timestamp;
            
            // ネットワークプロファイルの更新
            if (!string.IsNullOrEmpty(snapshot.SSID))
            {
                UpdateNetworkProfile(snapshot);
            }
        }
        
        private double CalculateDataRate(PerformanceSnapshot snapshot)
        {
            var totalBytes = snapshot.BytesSent + snapshot.BytesReceived;
            return totalBytes * 8.0 / 1024.0; // Kbps
        }
        
        private void UpdateNetworkProfile(PerformanceSnapshot snapshot)
        {
            if (!_networkProfiles.TryGetValue(snapshot.SSID, out var profile))
            {
                profile = new NetworkPerformanceProfile
                {
                    SSID = snapshot.SSID,
                    FirstSeen = snapshot.Timestamp
                };
                _networkProfiles[snapshot.SSID] = profile;
            }
            
            profile.LastSeen = snapshot.Timestamp;
            profile.MeasurementCount++;
            
            // 統計の更新
            profile.AverageLatency = UpdateAverage(profile.AverageLatency, snapshot.Latency.TotalMilliseconds, profile.MeasurementCount);
            profile.AverageSignalStrength = UpdateAverage(profile.AverageSignalStrength, snapshot.SignalStrength, profile.MeasurementCount);
            profile.AverageStability = UpdateAverage(profile.AverageStability, snapshot.ConnectionStability, profile.MeasurementCount);
        }
        
        private double UpdateAverage(double currentAverage, double newValue, int count)
        {
            return (currentAverage * (count - 1) + newValue) / count;
        }
        
        private void UpdatePerformanceHistory(PerformanceSnapshot snapshot)
        {
            _performanceHistory.Enqueue(snapshot);
            
            // 履歴サイズ制限
            while (_performanceHistory.Count > MaxHistoryItems)
            {
                _performanceHistory.Dequeue();
            }
        }
        
        /// <summary>
        /// ネットワーク性能サマリーを取得
        /// </summary>
        public NetworkPerformanceSummary GetPerformanceSummary(string? ssid = null)
        {
            var targetSSID = ssid ?? CurrentPerformance.SSID;
            if (string.IsNullOrEmpty(targetSSID)) return new NetworkPerformanceSummary();
            
            var profile = _networkProfiles.GetValueOrDefault(targetSSID);
            var recentHistory = _performanceHistory
                .Where(h => h.SSID == targetSSID)
                .TakeLast(30)
                .ToList();
            
            return new NetworkPerformanceSummary
            {
                SSID = targetSSID,
                CurrentLatency = CurrentPerformance.Latency,
                AverageLatency = profile?.AverageLatency ?? 0,
                CurrentSignalStrength = CurrentPerformance.SignalStrength,
                AverageSignalStrength = profile?.AverageSignalStrength ?? 0,
                ConnectionStability = CurrentPerformance.ConnectionStability,
                DataRateKbps = CurrentPerformance.DataRateKbps,
                MeasurementCount = profile?.MeasurementCount ?? 0,
                LatencyTrend = CalculateLatencyTrend(recentHistory),
                QualityRating = CalculateOverallQuality(profile, recentHistory)
            };
        }
        
        private TrendDirection CalculateLatencyTrend(List<PerformanceSnapshot> history)
        {
            if (history.Count < 3) return TrendDirection.Stable;
            
            var recent = history.TakeLast(3).Select(h => h.Latency.TotalMilliseconds).ToList();
            var trend = recent[2] - recent[0];
            
            return trend switch
            {
                > 20 => TrendDirection.Increasing,
                < -20 => TrendDirection.Decreasing,
                _ => TrendDirection.Stable
            };
        }
        
        private QualityRating CalculateOverallQuality(NetworkPerformanceProfile? profile, List<PerformanceSnapshot> history)
        {
            if (profile == null) return QualityRating.Unknown;
            
            var score = 0;
            
            // レイテンシ評価
            if (profile.AverageLatency <= 50) score += 30;
            else if (profile.AverageLatency <= 100) score += 20;
            else if (profile.AverageLatency <= 200) score += 10;
            
            // 信号強度評価
            if (profile.AverageSignalStrength >= 80) score += 25;
            else if (profile.AverageSignalStrength >= 60) score += 20;
            else if (profile.AverageSignalStrength >= 40) score += 10;
            
            // 安定性評価
            if (profile.AverageStability >= 0.9) score += 25;
            else if (profile.AverageStability >= 0.8) score += 20;
            else if (profile.AverageStability >= 0.6) score += 10;
            
            // 測定回数による信頼性評価
            if (profile.MeasurementCount >= 50) score += 20;
            else if (profile.MeasurementCount >= 20) score += 15;
            else if (profile.MeasurementCount >= 10) score += 10;
            else score += 5;
            
            return score switch
            {
                >= 85 => QualityRating.Excellent,
                >= 70 => QualityRating.Good,
                >= 50 => QualityRating.Fair,
                >= 30 => QualityRating.Poor,
                _ => QualityRating.VeryPoor
            };
        }
        
        public List<NetworkPerformanceProfile> GetAllNetworkProfiles()
        {
            return _networkProfiles.Values.ToList();
        }
        
        private void OnPerformanceChanged(PerformanceChangedEventArgs e) => PerformanceChanged?.Invoke(this, e);
        
        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            _performanceTimer?.Dispose();
            _performanceHistory.Clear();
        }
    }
    
    #region Data Classes
    
    public class NetworkPerformanceData
    {
        public string SSID { get; set; } = "";
        public bool IsConnected { get; set; } = false;
        public TimeSpan Latency { get; set; } = TimeSpan.Zero;
        public int SignalStrength { get; set; } = 0;
        public double ConnectionStability { get; set; } = 1.0;
        public double DataRateKbps { get; set; } = 0.0;
        public DateTime LastUpdated { get; set; } = DateTime.MinValue;
    }
    
    public class PerformanceSnapshot
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string SSID { get; set; } = "";
        public bool IsConnected { get; set; } = true;
        public TimeSpan Latency { get; set; } = TimeSpan.Zero;
        public int SignalStrength { get; set; } = 0;
        public double ConnectionStability { get; set; } = 1.0;
        public long BytesSent { get; set; } = 0;
        public long BytesReceived { get; set; } = 0;
        public bool HasError { get; set; } = false;
        public string? ErrorMessage { get; set; }
    }
    
    public class NetworkPerformanceProfile
    {
        public string SSID { get; set; } = "";
        public DateTime FirstSeen { get; set; } = DateTime.Now;
        public DateTime LastSeen { get; set; } = DateTime.Now;
        public int MeasurementCount { get; set; } = 0;
        public double AverageLatency { get; set; } = 0.0;
        public double AverageSignalStrength { get; set; } = 0.0;
        public double AverageStability { get; set; } = 1.0;
    }
    
    public class NetworkPerformanceSummary
    {
        public string SSID { get; set; } = "";
        public TimeSpan CurrentLatency { get; set; } = TimeSpan.Zero;
        public double AverageLatency { get; set; } = 0.0;
        public int CurrentSignalStrength { get; set; } = 0;
        public double AverageSignalStrength { get; set; } = 0.0;
        public double ConnectionStability { get; set; } = 1.0;
        public double DataRateKbps { get; set; } = 0.0;
        public int MeasurementCount { get; set; } = 0;
        public TrendDirection LatencyTrend { get; set; } = TrendDirection.Stable;
        public QualityRating QualityRating { get; set; } = QualityRating.Unknown;
        
        public string GetLatencyDescription() => CurrentLatency.TotalMilliseconds switch
        {
            <= 30 => "非常に良好",
            <= 60 => "良好",
            <= 100 => "普通",
            <= 200 => "やや遅い",
            _ => "遅い"
        };
        
        public string GetStabilityDescription() => ConnectionStability switch
        {
            >= 0.95 => "非常に安定",
            >= 0.85 => "安定",
            >= 0.70 => "やや不安定",
            >= 0.50 => "不安定",
            _ => "非常に不安定"
        };
    }
    
    public class DataUsageSnapshot
    {
        public long BytesSent { get; set; } = 0;
        public long BytesReceived { get; set; } = 0;
    }
    
    public class PerformanceCounters
    {
        public long BytesSent { get; set; } = 0;
        public long BytesReceived { get; set; } = 0;
        public DateTime Timestamp { get; set; } = DateTime.MinValue;
    }
    
    public class PerformanceChangedEventArgs : EventArgs
    {
        public PerformanceSnapshot Performance { get; set; } = new();
    }
    
    public enum TrendDirection
    {
        Decreasing = -1,
        Stable = 0,
        Increasing = 1
    }
    
    public enum QualityRating
    {
        Unknown,
        VeryPoor,
        Poor,
        Fair,
        Good,
        Excellent
    }
    
    #endregion
}