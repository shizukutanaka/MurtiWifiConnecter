using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// Advanced bandwidth monitoring and traffic analysis system
    /// </summary>
    public static class BandwidthMonitor
    {
        private static readonly string BandwidthDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MurtiWifiConnecter", "BandwidthData");

        private static readonly ConcurrentQueue<BandwidthMeasurement> _measurements = new();
        private static readonly ConcurrentDictionary<string, BandwidthAlert> _activeAlerts = new();
        private static readonly object _monitorLock = new();
        private static Timer _monitoringTimer;
        private static bool _isMonitoring = false;
        private static DateTime _lastCleanup = DateTime.Now;
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

        // Configuration
        private static TimeSpan _measurementInterval = TimeSpan.FromSeconds(30);
        private static TimeSpan _dataRetentionPeriod = TimeSpan.FromDays(7);
        private static long _bandwidthAlertThreshold = 80; // 80% utilization
        private static long _criticalAlertThreshold = 95; // 95% utilization

        // Performance counters for network monitoring
        private static PerformanceCounter _bytesSentCounter;
        private static PerformanceCounter _bytesReceivedCounter;
        private static PerformanceCounter _bytesTotalCounter;

        public static async Task InitializeAsync()
        {
            try
            {
                Directory.CreateDirectory(BandwidthDataPath);

                // Initialize performance counters for network monitoring
                InitializePerformanceCounters();

                await Logger.LogInfo("Bandwidth monitor initialized", nameof(BandwidthMonitor));
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Failed to initialize bandwidth monitor");
            }
        }

        private static void InitializePerformanceCounters()
        {
            try
            {
                // Get the active network interface
                var networkInterface = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                                         ni.OperationalStatus == OperationalStatus.Up);

                if (networkInterface != null)
                {
                    _bytesSentCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", networkInterface.Name, true);
                    _bytesReceivedCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", networkInterface.Name, true);
                    _bytesTotalCounter = new PerformanceCounter("Network Interface", "Bytes Total/sec", networkInterface.Name, true);
                }
            }
            catch (Exception ex)
            {
                // Performance counters may not be available on all systems
                _bytesSentCounter = null;
                _bytesReceivedCounter = null;
                _bytesTotalCounter = null;
            }
        }

        public static async Task StartMonitoringAsync()
        {
            lock (_monitorLock)
            {
                if (_isMonitoring) return;
                _isMonitoring = true;
            }

            _monitoringTimer = new Timer(async _ => await PerformMeasurementAsync(),
                null, TimeSpan.Zero, _measurementInterval);

            await Logger.LogInfo("Bandwidth monitoring started", nameof(BandwidthMonitor),
                new Dictionary<string, object> { ["interval"] = _measurementInterval.TotalSeconds });
        }

        public static async Task StopMonitoringAsync()
        {
            lock (_monitorLock)
            {
                if (!_isMonitoring) return;
                _isMonitoring = false;
            }

            _monitoringTimer?.Dispose();
            _monitoringTimer = null;

            await Logger.LogInfo("Bandwidth monitoring stopped", nameof(BandwidthMonitor));
        }

        private static async Task PerformMeasurementAsync()
        {
            try
            {
                var measurement = new BandwidthMeasurement
                {
                    Timestamp = DateTime.Now,
                    InterfaceName = GetActiveInterfaceName()
                };

                // Get current bandwidth usage
                if (_bytesTotalCounter != null)
                {
                    measurement.BytesPerSecond = (long)_bytesTotalCounter.NextValue();
                    measurement.BytesSentPerSecond = (long)(_bytesSentCounter?.NextValue() ?? 0);
                    measurement.BytesReceivedPerSecond = (long)(_bytesReceivedCounter?.NextValue() ?? 0);
                }

                // Get network interface statistics
                var networkInterface = GetActiveNetworkInterface();
                if (networkInterface != null)
                {
                    var stats = networkInterface.GetIPv4Statistics();
                    measurement.TotalBytesSent = stats.BytesSent;
                    measurement.TotalBytesReceived = stats.BytesReceived;
                    measurement.UnicastPacketsSent = stats.UnicastPacketsSent;
                    measurement.UnicastPacketsReceived = stats.UnicastPacketsReceived;
                    measurement.ErrorsSent = stats.OutgoingPacketsWithErrors;
                    measurement.ErrorsReceived = stats.IncomingPacketsWithErrors;

                    // Calculate bandwidth utilization
                    if (networkInterface.Speed > 0)
                    {
                        measurement.BandwidthUtilization = (double)measurement.BytesPerSecond * 8 / networkInterface.Speed * 100;
                        measurement.InterfaceSpeed = networkInterface.Speed;
                    }
                }

                // Add to measurements queue
                _measurements.Enqueue(measurement);

                // Keep only recent measurements in memory (last 24 hours)
                while (_measurements.Count > 2880) // 30 seconds * 2880 = 24 hours
                {
                    _measurements.TryDequeue(out _);
                }

                // Save measurement to disk
                await SaveMeasurementAsync(measurement);

                // Check for bandwidth alerts
                await CheckBandwidthAlertsAsync(measurement);

                // Periodic cleanup
                if (DateTime.Now - _lastCleanup > CleanupInterval)
                {
                    await CleanupOldDataAsync();
                    _lastCleanup = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Bandwidth measurement failed");
            }
        }

        private static string GetActiveInterfaceName()
        {
            try
            {
                var networkInterface = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                                         ni.OperationalStatus == OperationalStatus.Up);

                return networkInterface?.Name ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private static NetworkInterface GetActiveNetworkInterface()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                                     ni.OperationalStatus == OperationalStatus.Up);
        }

        private static async Task CheckBandwidthAlertsAsync(BandwidthMeasurement measurement)
        {
            if (measurement.BandwidthUtilization >= _criticalAlertThreshold)
            {
                var alertKey = "CriticalBandwidthUsage";
                if (!_activeAlerts.ContainsKey(alertKey))
                {
                    var alert = new BandwidthAlert
                    {
                        Id = alertKey,
                        Type = AlertType.Critical,
                        Message = $"Critical bandwidth usage: {measurement.BandwidthUtilization:F1}%",
                        Timestamp = DateTime.Now,
                        Utilization = measurement.BandwidthUtilization,
                        InterfaceName = measurement.InterfaceName
                    };

                    _activeAlerts[alertKey] = alert;

                    await Logger.LogError($"Critical bandwidth alert: {alert.Message}", nameof(BandwidthMonitor),
                        new Dictionary<string, object>
                        {
                            ["utilization"] = measurement.BandwidthUtilization,
                            ["interface"] = measurement.InterfaceName
                        });

                    await AuditTrail.RecordEventAsync("Bandwidth", "CriticalUsageAlert", new Dictionary<string, object>
                    {
                        ["utilization"] = measurement.BandwidthUtilization,
                        ["interface"] = measurement.InterfaceName,
                        ["bytesPerSecond"] = measurement.BytesPerSecond
                    }, "Critical");
                }
            }
            else if (measurement.BandwidthUtilization >= _bandwidthAlertThreshold)
            {
                var alertKey = "HighBandwidthUsage";
                if (!_activeAlerts.ContainsKey(alertKey))
                {
                    var alert = new BandwidthAlert
                    {
                        Id = alertKey,
                        Type = AlertType.Warning,
                        Message = $"High bandwidth usage: {measurement.BandwidthUtilization:F1}%",
                        Timestamp = DateTime.Now,
                        Utilization = measurement.BandwidthUtilization,
                        InterfaceName = measurement.InterfaceName
                    };

                    _activeAlerts[alertKey] = alert;

                    await Logger.LogWarning($"High bandwidth alert: {alert.Message}", nameof(BandwidthMonitor),
                        new Dictionary<string, object>
                        {
                            ["utilization"] = measurement.BandwidthUtilization,
                            ["interface"] = measurement.InterfaceName
                        });
                }
            }
            else
            {
                // Clear active alerts if utilization is back to normal
                var alertKeys = _activeAlerts.Keys.Where(k => k.Contains("BandwidthUsage")).ToList();
                foreach (var key in alertKeys)
                {
                    _activeAlerts.TryRemove(key, out _);
                }
            }
        }

        private static async Task SaveMeasurementAsync(BandwidthMeasurement measurement)
        {
            try
            {
                var fileName = $"bandwidth_{DateTime.Now:yyyy-MM-dd}.json";
                var filePath = Path.Combine(BandwidthDataPath, fileName);

                var measurements = new List<BandwidthMeasurement>();

                // Load existing measurements for the day
                if (File.Exists(filePath))
                {
                    var json = await File.ReadAllTextAsync(filePath);
                    measurements = System.Text.Json.JsonSerializer.Deserialize<List<BandwidthMeasurement>>(json) ?? new List<BandwidthMeasurement>();
                }

                measurements.Add(measurement);

                // Keep only measurements from the current day
                measurements = measurements.Where(m => m.Timestamp.Date == DateTime.Now.Date).ToList();

                var updatedJson = System.Text.Json.JsonSerializer.Serialize(measurements, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(filePath, updatedJson);
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Failed to save bandwidth measurement");
            }
        }

        private static async Task CleanupOldDataAsync()
        {
            try
            {
                var cutoffDate = DateTime.Now - _dataRetentionPeriod;
                var files = Directory.GetFiles(BandwidthDataPath, "bandwidth_*.json");

                foreach (var file in files)
                {
                    var fileDate = DateTime.ParseExact(Path.GetFileNameWithoutExtension(file).Replace("bandwidth_", ""),
                        "yyyy-MM-dd", null);
                    if (fileDate < cutoffDate)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Failed to cleanup old bandwidth data");
            }
        }

        public static async Task<BandwidthReport> GenerateReportAsync(TimeSpan period)
        {
            var cutoff = DateTime.Now - period;
            var measurements = new List<BandwidthMeasurement>();

            // Load measurements from files
            var files = Directory.GetFiles(BandwidthDataPath, "bandwidth_*.json")
                .Where(f => DateTime.ParseExact(Path.GetFileNameWithoutExtension(f).Replace("bandwidth_", ""), "yyyy-MM-dd", null) >= cutoff.Date)
                .OrderByDescending(f => f);

            foreach (var file in files)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var dailyMeasurements = System.Text.Json.JsonSerializer.Deserialize<List<BandwidthMeasurement>>(json);
                    if (dailyMeasurements != null)
                    {
                        measurements.AddRange(dailyMeasurements.Where(m => m.Timestamp >= cutoff));
                    }
                }
                catch
                {
                    // Skip corrupted files
                }
            }

            // Add current in-memory measurements
            measurements.AddRange(_measurements.Where(m => m.Timestamp >= cutoff));

            var report = new BandwidthReport
            {
                GeneratedAt = DateTime.Now,
                Period = period,
                TotalMeasurements = measurements.Count,
                Measurements = measurements.OrderBy(m => m.Timestamp).ToList()
            };

            if (measurements.Any())
            {
                report.AverageUtilization = measurements.Average(m => m.BandwidthUtilization);
                report.MaxUtilization = measurements.Max(m => m.BandwidthUtilization);
                report.MinUtilization = measurements.Min(m => m.BandwidthUtilization);
                report.TotalBytesTransferred = measurements.Sum(m => m.BytesPerSecond * _measurementInterval.TotalSeconds);

                // Peak usage analysis
                report.PeakUsageHours = measurements
                    .GroupBy(m => m.Timestamp.Hour)
                    .Select(g => new PeakUsageHour
                    {
                        Hour = g.Key,
                        AverageUtilization = g.Average(m => m.BandwidthUtilization),
                        MaxUtilization = g.Max(m => m.BandwidthUtilization),
                        MeasurementCount = g.Count()
                    })
                    .OrderByDescending(h => h.AverageUtilization)
                    .Take(5)
                    .ToList();

                // Traffic trends
                report.TrafficTrends = CalculateTrafficTrends(measurements);

                // Generate recommendations
                report.Recommendations = GenerateBandwidthRecommendations(report);
            }

            return report;
        }

        private static List<TrafficTrend> CalculateTrafficTrends(List<BandwidthMeasurement> measurements)
        {
            var trends = new List<TrafficTrend>();

            // Group by hour and calculate trends
            var hourlyGroups = measurements.GroupBy(m => new { m.Timestamp.Date, m.Timestamp.Hour })
                .OrderBy(g => g.Key.Date).ThenBy(g => g.Key.Hour);

            TrafficTrend currentTrend = null;
            foreach (var group in hourlyGroups)
            {
                var avgUtilization = group.Average(m => m.BandwidthUtilization);

                if (currentTrend == null)
                {
                    currentTrend = new TrafficTrend
                    {
                        StartTime = group.Key.Date.AddHours(group.Key.Hour),
                        AverageUtilization = avgUtilization,
                        Duration = TimeSpan.FromHours(1)
                    };
                }
                else if (Math.Abs(avgUtilization - currentTrend.AverageUtilization) < 5) // Similar utilization
                {
                    currentTrend.Duration = currentTrend.Duration.Add(TimeSpan.FromHours(1));
                    currentTrend.AverageUtilization = (currentTrend.AverageUtilization + avgUtilization) / 2;
                }
                else
                {
                    trends.Add(currentTrend);
                    currentTrend = new TrafficTrend
                    {
                        StartTime = group.Key.Date.AddHours(group.Key.Hour),
                        AverageUtilization = avgUtilization,
                        Duration = TimeSpan.FromHours(1)
                    };
                }
            }

            if (currentTrend != null)
            {
                trends.Add(currentTrend);
            }

            return trends.OrderByDescending(t => t.Duration).Take(10).ToList();
        }

        private static List<string> GenerateBandwidthRecommendations(BandwidthReport report)
        {
            var recommendations = new List<string>();

            if (report.AverageUtilization > 90)
            {
                recommendations.Add("帯域使用率が非常に高いです。インターネット接続のアップグレードを検討してください。");
            }
            else if (report.AverageUtilization > 70)
            {
                recommendations.Add("帯域使用率が高いです。トラフィックの最適化を検討してください。");
            }

            if (report.MaxUtilization > 95)
            {
                recommendations.Add("ピーク時の帯域使用率が極端に高いです。QoS設定やトラフィックシェーピングを検討してください。");
            }

            var peakHours = report.PeakUsageHours?.Take(3).ToList();
            if (peakHours?.Any() == true)
            {
                var peakHourString = string.Join(", ", peakHours.Select(h => $"{h.Hour}:00"));
                recommendations.Add($"主なピーク使用時間帯: {peakHourString}。この時間帯のトラフィック管理を検討してください。");
            }

            var highUtilizationPeriods = report.TrafficTrends?.Where(t => t.AverageUtilization > 80).ToList();
            if (highUtilizationPeriods?.Any() == true)
            {
                recommendations.Add("長時間の帯域高使用期間が検出されました。定期的なメンテナンスや最適化を検討してください。");
            }

            if (!recommendations.Any())
            {
                recommendations.Add("帯域使用状況は良好です。継続的な監視を推奨します。");
            }

            return recommendations;
        }

        public static async Task<BandwidthStatistics> GetCurrentStatisticsAsync()
        {
            var measurements = _measurements.ToList();
            var lastMeasurement = measurements.LastOrDefault();

            return new BandwidthStatistics
            {
                CurrentUtilization = lastMeasurement?.BandwidthUtilization ?? 0,
                CurrentBytesPerSecond = lastMeasurement?.BytesPerSecond ?? 0,
                AverageUtilizationLastHour = measurements.Any() ?
                    measurements.Where(m => m.Timestamp > DateTime.Now.AddHours(-1)).Average(m => m.BandwidthUtilization) : 0,
                PeakUtilizationLastHour = measurements.Any() ?
                    measurements.Where(m => m.Timestamp > DateTime.Now.AddHours(-1)).Max(m => m.BandwidthUtilization) : 0,
                TotalMeasurements = measurements.Count,
                LastMeasurementTime = lastMeasurement?.Timestamp ?? DateTime.MinValue,
                ActiveAlerts = _activeAlerts.Count
            };
        }

        // Data structures
        public class BandwidthMeasurement
        {
            public DateTime Timestamp { get; set; }
            public string InterfaceName { get; set; }
            public long BytesPerSecond { get; set; }
            public long BytesSentPerSecond { get; set; }
            public long BytesReceivedPerSecond { get; set; }
            public long TotalBytesSent { get; set; }
            public long TotalBytesReceived { get; set; }
            public long UnicastPacketsSent { get; set; }
            public long UnicastPacketsReceived { get; set; }
            public long ErrorsSent { get; set; }
            public long ErrorsReceived { get; set; }
            public double BandwidthUtilization { get; set; }
            public long InterfaceSpeed { get; set; }
        }

        public class BandwidthReport
        {
            public DateTime GeneratedAt { get; set; }
            public TimeSpan Period { get; set; }
            public int TotalMeasurements { get; set; }
            public double AverageUtilization { get; set; }
            public double MaxUtilization { get; set; }
            public double MinUtilization { get; set; }
            public double TotalBytesTransferred { get; set; }
            public List<PeakUsageHour> PeakUsageHours { get; set; } = new();
            public List<TrafficTrend> TrafficTrends { get; set; } = new();
            public List<string> Recommendations { get; set; } = new();
            public List<BandwidthMeasurement> Measurements { get; set; } = new();
        }

        public class PeakUsageHour
        {
            public int Hour { get; set; }
            public double AverageUtilization { get; set; }
            public double MaxUtilization { get; set; }
            public int MeasurementCount { get; set; }
        }

        public class TrafficTrend
        {
            public DateTime StartTime { get; set; }
            public double AverageUtilization { get; set; }
            public TimeSpan Duration { get; set; }
        }

        public class BandwidthAlert
        {
            public string Id { get; set; }
            public AlertType Type { get; set; }
            public string Message { get; set; }
            public DateTime Timestamp { get; set; }
            public double Utilization { get; set; }
            public string InterfaceName { get; set; }
        }

        public enum AlertType
        {
            Info,
            Warning,
            Critical
        }

        public class BandwidthStatistics
        {
            public double CurrentUtilization { get; set; }
            public long CurrentBytesPerSecond { get; set; }
            public double AverageUtilizationLastHour { get; set; }
            public double PeakUtilizationLastHour { get; set; }
            public int TotalMeasurements { get; set; }
            public DateTime LastMeasurementTime { get; set; }
            public int ActiveAlerts { get; set; }
        }
    }
}
