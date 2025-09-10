using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Interfaces
{
    public interface IWifiService
    {
        Task<List<WifiNetwork>> ScanNetworksAsync(CancellationToken cancellationToken = default);
        Task<WifiConnectionResult> ConnectAsync(string ssid, string password, CancellationToken cancellationToken = default);
        Task<string> GetCurrentConnectedSSIDAsync(CancellationToken cancellationToken = default);
        Task<bool> DisconnectAsync(CancellationToken cancellationToken = default);
    }

    public interface IConnectionManagementService
    {
        event EventHandler<ConnectionStatusChangedEventArgs> ConnectionStatusChanged;
        Task<WifiConnectionResult> ConnectWithRetryAsync(string ssid, string password, CancellationToken cancellationToken = default);
        Task<bool> TryAutoConnectAsync(string ssid, CancellationToken cancellationToken = default);
    }

    public interface INetworkService
    {
        Task<NetworkStatus> GetNetworkStatusAsync();
        Task<bool> IsInternetAvailableAsync();
        Task<PingResult> PingAsync(string host, int timeout = 5000);
        Task<List<System.Net.NetworkInformation.NetworkInterface>> GetNetworkInterfacesAsync();
        Task<DnsLookupResult> ResolveDnsAsync(string hostname);
        Task<TraceRouteResult> TraceRouteAsync(string destination, int maxHops = 30);
    }

    public interface IConfigurationService
    {
        event EventHandler<ConfigurationChangedEventArgs> ConfigurationChanged;
        T GetValue<T>(string key, T defaultValue = default);
        Task SetValueAsync<T>(string key, T value);
        Task<Dictionary<string, object>> GetAllAsync();
        Task ReloadAsync();
        Task ResetToDefaultsAsync();
        bool ContainsKey(string key);
        Task RemoveKeyAsync(string key);
    }

    public interface ILoggingService
    {
        event EventHandler<LogEventArgs> LogWritten;
        void LogDebug(string message);
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message, Exception exception = null);
        void LogCritical(string message, Exception exception = null);
        Task<List<LogEntry>> GetLogsAsync(DateTime? startTime = null, DateTime? endTime = null, LogLevel? minLevel = null);
        Task ClearLogsAsync();
    }

    public interface INotificationService
    {
        event EventHandler<NotificationEventArgs> NotificationSent;
        event EventHandler<NotificationActionEventArgs> NotificationActionInvoked;
        Task ShowInfoAsync(string title, string message, NotificationOptions options = null);
        Task ShowSuccessAsync(string title, string message, NotificationOptions options = null);
        Task ShowWarningAsync(string title, string message, NotificationOptions options = null);
        Task ShowErrorAsync(string title, string message, NotificationOptions options = null);
        Task ShowProgressAsync(string title, string message, int progress, NotificationOptions options = null);
        Task ShowCustomAsync(string title, string message, string iconPath, NotificationOptions options = null);
        void ShowToast(string message, ToastDuration duration = ToastDuration.Short);
        void ShowBalloonTip(string title, string message, BalloonIcon icon = BalloonIcon.Info);
        Task<List<NotificationHistory>> GetHistoryAsync(int count = 50);
        Task ClearHistoryAsync();
    }

    public interface ITelemetryService
    {
        event EventHandler<TelemetryEventArgs> MetricRecorded;
        event EventHandler<TelemetryEventArgs> EventTracked;
        void TrackMetric(string name, double value, Dictionary<string, string> properties = null);
        void TrackEvent(string name, Dictionary<string, string> properties = null, Dictionary<string, double> metrics = null);
        void TrackException(Exception exception, Dictionary<string, string> properties = null);
        void TrackDependency(string name, string type, string data, DateTime startTime, TimeSpan duration, bool success);
        void TrackPageView(string pageName, TimeSpan? duration = null, Dictionary<string, string> properties = null);
        Task<TelemetryReport> GetReportAsync(DateTime? startTime = null, DateTime? endTime = null);
        Task FlushAsync();
        Task ClearAsync();
    }

    public interface IHealthMonitor
    {
        event EventHandler<HealthStatusChangedEventArgs> HealthStatusChanged;
        Task<HealthStatus> GetHealthStatusAsync();
        Task<List<HealthCheckResult>> RunHealthChecksAsync();
        void RegisterHealthCheck(string name, Func<Task<HealthCheckResult>> check);
        void UnregisterHealthCheck(string name);
        Task StartMonitoringAsync(CancellationToken cancellationToken = default);
        Task StopMonitoringAsync();
    }

    public interface IPerformanceMonitor
    {
        event EventHandler<PerformanceMetricEventArgs> MetricCaptured;
        void StartCapture(string operationName);
        void StopCapture(string operationName);
        Task<PerformanceReport> GetReportAsync(DateTime? startTime = null, DateTime? endTime = null);
        Task<List<PerformanceMetric>> GetMetricsAsync(string operationName = null);
        void ClearMetrics();
    }

    public interface IMetricsCollector
    {
        Task CollectAsync(string metricName, object data);
        Task<List<MetricData>> GetMetricsAsync(string metricName = null, DateTime? startTime = null, DateTime? endTime = null);
        Task ClearMetricsAsync(string metricName = null);
        void RegisterMetricProvider(string name, Func<Task<object>> provider);
        void UnregisterMetricProvider(string name);
    }

    public interface IThemeManager
    {
        event EventHandler<ThemeChangedEventArgs> ThemeChanged;
        string CurrentTheme { get; }
        List<string> AvailableThemes { get; }
        Task ApplyThemeAsync(string themeName);
        Task<Dictionary<string, object>> GetThemeResourcesAsync(string themeName);
        Task RegisterCustomThemeAsync(string name, Dictionary<string, object> resources);
    }

    public interface IAccessibilityManager
    {
        event EventHandler<AccessibilitySettingsChangedEventArgs> SettingsChanged;
        bool IsHighContrastEnabled { get; }
        bool IsScreenReaderActive { get; }
        double TextScaleFactor { get; set; }
        Task ApplyAccessibilitySettingsAsync(AccessibilitySettings settings);
        Task<AccessibilitySettings> GetCurrentSettingsAsync();
    }

    public interface ILocalizationManager
    {
        event EventHandler<CultureChangedEventArgs> CultureChanged;
        string CurrentCulture { get; }
        List<string> SupportedCultures { get; }
        string GetString(string key, params object[] args);
        Task SetCultureAsync(string cultureName);
        Task LoadResourcesAsync(string cultureName);
    }
}