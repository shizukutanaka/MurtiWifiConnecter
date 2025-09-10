using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Infrastructure
{
    /// <summary>
    /// テレメトリサービスインターフェース
    /// </summary>
    public interface ITelemetryService
    {
        void TrackEvent(string eventName, Dictionary<string, string> properties = null, Dictionary<string, double> metrics = null);
        void TrackMetric(string name, double value, Dictionary<string, string> properties = null);
        void TrackException(Exception exception, Dictionary<string, string> properties = null);
        void TrackDependency(string name, string type, string data, DateTime startTime, TimeSpan duration, bool success);
        void TrackPageView(string pageName, TimeSpan? duration = null, Dictionary<string, string> properties = null);
        Task<TelemetryReport> GenerateReportAsync(DateTime startTime, DateTime endTime);
        void StartOperation(string operationName);
        void StopOperation(string operationName, bool success = true);
    }

    /// <summary>
    /// テレメトリサービスの実装
    /// </summary>
    public class TelemetryService : ITelemetryService
    {
        private readonly List<TelemetryEvent> _events;
        private readonly List<TelemetryMetric> _metrics;
        private readonly List<TelemetryException> _exceptions;
        private readonly List<TelemetryDependency> _dependencies;
        private readonly List<TelemetryPageView> _pageViews;
        private readonly Dictionary<string, Stopwatch> _operations;
        private readonly object _lock = new object();

        public TelemetryService()
        {
            _events = new List<TelemetryEvent>();
            _metrics = new List<TelemetryMetric>();
            _exceptions = new List<TelemetryException>();
            _dependencies = new List<TelemetryDependency>();
            _pageViews = new List<TelemetryPageView>();
            _operations = new Dictionary<string, Stopwatch>();
        }

        public void TrackEvent(string eventName, Dictionary<string, string> properties = null, Dictionary<string, double> metrics = null)
        {
            lock (_lock)
            {
                _events.Add(new TelemetryEvent
                {
                    Name = eventName,
                    Timestamp = DateTime.Now,
                    Properties = properties ?? new Dictionary<string, string>(),
                    Metrics = metrics ?? new Dictionary<string, double>()
                });
            }
        }

        public void TrackMetric(string name, double value, Dictionary<string, string> properties = null)
        {
            lock (_lock)
            {
                _metrics.Add(new TelemetryMetric
                {
                    Name = name,
                    Value = value,
                    Timestamp = DateTime.Now,
                    Properties = properties ?? new Dictionary<string, string>()
                });
            }
        }

        public void TrackException(Exception exception, Dictionary<string, string> properties = null)
        {
            lock (_lock)
            {
                _exceptions.Add(new TelemetryException
                {
                    Exception = exception,
                    Timestamp = DateTime.Now,
                    Properties = properties ?? new Dictionary<string, string>(),
                    SeverityLevel = DetermineSeverity(exception)
                });
            }
        }

        public void TrackDependency(string name, string type, string data, DateTime startTime, TimeSpan duration, bool success)
        {
            lock (_lock)
            {
                _dependencies.Add(new TelemetryDependency
                {
                    Name = name,
                    Type = type,
                    Data = data,
                    StartTime = startTime,
                    Duration = duration,
                    Success = success,
                    Properties = new Dictionary<string, string>()
                });
            }
        }

        public void TrackPageView(string pageName, TimeSpan? duration = null, Dictionary<string, string> properties = null)
        {
            lock (_lock)
            {
                _pageViews.Add(new TelemetryPageView
                {
                    PageName = pageName,
                    Timestamp = DateTime.Now,
                    Duration = duration,
                    Properties = properties ?? new Dictionary<string, string>()
                });
            }
        }

        public void StartOperation(string operationName)
        {
            lock (_lock)
            {
                if (!_operations.ContainsKey(operationName))
                {
                    _operations[operationName] = Stopwatch.StartNew();
                }
            }
        }

        public void StopOperation(string operationName, bool success = true)
        {
            lock (_lock)
            {
                if (_operations.TryGetValue(operationName, out var stopwatch))
                {
                    stopwatch.Stop();
                    TrackMetric($"{operationName}_Duration", stopwatch.ElapsedMilliseconds,
                        new Dictionary<string, string> { { "Success", success.ToString() } });
                    _operations.Remove(operationName);
                }
            }
        }

        public async Task<TelemetryReport> GenerateReportAsync(DateTime startTime, DateTime endTime)
        {
            return await Task.Run(() =>
            {
                lock (_lock)
                {
                    var report = new TelemetryReport
                    {
                        StartTime = startTime,
                        EndTime = endTime,
                        Events = _events.Where(e => e.Timestamp >= startTime && e.Timestamp <= endTime).ToList(),
                        Metrics = _metrics.Where(m => m.Timestamp >= startTime && m.Timestamp <= endTime).ToList(),
                        Exceptions = _exceptions.Where(e => e.Timestamp >= startTime && e.Timestamp <= endTime).ToList(),
                        Dependencies = _dependencies.Where(d => d.StartTime >= startTime && d.StartTime <= endTime).ToList(),
                        PageViews = _pageViews.Where(p => p.Timestamp >= startTime && p.Timestamp <= endTime).ToList()
                    };

                    report.Summary = new Dictionary<string, object>
                    {
                        { "TotalEvents", report.Events.Count },
                        { "TotalMetrics", report.Metrics.Count },
                        { "TotalExceptions", report.Exceptions.Count },
                        { "TotalDependencies", report.Dependencies.Count },
                        { "TotalPageViews", report.PageViews.Count },
                        { "SuccessRate", CalculateSuccessRate(report.Dependencies) },
                        { "AverageResponseTime", CalculateAverageResponseTime(report.Dependencies) }
                    };

                    return report;
                }
            });
        }

        private string DetermineSeverity(Exception exception)
        {
            if (exception is OutOfMemoryException || exception is StackOverflowException)
                return "Critical";
            if (exception is UnauthorizedAccessException || exception is System.Security.SecurityException)
                return "Error";
            if (exception is ArgumentException || exception is InvalidOperationException)
                return "Warning";
            return "Information";
        }

        private double CalculateSuccessRate(List<TelemetryDependency> dependencies)
        {
            if (dependencies.Count == 0) return 100.0;
            var successCount = dependencies.Count(d => d.Success);
            return (double)successCount / dependencies.Count * 100;
        }

        private double CalculateAverageResponseTime(List<TelemetryDependency> dependencies)
        {
            if (dependencies.Count == 0) return 0;
            return dependencies.Average(d => d.Duration.TotalMilliseconds);
        }
    }
}