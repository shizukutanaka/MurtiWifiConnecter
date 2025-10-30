using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// Enhanced network information with WiFi 7 support
    /// </summary>
    [DataContract]
    public class NetworkInfo : IEquatable<NetworkInfo>, IComparable<NetworkInfo>
    {
        // Basic network information
        [DataMember]
        [JsonPropertyName("ssid")]
        public string Ssid { get; set; } = string.Empty;

        [DataMember]
        [JsonPropertyName("signal")]
        public int Signal { get; set; }

        [DataMember]
        [JsonPropertyName("security")]
        public string Security { get; set; } = string.Empty;

        [DataMember]
        [JsonPropertyName("band")]
        public string Band { get; set; } = "Unknown";

        [DataMember]
        [JsonPropertyName("channel")]
        public int? Channel { get; set; }

        [DataMember]
        [JsonPropertyName("bssid")]
        public string Bssid { get; set; } = string.Empty;

        // WiFi 7 specific features
        [DataMember]
        [JsonPropertyName("wifiStandard")]
        public WifiStandard WifiStandard { get; set; } = WifiStandard.WiFi6;

        [DataMember]
        [JsonPropertyName("maxDataRate")]
        public double? MaxDataRate { get; set; }

        [DataMember]
        [JsonPropertyName("supportedFeatures")]
        public NetworkFeatures SupportedFeatures { get; set; } = new NetworkFeatures();

        [DataMember]
        [JsonPropertyName("multiLinkOperation")]
        public MultiLinkCapabilities MultiLinkOperation { get; set; } = new MultiLinkCapabilities();

        [DataMember]
        [JsonPropertyName("preamblePuncturing")]
        public bool PreamblePuncturing { get; set; }

        // Performance metrics
        [DataMember]
        [JsonPropertyName("isNativeApiResult")]
        public bool IsNativeApiResult { get; set; }

        [DataMember]
        [JsonPropertyName("isPowerShellResult")]
        public bool IsPowerShellResult { get; set; }

        [DataMember]
        [JsonPropertyName("scanTimestamp")]
        public DateTime ScanTimestamp { get; set; } = DateTime.UtcNow;

        [DataMember]
        [JsonPropertyName("qualityScore")]
        public double QualityScore { get; set; }

        // Enhanced properties for better user experience
        [JsonIgnore]
        public string DisplayName => string.IsNullOrEmpty(Ssid) ? "Unknown Network" : Ssid;

        [JsonIgnore]
        public string SecurityDisplay => string.IsNullOrEmpty(Security) ? "Open" : Security;

        [JsonIgnore]
        public string BandDisplay => string.IsNullOrEmpty(Band) ? "Unknown" : Band;

        [JsonIgnore]
        public string StandardDisplay => WifiStandard.ToString();

        [JsonIgnore]
        public bool IsSecure => !string.Equals(Security, "Open", StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public bool IsWiFi7 => WifiStandard == WifiStandard.WiFi7;

        [JsonIgnore]
        public bool IsWiFi6OrHigher => WifiStandard >= WifiStandard.WiFi6;

        [JsonIgnore]
        public string QualityDescription => GetQualityDescription(Signal);

        // Constructors
        public NetworkInfo() { }

        public NetworkInfo(string ssid)
        {
            Ssid = ssid ?? string.Empty;
        }

        public NetworkInfo(string ssid, int signal, string security, string band)
        {
            Ssid = ssid ?? string.Empty;
            Signal = Math.Clamp(signal, 0, 100);
            Security = security ?? string.Empty;
            Band = band ?? "Unknown";
            ScanTimestamp = DateTime.UtcNow;
            CalculateQualityScore();
        }

        private static string GetQualityDescription(int signal)
        {
            return signal switch
            {
                >= 90 => "Excellent",
                >= 75 => "Very Good",
                >= 60 => "Good",
                >= 40 => "Fair",
                >= 20 => "Poor",
                _ => "Very Poor"
            };
        }

        private void CalculateQualityScore()
        {
            // Calculate quality score based on multiple factors
            double score = 0;

            // Signal strength (40%)
            score += (Signal / 100.0) * 40;

            // Security (20%)
            score += IsSecure ? 20 : 0;

            // WiFi standard (20%)
            score += WifiStandard switch
            {
                WifiStandard.WiFi7 => 20,
                WifiStandard.WiFi6E => 18,
                WifiStandard.WiFi6 => 15,
                WifiStandard.WiFi5 => 10,
                WifiStandard.WiFi4 => 5,
                _ => 0
            };

            // Multi-link operation (10%)
            score += MultiLinkOperation.IsSupported ? 10 : 0;

            // 320MHz channel support (10%)
            score += SupportedFeatures.Supports320MHz ? 10 : 0;

            QualityScore = Math.Min(100, Math.Max(0, score));
        }

        // Equality and comparison implementations
        public bool Equals(NetworkInfo other)
        {
            if (other is null) return false;
            return string.Equals(Ssid, other.Ssid, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(Bssid, other.Bssid, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj) => Equals(obj as NetworkInfo);

        public override int GetHashCode() => HashCode.Combine(
            Ssid?.ToLowerInvariant(),
            Bssid?.ToLowerInvariant()
        );

        public int CompareTo(NetworkInfo other)
        {
            if (other is null) return 1;

            // Primary sort by quality score (descending)
            var qualityComparison = QualityScore.CompareTo(other.QualityScore);
            if (qualityComparison != 0) return -qualityComparison; // Descending

            // Secondary sort by signal strength (descending)
            var signalComparison = Signal.CompareTo(other.Signal);
            if (signalComparison != 0) return -signalComparison; // Descending

            // Tertiary sort by SSID (ascending)
            return string.Compare(Ssid, other.Ssid, StringComparison.OrdinalIgnoreCase);
        }

        public override string ToString()
        {
            var features = new List<string>();

            if (IsWiFi7) features.Add("WiFi 7");
            if (MultiLinkOperation.IsSupported) features.Add("MLO");
            if (SupportedFeatures.Supports320MHz) features.Add("320MHz");
            if (PreamblePuncturing) features.Add("Puncturing");

            var featureString = features.Count > 0 ? $" [{string.Join(", ", features)}]" : "";

            return $"{DisplayName}: {Signal}% ({SecurityDisplay}, {BandDisplay}){featureString}";
        }

        // Static factory methods for common scenarios
        public static NetworkInfo CreateOpenNetwork(string ssid, int signal = 0, string band = "Unknown")
        {
            return new NetworkInfo(ssid, signal, "Open", band);
        }

        public static NetworkInfo CreateSecureNetwork(string ssid, int signal, string security, string band = "Unknown")
        {
            return new NetworkInfo(ssid, signal, security, band);
        }
    }

    /// <summary>
    /// WiFi standards enumeration with WiFi 7 support
    /// </summary>
    [DataContract]
    public enum WifiStandard
    {
        [EnumMember(Value = "WiFi4")]
        WiFi4 = 1,    // 802.11n

        [EnumMember(Value = "WiFi5")]
        WiFi5 = 2,    // 802.11ac

        [EnumMember(Value = "WiFi6")]
        WiFi6 = 3,    // 802.11ax

        [EnumMember(Value = "WiFi6E")]
        WiFi6E = 4,   // 802.11ax with 6GHz

        [EnumMember(Value = "WiFi7")]
        WiFi7 = 5     // 802.11be
    }

    /// <summary>
    /// Supported network features for WiFi 7
    /// </summary>
    [DataContract]
    public class NetworkFeatures
    {
        [DataMember]
        [JsonPropertyName("supports320MHz")]
        public bool Supports320MHz { get; set; }

        [DataMember]
        [JsonPropertyName("supports4KQAM")]
        public bool Supports4KQAM { get; set; }

        [DataMember]
        [JsonPropertyName("supports16SpatialStreams")]
        public bool Supports16SpatialStreams { get; set; }

        [DataMember]
        [JsonPropertyName("supportsOFDMA")]
        public bool SupportsOFDMA { get; set; }

        [DataMember]
        [JsonPropertyName("supportsMUMIMO")]
        public bool SupportsMUMIMO { get; set; }

        [DataMember]
        [JsonPropertyName("supportsTWT")]
        public bool SupportsTWT { get; set; }  // Target Wake Time

        [DataMember]
        [JsonPropertyName("maxSpatialStreams")]
        public int MaxSpatialStreams { get; set; } = 4;
    }

    /// <summary>
    /// Multi-Link Operation capabilities for WiFi 7
    /// </summary>
    [DataContract]
    public class MultiLinkCapabilities
    {
        [DataMember]
        [JsonPropertyName("isSupported")]
        public bool IsSupported { get; set; }

        [DataMember]
        [JsonPropertyName("supportedBands")]
        public List<string> SupportedBands { get; set; } = new List<string> { "2.4GHz", "5GHz", "6GHz" };

        [DataMember]
        [JsonPropertyName("maxLinks")]
        public int MaxLinks { get; set; } = 3;

        [DataMember]
        [JsonPropertyName("linkAggregation")]
        public bool LinkAggregation { get; set; }

        [DataMember]
        [JsonPropertyName("seamlessSwitching")]
        public bool SeamlessSwitching { get; set; }
    }

    /// <summary>
    /// Connection status with enhanced WiFi 7 information
    /// </summary>
    [DataContract]
    public class ConnectionStatus
    {
        [DataMember]
        [JsonPropertyName("status")]
        public string Status { get; set; } = "Disconnected";

        [DataMember]
        [JsonPropertyName("ssid")]
        public string Ssid { get; set; } = string.Empty;

        [DataMember]
        [JsonPropertyName("signal")]
        public int? Signal { get; set; }

        [DataMember]
        [JsonPropertyName("ipAddress")]
        public string IpAddress { get; set; } = string.Empty;

        [DataMember]
        [JsonPropertyName("bssid")]
        public string Bssid { get; set; } = string.Empty;

        [DataMember]
        [JsonPropertyName("channel")]
        public int? Channel { get; set; }

        [DataMember]
        [JsonPropertyName("receiveRateMbps")]
        public double? ReceiveRateMbps { get; set; }

        [DataMember]
        [JsonPropertyName("transmitRateMbps")]
        public double? TransmitRateMbps { get; set; }

        [DataMember]
        [JsonPropertyName("radioType")]
        public string RadioType { get; set; } = string.Empty;

        [DataMember]
        [JsonPropertyName("checkedAtUtc")]
        public DateTime CheckedAtUtc { get; set; } = DateTime.UtcNow;

        // WiFi 7 specific status information
        [DataMember]
        [JsonPropertyName("wifiStandard")]
        public WifiStandard WifiStandard { get; set; } = WifiStandard.WiFi6;

        [DataMember]
        [JsonPropertyName("multiLinkActive")]
        public bool MultiLinkActive { get; set; }

        [DataMember]
        [JsonPropertyName("activeBands")]
        public List<string> ActiveBands { get; set; } = new List<string>();

        [DataMember]
        [JsonPropertyName("connectionQuality")]
        public double ConnectionQuality { get; set; }

        // Enhanced properties
        [JsonIgnore]
        public bool IsConnected => Status.Equals("Connected", StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public string QualityDescription => GetQualityDescription(Signal ?? 0);

        private static string GetQualityDescription(int signal)
        {
            return signal switch
            {
                >= 90 => "Excellent",
                >= 75 => "Very Good",
                >= 60 => "Good",
                >= 40 => "Fair",
                >= 20 => "Poor",
                _ => "Very Poor"
            };
        }

        public override string ToString()
        {
            if (!IsConnected)
                return $"Status: {Status}";

            var details = new List<string>
            {
                $"SSID: {Ssid}",
                $"Signal: {Signal}% ({QualityDescription})",
                $"IP: {IpAddress}"
            };

            if (!string.IsNullOrEmpty(Bssid))
                details.Add($"BSSID: {Bssid}");

            if (Channel.HasValue)
                details.Add($"Channel: {Channel}");

            if (WifiStandard >= WifiStandard.WiFi7)
                details.Add($"Standard: {WifiStandard}");

            if (MultiLinkActive)
                details.Add($"MLO: Active ({string.Join(", ", ActiveBands)})");

            return $"Status: {Status}\n" + string.Join("\n", details);
        }
    }
}
