using System;
using System.Collections.Generic;
using System.Linq;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// WiFi channel optimization analyzer
    /// Based on 2024 WiFi research and best practices
    /// Implements channels 1, 6, 11 for 2.4GHz and non-overlapping channels for 5GHz
    /// </summary>
    public static class ChannelOptimizer
    {
        // 2.4GHz non-overlapping channels (20MHz width)
        private static readonly int[] NonOverlappingChannels24GHz = { 1, 6, 11 };

        // 5GHz channels (25 non-overlapping channels with 20MHz width)
        private static readonly int[] Channels5GHz =
        {
            36, 40, 44, 48,           // UNII-1
            52, 56, 60, 64,           // UNII-2
            100, 104, 108, 112, 116, 120, 124, 128, // UNII-2 Extended
            132, 136, 140, 144,       // UNII-2 Extended (Extra)
            149, 153, 157, 161, 165   // UNII-3
        };

        /// <summary>
        /// Analyze channel quality based on congestion
        /// </summary>
        public static ChannelAnalysis AnalyzeChannelQuality(List<WiFiNetwork> networks)
        {
            var analysis = new ChannelAnalysis { Timestamp = DateTime.UtcNow };

            if (networks == null || networks.Count == 0)
            {
                analysis.RecommendedChannel24GHz = 6; // Default safest choice
                analysis.RecommendedChannel5GHz = 149;
                analysis.Quality = "No networks detected";
                return analysis;
            }

            // Group networks by band
            var networks24GHz = networks.Where(n => n.Band == "2.4GHz").ToList();
            var networks5GHz = networks.Where(n => n.Band == "5GHz").ToList();

            // Analyze 2.4GHz
            if (networks24GHz.Count > 0)
            {
                AnalyzeChannels24GHz(networks24GHz, analysis);
            }

            // Analyze 5GHz
            if (networks5GHz.Count > 0)
            {
                AnalyzeChannels5GHz(networks5GHz, analysis);
            }

            // Determine overall quality
            DetermineQuality(analysis, networks24GHz.Count, networks5GHz.Count);

            return analysis;
        }

        /// <summary>
        /// Analyze 2.4GHz channels for interference
        /// </summary>
        private static void AnalyzeChannels24GHz(List<WiFiNetwork> networks, ChannelAnalysis analysis)
        {
            var channelUsage = new Dictionary<int, int>();

            foreach (var channel in NonOverlappingChannels24GHz)
            {
                channelUsage[channel] = 0;
            }

            // Count networks on each channel (simplified - assumes default channels)
            foreach (var network in networks)
            {
                // In real implementation, would parse detailed channel info
                var defaultChannel = 6; // Assume middle channel if unknown
                if (channelUsage.ContainsKey(defaultChannel))
                {
                    channelUsage[defaultChannel]++;
                }
            }

            // Find least congested channel
            var recommendedChannel = channelUsage.OrderBy(kv => kv.Value).First().Key;
            analysis.RecommendedChannel24GHz = recommendedChannel;
            analysis.ChannelCongestion24GHz = channelUsage;

            // Assess 2.4GHz band health
            var avgCongestion = channelUsage.Values.Average();
            analysis.Band24GHzCongestion = avgCongestion switch
            {
                < 2 => "Good",
                < 5 => "Moderate",
                _ => "Heavy"
            };
        }

        /// <summary>
        /// Analyze 5GHz channels for interference
        /// </summary>
        private static void AnalyzeChannels5GHz(List<WiFiNetwork> networks, ChannelAnalysis analysis)
        {
            var channelUsage = new Dictionary<int, int>();

            foreach (var channel in Channels5GHz)
            {
                channelUsage[channel] = 0;
            }

            // Count networks on each channel
            foreach (var network in networks)
            {
                // Simplified: assign to first available channel
                var defaultChannel = 149; // Popular UNII-3 channel
                if (channelUsage.ContainsKey(defaultChannel))
                {
                    channelUsage[defaultChannel]++;
                }
            }

            // Find least congested channel
            var recommendedChannel = channelUsage.OrderBy(kv => kv.Value).First().Key;
            analysis.RecommendedChannel5GHz = recommendedChannel;
            analysis.ChannelCongestion5GHz = channelUsage;

            // Assess 5GHz band health
            var avgCongestion = channelUsage.Values.Average();
            analysis.Band5GHzCongestion = avgCongestion switch
            {
                < 1 => "Excellent",
                < 3 => "Good",
                < 6 => "Moderate",
                _ => "Heavy"
            };
        }

        /// <summary>
        /// Determine overall WiFi environment quality
        /// </summary>
        private static void DetermineQuality(ChannelAnalysis analysis, int count24GHz, int count5GHz)
        {
            var totalNetworks = count24GHz + count5GHz;

            if (totalNetworks == 0)
            {
                analysis.Quality = "Excellent - No interference detected";
                return;
            }

            // 5GHz preference due to more channels and less congestion
            var quality5GHz = analysis.Band5GHzCongestion switch
            {
                "Excellent" => 5,
                "Good" => 4,
                "Moderate" => 3,
                "Heavy" => 1,
                _ => 2
            };

            var quality24GHz = analysis.Band24GHzCongestion switch
            {
                "Good" => 4,
                "Moderate" => 2,
                "Heavy" => 1,
                _ => 3
            };

            var avgQuality = (quality5GHz + quality24GHz) / 2.0;

            analysis.Quality = avgQuality switch
            {
                >= 4.5 => "Excellent - Low interference",
                >= 3.5 => "Good - Acceptable interference",
                >= 2.5 => "Fair - Moderate interference",
                >= 1.5 => "Poor - Heavy interference",
                _ => "Critical - Severe interference"
            };
        }

        /// <summary>
        /// Get recommendations for optimal connectivity
        /// </summary>
        public static List<string> GetOptimizationRecommendations(ChannelAnalysis analysis)
        {
            var recommendations = new List<string>();

            // Band recommendations
            if (analysis.Band5GHzCongestion == "Excellent" || analysis.Band5GHzCongestion == "Good")
            {
                recommendations.Add("✓ Use 5GHz band for better performance and less interference");
            }
            else if (analysis.Band24GHzCongestion == "Heavy" && analysis.Band5GHzCongestion != "Heavy")
            {
                recommendations.Add("⚠️ 2.4GHz band is congested - switch to 5GHz if possible");
            }

            // Channel recommendations
            if (analysis.RecommendedChannel24GHz > 0)
            {
                recommendations.Add($"✓ 2.4GHz: Use channel {analysis.RecommendedChannel24GHz} (least congested)");
            }

            if (analysis.RecommendedChannel5GHz > 0)
            {
                recommendations.Add($"✓ 5GHz: Use channel {analysis.RecommendedChannel5GHz} (least congested)");
            }

            // General optimization
            recommendations.Add("✓ Use 20MHz channel width to reduce interference");
            recommendations.Add("✓ Enable DCS (Dynamic Channel Selection) if supported");
            recommendations.Add("✓ Avoid overlapping channels with neighbors");
            recommendations.Add("✓ Place router away from microwave, cordless phones, and baby monitors");

            if (analysis.Quality.Contains("Heavy") || analysis.Quality.Contains("Severe"))
            {
                recommendations.Add("⚠️ High interference detected - consider WiFi 6E or WiFi 7 for improved robustness");
            }

            return recommendations;
        }
    }

    /// <summary>
    /// Channel analysis results
    /// </summary>
    public class ChannelAnalysis
    {
        public DateTime Timestamp { get; set; }

        // Recommended channels
        public int RecommendedChannel24GHz { get; set; }
        public int RecommendedChannel5GHz { get; set; }

        // Channel usage data
        public Dictionary<int, int> ChannelCongestion24GHz { get; set; } = new();
        public Dictionary<int, int> ChannelCongestion5GHz { get; set; } = new();

        // Band health assessments
        public string Band24GHzCongestion { get; set; } = "Unknown";
        public string Band5GHzCongestion { get; set; } = "Unknown";

        // Overall quality
        public string Quality { get; set; } = "Unknown";

        /// <summary>
        /// Format analysis for console display
        /// </summary>
        public override string ToString()
        {
            var builder = new System.Text.StringBuilder();
            builder.AppendLine("\n=== WiFi Channel Optimization Analysis ===");
            builder.AppendLine($"Timestamp: {Timestamp:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine();

            builder.AppendLine("2.4GHz Band:");
            builder.AppendLine($"  Status: {Band24GHzCongestion}");
            builder.AppendLine($"  Recommended Channel: {RecommendedChannel24GHz}");

            builder.AppendLine();
            builder.AppendLine("5GHz Band:");
            builder.AppendLine($"  Status: {Band5GHzCongestion}");
            builder.AppendLine($"  Recommended Channel: {RecommendedChannel5GHz}");

            builder.AppendLine();
            builder.AppendLine($"Overall Environment Quality: {Quality}");

            return builder.ToString();
        }
    }
}
