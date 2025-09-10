using System;
using System.Collections.Generic;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// WiFiネットワークイベント引数
    /// </summary>
    public class WifiNetworkEventArgs : EventArgs
    {
        public List<WifiNetwork> Networks { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// WiFi接続イベント引数
    /// </summary>
    public class WifiConnectionEventArgs : EventArgs
    {
        public string SSID { get; set; }
        public bool IsConnected { get; set; }
        public string Status { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 監視アラートイベント引数
    /// </summary>
    public class MonitoringAlertEventArgs : EventArgs
    {
        public string AlertType { get; set; }
        public string Message { get; set; }
        public string Severity { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public Dictionary<string, object> Data { get; set; } = new();
    }

    /// <summary>
    /// メトリクス更新イベント引数
    /// </summary>
    public class MetricsUpdatedEventArgs : EventArgs
    {
        public Dictionary<string, double> Metrics { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 再接続イベント引数
    /// </summary>
    public class ReconnectEventArgs : EventArgs
    {
        public string SSID { get; set; }
        public int AttemptNumber { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 設定変更イベント引数
    /// </summary>
    public class ConfigurationChangedEventArgs : EventArgs
    {
        public string Key { get; set; }
        public object OldValue { get; set; }
        public object NewValue { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// テーマ変更イベント引数
    /// </summary>
    public class ThemeChangedEventArgs : EventArgs
    {
        public string OldTheme { get; set; }
        public string NewTheme { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// アクセシビリティ設定変更イベント引数
    /// </summary>
    public class AccessibilitySettingsChangedEventArgs : EventArgs
    {
        public AccessibilitySettings OldSettings { get; set; }
        public AccessibilitySettings NewSettings { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// カルチャ変更イベント引数
    /// </summary>
    public class CultureChangedEventArgs : EventArgs
    {
        public string OldCulture { get; set; }
        public string NewCulture { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// テレメトリイベント引数
    /// </summary>
    public class TelemetryEventArgs : EventArgs
    {
        public string EventName { get; set; }
        public Dictionary<string, string> Properties { get; set; }
        public Dictionary<string, double> Metrics { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 健全性ステータス変更イベント引数
    /// </summary>
    public class HealthStatusChangedEventArgs : EventArgs
    {
        public HealthStatus OldStatus { get; set; }
        public HealthStatus NewStatus { get; set; }
        public string Reason { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// パフォーマンスメトリクスイベント引数
    /// </summary>
    public class PerformanceMetricEventArgs : EventArgs
    {
        public string OperationName { get; set; }
        public double Duration { get; set; }
        public bool Success { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}