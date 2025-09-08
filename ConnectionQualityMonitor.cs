using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// リアルタイム接続品質監視
    /// </summary>
    public class ConnectionQualityMonitor : IDisposable
    {
        private readonly ConnectionStatistics _connectionStats;
        private readonly ConnectionLogger _connectionLogger;
        private readonly Timer _monitoringTimer;
        private readonly Queue<QualitySnapshot> _qualityHistory = new();
        private bool _disposed = false;
        
        public event EventHandler<QualityChangedEventArgs>? QualityChanged;
        
        public ConnectionQuality CurrentQuality { get; private set; } = ConnectionQuality.Unknown;
        public int CurrentSignalStrength { get; private set; } = 0;
        public string CurrentSSID { get; private set; } = "";
        public TimeSpan SessionDuration { get; private set; } = TimeSpan.Zero;
        
        private DateTime _sessionStartTime = DateTime.MinValue;
        private const int MonitoringIntervalMs = 5000; // 5秒間隔
        private const int MaxHistoryItems = 60; // 5分間の履歴
        
        public ConnectionQualityMonitor(ConnectionStatistics connectionStats, ConnectionLogger connectionLogger)
        {
            _connectionStats = connectionStats ?? throw new ArgumentNullException(nameof(connectionStats));
            _connectionLogger = connectionLogger ?? throw new ArgumentNullException(nameof(connectionLogger));
            
            _monitoringTimer = new Timer(MonitorQuality, null, MonitoringIntervalMs, MonitoringIntervalMs);
        }
        
        private async void MonitorQuality(object? state)
        {
            if (_disposed) return;
            
            try
            {
                await CheckConnectionQualityAsync();
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("ConnectionQualityMonitor.MonitorQuality", ex, _connectionLogger);
            }
        }
        
        private async Task CheckConnectionQualityAsync()
        {
            try
            {
                // 現在の接続情報を取得
                var currentSSID = await FastWifiConnector.GetCurrentConnectedSSIDAsync();
                var isConnected = !string.IsNullOrEmpty(currentSSID);
                
                if (!isConnected)
                {
                    UpdateConnectionState("", 0, ConnectionQuality.Disconnected, TimeSpan.Zero);
                    return;
                }
                
                // セッション開始時間の設定
                if (_sessionStartTime == DateTime.MinValue || CurrentSSID != currentSSID)
                {
                    _sessionStartTime = DateTime.Now;
                }
                
                // 信号強度の取得
                var signalStrength = NetworkUtils.GetSignalStrength(currentSSID);
                
                // 接続品質の評価
                var quality = EvaluateConnectionQuality(currentSSID, signalStrength);
                
                // セッション継続時間の計算
                var sessionDuration = DateTime.Now - _sessionStartTime;
                
                // 品質履歴の更新
                UpdateQualityHistory(currentSSID, signalStrength, quality);
                
                // 状態の更新
                UpdateConnectionState(currentSSID, signalStrength, quality, sessionDuration);
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("ConnectionQualityMonitor.CheckConnectionQualityAsync", ex, _connectionLogger);
                UpdateConnectionState("", 0, ConnectionQuality.Error, TimeSpan.Zero);
            }
        }
        
        private ConnectionQuality EvaluateConnectionQuality(string ssid, int signalStrength)
        {
            var networkStats = _connectionStats.GetNetworkStats(ssid);
            
            // 基本品質評価
            var qualityScore = 0;
            
            // 信号強度による評価（40%）
            if (signalStrength >= 80) qualityScore += 40;
            else if (signalStrength >= 60) qualityScore += 30;
            else if (signalStrength >= 40) qualityScore += 20;
            else if (signalStrength >= 20) qualityScore += 10;
            
            // 接続安定性による評価（30%）
            if (networkStats != null)
            {
                var successRate = networkStats.SuccessRate;
                if (successRate >= 95) qualityScore += 30;
                else if (successRate >= 85) qualityScore += 25;
                else if (successRate >= 70) qualityScore += 15;
                else if (successRate >= 50) qualityScore += 10;
            }
            else
            {
                qualityScore += 15; // デフォルト値
            }
            
            // 信号強度の傾向による評価（20%）
            var trendScore = GetSignalTrend();
            qualityScore += Math.Max(0, Math.Min(20, trendScore + 10));
            
            // 応答性による評価（10%）
            if (networkStats?.AverageConnectionTime != null)
            {
                var avgConnectionTime = networkStats.AverageConnectionTime.TotalSeconds;
                if (avgConnectionTime <= 3) qualityScore += 10;
                else if (avgConnectionTime <= 6) qualityScore += 8;
                else if (avgConnectionTime <= 10) qualityScore += 5;
                else if (avgConnectionTime <= 15) qualityScore += 3;
            }
            else
            {
                qualityScore += 5; // デフォルト値
            }
            
            // 品質レベルの決定
            return qualityScore switch
            {
                >= 85 => ConnectionQuality.Excellent,
                >= 70 => ConnectionQuality.Good,
                >= 50 => ConnectionQuality.Fair,
                >= 25 => ConnectionQuality.Poor,
                _ => ConnectionQuality.VeryPoor
            };
        }
        
        private int GetSignalTrend()
        {
            if (_qualityHistory.Count < 3) return 0;
            
            var recent = _qualityHistory.TakeLast(3).ToList();
            var trend = 0;
            
            for (int i = 1; i < recent.Count; i++)
            {
                if (recent[i].SignalStrength > recent[i-1].SignalStrength)
                    trend++;
                else if (recent[i].SignalStrength < recent[i-1].SignalStrength)
                    trend--;
            }
            
            return trend * 3; // -6 to +6 range
        }
        
        private void UpdateQualityHistory(string ssid, int signalStrength, ConnectionQuality quality)
        {
            var snapshot = new QualitySnapshot
            {
                Timestamp = DateTime.Now,
                SSID = ssid,
                SignalStrength = signalStrength,
                Quality = quality
            };
            
            _qualityHistory.Enqueue(snapshot);
            
            // 履歴サイズ制限
            while (_qualityHistory.Count > MaxHistoryItems)
            {
                _qualityHistory.Dequeue();
            }
        }
        
        private void UpdateConnectionState(string ssid, int signalStrength, ConnectionQuality quality, TimeSpan sessionDuration)
        {
            var previousQuality = CurrentQuality;
            
            CurrentSSID = ssid;
            CurrentSignalStrength = signalStrength;
            CurrentQuality = quality;
            SessionDuration = sessionDuration;
            
            // 品質変化の通知
            if (previousQuality != quality || Math.Abs(CurrentSignalStrength - signalStrength) >= 10)
            {
                OnQualityChanged(new QualityChangedEventArgs
                {
                    SSID = ssid,
                    SignalStrength = signalStrength,
                    Quality = quality,
                    SessionDuration = sessionDuration,
                    PreviousQuality = previousQuality
                });
            }
        }
        
        /// <summary>
        /// 接続品質の詳細サマリーを取得
        /// </summary>
        public ConnectionQualitySummary GetQualitySummary()
        {
            var summary = new ConnectionQualitySummary
            {
                SSID = CurrentSSID,
                SignalStrength = CurrentSignalStrength,
                Quality = CurrentQuality,
                SessionDuration = SessionDuration,
                IsConnected = CurrentQuality != ConnectionQuality.Disconnected && CurrentQuality != ConnectionQuality.Error
            };
            
            if (_qualityHistory.Count > 0)
            {
                var history = _qualityHistory.ToList();
                summary.SignalTrend = GetSignalTrend();
                summary.AverageSignalStrength = (int)history.Average(h => h.SignalStrength);
                summary.QualityStability = CalculateQualityStability(history);
            }
            
            return summary;
        }
        
        private double CalculateQualityStability(List<QualitySnapshot> history)
        {
            if (history.Count < 2) return 1.0;
            
            var qualityChanges = 0;
            for (int i = 1; i < history.Count; i++)
            {
                if (history[i].Quality != history[i-1].Quality)
                    qualityChanges++;
            }
            
            return Math.Max(0.0, 1.0 - (double)qualityChanges / (history.Count - 1));
        }
        
        private void OnQualityChanged(QualityChangedEventArgs e) => QualityChanged?.Invoke(this, e);
        
        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            _monitoringTimer?.Dispose();
            _qualityHistory.Clear();
        }
    }
    
    /// <summary>
    /// 品質スナップショット
    /// </summary>
    internal class QualitySnapshot
    {
        public DateTime Timestamp { get; set; }
        public string SSID { get; set; } = "";
        public int SignalStrength { get; set; }
        public ConnectionQuality Quality { get; set; }
    }
    
    /// <summary>
    /// 接続品質
    /// </summary>
    public enum ConnectionQuality
    {
        Unknown,
        Disconnected,
        Error,
        VeryPoor,
        Poor,
        Fair,
        Good,
        Excellent
    }
    
    /// <summary>
    /// 品質変化イベント引数
    /// </summary>
    public class QualityChangedEventArgs : EventArgs
    {
        public string SSID { get; set; } = "";
        public int SignalStrength { get; set; }
        public ConnectionQuality Quality { get; set; }
        public ConnectionQuality PreviousQuality { get; set; }
        public TimeSpan SessionDuration { get; set; }
    }
    
    /// <summary>
    /// 接続品質サマリー
    /// </summary>
    public class ConnectionQualitySummary
    {
        public string SSID { get; set; } = "";
        public int SignalStrength { get; set; }
        public ConnectionQuality Quality { get; set; }
        public TimeSpan SessionDuration { get; set; }
        public bool IsConnected { get; set; }
        public int SignalTrend { get; set; }
        public int AverageSignalStrength { get; set; }
        public double QualityStability { get; set; }
        
        public string GetQualityDescription() => Quality switch
        {
            ConnectionQuality.Excellent => "優秀",
            ConnectionQuality.Good => "良好",
            ConnectionQuality.Fair => "普通",
            ConnectionQuality.Poor => "弱い",
            ConnectionQuality.VeryPoor => "非常に弱い",
            ConnectionQuality.Disconnected => "未接続",
            ConnectionQuality.Error => "エラー",
            _ => "不明"
        };
        
        public string GetStabilityDescription() => QualityStability switch
        {
            >= 0.9 => "非常に安定",
            >= 0.8 => "安定",
            >= 0.6 => "やや不安定",
            >= 0.4 => "不安定",
            _ => "非常に不安定"
        };
    }
}