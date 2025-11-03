using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// WiFi bandwidth and speed testing
    /// Measures download/upload speeds and network performance
    /// </summary>
    public static class SpeedTest
    {
        // Use public, fast endpoints for speed testing
        private static readonly string[] TestServers = new[]
        {
            "https://www.google.com",
            "https://www.github.com",
            "https://www.cloudflare.com"
        };

        /// <summary>
        /// Test download speed with a known reliable server
        /// </summary>
        public static async Task<SpeedTestResult> TestDownloadSpeed()
        {
            var result = new SpeedTestResult
            {
                TestType = "Download",
                Timestamp = DateTime.UtcNow
            };

            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
                {
                    var stopwatch = Stopwatch.StartNew();
                    long bytesDownloaded = 0;

                    // Try to download from first available server
                    foreach (var server in TestServers)
                    {
                        try
                        {
                            var response = await client.GetAsync(server, HttpCompletionOption.ResponseContentRead);
                            if (response.IsSuccessStatusCode)
                            {
                                var content = await response.Content.ReadAsStreamAsync();
                                byte[] buffer = new byte[8192];
                                int bytesRead;

                                while ((bytesRead = await content.ReadAsync(buffer, 0, buffer.Length)) > 0)
                                {
                                    bytesDownloaded += bytesRead;
                                }

                                stopwatch.Stop();
                                result.BytesTransferred = bytesDownloaded;
                                result.DurationMs = stopwatch.ElapsedMilliseconds;
                                result.Success = true;
                                return result;
                            }
                        }
                        catch { }
                    }

                    result.Error = "Failed to connect to test servers";
                }
            }
            catch (Exception ex)
            {
                result.Error = $"Speed test error: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Estimate WiFi speed based on signal strength and band
        /// This is a practical estimation, not a precise measurement
        /// </summary>
        public static SpeedEstimate EstimateWiFiSpeed(WiFiNetwork network)
        {
            var estimate = new SpeedEstimate
            {
                SSID = network.SSID,
                Band = network.Band,
                SignalStrength = network.SignalStrength,
                Timestamp = DateTime.UtcNow
            };

            // Estimate based on band and signal strength
            // These are conservative estimates based on WiFi standards
            int baseSpeed = network.Band switch
            {
                "2.4GHz" => 150,      // 802.11n typical: 150 Mbps
                "5GHz" => 433,        // 802.11ac typical: 433-867 Mbps
                "6GHz" => 2400,       // 802.11ax (WiFi 6) typical: 1-2.4 Gbps
                _ => 50               // Conservative fallback
            };

            // Apply signal strength penalty (0-100%)
            // Signal strength degrades performance significantly below -70dBm
            double signalFactor = network.SignalStrength switch
            {
                >= 90 => 0.95,        // Excellent
                >= 80 => 0.85,        // Good
                >= 70 => 0.70,        // Fair
                >= 60 => 0.50,        // Weak
                >= 50 => 0.30,        // Poor
                _ => 0.10             // Very weak
            };

            estimate.EstimatedMbps = (int)(baseSpeed * signalFactor);
            estimate.QualityRating = network.SignalStrength switch
            {
                >= 90 => "Excellent",
                >= 80 => "Very Good",
                >= 70 => "Good",
                >= 60 => "Fair",
                >= 50 => "Poor",
                _ => "Very Poor"
            };

            return estimate;
        }

        /// <summary>
        /// Calculate optimal speed expectation based on WiFi standard
        /// </summary>
        public static int GetTheoreticalMaxSpeed(WiFiNetwork network)
        {
            // Practical maximum speeds (real-world, not theoretical)
            return network.Band switch
            {
                "2.4GHz" => 150,      // 802.11n @40MHz
                "5GHz" => 867,        // 802.11ac @80MHz
                "6GHz" => 2402,       // 802.11ax (WiFi 6E)
                _ => 50
            };
        }

        /// <summary>
        /// Format speed test result for console display
        /// </summary>
        public static string FormatSpeedResult(SpeedTestResult result)
        {
            if (!result.Success)
                return $"Speed Test Failed: {result.Error}";

            var speedMbps = (result.BytesTransferred * 8.0) / (result.DurationMs / 1000.0) / 1_000_000;
            var sizeKb = result.BytesTransferred / 1024.0;

            return $"Downloaded: {sizeKb:F1} KB in {result.DurationMs}ms\n" +
                   $"Speed: {speedMbps:F2} Mbps";
        }
    }

    /// <summary>
    /// Speed test result data
    /// </summary>
    public class SpeedTestResult
    {
        public string TestType { get; set; } = "Download";
        public DateTime Timestamp { get; set; }
        public long BytesTransferred { get; set; }
        public long DurationMs { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// WiFi speed estimation based on signal and band
    /// </summary>
    public class SpeedEstimate
    {
        public string? SSID { get; set; }
        public string? Band { get; set; }
        public int SignalStrength { get; set; }
        public DateTime Timestamp { get; set; }
        public int EstimatedMbps { get; set; }
        public string? QualityRating { get; set; }

        /// <summary>
        /// Format estimate for console display
        /// </summary>
        public override string ToString()
        {
            return $"WiFi Speed Estimate for {SSID}\n" +
                   $"Band: {Band} | Signal: {SignalStrength}%\n" +
                   $"Estimated Speed: {EstimatedMbps} Mbps\n" +
                   $"Quality: {QualityRating}";
        }
    }
}
