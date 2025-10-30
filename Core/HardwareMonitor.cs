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
    /// Hardware monitoring system for WiFi adapters and network devices
    /// </summary>
    public static class HardwareMonitor
    {
        private static readonly string HardwareDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MurtiWifiConnecter", "HardwareData");

        private static readonly ConcurrentQueue<HardwareMeasurement> _measurements = new();
        private static readonly ConcurrentDictionary<string, HardwareAlert> _activeAlerts = new();
        private static readonly object _monitorLock = new();
        private static Timer _monitoringTimer;
        private static bool _isMonitoring = false;
        private static DateTime _lastCleanup = DateTime.Now;
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

        // Configuration
        private static TimeSpan _measurementInterval = TimeSpan.FromMinutes(2);
        private static readonly object _configLock = new();

        public static async Task InitializeAsync()
        {
            try
            {
                Directory.CreateDirectory(HardwareDataPath);

                await Logger.LogInfo("Hardware monitor initialized", nameof(HardwareMonitor));
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Failed to initialize hardware monitor");
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

            await Logger.LogInfo("Hardware monitoring started", nameof(HardwareMonitor),
                new Dictionary<string, object> { ["interval"] = _measurementInterval.TotalMinutes });
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

            await Logger.LogInfo("Hardware monitoring stopped", nameof(HardwareMonitor));
        }

        private static async Task PerformMeasurementAsync()
        {
            try
            {
                var measurement = new HardwareMeasurement
                {
                    Timestamp = DateTime.Now,
                    NetworkInterfaces = new List<NetworkInterfaceInfo>()
                };

                // Get all network interfaces
                var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();

                foreach (var ni in networkInterfaces)
                {
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                    {
                        var interfaceInfo = new NetworkInterfaceInfo
                        {
                            Name = ni.Name,
                            Description = ni.Description,
                            Id = ni.Id,
                            Type = ni.NetworkInterfaceType.ToString(),
                            Status = ni.OperationalStatus.ToString(),
                            Speed = ni.Speed,
                            IsReceiveOnly = ni.IsReceiveOnly,
                            SupportsMulticast = ni.SupportsMulticast
                        };

                        // Get IPv4 statistics
                        try
                        {
                            var stats = ni.GetIPv4Statistics();
                            interfaceInfo.BytesReceived = stats.BytesReceived;
                            interfaceInfo.BytesSent = stats.BytesSent;
                            interfaceInfo.UnicastPacketsReceived = stats.UnicastPacketsReceived;
                            interfaceInfo.UnicastPacketsSent = stats.UnicastPacketsSent;
                            interfaceInfo.NonUnicastPacketsReceived = stats.NonUnicastPacketsReceived;
                            interfaceInfo.NonUnicastPacketsSent = stats.NonUnicastPacketsSent;
                            interfaceInfo.IncomingPacketsDiscarded = stats.IncomingPacketsDiscarded;
                            interfaceInfo.OutgoingPacketsDiscarded = stats.OutgoingPacketsDiscarded;
                            interfaceInfo.IncomingPacketsWithErrors = stats.IncomingPacketsWithErrors;
                            interfaceInfo.OutgoingPacketsWithErrors = stats.OutgoingPacketsWithErrors;
                            interfaceInfo.IncomingUnknownProtocolPackets = stats.IncomingUnknownProtocolPackets;
                        }
                        catch
                        {
                            // IPv4 statistics may not be available
                        }

                        // Get IP properties
                        try
                        {
                            var ipProperties = ni.GetIPProperties();
                            interfaceInfo.DnsServers = ipProperties.DnsAddresses
                                .Select(addr => addr.ToString()).ToList();
                            interfaceInfo.GatewayAddresses = ipProperties.GatewayAddresses
                                .Select(addr => addr.Address.ToString()).ToList();

                            if (ipProperties.UnicastAddresses.Any())
                            {
                                interfaceInfo.IpAddress = ipProperties.UnicastAddresses
                                    .First().Address.ToString();
                                interfaceInfo.SubnetMask = ipProperties.UnicastAddresses
                                    .First().IPv4Mask?.ToString();
                            }
                        }
                        catch
                        {
                            // IP properties may not be available
                        }

                        // WiFi specific information
                        if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                        {
                            interfaceInfo.WifiInfo = await GetWifiSpecificInfoAsync(ni.Name);
                        }

                        measurement.NetworkInterfaces.Add(interfaceInfo);
                    }
                }

                // Get system performance information
                measurement.SystemInfo = GetSystemPerformanceInfo();

                // Add to measurements queue
                _measurements.Enqueue(measurement);

                // Keep only recent measurements in memory (last 24 hours)
                while (_measurements.Count > 720) // 2 minutes * 720 = 24 hours
                {
                    _measurements.TryDequeue(out _);
                }

                // Save measurement to disk
                await SaveMeasurementAsync(measurement);

                // Check for hardware alerts
                await CheckHardwareAlertsAsync(measurement);

                // Periodic cleanup
                if (DateTime.Now - _lastCleanup > CleanupInterval)
                {
                    await CleanupOldDataAsync();
                    _lastCleanup = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Hardware measurement failed");
            }
        }

        private static async Task<WifiInterfaceInfo> GetWifiSpecificInfoAsync(string interfaceName)
        {
            var wifiInfo = new WifiInterfaceInfo();

            try
            {
                // Get WiFi interface information using netsh
                var output = await ExecuteNetshCommandAsync($"wlan show interfaces name=\"{interfaceName}\"");
                var lines = output.Split('\n');

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.Contains("State", StringComparison.OrdinalIgnoreCase))
                    {
                        wifiInfo.State = trimmed.Split(':').Last().Trim();
                    }
                    else if (trimmed.Contains("SSID", StringComparison.OrdinalIgnoreCase) && !trimmed.Contains("BSSID"))
                    {
                        wifiInfo.Ssid = trimmed.Split(':').Last().Trim();
                    }
                    else if (trimmed.Contains("BSSID", StringComparison.OrdinalIgnoreCase))
                    {
                        wifiInfo.Bssid = trimmed.Split(':').Last().Trim();
                    }
                    else if (trimmed.Contains("Signal", StringComparison.OrdinalIgnoreCase))
                    {
                        var signalMatch = System.Text.RegularExpressions.Regex.Match(trimmed, @"(\d+)%");
                        if (signalMatch.Success && int.TryParse(signalMatch.Groups[1].Value, out var signal))
                        {
                            wifiInfo.SignalQuality = signal;
                        }
                    }
                    else if (trimmed.Contains("Radio type", StringComparison.OrdinalIgnoreCase))
                    {
                        wifiInfo.RadioType = trimmed.Split(':').Last().Trim();
                    }
                    else if (trimmed.Contains("Authentication", StringComparison.OrdinalIgnoreCase))
                    {
                        wifiInfo.Authentication = trimmed.Split(':').Last().Trim();
                    }
                    else if (trimmed.Contains("Cipher", StringComparison.OrdinalIgnoreCase))
                    {
                        wifiInfo.Cipher = trimmed.Split(':').Last().Trim();
                    }
                    else if (trimmed.Contains("Connection mode", StringComparison.OrdinalIgnoreCase))
                    {
                        wifiInfo.ConnectionMode = trimmed.Split(':').Last().Trim();
                    }
                    else if (trimmed.Contains("Channel", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(trimmed.Split(':').Last().Trim(), out var channel))
                        {
                            wifiInfo.Channel = channel;
                        }
                    }
                    else if (trimmed.Contains("Receive rate", StringComparison.OrdinalIgnoreCase))
                    {
                        wifiInfo.ReceiveRateMbps = ParseRate(trimmed.Split(':').Last().Trim());
                    }
                    else if (trimmed.Contains("Transmit rate", StringComparison.OrdinalIgnoreCase))
                    {
                        wifiInfo.TransmitRateMbps = ParseRate(trimmed.Split(':').Last().Trim());
                    }
                }
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, $"Failed to get WiFi info for {interfaceName}");
            }

            return wifiInfo;
        }

        private static double ParseRate(string rateString)
        {
            try
            {
                var parts = rateString.Trim().Split(' ');
                if (parts.Length >= 1 && double.TryParse(parts[0], out var rate))
                {
                    return rate;
                }
            }
            catch
            {
                // Ignore parsing errors
            }
            return 0;
        }

        private static SystemPerformanceInfo GetSystemPerformanceInfo()
        {
            var info = new SystemPerformanceInfo
            {
                Timestamp = DateTime.Now
            };

            try
            {
                // Get CPU usage
                using (var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total"))
                {
                    cpuCounter.NextValue(); // First call returns 0
                    Thread.Sleep(100);
                    info.CpuUsagePercent = cpuCounter.NextValue();
                }

                // Get memory usage
                using (var memoryCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use"))
                {
                    info.MemoryUsagePercent = memoryCounter.NextValue();
                }

                // Get disk usage for system drive
                var systemDrive = Path.GetPathRoot(Environment.SystemDirectory);
                using (var diskCounter = new PerformanceCounter("LogicalDisk", "% Free Space", systemDrive.TrimEnd('\\')))
                {
                    info.DiskUsagePercent = 100 - diskCounter.NextValue();
                }
            }
            catch
            {
                // Performance counters may not be available
            }

            return info;
        }

        private static async Task CheckHardwareAlertsAsync(HardwareMeasurement measurement)
        {
            foreach (var interfaceInfo in measurement.NetworkInterfaces)
            {
                // Check for high error rates
                if (interfaceInfo.UnicastPacketsReceived > 0)
                {
                    var errorRate = (double)interfaceInfo.IncomingPacketsWithErrors / interfaceInfo.UnicastPacketsReceived * 100;
                    if (errorRate > 5) // More than 5% error rate
                    {
                        var alertKey = $"HighErrorRate_{interfaceInfo.Id}";
                        if (!_activeAlerts.ContainsKey(alertKey))
                        {
                            var alert = new HardwareAlert
                            {
                                Id = alertKey,
                                Type = AlertType.Warning,
                                Message = $"High error rate on {interfaceInfo.Name}: {errorRate:F1}%",
                                Timestamp = DateTime.Now,
                                InterfaceName = interfaceInfo.Name,
                                ErrorRate = errorRate
                            };

                            _activeAlerts[alertKey] = alert;

                            await Logger.LogWarning($"Hardware alert: {alert.Message}", nameof(HardwareMonitor),
                                new Dictionary<string, object>
                                {
                                    ["interface"] = interfaceInfo.Name,
                                    ["errorRate"] = errorRate
                                });
                        }
                    }
                }

                // Check WiFi signal quality
                if (interfaceInfo.WifiInfo != null && interfaceInfo.WifiInfo.SignalQuality < 30)
                {
                    var alertKey = $"LowSignal_{interfaceInfo.Id}";
                    if (!_activeAlerts.ContainsKey(alertKey))
                    {
                        var alert = new HardwareAlert
                        {
                            Id = alertKey,
                            Type = AlertType.Warning,
                            Message = $"Low WiFi signal on {interfaceInfo.Name}: {interfaceInfo.WifiInfo.SignalQuality}%",
                            Timestamp = DateTime.Now,
                            InterfaceName = interfaceInfo.Name,
                            SignalQuality = interfaceInfo.WifiInfo.SignalQuality
                        };

                        _activeAlerts[alertKey] = alert;

                        await Logger.LogWarning($"Hardware alert: {alert.Message}", nameof(HardwareMonitor),
                            new Dictionary<string, object>
                            {
                                ["interface"] = interfaceInfo.Name,
                                ["signalQuality"] = interfaceInfo.WifiInfo.SignalQuality
                            });
                    }
                }

                // Check interface status
                if (interfaceInfo.Status != "Up")
                {
                    var alertKey = $"InterfaceDown_{interfaceInfo.Id}";
                    if (!_activeAlerts.ContainsKey(alertKey))
                    {
                        var alert = new HardwareAlert
                        {
                            Id = alertKey,
                            Type = AlertType.Critical,
                            Message = $"Network interface {interfaceInfo.Name} is {interfaceInfo.Status}",
                            Timestamp = DateTime.Now,
                            InterfaceName = interfaceInfo.Name
                        };

                        _activeAlerts[alertKey] = alert;

                        await Logger.LogError($"Hardware alert: {alert.Message}", nameof(HardwareMonitor),
                            new Dictionary<string, object>
                            {
                                ["interface"] = interfaceInfo.Name,
                                ["status"] = interfaceInfo.Status
                            });
                    }
                }
                else
                {
                    // Clear down alerts when interface is back up
                    var downAlertKey = $"InterfaceDown_{interfaceInfo.Id}";
                    _activeAlerts.TryRemove(downAlertKey, out _);
                }
            }

            // Check system performance
            if (measurement.SystemInfo != null)
            {
                if (measurement.SystemInfo.CpuUsagePercent > 90)
                {
                    var alertKey = "HighCpuUsage";
                    if (!_activeAlerts.ContainsKey(alertKey))
                    {
                        var alert = new HardwareAlert
                        {
                            Id = alertKey,
                            Type = AlertType.Warning,
                            Message = $"High CPU usage: {measurement.SystemInfo.CpuUsagePercent:F1}%",
                            Timestamp = DateTime.Now,
                            CpuUsagePercent = measurement.SystemInfo.CpuUsagePercent
                        };

                        _activeAlerts[alertKey] = alert;

                        await Logger.LogWarning($"Hardware alert: {alert.Message}", nameof(HardwareMonitor),
                            new Dictionary<string, object>
                            {
                                ["cpuUsage"] = measurement.SystemInfo.CpuUsagePercent
                            });
                    }
                }

                if (measurement.SystemInfo.MemoryUsagePercent > 90)
                {
                    var alertKey = "HighMemoryUsage";
                    if (!_activeAlerts.ContainsKey(alertKey))
                    {
                        var alert = new HardwareAlert
                        {
                            Id = alertKey,
                            Type = AlertType.Warning,
                            Message = $"High memory usage: {measurement.SystemInfo.MemoryUsagePercent:F1}%",
                            Timestamp = DateTime.Now,
                            MemoryUsagePercent = measurement.SystemInfo.MemoryUsagePercent
                        };

                        _activeAlerts[alertKey] = alert;

                        await Logger.LogWarning($"Hardware alert: {alert.Message}", nameof(HardwareMonitor),
                            new Dictionary<string, object>
                            {
                                ["memoryUsage"] = measurement.SystemInfo.MemoryUsagePercent
                            });
                    }
                }
            }
        }

        private static async Task SaveMeasurementAsync(HardwareMeasurement measurement)
        {
            try
            {
                var fileName = $"hardware_{DateTime.Now:yyyy-MM-dd}.json";
                var filePath = Path.Combine(HardwareDataPath, fileName);

                var measurements = new List<HardwareMeasurement>();

                // Load existing measurements for the day
                if (File.Exists(filePath))
                {
                    var json = await File.ReadAllTextAsync(filePath);
                    measurements = System.Text.Json.JsonSerializer.Deserialize<List<HardwareMeasurement>>(json) ?? new List<HardwareMeasurement>();
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
                await ErrorHandler.LogError(ex, "Failed to save hardware measurement");
            }
        }

        private static async Task CleanupOldDataAsync()
        {
            try
            {
                var cutoffDate = DateTime.Now.AddDays(-7);
                var files = Directory.GetFiles(HardwareDataPath, "hardware_*.json");

                foreach (var file in files)
                {
                    var fileDate = DateTime.ParseExact(Path.GetFileNameWithoutExtension(file).Replace("hardware_", ""), "yyyy-MM-dd", null);
                    if (fileDate < cutoffDate)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Failed to cleanup old hardware data");
            }
        }

        public static async Task<HardwareReport> GenerateReportAsync(TimeSpan period)
        {
            var cutoff = DateTime.Now - period;
            var measurements = new List<HardwareMeasurement>();

            // Load measurements from files
            var files = Directory.GetFiles(HardwareDataPath, "hardware_*.json")
                .Where(f => DateTime.ParseExact(Path.GetFileNameWithoutExtension(f).Replace("hardware_", ""), "yyyy-MM-dd", null) >= cutoff.Date)
                .OrderByDescending(f => f);

            foreach (var file in files)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var dailyMeasurements = System.Text.Json.JsonSerializer.Deserialize<List<HardwareMeasurement>>(json);
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

            var report = new HardwareReport
            {
                GeneratedAt = DateTime.Now,
                Period = period,
                TotalMeasurements = measurements.Count,
                Measurements = measurements.OrderBy(m => m.Timestamp).ToList()
            };

            if (measurements.Any())
            {
                // Analyze interface performance
                report.InterfaceAnalysis = AnalyzeInterfacePerformance(measurements);

                // Generate recommendations
                report.Recommendations = GenerateHardwareRecommendations(report);
            }

            return report;
        }

        private static List<InterfacePerformanceAnalysis> AnalyzeInterfacePerformance(List<HardwareMeasurement> measurements)
        {
            var analysis = new List<InterfacePerformanceAnalysis>();

            // Group by interface
            var interfaceGroups = measurements
                .SelectMany(m => m.NetworkInterfaces)
                .GroupBy(i => i.Id);

            foreach (var interfaceGroup in interfaceGroups)
            {
                var interfaceMeasurements = interfaceGroup.ToList();
                var analysisItem = new InterfacePerformanceAnalysis
                {
                    InterfaceId = interfaceGroup.Key,
                    InterfaceName = interfaceMeasurements.First().Name,
                    InterfaceType = interfaceMeasurements.First().Type,
                    Measurements = interfaceMeasurements.Count,
                    AverageBytesReceivedPerSecond = interfaceMeasurements.Average(i => i.BytesReceived) / _measurementInterval.TotalSeconds,
                    AverageBytesSentPerSecond = interfaceMeasurements.Average(i => i.BytesSent) / _measurementInterval.TotalSeconds,
                    TotalErrors = interfaceMeasurements.Sum(i => i.IncomingPacketsWithErrors + i.OutgoingPacketsWithErrors),
                    AverageErrorRate = interfaceMeasurements.Average(i =>
                        i.UnicastPacketsReceived > 0 ? (double)(i.IncomingPacketsWithErrors + i.OutgoingPacketsWithErrors) / i.UnicastPacketsReceived * 100 : 0)
                };

                // WiFi specific analysis
                var wifiMeasurements = interfaceMeasurements.Where(i => i.WifiInfo != null).ToList();
                if (wifiMeasurements.Any())
                {
                    analysisItem.WifiAnalysis = new WifiPerformanceAnalysis
                    {
                        AverageSignalQuality = wifiMeasurements.Average(i => i.WifiInfo.SignalQuality),
                        MinSignalQuality = wifiMeasurements.Min(i => i.WifiInfo.SignalQuality),
                        MaxSignalQuality = wifiMeasurements.Max(i => i.WifiInfo.SignalQuality),
                        CommonSsids = wifiMeasurements
                            .Where(i => !string.IsNullOrEmpty(i.WifiInfo.Ssid))
                            .GroupBy(i => i.WifiInfo.Ssid)
                            .OrderByDescending(g => g.Count())
                            .Take(3)
                            .Select(g => new SsidFrequency { Ssid = g.Key, Frequency = g.Count() })
                            .ToList(),
                        AverageReceiveRateMbps = wifiMeasurements.Average(i => i.WifiInfo.ReceiveRateMbps),
                        AverageTransmitRateMbps = wifiMeasurements.Average(i => i.WifiInfo.TransmitRateMbps)
                    };
                }

                analysis.Add(analysisItem);
            }

            return analysis.OrderByDescending(a => a.Measurements).ToList();
        }

        private static List<string> GenerateHardwareRecommendations(HardwareReport report)
        {
            var recommendations = new List<string>();

            foreach (var interfaceAnalysis in report.InterfaceAnalysis)
            {
                // Check error rates
                if (interfaceAnalysis.AverageErrorRate > 2)
                {
                    recommendations.Add($"{interfaceAnalysis.InterfaceName}: 高エラー率 ({interfaceAnalysis.AverageErrorRate:F1}%) が検出されました。ケーブルやドライバを確認してください。");
                }

                // WiFi specific recommendations
                if (interfaceAnalysis.WifiAnalysis != null)
                {
                    if (interfaceAnalysis.WifiAnalysis.AverageSignalQuality < 50)
                    {
                        recommendations.Add($"{interfaceAnalysis.InterfaceName}: WiFi信号品質が低いです ({interfaceAnalysis.WifiAnalysis.AverageSignalQuality:F1}%)。アクセスポイントに近づくか、アンテナを調整してください。");
                    }

                    if (interfaceAnalysis.WifiAnalysis.MinSignalQuality < 20)
                    {
                        recommendations.Add($"{interfaceAnalysis.InterfaceName}: WiFi信号が非常に弱い期間があります。干渉源を特定して除去してください。");
                    }
                }
            }

            if (!recommendations.Any())
            {
                recommendations.Add("ハードウェア状態は良好です。定期的な監視を継続してください。");
            }

            return recommendations;
        }

        public static async Task<HardwareStatistics> GetCurrentStatisticsAsync()
        {
            var latestMeasurement = _measurements.LastOrDefault();

            return new HardwareStatistics
            {
                LastMeasurementTime = latestMeasurement?.Timestamp ?? DateTime.MinValue,
                TotalMeasurements = _measurements.Count,
                ActiveAlerts = _activeAlerts.Count,
                NetworkInterfaces = latestMeasurement?.NetworkInterfaces.Count ?? 0,
                SystemPerformance = latestMeasurement?.SystemInfo
            };
        }

        private static async Task<string> ExecuteNetshCommandAsync(string arguments)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                return output;
            }
            catch
            {
                return string.Empty;
            }
        }

        // Data structures
        public class HardwareMeasurement
        {
            public DateTime Timestamp { get; set; }
            public List<NetworkInterfaceInfo> NetworkInterfaces { get; set; } = new();
            public SystemPerformanceInfo SystemInfo { get; set; }
        }

        public class NetworkInterfaceInfo
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public string Id { get; set; }
            public string Type { get; set; }
            public string Status { get; set; }
            public long Speed { get; set; }
            public bool IsReceiveOnly { get; set; }
            public bool SupportsMulticast { get; set; }
            public string IpAddress { get; set; }
            public string SubnetMask { get; set; }
            public List<string> DnsServers { get; set; } = new();
            public List<string> GatewayAddresses { get; set; } = new();
            public long BytesReceived { get; set; }
            public long BytesSent { get; set; }
            public long UnicastPacketsReceived { get; set; }
            public long UnicastPacketsSent { get; set; }
            public long NonUnicastPacketsReceived { get; set; }
            public long NonUnicastPacketsSent { get; set; }
            public long IncomingPacketsDiscarded { get; set; }
            public long OutgoingPacketsDiscarded { get; set; }
            public long IncomingPacketsWithErrors { get; set; }
            public long OutgoingPacketsWithErrors { get; set; }
            public long IncomingUnknownProtocolPackets { get; set; }
            public WifiInterfaceInfo WifiInfo { get; set; }
        }

        public class WifiInterfaceInfo
        {
            public string State { get; set; }
            public string Ssid { get; set; }
            public string Bssid { get; set; }
            public int SignalQuality { get; set; }
            public string RadioType { get; set; }
            public string Authentication { get; set; }
            public string Cipher { get; set; }
            public string ConnectionMode { get; set; }
            public int Channel { get; set; }
            public double ReceiveRateMbps { get; set; }
            public double TransmitRateMbps { get; set; }
        }

        public class SystemPerformanceInfo
        {
            public DateTime Timestamp { get; set; }
            public double CpuUsagePercent { get; set; }
            public double MemoryUsagePercent { get; set; }
            public double DiskUsagePercent { get; set; }
        }

        public class HardwareReport
        {
            public DateTime GeneratedAt { get; set; }
            public TimeSpan Period { get; set; }
            public int TotalMeasurements { get; set; }
            public List<HardwareMeasurement> Measurements { get; set; } = new();
            public List<InterfacePerformanceAnalysis> InterfaceAnalysis { get; set; } = new();
            public List<string> Recommendations { get; set; } = new();
        }

        public class InterfacePerformanceAnalysis
        {
            public string InterfaceId { get; set; }
            public string InterfaceName { get; set; }
            public string InterfaceType { get; set; }
            public int Measurements { get; set; }
            public double AverageBytesReceivedPerSecond { get; set; }
            public double AverageBytesSentPerSecond { get; set; }
            public long TotalErrors { get; set; }
            public double AverageErrorRate { get; set; }
            public WifiPerformanceAnalysis WifiAnalysis { get; set; }
        }

        public class WifiPerformanceAnalysis
        {
            public double AverageSignalQuality { get; set; }
            public int MinSignalQuality { get; set; }
            public int MaxSignalQuality { get; set; }
            public List<SsidFrequency> CommonSsids { get; set; } = new();
            public double AverageReceiveRateMbps { get; set; }
            public double AverageTransmitRateMbps { get; set; }
        }

        public class SsidFrequency
        {
            public string Ssid { get; set; }
            public int Frequency { get; set; }
        }

        public class HardwareAlert
        {
            public string Id { get; set; }
            public AlertType Type { get; set; }
            public string Message { get; set; }
            public DateTime Timestamp { get; set; }
            public string InterfaceName { get; set; }
            public double ErrorRate { get; set; }
            public int SignalQuality { get; set; }
            public double CpuUsagePercent { get; set; }
            public double MemoryUsagePercent { get; set; }
        }

        public enum AlertType
        {
            Info,
            Warning,
            Critical
        }

        public class HardwareStatistics
        {
            public DateTime LastMeasurementTime { get; set; }
            public int TotalMeasurements { get; set; }
            public int ActiveAlerts { get; set; }
            public int NetworkInterfaces { get; set; }
            public SystemPerformanceInfo SystemPerformance { get; set; }
        }
    }
}
