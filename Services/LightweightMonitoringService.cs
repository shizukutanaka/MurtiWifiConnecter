using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace MurtiWifiConnecter.Services
{
    /// <summary>
    /// 軽量監視サービス - システム状態の基本監視
    /// </summary>
    public class LightweightMonitoringService : IDisposable
    {
        private readonly Timer _monitorTimer;
        private readonly Dictionary<string, double> _metrics = new();
        private readonly object _metricsLock = new();
        private bool _disposed = false;

        public event EventHandler<MonitoringAlertEventArgs> AlertTriggered;
        public event EventHandler<MetricsUpdatedEventArgs> MetricsUpdated;

        public LightweightMonitoringService()
        {
            // 30秒間隔で監視
            _monitorTimer = new Timer(CollectMetrics, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
        }

        private async void CollectMetrics(object state)
        {
            if (_disposed) return;

            try
            {
                var metrics = new Dictionary<string, double>();

                // メモリ使用量 (MB)
                var memoryUsage = GC.GetTotalMemory(false) / 1024.0 / 1024.0;
                metrics["memory_usage_mb"] = memoryUsage;

                // GC回数
                var gcCount = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
                metrics["gc_collections"] = gcCount;

                // プロセス実行時間 (秒)
                using var process = Process.GetCurrentProcess();
                metrics["uptime_seconds"] = (DateTime.Now - process.StartTime).TotalSeconds;

                // ワーキングセット (MB)
                metrics["working_set_mb"] = process.WorkingSet64 / 1024.0 / 1024.0;

                // CPU時間 (秒)
                metrics["cpu_time_seconds"] = process.TotalProcessorTime.TotalSeconds;

                // WiFi接続状態チェック
                var currentSSID = await NetworkUtils.GetCurrentConnectedSSIDAsync();
                metrics["wifi_connected"] = string.IsNullOrEmpty(currentSSID) ? 0 : 1;
                
                if (!string.IsNullOrEmpty(currentSSID))
                {
                    // 信号強度チェック
                    var networks = await NetworkUtils.ScanWifiNetworksAsync();
                    if (networks.TryGetValue(currentSSID, out var signal))
                    {
                        metrics["wifi_signal_strength"] = signal;
                    }
                }

                // メトリクス更新
                lock (_metricsLock)
                {
                    foreach (var metric in metrics)
                    {
                        _metrics[metric.Key] = metric.Value;
                    }
                }

                // アラートチェック
                CheckAlerts(metrics);

                // イベント発火
                MetricsUpdated?.Invoke(this, new MetricsUpdatedEventArgs { Metrics = new Dictionary<string, double>(metrics) });
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("LightweightMonitoringService.CollectMetrics", ex);
            }
        }

        private void CheckAlerts(Dictionary<string, double> metrics)
        {
            // メモリ使用量アラート (100MB超)
            if (metrics.TryGetValue("memory_usage_mb", out var memory) && memory > 100)
            {
                TriggerAlert("high_memory_usage", $"Memory usage: {memory:F1} MB", AlertLevel.Warning);
            }

            // WiFi切断アラート
            if (metrics.TryGetValue("wifi_connected", out var connected) && connected == 0)
            {
                TriggerAlert("wifi_disconnected", "WiFi connection lost", AlertLevel.Critical);
            }

            // 信号強度低下アラート (20%未満)
            if (metrics.TryGetValue("wifi_signal_strength", out var signal) && signal < 20)
            {
                TriggerAlert("weak_wifi_signal", $"WiFi signal strength: {signal}%", AlertLevel.Warning);
            }
        }

        private void TriggerAlert(string alertType, string message, AlertLevel level)
        {
            AlertTriggered?.Invoke(this, new MonitoringAlertEventArgs
            {
                AlertType = alertType,
                Message = message,
                Level = level,
                Timestamp = DateTime.Now
            });
        }

        public Dictionary<string, double> GetCurrentMetrics()
        {
            lock (_metricsLock)
            {
                return new Dictionary<string, double>(_metrics);
            }
        }

        public double GetMetric(string name, double defaultValue = 0)
        {
            lock (_metricsLock)
            {
                return _metrics.TryGetValue(name, out var value) ? value : defaultValue;
            }
        }

        public bool IsHealthy()
        {
            lock (_metricsLock)
            {
                // 基本的なヘルス状態判定
                var memoryOk = !_metrics.TryGetValue("memory_usage_mb", out var memory) || memory < 200;
                var wifiOk = !_metrics.TryGetValue("wifi_connected", out var connected) || connected == 1;
                
                return memoryOk && wifiOk;
            }
        }

        public string GetHealthSummary()
        {
            var metrics = GetCurrentMetrics();
            var memoryMb = metrics.GetValueOrDefault("memory_usage_mb", 0);
            var isConnected = metrics.GetValueOrDefault("wifi_connected", 0) == 1;
            var signalStrength = metrics.GetValueOrDefault("wifi_signal_strength", 0);
            var uptimeSeconds = metrics.GetValueOrDefault("uptime_seconds", 0);
            
            var uptime = TimeSpan.FromSeconds(uptimeSeconds);
            var uptimeStr = uptime.TotalHours >= 1 
                ? $"{uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}"
                : $"{uptime.Minutes:D2}:{uptime.Seconds:D2}";

            return $"メモリ: {memoryMb:F1}MB | WiFi: {(isConnected ? $"接続中 ({signalStrength}%)" : "未接続")} | 稼働: {uptimeStr}";
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            _monitorTimer?.Dispose();
        }
    }

    public class MonitoringAlertEventArgs : EventArgs
    {
        public string AlertType { get; set; }
        public string Message { get; set; }
        public AlertLevel Level { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class MetricsUpdatedEventArgs : EventArgs
    {
        public Dictionary<string, double> Metrics { get; set; }
    }

    public enum AlertLevel
    {
        Info,
        Warning,
        Critical
    }
}