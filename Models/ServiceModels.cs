using System;
using System.Collections.Generic;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// ネットワーク状態
    /// </summary>
    public class NetworkStatus
    {
        public bool IsConnected { get; set; }
        public string ConnectedSSID { get; set; }
        public int SignalStrength { get; set; }
        public string IpAddress { get; set; }
        public string MacAddress { get; set; }
        public long BytesSent { get; set; }
        public long BytesReceived { get; set; }
        public DateTime ConnectedSince { get; set; }
    }

    /// <summary>
    /// Ping結果
    /// </summary>
    public class PingResult
    {
        public string Host { get; set; }
        public bool Success { get; set; }
        public long RoundtripTime { get; set; }
        public int TimeToLive { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// DNS検索結果
    /// </summary>
    public class DnsLookupResult
    {
        public string Hostname { get; set; }
        public List<string> IpAddresses { get; set; } = new();
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// トレースルート結果
    /// </summary>
    public class TraceRouteResult
    {
        public string Destination { get; set; }
        public List<TraceRouteHop> Hops { get; set; } = new();
        public bool Completed { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// トレースルートホップ
    /// </summary>
    public class TraceRouteHop
    {
        public int HopNumber { get; set; }
        public string IpAddress { get; set; }
        public string Hostname { get; set; }
        public long RoundtripTime { get; set; }
        public bool TimedOut { get; set; }
    }

    /// <summary>
    /// テレメトリレポート
    /// </summary>
    public class TelemetryReport
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public Dictionary<string, int> EventCounts { get; set; } = new();
        public Dictionary<string, double> MetricAverages { get; set; } = new();
        public int TotalEvents { get; set; }
        public int TotalExceptions { get; set; }
    }

    /// <summary>
    /// 健全性状態
    /// </summary>
    public class HealthStatus
    {
        public bool IsHealthy { get; set; }
        public string Status { get; set; }
        public Dictionary<string, HealthCheckResult> CheckResults { get; set; } = new();
        public DateTime LastChecked { get; set; }
    }

    /// <summary>
    /// 健全性チェック結果
    /// </summary>
    public class HealthCheckResult
    {
        public string Name { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public TimeSpan Duration { get; set; }
        public DateTime CheckedAt { get; set; }
    }

    /// <summary>
    /// パフォーマンスレポート
    /// </summary>
    public class PerformanceReport
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public Dictionary<string, List<PerformanceMetric>> OperationMetrics { get; set; } = new();
        public double AverageDuration { get; set; }
        public int TotalOperations { get; set; }
        public int SuccessfulOperations { get; set; }
    }

    /// <summary>
    /// パフォーマンスメトリクス
    /// </summary>
    public class PerformanceMetric
    {
        public string OperationName { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public double Duration { get; set; }
        public bool Success { get; set; }
        public Dictionary<string, object> AdditionalData { get; set; } = new();
    }

    /// <summary>
    /// メトリクスデータ
    /// </summary>
    public class MetricData
    {
        public string Name { get; set; }
        public object Value { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, string> Tags { get; set; } = new();
    }

    /// <summary>
    /// アクセシビリティ設定
    /// </summary>
    public class AccessibilitySettings
    {
        public bool HighContrastEnabled { get; set; }
        public bool ScreenReaderActive { get; set; }
        public double TextScaleFactor { get; set; } = 1.0;
        public bool ReducedMotion { get; set; }
        public bool KeyboardNavigationEnabled { get; set; }
        public int FocusIndicatorWidth { get; set; } = 2;
    }
}