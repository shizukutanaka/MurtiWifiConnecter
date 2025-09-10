using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;

namespace MurtiWifiConnecter
{
    public class WifiNetwork
    {
        public string SSID { get; set; }
        public string BSSID { get; set; }
        public int SignalStrength { get; set; }
        public int Channel { get; set; }
        public bool IsSecured { get; set; }
        public string SecurityType { get; set; }
        public bool IsConnected { get; set; }
        public DateTime LastSeen { get; set; }
    }

    public class WifiConnectionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string ConnectedSSID { get; set; }
        public TimeSpan ConnectionTime { get; set; }
        public int RetryCount { get; set; }
        public Exception Error { get; set; }
    }

    public class ConnectionStatusChangedEventArgs : EventArgs
    {
        public bool IsConnected { get; set; }
        public string SSID { get; set; }
        public int SignalStrength { get; set; }
        public DateTime Timestamp { get; set; }
        public string Reason { get; set; }
    }

    public class HealthStatus
    {
        public HealthStatusLevel Level { get; set; }
        public string Summary { get; set; }
        public List<HealthCheckResult> CheckResults { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public enum HealthStatusLevel
    {
        Healthy,
        Degraded,
        Unhealthy,
        Unknown
    }

    public class HealthCheckResult
    {
        public string Name { get; set; }
        public HealthStatusLevel Status { get; set; }
        public string Description { get; set; }
        public TimeSpan Duration { get; set; }
        public Dictionary<string, object> Data { get; set; }
        public Exception Exception { get; set; }
    }

    public class HealthStatusChangedEventArgs : EventArgs
    {
        public HealthStatus OldStatus { get; set; }
        public HealthStatus NewStatus { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class PerformanceMetric
    {
        public string OperationName { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public double CpuUsage { get; set; }
        public long MemoryUsed { get; set; }
        public Dictionary<string, object> CustomMetrics { get; set; }
    }

    public class PerformanceReport
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public List<PerformanceMetric> Metrics { get; set; }
        public Dictionary<string, PerformanceSummary> Summaries { get; set; }
    }

    public class PerformanceSummary
    {
        public string OperationName { get; set; }
        public int Count { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public TimeSpan AverageDuration { get; set; }
        public TimeSpan MinDuration { get; set; }
        public TimeSpan MaxDuration { get; set; }
        public double AverageCpuUsage { get; set; }
        public long AverageMemoryUsed { get; set; }
    }

    public class PerformanceMetricEventArgs : EventArgs
    {
        public PerformanceMetric Metric { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class MetricData
    {
        public string Name { get; set; }
        public object Value { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, string> Tags { get; set; }
    }

    public class AccessibilitySettings
    {
        public bool HighContrastEnabled { get; set; }
        public bool ScreenReaderActive { get; set; }
        public double TextScaleFactor { get; set; }
        public bool ReducedMotion { get; set; }
        public bool KeyboardNavigationOnly { get; set; }
        public Dictionary<string, object> CustomSettings { get; set; }
    }

    public class AccessibilitySettingsChangedEventArgs : EventArgs
    {
        public AccessibilitySettings OldSettings { get; set; }
        public AccessibilitySettings NewSettings { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class ThemeChangedEventArgs : EventArgs
    {
        public string OldTheme { get; set; }
        public string NewTheme { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class CultureChangedEventArgs : EventArgs
    {
        public string OldCulture { get; set; }
        public string NewCulture { get; set; }
        public DateTime Timestamp { get; set; }
    }

    // Network-related models
    public class NetworkStatus
    {
        public bool IsConnected { get; set; }
        public string ConnectionType { get; set; }
        public string SSID { get; set; }
        public int SignalStrength { get; set; }
        public string IPAddress { get; set; }
        public string SubnetMask { get; set; }
        public string Gateway { get; set; }
        public List<string> DNSServers { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public class PingResult
    {
        public string Host { get; set; }
        public bool Success { get; set; }
        public long RoundtripTime { get; set; }
        public int PacketLoss { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class DnsLookupResult
    {
        public string Hostname { get; set; }
        public List<string> IPAddresses { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public TimeSpan LookupTime { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class TraceRouteResult
    {
        public string Destination { get; set; }
        public List<TraceRouteHop> Hops { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public TimeSpan TotalTime { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class TraceRouteHop
    {
        public int HopNumber { get; set; }
        public string IPAddress { get; set; }
        public string Hostname { get; set; }
        public long ResponseTime { get; set; }
        public bool TimedOut { get; set; }
    }

    // Configuration-related models
    public class ConfigurationChangedEventArgs : EventArgs
    {
        public string Key { get; set; }
        public object OldValue { get; set; }
        public object NewValue { get; set; }
        public DateTime Timestamp { get; set; }
    }

    // Logging-related models
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; }
        public string Category { get; set; }
        public Exception Exception { get; set; }
        public Dictionary<string, object> Properties { get; set; }
    }

    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        Critical = 4
    }

    public class LogEventArgs : EventArgs
    {
        public LogEntry Entry { get; set; }
    }

    // Notification-related models
    public class NotificationOptions
    {
        public TimeSpan? DisplayDuration { get; set; }
        public bool IsClickable { get; set; }
        public List<NotificationAction> Actions { get; set; }
        public Dictionary<string, string> CustomData { get; set; }
        public string Sound { get; set; }
        public bool Silent { get; set; }
    }

    public class NotificationAction
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Icon { get; set; }
    }

    public class NotificationEventArgs : EventArgs
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
        public NotificationOptions Options { get; set; }
    }

    public class NotificationActionEventArgs : EventArgs
    {
        public string NotificationId { get; set; }
        public string ActionId { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class NotificationHistory
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
        public bool WasRead { get; set; }
        public bool WasClicked { get; set; }
    }

    public enum ToastDuration
    {
        Short = 2000,
        Long = 5000
    }

    public enum BalloonIcon
    {
        None,
        Info,
        Warning,
        Error
    }

    // Telemetry-related models
    public class TelemetryReport
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public List<TelemetryMetric> Metrics { get; set; }
        public List<TelemetryEvent> Events { get; set; }
        public List<TelemetryException> Exceptions { get; set; }
        public List<TelemetryDependency> Dependencies { get; set; }
        public List<TelemetryPageView> PageViews { get; set; }
        public Dictionary<string, object> Summary { get; set; }
    }

    public class TelemetryMetric
    {
        public string Name { get; set; }
        public double Value { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, string> Properties { get; set; }
    }

    public class TelemetryEvent
    {
        public string Name { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, string> Properties { get; set; }
        public Dictionary<string, double> Metrics { get; set; }
    }

    public class TelemetryException
    {
        public Exception Exception { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, string> Properties { get; set; }
        public string SeverityLevel { get; set; }
    }

    public class TelemetryDependency
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Data { get; set; }
        public DateTime StartTime { get; set; }
        public TimeSpan Duration { get; set; }
        public bool Success { get; set; }
        public Dictionary<string, string> Properties { get; set; }
    }

    public class TelemetryPageView
    {
        public string PageName { get; set; }
        public DateTime Timestamp { get; set; }
        public TimeSpan? Duration { get; set; }
        public Dictionary<string, string> Properties { get; set; }
    }

    public class TelemetryEventArgs : EventArgs
    {
        public string Type { get; set; }
        public object Data { get; set; }
        public DateTime Timestamp { get; set; }
    }
}