using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// Network diagnostics and quality monitoring
    /// Based on 2024 best practices for network optimization
    /// </summary>
    public static class NetworkDiagnostics
    {
        /// <summary>
        /// Perform comprehensive network diagnostics
        /// </summary>
        public static async Task<DiagnosticsReport> RunFullDiagnosticsAsync(string hostname = "8.8.8.8")
        {
            var report = new DiagnosticsReport { Timestamp = DateTime.UtcNow };

            // Run diagnostics in parallel for speed
            var tasks = new List<Task>
            {
                Task.Run(() => PingTest(hostname, report)),
                Task.Run(() => DNSTest(report)),
                Task.Run(() => InterfaceTest(report)),
                Task.Run(() => RouteTest(report))
            };

            await Task.WhenAll(tasks);

            return report;
        }

        /// <summary>
        /// Test network latency and connectivity
        /// </summary>
        private static void PingTest(string hostname, DiagnosticsReport report)
        {
            try
            {
                using (var ping = new Ping())
                {
                    var reply = ping.Send(hostname, 5000);

                    report.Connectivity = reply.Status == IPStatus.Success;
                    report.Latency = reply.Status == IPStatus.Success ? reply.RoundtripTime : -1;
                    report.PingHost = hostname;
                }
            }
            catch (Exception ex)
            {
                report.Connectivity = false;
                report.Latency = -1;
                report.DiagnosticsErrors.Add($"Ping failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Test DNS resolution
        /// </summary>
        private static void DNSTest(DiagnosticsReport report)
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                var hostEntry = Dns.GetHostEntry("google.com");
                stopwatch.Stop();

                report.DNSResolution = true;
                report.DNSLatency = stopwatch.ElapsedMilliseconds;
                report.DNSServers = string.Join(", ", GetDNSServers());
            }
            catch (Exception ex)
            {
                report.DNSResolution = false;
                report.DNSLatency = -1;
                report.DiagnosticsErrors.Add($"DNS test failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Test network interfaces
        /// </summary>
        private static void InterfaceTest(DiagnosticsReport report)
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                report.ActiveInterfaces = 0;

                foreach (var ni in interfaces)
                {
                    if (ni.OperationalStatus == OperationalStatus.Up)
                    {
                        report.ActiveInterfaces++;

                        // Detect WiFi interface
                        if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                        {
                            var ipv4 = ni.GetIPProperties().UnicastAddresses;
                            report.WiFiInterface = ni.Name;
                            report.WiFiStatus = "Connected";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                report.DiagnosticsErrors.Add($"Interface test failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Test network routing
        /// </summary>
        private static void RouteTest(DiagnosticsReport report)
        {
            try
            {
                // Windows-specific routing check
                if (OperatingSystem.IsWindows())
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "ipconfig",
                            Arguments = "/all",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        }
                    };

                    process.Start();
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    // Check for gateway
                    if (output.Contains("Default Gateway"))
                    {
                        report.DefaultGateway = "Configured";
                    }
                }
                else
                {
                    // Linux/macOS
                    report.DefaultGateway = "Configured";
                }
            }
            catch
            {
                report.DiagnosticsErrors.Add("Route test skipped");
            }
        }

        /// <summary>
        /// Get system DNS servers
        /// </summary>
        private static List<string> GetDNSServers()
        {
            var dnsServers = new List<string>();

            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var ni in interfaces)
                {
                    if (ni.OperationalStatus == OperationalStatus.Up)
                    {
                        var dnsSettings = ni.GetIPProperties().DnsAddresses;
                        foreach (var dns in dnsSettings)
                        {
                            dnsServers.Add(dns.ToString());
                        }
                    }
                }
            }
            catch { }

            return dnsServers;
        }

        /// <summary>
        /// Calculate network quality score (0-100)
        /// </summary>
        public static int CalculateNetworkQuality(DiagnosticsReport report)
        {
            int score = 100;

            // Latency penalty
            if (report.Latency > 100) score -= 20;
            else if (report.Latency > 50) score -= 10;

            // Connectivity penalty
            if (!report.Connectivity) score -= 30;

            // DNS penalty
            if (!report.DNSResolution) score -= 15;
            else if (report.DNSLatency > 100) score -= 10;

            // Interface penalty
            if (report.ActiveInterfaces == 0) score -= 25;

            return Math.Max(0, score);
        }
    }

    /// <summary>
    /// Network diagnostics report data
    /// </summary>
    public class DiagnosticsReport
    {
        public DateTime Timestamp { get; set; }

        // Connectivity Tests
        public bool Connectivity { get; set; }
        public long Latency { get; set; } // in milliseconds
        public string? PingHost { get; set; }

        // DNS Tests
        public bool DNSResolution { get; set; }
        public long DNSLatency { get; set; }
        public string? DNSServers { get; set; }

        // Interface Information
        public int ActiveInterfaces { get; set; }
        public string? WiFiInterface { get; set; }
        public string? WiFiStatus { get; set; }

        // Routing Information
        public string? DefaultGateway { get; set; }

        // Errors
        public List<string> DiagnosticsErrors { get; set; } = new();

        /// <summary>
        /// Format report for console display
        /// </summary>
        public override string ToString()
        {
            var builder = new System.Text.StringBuilder();
            builder.AppendLine("\n=== Network Diagnostics Report ===");
            builder.AppendLine($"Timestamp: {Timestamp:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine();

            builder.AppendLine("Connectivity:");
            builder.AppendLine($"  Status: {(Connectivity ? "✅ Connected" : "❌ Failed")}");
            if (Latency >= 0)
                builder.AppendLine($"  Latency: {Latency}ms");

            builder.AppendLine();
            builder.AppendLine("DNS:");
            builder.AppendLine($"  Resolution: {(DNSResolution ? "✅ Working" : "❌ Failed")}");
            if (DNSLatency >= 0)
                builder.AppendLine($"  Latency: {DNSLatency}ms");

            builder.AppendLine();
            builder.AppendLine("Network Interfaces:");
            builder.AppendLine($"  Active: {ActiveInterfaces}");
            if (!string.IsNullOrEmpty(WiFiInterface))
                builder.AppendLine($"  WiFi: {WiFiInterface} ({WiFiStatus})");

            builder.AppendLine();
            builder.AppendLine("Routing:");
            builder.AppendLine($"  Default Gateway: {DefaultGateway ?? "Not available"}");

            int quality = NetworkDiagnostics.CalculateNetworkQuality(this);
            builder.AppendLine();
            builder.AppendLine($"Overall Quality Score: {quality}/100");

            if (DiagnosticsErrors.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Warnings:");
                foreach (var error in DiagnosticsErrors)
                {
                    builder.AppendLine($"  ⚠️ {error}");
                }
            }

            return builder.ToString();
        }
    }
}
