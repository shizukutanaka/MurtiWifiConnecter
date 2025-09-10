using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;

namespace MurtiWifiConnecter
{



    public enum HealthStatusLevel
    {
        Healthy,
        Degraded,
        Unhealthy,
        Unknown
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


    // Configuration-related models

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

}