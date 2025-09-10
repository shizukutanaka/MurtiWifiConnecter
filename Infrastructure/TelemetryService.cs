using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MurtiWifiConnecter.Interfaces;

namespace MurtiWifiConnecter.Infrastructure
{
    public class TelemetryService : ITelemetryService, IDisposable
    {
        private readonly IConfigurationService _configService;
        private readonly ILoggingService _logger;
        private readonly ConcurrentDictionary<string, TelemetryMetric> _metrics;
        private readonly ConcurrentDictionary<string, TelemetryEvent> _events;
        private readonly Timer _aggregationTimer;
        private readonly SemaphoreSlim _aggregationLock;
        private bool _telemetryEnabled;

        public event EventHandler<TelemetryEventArgs> MetricRecorded;
        public event EventHandler<TelemetryEventArgs> EventTracked;

        public TelemetryService(IConfigurationService configService, ILoggingService logger)
        {
            _configService = configService;
            _logger = logger;
            _metrics = new ConcurrentDictionary<string, TelemetryMetric>();
            _events = new ConcurrentDictionary<string, TelemetryEvent>();
            _aggregationLock = new SemaphoreSlim(1, 1);
            
            LoadConfiguration();
            
            // Start aggregation timer (every minute)
            _aggregationTimer = new Timer(AggregateMetrics, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        public void TrackMetric(string name, double value, Dictionary<string, string> properties = null)
        {
            if (!_telemetryEnabled)
                return;

            try
            {
                var key = GenerateMetricKey(name, properties);
                var metric = _metrics.AddOrUpdate(key,
                    k => new TelemetryMetric
                    {
                        Name = name,
                        Count = 1,
                        Sum = value,
                        Min = value,
                        Max = value,
                        LastValue = value,
                        Properties = properties ?? new Dictionary<string, string>(),
                        FirstOccurrence = DateTime.UtcNow,
                        LastOccurrence = DateTime.UtcNow
                    },
                    (k, existing) =>
                    {
                        existing.Count++;
                        existing.Sum += value;
                        existing.Min = Math.Min(existing.Min, value);
                        existing.Max = Math.Max(existing.Max, value);
                        existing.LastValue = value;
                        existing.LastOccurrence = DateTime.UtcNow;
                        return existing;
                    });

                MetricRecorded?.Invoke(this, new TelemetryEventArgs
                {
                    Name = name,
                    Value = value,
                    Properties = properties,
                    Timestamp = DateTime.UtcNow
                });

                _logger.LogDebug($"Metric tracked: {name} = {value}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to track metric: {name}", ex);
            }
        }

        public void TrackEvent(string name, Dictionary<string, string> properties = null, Dictionary<string, double> metrics = null)
        {
            if (!_telemetryEnabled)
                return;

            try
            {
                var key = GenerateEventKey(name, properties);
                var telemetryEvent = _events.AddOrUpdate(key,
                    k => new TelemetryEvent
                    {
                        Name = name,
                        Count = 1,
                        Properties = properties ?? new Dictionary<string, string>(),
                        Metrics = metrics ?? new Dictionary<string, double>(),
                        FirstOccurrence = DateTime.UtcNow,
                        LastOccurrence = DateTime.UtcNow
                    },
                    (k, existing) =>
                    {
                        existing.Count++;
                        existing.LastOccurrence = DateTime.UtcNow;
                        
                        // Update metrics
                        if (metrics != null)
                        {
                            foreach (var metric in metrics)
                            {
                                if (existing.Metrics.ContainsKey(metric.Key))
                                {
                                    existing.Metrics[metric.Key] += metric.Value;
                                }
                                else
                                {
                                    existing.Metrics[metric.Key] = metric.Value;
                                }
                            }
                        }
                        
                        return existing;
                    });

                EventTracked?.Invoke(this, new TelemetryEventArgs
                {
                    Name = name,
                    Properties = properties,
                    Metrics = metrics,
                    Timestamp = DateTime.UtcNow
                });

                _logger.LogDebug($"Event tracked: {name}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to track event: {name}", ex);
            }
        }

        public void TrackException(Exception exception, Dictionary<string, string> properties = null)
        {
            if (!_telemetryEnabled)
                return;

            try
            {
                var exceptionProperties = new Dictionary<string, string>(properties ?? new Dictionary<string, string>())
                {
                    ["ExceptionType"] = exception.GetType().FullName,
                    ["Message"] = exception.Message,
                    ["StackTrace"] = exception.StackTrace ?? string.Empty
                };

                if (exception.InnerException != null)
                {
                    exceptionProperties["InnerException"] = exception.InnerException.Message;
                }

                TrackEvent("Exception", exceptionProperties);
                
                _logger.LogError($"Exception tracked: {exception.GetType().Name}", exception);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to track exception", ex);
            }
        }

        public void TrackDependency(string name, string type, string data, DateTime startTime, TimeSpan duration, bool success)
        {
            if (!_telemetryEnabled)
                return;

            try
            {
                var properties = new Dictionary<string, string>
                {
                    ["Type"] = type,
                    ["Data"] = data,
                    ["Success"] = success.ToString(),
                    ["StartTime"] = startTime.ToString("O")
                };

                var metrics = new Dictionary<string, double>
                {
                    ["Duration"] = duration.TotalMilliseconds
                };

                TrackEvent($"Dependency.{name}", properties, metrics);
                
                _logger.LogDebug($"Dependency tracked: {name} ({type}) - {duration.TotalMilliseconds}ms");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to track dependency: {name}", ex);
            }
        }

        public void TrackPageView(string pageName, TimeSpan? duration = null, Dictionary<string, string> properties = null)
        {
            if (!_telemetryEnabled)
                return;

            try
            {
                var metrics = new Dictionary<string, double>();
                if (duration.HasValue)
                {
                    metrics["Duration"] = duration.Value.TotalMilliseconds;
                }

                TrackEvent($"PageView.{pageName}", properties, metrics);
                
                _logger.LogDebug($"Page view tracked: {pageName}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to track page view: {pageName}", ex);
            }
        }

        public async Task<TelemetryReport> GetReportAsync(DateTime? startTime = null, DateTime? endTime = null)
        {
            await _aggregationLock.WaitAsync();
            try
            {
                var report = new TelemetryReport
                {
                    StartTime = startTime ?? DateTime.UtcNow.AddHours(-24),
                    EndTime = endTime ?? DateTime.UtcNow,
                    GeneratedAt = DateTime.UtcNow
                };

                // Filter and aggregate metrics
                report.Metrics = _metrics.Values
                    .Where(m => (!startTime.HasValue || m.FirstOccurrence >= startTime.Value) &&
                               (!endTime.HasValue || m.LastOccurrence <= endTime.Value))
                    .Select(m => new TelemetryMetricSummary
                    {
                        Name = m.Name,
                        Count = m.Count,
                        Average = m.Sum / m.Count,
                        Min = m.Min,
                        Max = m.Max,
                        Sum = m.Sum,
                        Properties = m.Properties
                    })
                    .ToList();

                // Filter and aggregate events
                report.Events = _events.Values
                    .Where(e => (!startTime.HasValue || e.FirstOccurrence >= startTime.Value) &&
                               (!endTime.HasValue || e.LastOccurrence <= endTime.Value))
                    .Select(e => new TelemetryEventSummary
                    {
                        Name = e.Name,
                        Count = e.Count,
                        Properties = e.Properties,
                        Metrics = e.Metrics
                    })
                    .ToList();

                return report;
            }
            finally
            {
                _aggregationLock.Release();
            }
        }

        public async Task FlushAsync()
        {
            await _aggregationLock.WaitAsync();
            try
            {
                // In a real implementation, this would send data to a telemetry service
                _logger.LogInfo($"Flushing telemetry: {_metrics.Count} metrics, {_events.Count} events");
                
                // For now, just log summary
                foreach (var metric in _metrics.Values)
                {
                    _logger.LogDebug($"Metric: {metric.Name} - Count: {metric.Count}, Avg: {metric.Sum / metric.Count:F2}");
                }
                
                foreach (var evt in _events.Values)
                {
                    _logger.LogDebug($"Event: {evt.Name} - Count: {evt.Count}");
                }
            }
            finally
            {
                _aggregationLock.Release();
            }
        }

        public async Task ClearAsync()
        {
            await _aggregationLock.WaitAsync();
            try
            {
                _metrics.Clear();
                _events.Clear();
                _logger.LogInfo("Telemetry data cleared");
            }
            finally
            {
                _aggregationLock.Release();
            }
        }

        private void AggregateMetrics(object state)
        {
            if (!_telemetryEnabled)
                return;

            Task.Run(async () =>
            {
                await _aggregationLock.WaitAsync();
                try
                {
                    // Remove old metrics (older than 24 hours)
                    var cutoff = DateTime.UtcNow.AddHours(-24);
                    
                    var oldMetrics = _metrics.Where(kvp => kvp.Value.LastOccurrence < cutoff).Select(kvp => kvp.Key).ToList();
                    foreach (var key in oldMetrics)
                    {
                        _metrics.TryRemove(key, out _);
                    }
                    
                    var oldEvents = _events.Where(kvp => kvp.Value.LastOccurrence < cutoff).Select(kvp => kvp.Key).ToList();
                    foreach (var key in oldEvents)
                    {
                        _events.TryRemove(key, out _);
                    }
                    
                    _logger.LogDebug($"Telemetry aggregation: Removed {oldMetrics.Count} old metrics and {oldEvents.Count} old events");
                }
                catch (Exception ex)
                {
                    _logger.LogError("Failed to aggregate telemetry", ex);
                }
                finally
                {
                    _aggregationLock.Release();
                }
            });
        }

        private string GenerateMetricKey(string name, Dictionary<string, string> properties)
        {
            if (properties == null || properties.Count == 0)
                return name;
            
            var sortedProps = string.Join(",", properties.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));
            return $"{name}|{sortedProps}";
        }

        private string GenerateEventKey(string name, Dictionary<string, string> properties)
        {
            return GenerateMetricKey(name, properties);
        }

        private void LoadConfiguration()
        {
            _telemetryEnabled = _configService.GetValue("Telemetry:Enabled", true);
        }

        public void Dispose()
        {
            _aggregationTimer?.Dispose();
            FlushAsync().Wait(5000);
            _aggregationLock?.Dispose();
        }
    }

    public class TelemetryMetric
    {
        public string Name { get; set; }
        public long Count { get; set; }
        public double Sum { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double LastValue { get; set; }
        public Dictionary<string, string> Properties { get; set; }
        public DateTime FirstOccurrence { get; set; }
        public DateTime LastOccurrence { get; set; }
    }

    public class TelemetryEvent
    {
        public string Name { get; set; }
        public long Count { get; set; }
        public Dictionary<string, string> Properties { get; set; }
        public Dictionary<string, double> Metrics { get; set; }
        public DateTime FirstOccurrence { get; set; }
        public DateTime LastOccurrence { get; set; }
    }

    public class TelemetryReport
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public DateTime GeneratedAt { get; set; }
        public List<TelemetryMetricSummary> Metrics { get; set; }
        public List<TelemetryEventSummary> Events { get; set; }
    }

    public class TelemetryMetricSummary
    {
        public string Name { get; set; }
        public long Count { get; set; }
        public double Average { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double Sum { get; set; }
        public Dictionary<string, string> Properties { get; set; }
    }

    public class TelemetryEventSummary
    {
        public string Name { get; set; }
        public long Count { get; set; }
        public Dictionary<string, string> Properties { get; set; }
        public Dictionary<string, double> Metrics { get; set; }
    }

    public class TelemetryEventArgs : EventArgs
    {
        public string Name { get; set; }
        public double? Value { get; set; }
        public Dictionary<string, string> Properties { get; set; }
        public Dictionary<string, double> Metrics { get; set; }
        public DateTime Timestamp { get; set; }
    }
}