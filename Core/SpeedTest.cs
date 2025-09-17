using System;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// Simple network speed test utility
    /// </summary>
    public class SpeedTest
    {
        private readonly ProcessExecutor _processExecutor;

        public SpeedTest()
        {
            _processExecutor = new ProcessExecutor();
        }

        /// <summary>
        /// Test network latency (ping)
        /// </summary>
        public async Task<PingResult> TestLatencyAsync(string host = "8.8.8.8", CancellationToken ct = default)
        {
            var result = new PingResult();

            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(host);

                if (reply.Status == IPStatus.Success)
                {
                    result.Success = true;
                    result.RoundtripTime = reply.RoundtripTime;
                    result.Status = $"Reply from {reply.Address}: time={reply.RoundtripTime}ms";
                }
                else
                {
                    result.Success = false;
                    result.Status = $"Ping failed: {reply.Status}";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Status = $"Error: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Test download speed
        /// </summary>
        public async Task<SpeedTestResult> TestDownloadSpeedAsync(CancellationToken ct = default)
        {
            var result = new SpeedTestResult();

            try
            {
                // Use a small test file from a reliable CDN
                var testUrls = new[]
                {
                    "http://ipv4.download.thinkbroadband.com/5MB.zip",
                    "http://speedtest.tele2.net/1MB.zip",
                    "http://www.ovh.net/files/1Mb.dat"
                };

                foreach (var url in testUrls)
                {
                    if (ct.IsCancellationRequested)
                        break;

                    try
                    {
                        var sw = Stopwatch.StartNew();
                        using var client = new WebClient();

                        var downloadTask = client.DownloadDataTaskAsync(url);
                        var timeoutTask = Task.Delay(10000, ct); // 10 second timeout

                        var completedTask = await Task.WhenAny(downloadTask, timeoutTask);

                        if (completedTask == downloadTask)
                        {
                            var data = await downloadTask;
                            sw.Stop();

                            var sizeMB = data.Length / (1024.0 * 1024.0);
                            var seconds = sw.Elapsed.TotalSeconds;
                            var speedMbps = (sizeMB * 8) / seconds;

                            result.DownloadSpeed = speedMbps;
                            result.Success = true;
                            result.Message = $"Download speed: {speedMbps:F2} Mbps";
                            break;
                        }
                    }
                    catch
                    {
                        // Try next URL
                        continue;
                    }
                }

                if (!result.Success)
                {
                    result.Message = "Could not complete speed test";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Speed test failed: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Test network connectivity
        /// </summary>
        public async Task<ConnectivityTestResult> TestConnectivityAsync(CancellationToken ct = default)
        {
            var result = new ConnectivityTestResult();

            try
            {
                // Test DNS
                var dnsTask = TestDNSAsync(ct);

                // Test gateway
                var gatewayTask = TestGatewayAsync(ct);

                // Test internet
                var internetTask = TestInternetAsync(ct);

                await Task.WhenAll(dnsTask, gatewayTask, internetTask);

                result.DNSWorking = await dnsTask;
                result.GatewayReachable = await gatewayTask;
                result.InternetAccess = await internetTask;

                result.Success = result.DNSWorking && result.GatewayReachable && result.InternetAccess;

                if (result.Success)
                {
                    result.Message = "All connectivity tests passed";
                }
                else
                {
                    var issues = new System.Collections.Generic.List<string>();
                    if (!result.DNSWorking) issues.Add("DNS");
                    if (!result.GatewayReachable) issues.Add("Gateway");
                    if (!result.InternetAccess) issues.Add("Internet");
                    result.Message = $"Issues detected: {string.Join(", ", issues)}";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Connectivity test failed: {ex.Message}";
            }

            return result;
        }

        private async Task<bool> TestDNSAsync(CancellationToken ct)
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync("www.google.com");
                return addresses.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> TestGatewayAsync(CancellationToken ct)
        {
            try
            {
                var gateway = GetDefaultGateway();
                if (string.IsNullOrEmpty(gateway))
                    return false;

                using var ping = new Ping();
                var reply = await ping.SendPingAsync(gateway);
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> TestInternetAsync(CancellationToken ct)
        {
            try
            {
                using var ping = new Ping();

                // Try multiple reliable servers
                var servers = new[] { "8.8.8.8", "1.1.1.1", "208.67.222.222" };

                foreach (var server in servers)
                {
                    try
                    {
                        var reply = await ping.SendPingAsync(server);
                        if (reply.Status == IPStatus.Success)
                            return true;
                    }
                    catch
                    {
                        continue;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private string? GetDefaultGateway()
        {
            try
            {
                foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (networkInterface.OperationalStatus == OperationalStatus.Up &&
                        networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        var properties = networkInterface.GetIPProperties();
                        foreach (var gateway in properties.GatewayAddresses)
                        {
                            if (gateway.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            {
                                return gateway.Address.ToString();
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors
            }

            return null;
        }

        /// <summary>
        /// Get network statistics
        /// </summary>
        public NetworkStatistics GetNetworkStatistics()
        {
            var stats = new NetworkStatistics();

            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();

                foreach (var ni in interfaces)
                {
                    if (ni.OperationalStatus == OperationalStatus.Up &&
                        ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    {
                        var statistics = ni.GetIPv4Statistics();

                        stats.BytesSent = statistics.BytesSent;
                        stats.BytesReceived = statistics.BytesReceived;
                        stats.PacketsSent = statistics.UnicastPacketsSent;
                        stats.PacketsReceived = statistics.UnicastPacketsReceived;
                        stats.ErrorsReceived = statistics.IncomingPacketsWithErrors;
                        stats.PacketsDiscarded = statistics.IncomingPacketsDiscarded;

                        // Calculate speeds if we have previous data
                        stats.CurrentSpeed = ni.Speed / 1_000_000.0; // Convert to Mbps

                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to get network statistics: {ex.Message}", ex);
            }

            return stats;
        }
    }

    public class PingResult
    {
        public bool Success { get; set; }
        public long RoundtripTime { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class SpeedTestResult
    {
        public bool Success { get; set; }
        public double DownloadSpeed { get; set; } // Mbps
        public double UploadSpeed { get; set; } // Mbps
        public string Message { get; set; } = string.Empty;
    }

    public class ConnectivityTestResult
    {
        public bool Success { get; set; }
        public bool DNSWorking { get; set; }
        public bool GatewayReachable { get; set; }
        public bool InternetAccess { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class NetworkStatistics
    {
        public long BytesSent { get; set; }
        public long BytesReceived { get; set; }
        public long PacketsSent { get; set; }
        public long PacketsReceived { get; set; }
        public long ErrorsReceived { get; set; }
        public long PacketsDiscarded { get; set; }
        public double CurrentSpeed { get; set; } // Mbps

        public string FormatBytesReceived() => FormatBytes(BytesReceived);
        public string FormatBytesSent() => FormatBytes(BytesSent);

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:F2} {sizes[order]}";
        }
    }
}