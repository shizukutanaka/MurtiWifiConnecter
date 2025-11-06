using System;
using System.Collections.Generic;
using System.Linq;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// WiFi roaming optimization and fast transition detection
    /// Analyzes 802.11k, 802.11r, and 802.11v support
    /// Based on 2024-2025 WiFi roaming standards
    /// </summary>
    public static class RoamingOptimization
    {
        /// <summary>
        /// Detect potential roaming-enabled networks
        /// </summary>
        public static RoamingAnalysis AnalyzeRoamingCapability(List<WiFiNetwork> networks)
        {
            var analysis = new RoamingAnalysis
            {
                Timestamp = DateTime.UtcNow,
                TotalNetworks = networks.Count
            };

            // Identify networks by SSID (same SSID = potential roaming network)
            var ssidGroups = networks.GroupBy(n => n.SSID).ToList();
            analysis.UniqueSSIDs = ssidGroups.Count;

            // Analyze roaming network clusters
            foreach (var group in ssidGroups.Where(g => g.Count() > 1))
            {
                var cluster = new RoamingCluster
                {
                    SSID = group.Key,
                    APCount = group.Count(),
                    Networks = group.ToList()
                };

                // Check for 802.11k capability in security type
                cluster.Supports802_11k = DetectStandard(group, "802.11k");
                cluster.Supports802_11r = DetectStandard(group, "802.11r");
                cluster.Supports802_11v = DetectStandard(group, "802.11v");

                // Calculate roaming potential
                cluster.RoamingScore = CalculateRoamingScore(cluster);
                cluster.RoamingCapability = GetRoamingCapabilityRating(cluster);

                // Find strongest AP
                var strongestAP = group.OrderByDescending(n => n.SignalStrength).First();
                cluster.StrongestAPSignal = strongestAP.SignalStrength;
                cluster.StrongestAPBand = strongestAP.Band;

                analysis.RoamingClusters.Add(cluster);
            }

            // Provide overall roaming optimization assessment
            analysis.RoamingEnvironment = EvaluateRoamingEnvironment(analysis);

            return analysis;
        }

        /// <summary>
        /// Detect if a network supports a roaming standard
        /// </summary>
        private static bool DetectStandard(IGrouping<string, WiFiNetwork> networks, string standard)
        {
            // In real implementation, this would parse beacon frames
            // For now, we provide framework for detection logic
            foreach (var network in networks)
            {
                if (!string.IsNullOrEmpty(network.SecurityType))
                {
                    // Check security type for standard markers
                    if (network.SecurityType.Contains("WPA3") && standard == "802.11k")
                        return true;
                    if (network.SecurityType.Contains("WPA3") && standard == "802.11v")
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Calculate roaming score based on AP availability and signal
        /// </summary>
        private static int CalculateRoamingScore(RoamingCluster cluster)
        {
            int score = 0;

            // AP count (2-3 APs optimal)
            if (cluster.APCount >= 3)
                score += 30;
            else if (cluster.APCount == 2)
                score += 25;
            else
                score += 10;

            // Signal strength diversity
            var signalLevels = cluster.Networks.Select(n => n.SignalStrength).ToList();
            if (signalLevels.All(s => s > 70))
                score += 25;
            else if (signalLevels.All(s => s > 60))
                score += 20;
            else if (signalLevels.Any(s => s > 70))
                score += 15;

            // Standards support
            if (cluster.Supports802_11k)
                score += 15;
            if (cluster.Supports802_11r)
                score += 15;
            if (cluster.Supports802_11v)
                score += 15;

            return Math.Min(100, score);
        }

        /// <summary>
        /// Get human-readable roaming capability rating
        /// </summary>
        private static string GetRoamingCapabilityRating(RoamingCluster cluster)
        {
            return cluster.RoamingScore switch
            {
                >= 85 => "Excellent - Seamless roaming expected",
                >= 70 => "Good - Smooth roaming",
                >= 50 => "Fair - Some roaming support",
                >= 30 => "Poor - Limited roaming",
                _ => "Very Poor - No roaming optimization"
            };
        }

        /// <summary>
        /// Evaluate overall roaming environment quality
        /// </summary>
        private static string EvaluateRoamingEnvironment(RoamingAnalysis analysis)
        {
            if (analysis.RoamingClusters.Count == 0)
                return "No multi-AP networks detected";

            var avgScore = analysis.RoamingClusters.Average(c => c.RoamingScore);

            if (avgScore >= 80)
                return "Excellent roaming environment - multiple optimized networks";
            if (avgScore >= 70)
                return "Good roaming environment - multi-AP support available";
            if (avgScore >= 50)
                return "Moderate roaming - some optimization possible";
            return "Limited roaming capability - improve AP coverage";
        }

        /// <summary>
        /// Get roaming optimization recommendations
        /// </summary>
        public static List<string> GetRoamingRecommendations(RoamingAnalysis analysis)
        {
            var recommendations = new List<string>();

            if (analysis.RoamingClusters.Count == 0)
            {
                recommendations.Add("No multi-AP networks detected - consider mesh WiFi setup");
                return recommendations;
            }

            foreach (var cluster in analysis.RoamingClusters)
            {
                if (cluster.APCount < 2)
                    recommendations.Add($"{cluster.SSID}: Add more APs for roaming capability");

                if (!cluster.Supports802_11k && !cluster.Supports802_11r && !cluster.Supports802_11v)
                    recommendations.Add($"{cluster.SSID}: Enable roaming standards (802.11k/r/v) on router");

                if (cluster.Networks.Any(n => n.SignalStrength < 40))
                    recommendations.Add($"{cluster.SSID}: Weak AP detected - improve coverage");
            }

            if (recommendations.Count == 0)
                recommendations.Add("Roaming environment is well-optimized");

            return recommendations;
        }

        /// <summary>
        /// Calculate neighbor AP list for current network
        /// </summary>
        public static NeighborAPList GenerateNeighborAPList(WiFiNetwork currentNetwork, List<WiFiNetwork> availableNetworks)
        {
            var neighborList = new NeighborAPList
            {
                CurrentNetwork = currentNetwork,
                Timestamp = DateTime.UtcNow
            };

            // Find networks with same SSID (potential roaming targets)
            var sameSSIDNetworks = availableNetworks
                .Where(n => n.SSID == currentNetwork.SSID && n.BSSID != currentNetwork.BSSID)
                .OrderByDescending(n => n.SignalStrength)
                .ToList();

            neighborList.NeighborAPs = sameSSIDNetworks
                .Select(n => new NeighborAP
                {
                    BSSID = n.BSSID,
                    SignalStrength = n.SignalStrength,
                    Band = n.Band,
                    Channel = n.Channel,
                    RoamingPriority = n.SignalStrength > currentNetwork.SignalStrength ? "Recommended" : "Fallback"
                })
                .ToList();

            return neighborList;
        }
    }

    /// <summary>
    /// Complete roaming analysis report
    /// </summary>
    public class RoamingAnalysis
    {
        public DateTime Timestamp { get; set; }
        public int TotalNetworks { get; set; }
        public int UniqueSSIDs { get; set; }
        public List<RoamingCluster> RoamingClusters { get; set; } = new();
        public string RoamingEnvironment { get; set; } = "Unknown";

        /// <summary>
        /// Format analysis for console display
        /// </summary>
        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("\n=== WiFi Roaming Analysis ===");
            sb.AppendLine($"Timestamp: {Timestamp:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Total Networks: {TotalNetworks}");
            sb.AppendLine($"Unique SSIDs: {UniqueSSIDs}");
            sb.AppendLine($"Roaming Clusters: {RoamingClusters.Count}");
            sb.AppendLine();

            foreach (var cluster in RoamingClusters)
            {
                sb.AppendLine($"Network: {cluster.SSID}");
                sb.AppendLine($"  APs: {cluster.APCount}");
                sb.AppendLine($"  Roaming Score: {cluster.RoamingScore}/100");
                sb.AppendLine($"  Capability: {cluster.RoamingCapability}");
                sb.AppendLine($"  802.11k: {(cluster.Supports802_11k ? "✓" : "✗")} | " +
                            $"802.11r: {(cluster.Supports802_11r ? "✓" : "✗")} | " +
                            $"802.11v: {(cluster.Supports802_11v ? "✓" : "✗")}");
                sb.AppendLine($"  Strongest Signal: {cluster.StrongestAPSignal}% ({cluster.StrongestAPBand})");
                sb.AppendLine();
            }

            sb.AppendLine($"Environment: {RoamingEnvironment}");

            return sb.ToString();
        }
    }

    /// <summary>
    /// Multi-AP network cluster
    /// </summary>
    public class RoamingCluster
    {
        public string? SSID { get; set; }
        public int APCount { get; set; }
        public List<WiFiNetwork> Networks { get; set; } = new();
        public int RoamingScore { get; set; }
        public string RoamingCapability { get; set; } = "Unknown";
        public bool Supports802_11k { get; set; }
        public bool Supports802_11r { get; set; }
        public bool Supports802_11v { get; set; }
        public int StrongestAPSignal { get; set; }
        public string? StrongestAPBand { get; set; }
    }

    /// <summary>
    /// Neighbor AP list for roaming
    /// </summary>
    public class NeighborAPList
    {
        public WiFiNetwork? CurrentNetwork { get; set; }
        public DateTime Timestamp { get; set; }
        public List<NeighborAP> NeighborAPs { get; set; } = new();

        /// <summary>
        /// Format neighbor list for display
        /// </summary>
        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"\n=== Roaming Neighbors for {CurrentNetwork?.SSID} ===");
            sb.AppendLine($"Current AP: {CurrentNetwork?.BSSID} ({CurrentNetwork?.SignalStrength}%)");
            sb.AppendLine();

            foreach (var neighbor in NeighborAPs)
            {
                var priority = neighbor.RoamingPriority == "Recommended" ? "→" : "↓";
                sb.AppendLine($"{priority} {neighbor.BSSID}");
                sb.AppendLine($"   Signal: {neighbor.SignalStrength}% | Band: {neighbor.Band} | Channel: {neighbor.Channel}");
                sb.AppendLine($"   Priority: {neighbor.RoamingPriority}");
            }

            if (NeighborAPs.Count == 0)
                sb.AppendLine("No roaming neighbors available");

            return sb.ToString();
        }
    }

    /// <summary>
    /// Single neighbor AP for roaming
    /// </summary>
    public class NeighborAP
    {
        public string? BSSID { get; set; }
        public int SignalStrength { get; set; }
        public string? Band { get; set; }
        public int Channel { get; set; }
        public string RoamingPriority { get; set; } = "Fallback";
    }
}
