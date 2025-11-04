using System;
using System.Collections.Generic;
using System.Linq;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// WiFi channel interference and noise analysis
    /// Detects overlapping networks and estimates interference levels
    /// Based on 2024-2025 research on WiFi spectrum utilization
    /// </summary>
    public static class InterferenceAnalyzer
    {
        /// <summary>
        /// Channel overlap definitions for 2.4GHz (802.11b/g/n)
        /// Each channel has 22MHz bandwidth but spaced 5MHz apart
        /// </summary>
        private static readonly Dictionary<int, List<int>> Channel24GHzOverlap = new()
        {
            { 1, new List<int> { 1, 2, 3, 4, 5, 6, 7 } },
            { 6, new List<int> { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 } },
            { 11, new List<int> { 6, 7, 8, 9, 10, 11, 12, 13, 14 } }
        };

        /// <summary>
        /// 5GHz UNII band channel groups (non-overlapping)
        /// </summary>
        private static readonly List<List<int>> Channel5GHzBands = new()
        {
            new List<int> { 36, 40, 44, 48 },           // UNII-1 (5.15-5.25 GHz)
            new List<int> { 52, 56, 60, 64 },           // UNII-2 (5.25-5.35 GHz)
            new List<int> { 100, 104, 108, 112, 116, 120, 124, 128, 132, 136, 140, 144 }, // UNII-2 Extended
            new List<int> { 149, 153, 157, 161, 165 }   // UNII-3 (5.725-5.850 GHz)
        };

        /// <summary>
        /// Analyze interference for a set of networks
        /// </summary>
        public static InterferenceReport AnalyzeInterference(List<WiFiNetwork> networks)
        {
            var report = new InterferenceReport
            {
                Timestamp = DateTime.UtcNow,
                TotalNetworks = networks.Count
            };

            if (networks.Count == 0)
                return report;

            // Separate by band
            var networks24 = networks.Where(n => n.Band == "2.4GHz").ToList();
            var networks5 = networks.Where(n => n.Band == "5GHz").ToList();
            var networks6 = networks.Where(n => n.Band == "6GHz").ToList();

            // Analyze 2.4GHz interference
            if (networks24.Count > 0)
            {
                report.Interference24GHz = Analyze24GHzInterference(networks24);
            }

            // Analyze 5GHz interference
            if (networks5.Count > 0)
            {
                report.Interference5GHz = Analyze5GHzInterference(networks5);
            }

            // Calculate overall congestion
            report.OverallCongestion = CalculateOverallCongestion(networks);
            report.RecommendedBand = GetRecommendedBand(report);

            return report;
        }

        /// <summary>
        /// Analyze 2.4GHz channel interference
        /// </summary>
        private static InterferenceBandReport Analyze24GHzInterference(List<WiFiNetwork> networks)
        {
            var report = new InterferenceBandReport { Band = "2.4GHz" };
            var channelUsage = new Dictionary<int, List<WiFiNetwork>>();

            // Group networks by channel
            foreach (var network in networks)
            {
                if (network.Channel == 0) continue;

                if (!channelUsage.ContainsKey(network.Channel))
                    channelUsage[network.Channel] = new List<WiFiNetwork>();

                channelUsage[network.Channel].Add(network);
            }

            report.ChannelsInUse = channelUsage.Count;

            // Check for overlapping networks (main interference sources)
            // Recommend channels 1, 6, 11 for minimum overlap
            var recommendedChannels = new[] { 1, 6, 11 };

            foreach (var channel in recommendedChannels)
            {
                int overlappingNetworks = 0;
                double maxSignal = -100;

                // Count overlapping networks
                foreach (var usedChannel in channelUsage.Keys)
                {
                    if (Math.Abs(usedChannel - channel) <= 4) // Channels within 4 overlap
                    {
                        overlappingNetworks += channelUsage[usedChannel].Count;
                        maxSignal = Math.Max(maxSignal,
                            channelUsage[usedChannel].Max(n => n.SignalStrength));
                    }
                }

                report.ChannelInterference[channel] = new ChannelInterference
                {
                    Channel = channel,
                    OverlappingNetworks = overlappingNetworks,
                    InterferenceLevel = CalculateInterferenceLevel(overlappingNetworks, maxSignal)
                };
            }

            // Find best channel
            report.RecommendedChannel = report.ChannelInterference
                .OrderBy(ci => ci.Value.OverlappingNetworks)
                .ThenBy(ci => ci.Value.InterferenceLevel)
                .First().Key;

            return report;
        }

        /// <summary>
        /// Analyze 5GHz channel interference
        /// </summary>
        private static InterferenceBandReport Analyze5GHzInterference(List<WiFiNetwork> networks)
        {
            var report = new InterferenceBandReport { Band = "5GHz" };
            var channelUsage = new Dictionary<int, List<WiFiNetwork>>();

            foreach (var network in networks)
            {
                if (network.Channel == 0) continue;

                if (!channelUsage.ContainsKey(network.Channel))
                    channelUsage[network.Channel] = new List<WiFiNetwork>();

                channelUsage[network.Channel].Add(network);
            }

            report.ChannelsInUse = channelUsage.Count;

            // Analyze within bands (channels in same band don't overlap)
            foreach (var band in Channel5GHzBands)
            {
                foreach (var channel in band)
                {
                    int networkCount = channelUsage.ContainsKey(channel) ? channelUsage[channel].Count : 0;
                    double signal = networkCount > 0 ? channelUsage[channel].Max(n => n.SignalStrength) : -100;

                    report.ChannelInterference[channel] = new ChannelInterference
                    {
                        Channel = channel,
                        OverlappingNetworks = networkCount,
                        InterferenceLevel = networkCount == 0 ? "None" : "Same-Band"
                    };
                }
            }

            // Find best channel (least used)
            report.RecommendedChannel = report.ChannelInterference
                .OrderBy(ci => ci.Value.OverlappingNetworks)
                .First().Key;

            return report;
        }

        /// <summary>
        /// Calculate overall network congestion score
        /// </summary>
        private static string CalculateOverallCongestion(List<WiFiNetwork> networks)
        {
            if (networks.Count == 0)
                return "None";

            if (networks.Count <= 2)
                return "Light";

            if (networks.Count <= 5)
                return "Moderate";

            if (networks.Count <= 10)
                return "Heavy";

            return "Severe";
        }

        /// <summary>
        /// Determine interference level based on overlapping networks and signal strength
        /// </summary>
        private static string CalculateInterferenceLevel(int overlappingNetworks, double maxSignal)
        {
            if (overlappingNetworks == 0)
                return "None";

            // More networks and stronger signals = more interference
            double interferenceScore = overlappingNetworks;

            // Strong signals (above -60 dBm) have greater impact
            if (maxSignal > -60)
                interferenceScore *= 1.5;

            if (interferenceScore <= 1)
                return "Light";

            if (interferenceScore <= 3)
                return "Moderate";

            if (interferenceScore <= 6)
                return "Heavy";

            return "Severe";
        }

        /// <summary>
        /// Recommend optimal band based on congestion analysis
        /// </summary>
        private static string GetRecommendedBand(InterferenceReport report)
        {
            // Prefer 5GHz if available and less congested
            if (report.Interference5GHz != null &&
                (report.Interference24GHz == null ||
                 report.Interference5GHz.ChannelsInUse < report.Interference24GHz.ChannelsInUse))
            {
                return "5GHz (less congested)";
            }

            // Prefer 6GHz if available (newest, widest channels)
            if (report.Interference5GHz?.ChannelsInUse == 0)
                return "6GHz (if available)";

            return "2.4GHz (check channel selection)";
        }

        /// <summary>
        /// Get SNR (Signal-to-Noise Ratio) estimate
        /// SNR = Signal Strength - Noise Floor
        /// Typical noise floor is around -90 to -100 dBm
        /// </summary>
        public static int CalculateSNR(WiFiNetwork network, int estimatedNoiseFloor = -95)
        {
            // Convert signal strength percentage (0-100) to dBm
            // 0% = -100dBm, 100% = -30dBm
            int signalDbm = -100 + (network.SignalStrength);

            return signalDbm - estimatedNoiseFloor;
        }

        /// <summary>
        /// Get SNR quality rating
        /// </summary>
        public static string GetSNRRating(int snr)
        {
            return snr switch
            {
                >= 40 => "Excellent (>40dB)",
                >= 30 => "Very Good (30-40dB)",
                >= 20 => "Good (20-30dB)",
                >= 10 => "Fair (10-20dB)",
                >= 5 => "Poor (5-10dB)",
                _ => "Very Poor (<5dB)"
            };
        }
    }

    /// <summary>
    /// Complete interference analysis report
    /// </summary>
    public class InterferenceReport
    {
        public DateTime Timestamp { get; set; }
        public int TotalNetworks { get; set; }
        public InterferenceBandReport? Interference24GHz { get; set; }
        public InterferenceBandReport? Interference5GHz { get; set; }
        public string OverallCongestion { get; set; } = "Unknown";
        public string RecommendedBand { get; set; } = "Unknown";

        /// <summary>
        /// Format report for console display
        /// </summary>
        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("\n=== WiFi Interference Analysis ===");
            sb.AppendLine($"Timestamp: {Timestamp:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Total Networks Detected: {TotalNetworks}");
            sb.AppendLine($"Overall Congestion: {OverallCongestion}");
            sb.AppendLine();

            if (Interference24GHz != null)
            {
                sb.AppendLine("2.4GHz Band Analysis:");
                sb.AppendLine(Interference24GHz.ToString());
                sb.AppendLine();
            }

            if (Interference5GHz != null)
            {
                sb.AppendLine("5GHz Band Analysis:");
                sb.AppendLine(Interference5GHz.ToString());
                sb.AppendLine();
            }

            sb.AppendLine($"Recommendation: {RecommendedBand}");

            return sb.ToString();
        }
    }

    /// <summary>
    /// Interference analysis for a specific band
    /// </summary>
    public class InterferenceBandReport
    {
        public string Band { get; set; } = "Unknown";
        public int ChannelsInUse { get; set; }
        public int RecommendedChannel { get; set; }
        public Dictionary<int, ChannelInterference> ChannelInterference { get; set; } = new();

        /// <summary>
        /// Format band report for display
        /// </summary>
        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"  Band: {Band}");
            sb.AppendLine($"  Channels in Use: {ChannelsInUse}");
            sb.AppendLine($"  Recommended Channel: {RecommendedChannel}");
            sb.AppendLine("  Channel Details:");

            foreach (var ci in ChannelInterference.OrderBy(ci => ci.Key))
            {
                sb.AppendLine($"    Channel {ci.Key}: {ci.Value.OverlappingNetworks} networks, " +
                            $"Interference: {ci.Value.InterferenceLevel}");
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Interference information for a specific channel
    /// </summary>
    public class ChannelInterference
    {
        public int Channel { get; set; }
        public int OverlappingNetworks { get; set; }
        public string InterferenceLevel { get; set; } = "Unknown";
    }
}
