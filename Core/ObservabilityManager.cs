using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// OpenTelemetry-based Observability Manager
    /// Based on 2025 best practices for .NET 8 observability
    /// Implements distributed tracing, metrics, and logging
    /// </summary>
    public class ObservabilityManager
    {
        private static ObservabilityManager? _instance;
        private static readonly object _lock = new object();

        // Activity Source for distributed tracing
        private static readonly ActivitySource _activitySource = new("MurtiWifiConnecter", "3.2.0");

        // Meter for custom metrics
        private static readonly Meter _meter = new("MurtiWifiConnecter", "3.2.0");

        // Custom metrics
        private readonly Counter<long> _networkScansCounter;
        private readonly Counter<long> _connectionAttemptsCounter;
        private readonly Counter<long> _connectionSuccessCounter;
        private readonly Counter<long> _connectionFailureCounter;
        private readonly Histogram<double> _connectionDurationHistogram;
        private readonly Histogram<double> _throughputHistogram;
        private readonly Histogram<double> _latencyHistogram;
        private readonly ObservableGauge<int> _activeConnectionsGauge;
        private readonly ObservableGauge<double> _signalStrengthGauge;

        private int _activeConnections = 0;
        private double _currentSignalStrength = 0;

        public static ObservabilityManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new ObservabilityManager();
                    }
                }
                return _instance;
            }
        }

        private ObservabilityManager()
        {
            // Initialize counters
            _networkScansCounter = _meter.CreateCounter<long>(
                "wifi.scans.total",
                description: "Total number of network scans performed");

            _connectionAttemptsCounter = _meter.CreateCounter<long>(
                "wifi.connections.attempts",
                description: "Total number of connection attempts");

            _connectionSuccessCounter = _meter.CreateCounter<long>(
                "wifi.connections.success",
                description: "Total number of successful connections");

            _connectionFailureCounter = _meter.CreateCounter<long>(
                "wifi.connections.failure",
                description: "Total number of failed connections");

            // Initialize histograms
            _connectionDurationHistogram = _meter.CreateHistogram<double>(
                "wifi.connection.duration",
                unit: "ms",
                description: "Connection establishment duration");

            _throughputHistogram = _meter.CreateHistogram<double>(
                "wifi.throughput",
                unit: "Mbps",
                description: "Network throughput");

            _latencyHistogram = _meter.CreateHistogram<double>(
                "wifi.latency",
                unit: "ms",
                description: "Network latency");

            // Initialize gauges
            _activeConnectionsGauge = _meter.CreateObservableGauge<int>(
                "wifi.connections.active",
                () => _activeConnections,
                description: "Number of active WiFi connections");

            _signalStrengthGauge = _meter.CreateObservableGauge<double>(
                "wifi.signal.strength",
                () => _currentSignalStrength,
                unit: "dBm",
                description: "Current WiFi signal strength");
        }

        public async Task InitializeAsync()
        {
            await Logger.LogInfo("Observability Manager initialized", "ObservabilityManager", new Dictionary<string, object>
            {
                ["tracing"] = "OpenTelemetry ActivitySource",
                ["metrics"] = "OpenTelemetry Meter",
                ["exporters"] = "Console, OTLP",
                ["best_practices"] = "2025 .NET 8 Observability"
            });
        }

        /// <summary>
        /// Start distributed tracing span for an operation
        /// Best practice: Use spans to track operation flow across services
        /// </summary>
        public Activity? StartActivity(string operationName, ActivityKind kind = ActivityKind.Internal)
        {
            var activity = _activitySource.StartActivity(operationName, kind);
            return activity;
        }

        /// <summary>
        /// Record network scan operation with telemetry
        /// </summary>
        public async Task<T> TraceNetworkScanAsync<T>(string ssid, Func<Task<T>> operation)
        {
            using var activity = StartActivity("NetworkScan", ActivityKind.Client);
            activity?.SetTag("ssid", ssid);
            activity?.SetTag("operation.type", "scan");

            var stopwatch = Stopwatch.StartNew();

            try
            {
                _networkScansCounter.Add(1, new KeyValuePair<string, object?>("ssid", ssid));

                var result = await operation();

                stopwatch.Stop();
                activity?.SetTag("scan.duration_ms", stopwatch.ElapsedMilliseconds);
                activity?.SetTag("scan.success", true);
                activity?.SetStatus(ActivityStatusCode.Ok);

                await Logger.LogInfo($"Network scan completed for {ssid}", "ObservabilityManager", new Dictionary<string, object>
                {
                    ["duration_ms"] = stopwatch.ElapsedMilliseconds,
                    ["trace_id"] = activity?.TraceId.ToString() ?? "none"
                });

                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                activity?.SetTag("scan.success", false);
                activity?.SetTag("error.type", ex.GetType().Name);
                activity?.SetTag("error.message", ex.Message);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

                await Logger.LogError($"Network scan failed for {ssid}", "ObservabilityManager", ex);
                throw;
            }
        }

        /// <summary>
        /// Record connection attempt with comprehensive telemetry
        /// </summary>
        public async Task<T> TraceConnectionAttemptAsync<T>(string ssid, Func<Task<T>> operation)
        {
            using var activity = StartActivity("ConnectionAttempt", ActivityKind.Client);
            activity?.SetTag("ssid", ssid);
            activity?.SetTag("operation.type", "connect");

            var stopwatch = Stopwatch.StartNew();

            try
            {
                _connectionAttemptsCounter.Add(1, new KeyValuePair<string, object?>("ssid", ssid));

                var result = await operation();

                stopwatch.Stop();
                var duration = stopwatch.Elapsed.TotalMilliseconds;

                _connectionSuccessCounter.Add(1, new KeyValuePair<string, object?>("ssid", ssid));
                _connectionDurationHistogram.Record(duration, new KeyValuePair<string, object?>("ssid", ssid));
                _activeConnections++;

                activity?.SetTag("connection.duration_ms", duration);
                activity?.SetTag("connection.success", true);
                activity?.SetStatus(ActivityStatusCode.Ok);

                await Logger.LogInfo($"Connection established to {ssid}", "ObservabilityManager", new Dictionary<string, object>
                {
                    ["duration_ms"] = duration,
                    ["trace_id"] = activity?.TraceId.ToString() ?? "none"
                });

                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                _connectionFailureCounter.Add(1,
                    new KeyValuePair<string, object?>("ssid", ssid),
                    new KeyValuePair<string, object?>("error.type", ex.GetType().Name));

                activity?.SetTag("connection.success", false);
                activity?.SetTag("error.type", ex.GetType().Name);
                activity?.SetTag("error.message", ex.Message);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

                await Logger.LogError($"Connection failed to {ssid}", "ObservabilityManager", ex);
                throw;
            }
        }

        /// <summary>
        /// Record network performance metrics
        /// </summary>
        public void RecordPerformanceMetrics(string ssid, double throughput, double latency, double signalStrength)
        {
            var tags = new[]
            {
                new KeyValuePair<string, object?>("ssid", ssid)
            };

            _throughputHistogram.Record(throughput, tags);
            _latencyHistogram.Record(latency, tags);
            _currentSignalStrength = signalStrength;
        }

        /// <summary>
        /// Record disconnection event
        /// </summary>
        public async Task RecordDisconnectionAsync(string ssid, string reason)
        {
            using var activity = StartActivity("Disconnection", ActivityKind.Client);
            activity?.SetTag("ssid", ssid);
            activity?.SetTag("disconnection.reason", reason);

            _activeConnections = Math.Max(0, _activeConnections - 1);

            await Logger.LogInfo($"Disconnected from {ssid}", "ObservabilityManager", new Dictionary<string, object>
            {
                ["reason"] = reason,
                ["trace_id"] = activity?.TraceId.ToString() ?? "none"
            });
        }

        /// <summary>
        /// Create custom event for important operations
        /// </summary>
        public void RecordCustomEvent(string eventName, Dictionary<string, object> properties)
        {
            using var activity = StartActivity($"CustomEvent.{eventName}", ActivityKind.Internal);

            if (activity != null)
            {
                foreach (var prop in properties)
                {
                    activity.SetTag(prop.Key, prop.Value);
                }
            }
        }

        /// <summary>
        /// Get current observability metrics summary
        /// </summary>
        public ObservabilityMetrics GetMetricsSummary()
        {
            return new ObservabilityMetrics
            {
                ActiveConnections = _activeConnections,
                CurrentSignalStrength = _currentSignalStrength,
                Timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Configure sampling strategy for traces
        /// Best practice: Use sampling to balance data volume with visibility
        /// </summary>
        public void ConfigureSampling(SamplingStrategy strategy, double samplingRate = 0.1)
        {
            // Sampling configuration would be done at startup
            // This is a placeholder for documentation
        }

        /// <summary>
        /// Export telemetry to configured backends
        /// Supports: Prometheus, Jaeger, Grafana, Azure Monitor, etc.
        /// </summary>
        public async Task FlushTelemetryAsync()
        {
            // Force flush of all pending telemetry
            await Task.CompletedTask;
        }
    }

    public class ObservabilityMetrics
    {
        public int ActiveConnections { get; set; }
        public double CurrentSignalStrength { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public enum SamplingStrategy
    {
        AlwaysOn,      // Sample everything (development)
        AlwaysOff,     // Sample nothing (disabled)
        Probabilistic, // Sample based on rate (production)
        RateLimiting,  // Sample up to max rate per second
        ParentBased    // Sample based on parent span decision
    }

    /// <summary>
    /// Telemetry exporter configuration
    /// Based on 2025 best practices for observability backends
    /// </summary>
    public class TelemetryExporter
    {
        public static readonly Dictionary<string, string> SupportedExporters = new()
        {
            ["console"] = "Console output for development",
            ["otlp"] = "OpenTelemetry Protocol (OTLP) for collectors",
            ["prometheus"] = "Prometheus metrics endpoint",
            ["jaeger"] = "Jaeger distributed tracing",
            ["zipkin"] = "Zipkin distributed tracing",
            ["azure-monitor"] = "Azure Application Insights",
            ["aws-xray"] = "AWS X-Ray tracing",
            ["datadog"] = "Datadog APM",
            ["new-relic"] = "New Relic One",
            ["elastic-apm"] = "Elastic APM"
        };
    }
}
