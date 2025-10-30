using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// Monitors network connection stability and provides intelligent switching recommendations
    /// </summary>
    public class ConnectionStabilityMonitor : IDisposable
    {
        private readonly ConcurrentDictionary<string, ConnectionMetrics> _metricsHistory;
        private readonly ConcurrentDictionary<string, NetworkProfile> _networkProfiles;
        private readonly Timer _monitoringTimer;
        private readonly SemaphoreSlim _switchingSemaphore;

        private volatile bool _isMonitoring;
        private volatile bool _disposed;
        private string _currentNetwork;
        private DateTime _lastSwitchTime;

        // Thresholds for stability detection
        private const int MinimumSampleSize = 10;
        private const double PacketLossThreshold = 5.0; // 5% packet loss
        private const double LatencyThreshold = 200.0; // 200ms
        private const double JitterThreshold = 50.0; // 50ms jitter
        private const int SignalStrengthThreshold = 30; // 30% signal strength
        private const int MinimumSwitchInterval = 30; // 30 seconds between switches

        public ConnectionStabilityMonitor()
        {
            _metricsHistory = new ConcurrentDictionary<string, ConnectionMetrics>();
            _networkProfiles = new ConcurrentDictionary<string, NetworkProfile>();
            _switchingSemaphore = new SemaphoreSlim(1, 1);
            _monitoringTimer = new Timer(MonitoringCallback, null, Timeout.Infinite, Timeout.Infinite);
            _lastSwitchTime = DateTime.MinValue;
        }

        public async Task StartMonitoring(string? networkName = null)
        {
            if (_isMonitoring)
                return;

            _isMonitoring = true;
            _currentNetwork = networkName ?? await GetCurrentNetworkName();

            await Logger.LogInfo("Connection stability monitoring started", nameof(ConnectionStabilityMonitor), new Dictionary<string, object>
            {
                ["network"] = _currentNetwork,
                ["timestamp"] = DateTime.Now
            });

            // Start monitoring timer - check every 5 seconds
            _monitoringTimer.Change(0, 5000);
        }

        public async Task StopMonitoring()
        {
            if (!_isMonitoring)
                return;

            _isMonitoring = false;
            _monitoringTimer.Change(Timeout.Infinite, Timeout.Infinite);

            await Logger.LogInfo("Connection stability monitoring stopped", nameof(ConnectionStabilityMonitor));
        }

        private async void MonitoringCallback(object state)
        {
            if (!_isMonitoring || _disposed)
                return;

            try
            {
                var currentNetwork = await GetCurrentNetworkName();
                if (string.IsNullOrEmpty(currentNetwork))
                    return;

                // Collect current metrics
                var metrics = await CollectConnectionMetrics(currentNetwork);

                // Update metrics history
                if (!_metricsHistory.ContainsKey(currentNetwork))
                {
                    _metricsHistory[currentNetwork] = new ConnectionMetrics { NetworkName = currentNetwork };
                }

                _metricsHistory[currentNetwork].AddSample(metrics);

                // Analyze stability
                var stabilityReport = AnalyzeStability(_metricsHistory[currentNetwork]);

                // Check if we need to switch networks
                if (stabilityReport.RequiresSwitch && CanSwitch())
                {
                    await ConsiderNetworkSwitch(stabilityReport);
                }

                // Update network profile
                UpdateNetworkProfile(currentNetwork, stabilityReport);
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Error during connection monitoring");
            }
        }

        private async Task<MetricsSample> CollectConnectionMetrics(string networkName)
        {
            var sample = new MetricsSample
            {
                Timestamp = DateTime.Now,
                NetworkName = networkName
            };

            try
            {
                // Get signal strength
                var signalInfo = await NetworkOperations.GetSignalStrengthAsync(networkName);
                sample.SignalStrength = signalInfo ?? 0;

                // Perform ping test for latency and packet loss
                using (var ping = new Ping())
                {
                    var replies = new List<PingReply>();
                    var hosts = new[] { "8.8.8.8", "1.1.1.1", "9.9.9.9" }; // Multiple DNS servers for reliability

                    foreach (var host in hosts)
                    {
                        try
                        {
                            var reply = await ping.SendPingAsync(host);
                            if (reply.Status == IPStatus.Success)
                            {
                                replies.Add(reply);
                            }
                        }
                        catch
                        {
                            // Ignore individual ping failures
                        }
                    }

                    if (replies.Any())
                    {
                        sample.Latency = (double)replies.Average(r => r.RoundtripTime);
                        sample.PacketLoss = ((hosts.Length - replies.Count) / (double)hosts.Length) * 100;

                        // Calculate jitter (variation in latency)
                        if (replies.Count > 1)
                        {
                            var latencies = replies.Select(r => (double)r.RoundtripTime).ToList();
                            var avgLatency = latencies.Average();
                            sample.Jitter = Math.Sqrt(latencies.Sum(l => Math.Pow(l - avgLatency, 2)) / latencies.Count);
                        }
                    }
                    else
                    {
                        sample.PacketLoss = 100;
                        sample.Latency = double.MaxValue;
                    }
                }

                // Measure bandwidth (simplified - in production would use more sophisticated methods)
                sample.Bandwidth = await MeasureBandwidth();

                // Check for connection drops
                sample.ConnectionDropped = !await IsConnected();
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Error collecting connection metrics");
            }

            return sample;
        }

        private async Task<double> MeasureBandwidth()
        {
            // Simplified bandwidth measurement
            // In production, this would download a test file or use more sophisticated methods
            try
            {
                var stopwatch = Stopwatch.StartNew();
                using (var ping = new Ping())
                {
                    var buffer = new byte[1024]; // 1KB test packet
                    var reply = await ping.SendPingAsync("8.8.8.8", 1000, buffer);
                    stopwatch.Stop();

                    if (reply.Status == IPStatus.Success)
                    {
                        // Very rough estimate: bytes per millisecond * 8 / 1000 = Mbps
                        return (buffer.Length * 8.0) / stopwatch.ElapsedMilliseconds / 1000.0;
                    }
                }
            }
            catch
            {
                // Ignore bandwidth measurement errors
            }

            return 0;
        }

        private StabilityReport AnalyzeStability(ConnectionMetrics metrics)
        {
            var report = new StabilityReport
            {
                NetworkName = metrics.NetworkName,
                Timestamp = DateTime.Now
            };

            if (metrics.Samples.Count < MinimumSampleSize)
            {
                report.HasSufficientData = false;
                return report;
            }

            report.HasSufficientData = true;

            // Calculate averages from recent samples
            var recentSamples = metrics.Samples.TakeLast(MinimumSampleSize).ToList();

            report.AverageLatency = recentSamples.Average(s => s.Latency);
            report.AveragePacketLoss = recentSamples.Average(s => s.PacketLoss);
            report.AverageJitter = recentSamples.Average(s => s.Jitter);
            report.AverageSignalStrength = recentSamples.Average(s => s.SignalStrength);
            report.ConnectionDropCount = recentSamples.Count(s => s.ConnectionDropped);

            // Calculate stability score (0-100)
            var latencyScore = Math.Max(0, 100 - (report.AverageLatency / LatencyThreshold * 50));
            var packetLossScore = Math.Max(0, 100 - (report.AveragePacketLoss / PacketLossThreshold * 100));
            var jitterScore = Math.Max(0, 100 - (report.AverageJitter / JitterThreshold * 50));
            var signalScore = report.AverageSignalStrength;
            var dropScore = Math.Max(0, 100 - (report.ConnectionDropCount * 20));

            report.StabilityScore = (int)((latencyScore + packetLossScore + jitterScore + signalScore + dropScore) / 5);

            // Determine if switch is needed
            report.RequiresSwitch = report.StabilityScore < 50 ||
                                   report.AveragePacketLoss > PacketLossThreshold ||
                                   report.AverageLatency > LatencyThreshold ||
                                   report.ConnectionDropCount > 2;

            // Generate recommendations
            if (report.AveragePacketLoss > PacketLossThreshold)
                report.Issues.Add($"High packet loss: {report.AveragePacketLoss:F1}%");

            if (report.AverageLatency > LatencyThreshold)
                report.Issues.Add($"High latency: {report.AverageLatency:F1}ms");

            if (report.AverageJitter > JitterThreshold)
                report.Issues.Add($"High jitter: {report.AverageJitter:F1}ms");

            if (report.AverageSignalStrength < SignalStrengthThreshold)
                report.Issues.Add($"Weak signal: {report.AverageSignalStrength}%");

            if (report.ConnectionDropCount > 0)
                report.Issues.Add($"Connection drops detected: {report.ConnectionDropCount}");

            return report;
        }

        private bool CanSwitch()
        {
            return (DateTime.Now - _lastSwitchTime).TotalSeconds >= MinimumSwitchInterval;
        }

        private async Task ConsiderNetworkSwitch(StabilityReport currentReport)
        {
            await _switchingSemaphore.WaitAsync();
            try
            {
                if (!CanSwitch())
                    return;

                // Network switching disabled (requires AdvancedScanner which was removed)
                await Logger.LogWarning("Automatic network switching is not available", nameof(ConnectionStabilityMonitor), new Dictionary<string, object>
                {
                    ["current_network"] = currentReport.NetworkName,
                    ["current_score"] = currentReport.StabilityScore,
                    ["issues"] = string.Join(", ", currentReport.Issues)
                });
            }
            finally
            {
                _switchingSemaphore.Release();
            }
        }


        private async Task<bool> AttemptNetworkSwitch(string targetNetwork)
        {
            try
            {
                await Logger.LogInfo($"Attempting to switch to network: {targetNetwork}", nameof(ConnectionStabilityMonitor));

                // Check if we have credentials
                var hasCredentials = await NetworkOperations.HasSavedProfileAsync(targetNetwork);
                if (!hasCredentials)
                {
                    await Logger.LogWarning($"No saved credentials for network: {targetNetwork}", nameof(ConnectionStabilityMonitor));
                    return false;
                }

                // Disconnect from current network
                await NetworkOperations.DisconnectAsync();
                await Task.Delay(2000); // Wait for disconnect

                // Connect to new network
                var connected = await NetworkOperations.ConnectToNetworkAsync(targetNetwork, null);

                if (connected)
                {
                    await Logger.LogInfo($"Successfully switched to network: {targetNetwork}", nameof(ConnectionStabilityMonitor));
                    await AuditTrail.RecordEventAsync("Network", "AutoSwitch", new Dictionary<string, object>
                    {
                        ["from"] = _currentNetwork,
                        ["to"] = targetNetwork,
                        ["reason"] = "Stability improvement"
                    });
                }

                return connected;
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, $"Failed to switch to network: {targetNetwork}");
                return false;
            }
        }

        private void UpdateNetworkProfile(string networkName, StabilityReport report)
        {
            if (!report.HasSufficientData)
                return;

            if (!_networkProfiles.ContainsKey(networkName))
            {
                _networkProfiles[networkName] = new NetworkProfile { NetworkName = networkName };
            }

            var profile = _networkProfiles[networkName];
            profile.LastUpdated = DateTime.Now;
            profile.HistoricalStabilityScore = (profile.HistoricalStabilityScore * 0.7) + (report.StabilityScore * 0.3);
            profile.TotalConnections++;

            if (report.RequiresSwitch)
                profile.FailureCount++;
        }

        private async Task<string> GetCurrentNetworkName()
        {
            try
            {
                var currentConnection = await NetworkOperations.GetCurrentConnectionAsync();
                return currentConnection?.Ssid;
            }
            catch
            {
                return null;
            }
        }

        private async Task<bool> IsConnected()
        {
            try
            {
                var currentConnection = await NetworkOperations.GetCurrentConnectionAsync();
                return currentConnection != null && !string.IsNullOrEmpty(currentConnection.Ssid);
            }
            catch
            {
                return false;
            }
        }

        public StabilityReport GetCurrentStabilityReport()
        {
            if (string.IsNullOrEmpty(_currentNetwork) || !_metricsHistory.ContainsKey(_currentNetwork))
                return null;

            return AnalyzeStability(_metricsHistory[_currentNetwork]);
        }

        public Dictionary<string, StabilityReport> GetAllStabilityReports()
        {
            var reports = new Dictionary<string, StabilityReport>();

            foreach (var kvp in _metricsHistory)
            {
                reports[kvp.Key] = AnalyzeStability(kvp.Value);
            }

            return reports;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _isMonitoring = false;
            _monitoringTimer?.Dispose();
            _switchingSemaphore?.Dispose();
        }

        private class ConnectionMetrics
        {
            public string NetworkName { get; set; }
            public Queue<MetricsSample> Samples { get; } = new Queue<MetricsSample>();
            private readonly object _lock = new object();

            public void AddSample(MetricsSample sample)
            {
                lock (_lock)
                {
                    Samples.Enqueue(sample);

                    // Keep only last 100 samples
                    while (Samples.Count > 100)
                    {
                        Samples.Dequeue();
                    }
                }
            }
        }

        private class MetricsSample
        {
            public DateTime Timestamp { get; set; }
            public string NetworkName { get; set; }
            public double Latency { get; set; }
            public double PacketLoss { get; set; }
            public double Jitter { get; set; }
            public double Bandwidth { get; set; }
            public int SignalStrength { get; set; }
            public bool ConnectionDropped { get; set; }
        }

        private class NetworkProfile
        {
            public string NetworkName { get; set; }
            public double HistoricalStabilityScore { get; set; } = 50;
            public int TotalConnections { get; set; }
            public int FailureCount { get; set; }
            public DateTime LastUpdated { get; set; }
        }

        public class StabilityReport
        {
            public string NetworkName { get; set; }
            public DateTime Timestamp { get; set; }
            public int StabilityScore { get; set; }
            public double AverageLatency { get; set; }
            public double AveragePacketLoss { get; set; }
            public double AverageJitter { get; set; }
            public double AverageSignalStrength { get; set; }
            public int ConnectionDropCount { get; set; }
            public bool RequiresSwitch { get; set; }
            public bool HasSufficientData { get; set; }
            public List<string> Issues { get; set; } = new List<string>();
        }
    }
}