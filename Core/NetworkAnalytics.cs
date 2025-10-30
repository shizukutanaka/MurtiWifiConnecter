using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    public static class NetworkAnalytics
    {
        private static readonly string DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MurtiWifiConnecter", "Analytics");

        private static readonly List<NetworkMeasurement> _measurements = new();
        private static DateTime _lastCleanup = DateTime.Now;

        public static async Task<NetworkQualityReport> GenerateQualityReport(string? ssid = null)
        {
            await LoadHistoricalData();

            var measurements = string.IsNullOrEmpty(ssid)
                ? _measurements
                : _measurements.Where(m => m.Ssid.Equals(ssid, StringComparison.OrdinalIgnoreCase)).ToList();

            if (measurements.Count == 0)
            {
                return new NetworkQualityReport
                {
                    Ssid = ssid ?? "All Networks",
                    GeneratedAt = DateTime.Now,
                    Message = "No data available"
                };
            }

            var report = new NetworkQualityReport
            {
                Ssid = ssid ?? "All Networks",
                GeneratedAt = DateTime.Now,
                TotalMeasurements = measurements.Count,
                TimeSpan = measurements.Count > 0 ? measurements.Max(m => m.Timestamp) - measurements.Min(m => m.Timestamp) : TimeSpan.Zero
            };

            // Signal strength analysis
            var signals = measurements.Where(m => m.SignalStrength > 0).Select(m => m.SignalStrength).ToList();
            if (signals.Count > 0)
            {
                report.AverageSignalStrength = signals.Average();
                report.MinSignalStrength = signals.Min();
                report.MaxSignalStrength = signals.Max();
                report.SignalStability = CalculateStability(signals);
            }

            // Connection analysis
            var connectionAttempts = measurements.Where(m => !string.IsNullOrEmpty(m.ConnectionResult)).ToList();
            if (connectionAttempts.Count > 0)
            {
                var successful = connectionAttempts.Count(m => m.ConnectionResult.Contains("success", StringComparison.OrdinalIgnoreCase));
                report.SuccessRate = (double)successful / connectionAttempts.Count * 100;
            }

            // Speed analysis
            var speeds = measurements.Where(m => m.DownloadSpeed > 0).Select(m => m.DownloadSpeed).ToList();
            if (speeds.Count > 0)
            {
                report.AverageDownloadSpeed = speeds.Average();
                report.MaxDownloadSpeed = speeds.Max();
            }

            // Latency analysis
            var latencies = measurements.Where(m => m.Latency > 0).Select(m => m.Latency).ToList();
            if (latencies.Count > 0)
            {
                report.AverageLatency = latencies.Average();
                report.MinLatency = latencies.Min();
                report.MaxLatency = latencies.Max();
            }

            // Peak usage times
            report.PeakUsageHours = measurements
                .GroupBy(m => m.Timestamp.Hour)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .Select(g => g.Key)
                .ToList();

            // Quality score calculation
            report.QualityScore = CalculateQualityScore(report);
            report.Recommendations = GenerateRecommendations(report);

            return report;
        }

        public static async Task RecordMeasurement(NetworkMeasurement measurement)
        {
            measurement.Timestamp = DateTime.Now;
            _measurements.Add(measurement);

            // Keep only recent measurements in memory
            var cutoff = DateTime.Now.AddDays(-7);
            _measurements.RemoveAll(m => m.Timestamp < cutoff);

            await SaveMeasurement(measurement);

            // Periodic cleanup
            if (DateTime.Now - _lastCleanup > TimeSpan.FromHours(1))
            {
                await CleanupOldData();
                _lastCleanup = DateTime.Now;
            }
        }

        public static async Task<List<NetworkTrend>> GetUsageTrends(TimeSpan period)
        {
            await LoadHistoricalData();

            var cutoff = DateTime.Now - period;
            var recent = _measurements.Where(m => m.Timestamp >= cutoff).ToList();

            var trends = recent
                .GroupBy(m => m.Ssid)
                .Select(g => new NetworkTrend
                {
                    Ssid = g.Key,
                    ConnectionCount = g.Count(),
                    TotalTime = g.Sum(m => (m.DisconnectedAt - m.ConnectedAt)?.TotalMinutes ?? 0),
                    AverageSignal = g.Where(m => m.SignalStrength > 0).Select(m => m.SignalStrength).DefaultIfEmpty(0).Average(),
                    LastUsed = g.Max(m => m.Timestamp)
                })
                .OrderByDescending(t => t.ConnectionCount)
                .ToList();

            return trends;
        }

        public static async Task<NetworkSpeedTest> RunSpeedTest(string ssid)
        {
            Console.WriteLine("Running speed test...");

            var speedTest = new NetworkSpeedTest
            {
                Ssid = ssid,
                StartTime = DateTime.Now
            };

            try
            {
                // Ping test
                using var ping = new Ping();
                var pingTimes = new List<long>();

                for (int i = 0; i < 5; i++)
                {
                    var reply = await ping.SendPingAsync("8.8.8.8", 5000);
                    if (reply.Status == IPStatus.Success)
                    {
                        pingTimes.Add(reply.RoundtripTime);
                    }
                    await Task.Delay(100);
                }

                if (pingTimes.Count > 0)
                {
                    speedTest.AverageLatency = pingTimes.Average();
                    speedTest.MinLatency = pingTimes.Min();
                    speedTest.MaxLatency = pingTimes.Max();
                    speedTest.PacketLoss = (5 - pingTimes.Count) / 5.0 * 100;
                }

                // DNS resolution test
                var dnsStart = DateTime.Now;
                await System.Net.Dns.GetHostAddressesAsync("google.com");
                speedTest.DnsResolutionTime = (DateTime.Now - dnsStart).TotalMilliseconds;

                speedTest.EndTime = DateTime.Now;
                speedTest.TestDuration = speedTest.EndTime - speedTest.StartTime;

                // Record the measurement
                var measurement = new NetworkMeasurement
                {
                    Ssid = ssid,
                    Latency = speedTest.AverageLatency,
                    ConnectionResult = "Speed test completed"
                };

                await RecordMeasurement(measurement);
            }
            catch (Exception ex)
            {
                speedTest.Error = ex.Message;
                await ErrorHandler.LogError(ex, "Speed test failed");
            }

            return speedTest;
        }

        public static async Task<SignalQualityAnalysis> AnalyzeSignalQualityAsync(string ssid = null, TimeSpan analysisPeriod = default)
        {
            if (analysisPeriod == default)
                analysisPeriod = TimeSpan.FromMinutes(5);

            await LoadHistoricalData();

            var cutoff = DateTime.Now - analysisPeriod;
            var relevantMeasurements = _measurements.Where(m => m.Timestamp >= cutoff).ToList();

            if (!string.IsNullOrEmpty(ssid))
            {
                relevantMeasurements = relevantMeasurements.Where(m => m.Ssid.Equals(ssid, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            var analysis = new SignalQualityAnalysis
            {
                Ssid = ssid ?? "All Networks",
                AnalysisPeriod = analysisPeriod,
                GeneratedAt = DateTime.Now,
                TotalMeasurements = relevantMeasurements.Count
            };

            if (!relevantMeasurements.Any())
            {
                analysis.Message = "指定された期間内に測定データが見つかりません。";
                return analysis;
            }

            // Signal strength analysis
            var signalStrengths = relevantMeasurements.Where(m => m.SignalStrength > 0).Select(m => m.SignalStrength).ToList();
            if (signalStrengths.Any())
            {
                analysis.AverageSignalStrength = signalStrengths.Average();
                analysis.MinSignalStrength = signalStrengths.Min();
                analysis.MaxSignalStrength = signalStrengths.Max();
                analysis.SignalStability = CalculateStability(signalStrengths);
            }

            // SNR estimation (approximated from signal strength and noise floor)
            analysis.EstimatedSNR = EstimateSNR(signalStrengths);

            // Interference analysis
            analysis.InterferenceLevel = AnalyzeInterference(signalStrengths, relevantMeasurements);

            // Channel analysis
            analysis.ChannelUtilization = AnalyzeChannelUtilization(relevantMeasurements);

            // Generate heat map data
            analysis.HeatMapData = GenerateHeatMapData(relevantMeasurements);

            // Generate recommendations
            analysis.Recommendations = GenerateSignalRecommendations(analysis);

            return analysis;
        }

        private static double EstimateSNR(List<int> signalStrengths)
        {
            if (!signalStrengths.Any()) return 0;

            // Estimate SNR based on signal strength distribution
            // Typical noise floor is around -90 to -100 dBm
            const int typicalNoiseFloor = -95;

            var avgSignal = signalStrengths.Average();
            var snr = avgSignal - typicalNoiseFloor;

            // Ensure reasonable bounds
            return Math.Max(0, Math.Min(60, snr));
        }

        private static InterferenceLevel AnalyzeInterference(List<int> signalStrengths, List<NetworkMeasurement> measurements)
        {
            if (!signalStrengths.Any()) return InterferenceLevel.Unknown;

            var avgSignal = signalStrengths.Average();
            var signalVariance = signalStrengths.Select(s => Math.Pow(s - avgSignal, 2)).Average();
            var signalStdDev = Math.Sqrt(signalVariance);

            // Analyze signal fluctuations (potential interference indicators)
            var fluctuationRate = signalStdDev / Math.Max(avgSignal, 1);

            // Count signal drops (potential interference events)
            var signalDrops = 0;
            for (int i = 1; i < signalStrengths.Count; i++)
            {
                if (signalStrengths[i] < signalStrengths[i - 1] - 10) // 10dB drop
                {
                    signalDrops++;
                }
            }

            var dropRate = (double)signalDrops / signalStrengths.Count;

            // Determine interference level
            if (fluctuationRate > 0.3 || dropRate > 0.2)
                return InterferenceLevel.High;
            else if (fluctuationRate > 0.15 || dropRate > 0.1)
                return InterferenceLevel.Medium;
            else
                return InterferenceLevel.Low;
        }

        private static Dictionary<int, double> AnalyzeChannelUtilization(List<NetworkMeasurement> measurements)
        {
            // This is a simplified channel analysis
            // In a real implementation, this would require access to wireless driver APIs
            var channelUsage = new Dictionary<int, double>();

            // Common WiFi channels
            for (int channel = 1; channel <= 14; channel++)
            {
                // Simulate channel utilization based on measurement density
                // In practice, this would use native WiFi APIs to get actual channel utilization
                var channelMeasurements = measurements.Where(m =>
                    // Simulate channel assignment based on SSID hash (simplified)
                    Math.Abs(m.Ssid.GetHashCode()) % 14 + 1 == channel).ToList();

                var utilization = Math.Min(1.0, channelMeasurements.Count / 10.0); // Normalize
                channelUsage[channel] = utilization;
            }

            return channelUsage;
        }

        private static List<HeatMapPoint> GenerateHeatMapData(List<NetworkMeasurement> measurements)
        {
            var heatMapData = new List<HeatMapPoint>();

            // Group measurements by time windows for temporal heat map
            var timeWindows = measurements
                .GroupBy(m => m.Timestamp.Hour)
                .OrderBy(g => g.Key);

            foreach (var window in timeWindows)
            {
                var avgSignal = window.Where(m => m.SignalStrength > 0).Select(m => m.SignalStrength).DefaultIfEmpty(0).Average();
                var measurementCount = window.Count();

                heatMapData.Add(new HeatMapPoint
                {
                    TimeSlot = window.Key,
                    AverageSignalStrength = avgSignal,
                    MeasurementCount = measurementCount,
                    QualityScore = avgSignal > 0 ? Math.Min(100, avgSignal) : 0
                });
            }

            return heatMapData.OrderBy(p => p.TimeSlot).ToList();
        }

        private static List<string> GenerateSignalRecommendations(SignalQualityAnalysis analysis)
        {
            var recommendations = new List<string>();

            // Signal strength recommendations
            if (analysis.AverageSignalStrength < 30)
            {
                recommendations.Add("信号強度が非常に弱いです。アクセスポイントに近づくか、WiFiエクステンダーを検討してください。");
            }
            else if (analysis.AverageSignalStrength < 50)
            {
                recommendations.Add("信号強度が中程度です。障害物がないか確認してください。");
            }

            // SNR recommendations
            if (analysis.EstimatedSNR < 20)
            {
                recommendations.Add("SNRが低すぎます。ノイズ源を特定して除去してください。");
            }
            else if (analysis.EstimatedSNR < 30)
            {
                recommendations.Add("SNRが最適値より低いです。チャンネルを変更することを検討してください。");
            }

            // Interference recommendations
            switch (analysis.InterferenceLevel)
            {
                case InterferenceLevel.High:
                    recommendations.Add("干渉レベルが高いです。チャンネルを変更するか、他のWiFiネットワークとの競合を避けてください。");
                    recommendations.Add("2.4GHz帯から5GHz帯への変更を検討してください。");
                    break;
                case InterferenceLevel.Medium:
                    recommendations.Add("中程度の干渉が検出されました。チャンネルを変更することを検討してください。");
                    break;
            }

            // Channel utilization recommendations
            var crowdedChannels = analysis.ChannelUtilization.Where(kv => kv.Value > 0.7).Select(kv => kv.Key).ToList();
            if (crowdedChannels.Any())
            {
                recommendations.Add($"チャンネル {string.Join(", ", crowdedChannels)} が混雑しています。空いているチャンネルへの変更を検討してください。");
            }

            // Stability recommendations
            if (analysis.SignalStability < 70)
            {
                recommendations.Add("信号が不安定です。干渉源や接続の問題を確認してください。");
            }

            if (!recommendations.Any())
            {
                recommendations.Add("信号品質は良好です。定期的な監視を継続してください。");
            }

            return recommendations;
        }

        public static async Task<List<SecurityAlert>> AnalyzeSecurity()
            var alerts = new List<SecurityAlert>();
            var profiles = await NetworkOperations.GetSavedProfilesAsync();

            foreach (var profileName in profiles)
            {
                try
                {
                    var output = ExecuteNetshCommand($"wlan show profile name=\"{profileName}\" key=clear");
                    var analysis = AnalyzeProfileSecurity(profileName, output);
                    alerts.AddRange(analysis);
                }
                catch (Exception ex)
                {
                    await ErrorHandler.LogError(ex, $"Security analysis failed for {profileName}");
                }
            }

            return alerts;
        }

        private static List<SecurityAlert> AnalyzeProfileSecurity(string profileName, string profileData)
        {
            var alerts = new List<SecurityAlert>();
            var lines = profileData.Split('\n');

            var auth = "";
            var cipher = "";

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("Authentication"))
                {
                    auth = trimmed.Split(':').Last().Trim();
                }
                else if (trimmed.StartsWith("Cipher"))
                {
                    cipher = trimmed.Split(':').Last().Trim();
                }
                else if (trimmed.Contains("Security key") && trimmed.Contains("Present"))
                {
                    // Security key is present - password protected
                }
            }

            // Enhanced WPA3 support with detailed analysis
            if (auth.Equals("Open", StringComparison.OrdinalIgnoreCase))
            {
                alerts.Add(new SecurityAlert
                {
                    Level = AlertLevel.High,
                    NetworkName = profileName,
                    Issue = "Open network (no encryption)",
                    Recommendation = "Avoid using open networks for sensitive activities. Consider WPA3-Personal for home networks or WPA3-Enterprise for business environments."
                });
            }
            else if (auth.Contains("WEP", StringComparison.OrdinalIgnoreCase))
            {
                alerts.Add(new SecurityAlert
                {
                    Level = AlertLevel.High,
                    NetworkName = profileName,
                    Issue = "WEP encryption (deprecated and insecure)",
                    Recommendation = "Immediately upgrade to WPA3-Personal or WPA3-Enterprise. WEP has been deprecated since 2004 and can be cracked in minutes."
                });
            }
            else if (auth.Contains("WPA") && !auth.Contains("WPA2") && !auth.Contains("WPA3"))
            {
                alerts.Add(new SecurityAlert
                {
                    Level = AlertLevel.Medium,
                    NetworkName = profileName,
                    Issue = "WPA encryption (upgrade recommended)",
                    Recommendation = "Upgrade to WPA2 or WPA3 for better security. WPA3 offers enhanced protection against brute-force attacks and forward secrecy."
                });
            }
            else if (auth.Contains("WPA3", StringComparison.OrdinalIgnoreCase))
            {
                // WPA3 detected - provide positive feedback and check for specific features
                if (auth.Contains("WPA3-Personal", StringComparison.OrdinalIgnoreCase))
                {
                    alerts.Add(new SecurityAlert
                    {
                        Level = AlertLevel.Low,
                        NetworkName = profileName,
                        Issue = "WPA3-Personal detected (excellent security)",
                        Recommendation = "Excellent! WPA3-Personal provides robust protection for home and small office networks with enhanced resistance to password cracking."
                    });
                }
                else if (auth.Contains("WPA3-Enterprise", StringComparison.OrdinalIgnoreCase))
                {
                    alerts.Add(new SecurityAlert
                    {
                        Level = AlertLevel.Low,
                        NetworkName = profileName,
                        Issue = "WPA3-Enterprise detected (enterprise-grade security)",
                        Recommendation = "Excellent! WPA3-Enterprise provides the highest level of security with 192-bit encryption and Protected Management Frames for enterprise environments."
                    });
                }
                else
                {
                    alerts.Add(new SecurityAlert
                    {
                        Level = AlertLevel.Low,
                        NetworkName = profileName,
                        Issue = "WPA3 encryption detected (strong security)",
                        Recommendation = "Good! WPA3 provides modern encryption with enhanced security features. Consider WPA3-Enterprise for sensitive business networks."
                    });
                }
            }
            else if (auth.Contains("WPA2", StringComparison.OrdinalIgnoreCase))
            {
                if (auth.Contains("WPA2-Enterprise", StringComparison.OrdinalIgnoreCase))
                {
                    alerts.Add(new SecurityAlert
                    {
                        Level = AlertLevel.Low,
                        NetworkName = profileName,
                        Issue = "WPA2-Enterprise detected (good security)",
                        Recommendation = "Good security level. Consider upgrading to WPA3-Enterprise when possible for enhanced protection and future-proofing."
                    });
                }
                else
                {
                    alerts.Add(new SecurityAlert
                    {
                        Level = AlertLevel.Medium,
                        NetworkName = profileName,
                        Issue = "WPA2-Personal detected (adequate but upgradeable)",
                        Recommendation = "Adequate for most uses. Upgrade to WPA3-Personal when your devices support it for improved security against modern threats."
                    });
                }
            }

            // Additional WPA3-specific checks
            if (auth.Contains("WPA3", StringComparison.OrdinalIgnoreCase))
            {
                // Check for weak cipher with WPA3 (should use stronger ciphers)
                if (cipher.Contains("TKIP", StringComparison.OrdinalIgnoreCase))
                {
                    alerts.Add(new SecurityAlert
                    {
                        Level = AlertLevel.Medium,
                        NetworkName = profileName,
                        Issue = "TKIP cipher detected with WPA3 (suboptimal)",
                        Recommendation = "WPA3 should use AES or GCMP ciphers. TKIP is deprecated and reduces the security benefits of WPA3."
                    });
                }
                else if (cipher.Contains("AES", StringComparison.OrdinalIgnoreCase) ||
                         cipher.Contains("GCMP", StringComparison.OrdinalIgnoreCase))
                {
                    // Good cipher for WPA3
                }
                else
                {
                    alerts.Add(new SecurityAlert
                    {
                        Level = AlertLevel.Low,
                        NetworkName = profileName,
                        Issue = "Cipher verification needed",
                        Recommendation = "Ensure WPA3 is using AES or GCMP cipher for optimal security. Check router configuration."
                    });
                }
            }

            return alerts;
        }

        private static double CalculateStability(List<int> signals)
        {
            if (signals.Count < 2) return 100.0;

            var avg = signals.Average();
            var variance = signals.Select(s => Math.Pow(s - avg, 2)).Average();
            var stdDev = Math.Sqrt(variance);

            // Convert to stability percentage (lower std dev = higher stability)
            return Math.Max(0, 100 - (stdDev / avg * 100));
        }

        private static double CalculateQualityScore(NetworkQualityReport report)
        {
            var score = 0.0;
            var factors = 0;

            // Signal strength factor (40% weight)
            if (report.AverageSignalStrength > 0)
            {
                score += (report.AverageSignalStrength / 100.0) * 40;
                factors++;
            }

            // Success rate factor (30% weight)
            if (report.SuccessRate >= 0)
            {
                score += (report.SuccessRate / 100.0) * 30;
                factors++;
            }

            // Stability factor (20% weight)
            if (report.SignalStability > 0)
            {
                score += (report.SignalStability / 100.0) * 20;
                factors++;
            }

            // Latency factor (10% weight) - lower is better
            if (report.AverageLatency > 0)
            {
                var latencyScore = Math.Max(0, 1 - (report.AverageLatency / 200.0)); // 200ms = poor
                score += latencyScore * 10;
                factors++;
            }

            return factors > 0 ? score : 0;
        }

        private static List<string> GenerateRecommendations(NetworkQualityReport report)
        {
            var recommendations = new List<string>();

            if (report.AverageSignalStrength < 30)
            {
                recommendations.Add("Signal strength is weak. Consider moving closer to the router or using a WiFi extender.");
            }
            else if (report.AverageSignalStrength < 50)
            {
                recommendations.Add("Signal strength is moderate. Check for obstacles between device and router.");
            }

            if (report.SuccessRate < 80)
            {
                recommendations.Add("Connection success rate is low. Check network credentials and router settings.");
            }

            if (report.SignalStability < 70)
            {
                recommendations.Add("Signal is unstable. Check for interference from other devices.");
            }

            if (report.AverageLatency > 100)
            {
                recommendations.Add("High latency detected. Check internet connection and router performance.");
            }

            if (report.QualityScore > 80)
            {
                recommendations.Add("Network quality is excellent! No issues detected.");
            }
            else if (report.QualityScore < 50)
            {
                recommendations.Add("Network quality is poor. Consider troubleshooting or using a different network.");
            }

            return recommendations;
        }

        private static async Task LoadHistoricalData()
        {
            try
            {
                if (!Directory.Exists(DataDirectory))
                    return;

                var files = Directory.GetFiles(DataDirectory, "measurements_*.json")
                    .OrderByDescending(f => new FileInfo(f).CreationTime)
                    .Take(7); // Last 7 days

                foreach (var file in files)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(file);
                        var dailyMeasurements = JsonSerializer.Deserialize<List<NetworkMeasurement>>(json, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        });

                        if (dailyMeasurements != null)
                        {
                            _measurements.AddRange(dailyMeasurements.Where(m => !_measurements.Any(existing =>
                                existing.Timestamp == m.Timestamp && existing.Ssid == m.Ssid)));
                        }
                    }
                    catch
                    {
                        // Skip corrupted files
                    }
                }
            }
            catch
            {
                // Ignore load errors
            }
        }

        private static async Task SaveMeasurement(NetworkMeasurement measurement)
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);

                var fileName = $"measurements_{DateTime.Now:yyyy-MM-dd}.json";
                var filePath = Path.Combine(DataDirectory, fileName);

                List<NetworkMeasurement> dailyMeasurements;

                if (File.Exists(filePath))
                {
                    var json = await File.ReadAllTextAsync(filePath);
                    dailyMeasurements = JsonSerializer.Deserialize<List<NetworkMeasurement>>(json, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    }) ?? new List<NetworkMeasurement>();
                }
                else
                {
                    dailyMeasurements = new List<NetworkMeasurement>();
                }

                dailyMeasurements.Add(measurement);

                var updatedJson = JsonSerializer.Serialize(dailyMeasurements, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await File.WriteAllTextAsync(filePath, updatedJson);
            }
            catch
            {
                // Ignore save errors
            }
        }

        private static async Task CleanupOldData()
        {
            try
            {
                if (!Directory.Exists(DataDirectory))
                    return;

                var cutoffDate = DateTime.Now.AddDays(-30);
                var files = Directory.GetFiles(DataDirectory, "measurements_*.json");

                foreach (var file in files)
                {
                    if (File.GetCreationTime(file) < cutoffDate)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        private static string ExecuteNetshCommand(string arguments)
        {
            try
            {
                using var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);
                return output;
            }
            catch
            {
                return string.Empty;
            }
        }

        public class NetworkMeasurement
        {
            public DateTime Timestamp { get; set; }
            public string Ssid { get; set; }
            public int SignalStrength { get; set; }
            public double DownloadSpeed { get; set; }
            public double UploadSpeed { get; set; }
            public double Latency { get; set; }
            public string ConnectionResult { get; set; }
            public DateTime? ConnectedAt { get; set; }
            public DateTime? DisconnectedAt { get; set; }
        }

        public class NetworkQualityReport
        {
            public string Ssid { get; set; }
            public DateTime GeneratedAt { get; set; }
            public int TotalMeasurements { get; set; }
            public TimeSpan TimeSpan { get; set; }
            public double AverageSignalStrength { get; set; }
            public int MinSignalStrength { get; set; }
            public int MaxSignalStrength { get; set; }
            public double SignalStability { get; set; }
            public double SuccessRate { get; set; }
            public double AverageDownloadSpeed { get; set; }
            public double MaxDownloadSpeed { get; set; }
            public double AverageLatency { get; set; }
            public double MinLatency { get; set; }
            public double MaxLatency { get; set; }
            public List<int> PeakUsageHours { get; set; } = new();
            public double QualityScore { get; set; }
            public List<string> Recommendations { get; set; } = new();
            public string Message { get; set; }
        }

        public class NetworkTrend
        {
            public string Ssid { get; set; }
            public int ConnectionCount { get; set; }
            public double TotalTime { get; set; }
            public double AverageSignal { get; set; }
            public DateTime LastUsed { get; set; }
        }

        public class NetworkSpeedTest
        {
            public string Ssid { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public TimeSpan TestDuration { get; set; }
            public double AverageLatency { get; set; }
            public double MinLatency { get; set; }
            public double MaxLatency { get; set; }
            public double PacketLoss { get; set; }
            public double DnsResolutionTime { get; set; }
            public string Error { get; set; }
        }

        public class SecurityAlert
        {
            public AlertLevel Level { get; set; }
            public string NetworkName { get; set; }
            public string Issue { get; set; }
            public string Recommendation { get; set; }
            public DateTime DetectedAt { get; set; } = DateTime.Now;
        }

        public enum AlertLevel
        {
            Low,
            Medium,
            High,
            Critical
        }

        public enum InterferenceLevel
        {
            Low,
            Medium,
            High,
            Unknown
        }

        public class SignalQualityAnalysis
        {
            public string Ssid { get; set; }
            public TimeSpan AnalysisPeriod { get; set; }
            public DateTime GeneratedAt { get; set; }
            public int TotalMeasurements { get; set; }
            public double AverageSignalStrength { get; set; }
            public int MinSignalStrength { get; set; }
            public int MaxSignalStrength { get; set; }
            public double SignalStability { get; set; }
            public double EstimatedSNR { get; set; }
            public InterferenceLevel InterferenceLevel { get; set; }
            public Dictionary<int, double> ChannelUtilization { get; set; } = new();
            public List<HeatMapPoint> HeatMapData { get; set; } = new();
            public List<string> Recommendations { get; set; } = new();
            public string Message { get; set; }
        }

        public class HeatMapPoint
        {
            public int TimeSlot { get; set; }
            public double AverageSignalStrength { get; set; }
            public int MeasurementCount { get; set; }
            public double QualityScore { get; set; }
        }
    }
}