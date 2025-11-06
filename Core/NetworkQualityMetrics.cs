using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// Network quality metrics analyzer
    /// Measures latency, jitter, and packet loss for WiFi connections
    /// Based on 2024-2025 network performance standards
    /// </summary>
    public static class NetworkQualityMetrics
    {
        /// <summary>
        /// Test network quality with multiple ping attempts
        /// </summary>
        public static async Task<QualityReport> MeasureNetworkQuality(string target = "8.8.8.8", int pingCount = 20)
        {
            var report = new QualityReport
            {
                Target = target,
                Timestamp = DateTime.UtcNow,
                PingCount = pingCount
            };

            try
            {
                var latencies = new List<double>();
                using (var ping = new Ping())
                {
                    for (int i = 0; i < pingCount; i++)
                    {
                        try
                        {
                            var reply = await ping.SendPingAsync(target, 5000);
                            if (reply.Status == IPStatus.Success)
                            {
                                latencies.Add(reply.RoundtripTime);
                            }
                            else
                            {
                                report.PacketsLost++;
                            }
                        }
                        catch
                        {
                            report.PacketsLost++;
                        }

                        // Small delay between pings to avoid network flooding
                        if (i < pingCount - 1)
                            await Task.Delay(100);
                    }
                }

                if (latencies.Count == 0)
                {
                    report.Error = "All packets lost - network unreachable";
                    return report;
                }

                // Calculate metrics
                report.SuccessfulPings = latencies.Count;
                report.PacketLossPercent = (double)report.PacketsLost / pingCount * 100;
                report.MinLatencyMs = latencies.Min();
                report.MaxLatencyMs = latencies.Max();
                report.AvgLatencyMs = latencies.Average();

                // Calculate jitter (standard deviation of latency)
                var variance = latencies.Select(x => Math.Pow(x - report.AvgLatencyMs, 2)).Average();
                report.JitterMs = Math.Sqrt(variance);

                report.Success = true;

                return report;
            }
            catch (Exception ex)
            {
                report.Error = $"Quality test failed: {ex.Message}";
                return report;
            }
        }

        /// <summary>
        /// Evaluate connection quality based on metrics
        /// </summary>
        public static QualityRating EvaluateQuality(QualityReport report)
        {
            var rating = new QualityRating();

            if (!report.Success)
            {
                rating.Overall = "Failed";
                rating.OverallScore = 0;
                return rating;
            }

            // Evaluate latency
            rating.LatencyRating = report.AvgLatencyMs switch
            {
                <= 10 => "Excellent",
                <= 25 => "Very Good",
                <= 50 => "Good",
                <= 100 => "Fair",
                <= 150 => "Poor",
                _ => "Very Poor"
            };

            var latencyScore = report.AvgLatencyMs switch
            {
                <= 10 => 100,
                <= 25 => 90,
                <= 50 => 80,
                <= 100 => 60,
                <= 150 => 40,
                _ => 20
            };

            // Evaluate jitter
            rating.JitterRating = report.JitterMs switch
            {
                < 5 => "Excellent",
                < 10 => "Very Good",
                < 20 => "Good",
                < 30 => "Fair",
                < 50 => "Poor",
                _ => "Very Poor"
            };

            var jitterScore = report.JitterMs switch
            {
                < 5 => 100,
                < 10 => 90,
                < 20 => 80,
                < 30 => 60,
                < 50 => 40,
                _ => 20
            };

            // Evaluate packet loss
            rating.PacketLossRating = report.PacketLossPercent switch
            {
                0 => "Excellent",
                < 0.5 => "Very Good",
                < 1.0 => "Good",
                < 2.0 => "Fair",
                < 5.0 => "Poor",
                _ => "Very Poor"
            };

            var packetLossScore = report.PacketLossPercent switch
            {
                0 => 100,
                < 0.5 => 90,
                < 1.0 => 80,
                < 2.0 => 60,
                < 5.0 => 40,
                _ => 20
            };

            // Calculate overall score
            rating.OverallScore = (int)((latencyScore * 0.4 + jitterScore * 0.3 + packetLossScore * 0.3));

            rating.Overall = rating.OverallScore switch
            {
                >= 90 => "Excellent",
                >= 80 => "Very Good",
                >= 70 => "Good",
                >= 50 => "Fair",
                >= 30 => "Poor",
                _ => "Very Poor"
            };

            // Provide recommendations
            rating.Recommendations = GenerateRecommendations(report, rating);

            return rating;
        }

        /// <summary>
        /// Generate improvement recommendations
        /// </summary>
        private static List<string> GenerateRecommendations(QualityReport report, QualityRating rating)
        {
            var recommendations = new List<string>();

            if (report.AvgLatencyMs > 50)
                recommendations.Add("High latency detected: Move closer to router or reduce network congestion");

            if (report.JitterMs > 20)
                recommendations.Add("High jitter detected: Check for interference, reduce distance from AP");

            if (report.PacketLossPercent > 0.5)
                recommendations.Add("Packet loss detected: Improve signal strength or reduce interference");

            if (report.AvgLatencyMs > 100 && report.JitterMs > 30)
                recommendations.Add("Consider switching to 5GHz band for better performance");

            if (recommendations.Count == 0)
                recommendations.Add("Network quality is good - no immediate improvements needed");

            return recommendations;
        }

        /// <summary>
        /// Test DNS resolution quality
        /// </summary>
        public static async Task<DNSQualityReport> TestDNSQuality(string[] dnsServers, int queryCount = 5)
        {
            var report = new DNSQualityReport
            {
                Timestamp = DateTime.UtcNow,
                QueryCount = queryCount
            };

            foreach (var dnsServer in dnsServers)
            {
                var serverReport = new DNSServerReport { Server = dnsServer };
                var latencies = new List<double>();

                for (int i = 0; i < queryCount; i++)
                {
                    try
                    {
                        var sw = Stopwatch.StartNew();
                        var addresses = await Dns.GetHostAddressesAsync("google.com");
                        sw.Stop();

                        if (addresses.Length > 0)
                            latencies.Add(sw.ElapsedMilliseconds);
                        else
                            serverReport.FailedQueries++;
                    }
                    catch
                    {
                        serverReport.FailedQueries++;
                    }

                    if (i < queryCount - 1)
                        await Task.Delay(50);
                }

                if (latencies.Count > 0)
                {
                    serverReport.AvgLatencyMs = latencies.Average();
                    serverReport.MinLatencyMs = latencies.Min();
                    serverReport.MaxLatencyMs = latencies.Max();
                    serverReport.Success = true;
                }

                report.ServerReports.Add(serverReport);
            }

            return report;
        }

        /// <summary>
        /// Get recommendations for network use cases
        /// </summary>
        public static string GetUseCaseRecommendation(QualityRating rating)
        {
            return rating.OverallScore switch
            {
                >= 90 => "✓ Excellent for: Video calls, gaming, streaming HD, VoIP",
                >= 80 => "✓ Good for: Video calls, streaming, general browsing",
                >= 70 => "✓ Fair for: General browsing, email, social media",
                >= 50 => "⚠ Limited for: Some video streaming may buffer, avoid real-time applications",
                >= 30 => "✗ Poor for: Real-time applications, gaming, video calls",
                _ => "✗ Not suitable for: Any demanding network applications"
            };
        }
    }

    /// <summary>
    /// Network quality measurement report
    /// </summary>
    public class QualityReport
    {
        public string Target { get; set; } = "8.8.8.8";
        public DateTime Timestamp { get; set; }
        public int PingCount { get; set; }
        public int SuccessfulPings { get; set; }
        public int PacketsLost { get; set; }
        public double PacketLossPercent { get; set; }
        public double MinLatencyMs { get; set; }
        public double MaxLatencyMs { get; set; }
        public double AvgLatencyMs { get; set; }
        public double JitterMs { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }

        /// <summary>
        /// Format report for console display
        /// </summary>
        public override string ToString()
        {
            if (!Success)
                return $"Quality Test Failed: {Error}";

            return $"Network Quality Test Results (Target: {Target})\n" +
                   $"Pings: {SuccessfulPings}/{PingCount} successful\n" +
                   $"Packet Loss: {PacketLossPercent:F2}%\n" +
                   $"Latency: {AvgLatencyMs:F1}ms (min: {MinLatencyMs:F1}ms, max: {MaxLatencyMs:F1}ms)\n" +
                   $"Jitter: {JitterMs:F1}ms";
        }
    }

    /// <summary>
    /// Quality rating and evaluation
    /// </summary>
    public class QualityRating
    {
        public string LatencyRating { get; set; } = "Unknown";
        public string JitterRating { get; set; } = "Unknown";
        public string PacketLossRating { get; set; } = "Unknown";
        public string Overall { get; set; } = "Unknown";
        public int OverallScore { get; set; }
        public List<string> Recommendations { get; set; } = new();

        /// <summary>
        /// Format rating for console display
        /// </summary>
        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("\n=== Network Quality Rating ===");
            sb.AppendLine($"Overall: {Overall} ({OverallScore}/100)");
            sb.AppendLine($"Latency: {LatencyRating}");
            sb.AppendLine($"Jitter: {JitterRating}");
            sb.AppendLine($"Packet Loss: {PacketLossRating}");

            if (Recommendations.Count > 0)
            {
                sb.AppendLine("\nRecommendations:");
                foreach (var rec in Recommendations)
                    sb.AppendLine($"  • {rec}");
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// DNS quality report
    /// </summary>
    public class DNSQualityReport
    {
        public DateTime Timestamp { get; set; }
        public int QueryCount { get; set; }
        public List<DNSServerReport> ServerReports { get; set; } = new();

        /// <summary>
        /// Format DNS report for display
        /// </summary>
        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("\n=== DNS Quality Report ===");

            foreach (var server in ServerReports)
            {
                sb.AppendLine($"Server: {server.Server}");
                if (server.Success)
                {
                    sb.AppendLine($"  Avg Latency: {server.AvgLatencyMs:F1}ms");
                    sb.AppendLine($"  Range: {server.MinLatencyMs:F0}-{server.MaxLatencyMs:F0}ms");
                    sb.AppendLine($"  Failed: {server.FailedQueries}/{QueryCount}");
                }
                else
                {
                    sb.AppendLine($"  Status: Failed");
                }
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// DNS server quality metrics
    /// </summary>
    public class DNSServerReport
    {
        public string Server { get; set; } = "Unknown";
        public double AvgLatencyMs { get; set; }
        public double MinLatencyMs { get; set; }
        public double MaxLatencyMs { get; set; }
        public int FailedQueries { get; set; }
        public bool Success { get; set; }
    }
}
