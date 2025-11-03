using System;
using System.Collections.Generic;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// WiFi security analysis and recommendations
    /// Based on 2024-2025 security standards (WPA3, WPA2, etc.)
    /// </summary>
    public static class WiFiSecurityAnalyzer
    {
        public enum SecurityLevel
        {
            Critical,  // Unsecured or WEP
            High,      // WPA/TKIP only
            Medium,    // WPA2 only
            Good,      // WPA2/WPA3 mixed
            Excellent  // WPA3 only
        }

        /// <summary>
        /// Analyze WiFi network security
        /// </summary>
        public static SecurityAnalysis AnalyzeSecurity(WiFiNetwork network)
        {
            var analysis = new SecurityAnalysis
            {
                SSID = network.SSID,
                Timestamp = DateTime.UtcNow,
                RawSecurityType = network.SecurityType ?? "Open"
            };

            // Parse security type
            ParseSecurityType(network.SecurityType, analysis);

            // Assign security level
            AssignSecurityLevel(analysis);

            // Generate recommendations
            GenerateRecommendations(analysis);

            return analysis;
        }

        /// <summary>
        /// Parse security type string
        /// </summary>
        private static void ParseSecurityType(string? securityType, SecurityAnalysis analysis)
        {
            if (string.IsNullOrEmpty(securityType))
            {
                analysis.IsOpen = true;
                analysis.EncryptionTypes.Add("None");
                return;
            }

            var lower = securityType.ToLower();

            // WPA3 Detection
            if (lower.Contains("wpa3"))
            {
                analysis.SupportsWPA3 = true;
                if (lower.Contains("personal"))
                    analysis.EncryptionTypes.Add("WPA3 Personal");
                else if (lower.Contains("enterprise"))
                    analysis.EncryptionTypes.Add("WPA3 Enterprise");
                else
                    analysis.EncryptionTypes.Add("WPA3");
            }

            // WPA2 Detection
            if (lower.Contains("wpa2") || lower.Contains("wpa/2"))
            {
                analysis.SupportsWPA2 = true;
                analysis.EncryptionTypes.Add("WPA2");
            }

            // WPA Detection
            if (lower.Contains("wpa") && !lower.Contains("wpa2") && !lower.Contains("wpa3"))
            {
                analysis.SupportsWPA = true;
                analysis.EncryptionTypes.Add("WPA");
            }

            // Check for TKIP (weak)
            if (lower.Contains("tkip"))
            {
                analysis.HasWeakEncryption = true;
                analysis.EncryptionTypes.Add("TKIP (Legacy)");
            }

            // Check for CCMP/AES (strong)
            if (lower.Contains("ccmp") || lower.Contains("aes"))
            {
                analysis.HasStrongEncryption = true;
            }

            // WEP Detection (critically weak)
            if (lower.Contains("wep"))
            {
                analysis.HasWeakEncryption = true;
                analysis.EncryptionTypes.Add("WEP (Critical)");
            }

            // Open Detection
            if (lower.Contains("open") || lower.Contains("none") || securityType == "")
            {
                analysis.IsOpen = true;
                analysis.EncryptionTypes.Add("Open");
            }
        }

        /// <summary>
        /// Assign security level based on capabilities
        /// </summary>
        private static void AssignSecurityLevel(SecurityAnalysis analysis)
        {
            if (analysis.IsOpen)
            {
                analysis.Level = SecurityLevel.Critical;
                return;
            }

            if (analysis.SupportsWPA3)
            {
                analysis.Level = SecurityLevel.Excellent;
                return;
            }

            if (analysis.SupportsWPA2 && !analysis.HasWeakEncryption)
            {
                analysis.Level = SecurityLevel.Good;
                return;
            }

            if (analysis.SupportsWPA2 || analysis.SupportsWPA)
            {
                if (analysis.HasWeakEncryption)
                    analysis.Level = SecurityLevel.High;
                else
                    analysis.Level = SecurityLevel.Medium;
                return;
            }

            analysis.Level = SecurityLevel.Critical;
        }

        /// <summary>
        /// Generate security recommendations
        /// </summary>
        private static void GenerateRecommendations(SecurityAnalysis analysis)
        {
            if (analysis.IsOpen)
            {
                analysis.Recommendations.Add("🚨 CRITICAL: Network is completely open. Do not use for sensitive data.");
                analysis.Recommendations.Add("Enable WPA3 Personal or at minimum WPA2 with strong password");
                return;
            }

            if (!analysis.SupportsWPA3)
            {
                analysis.Recommendations.Add("⚠️ Update to WPA3 when devices support it (WiFi 6E compatible)");
            }

            if (analysis.HasWeakEncryption)
            {
                analysis.Recommendations.Add("⚠️ TKIP/WEP detected: Disable weak encryption methods");
                analysis.Recommendations.Add("Use CCMP/AES encryption instead");
            }

            if (!analysis.HasStrongEncryption && !analysis.SupportsWPA3)
            {
                analysis.Recommendations.Add("✓ Consider enabling CCMP/AES encryption");
            }

            // 2024 Best Practices
            analysis.Recommendations.Add("✓ Use strong passphrase (16+ characters with mixed case/symbols)");
            analysis.Recommendations.Add("✓ Enable 802.11k/v/r for fast roaming support");
            analysis.Recommendations.Add("✓ Disable WPS (WiFi Protected Setup) if enabled");
            analysis.Recommendations.Add("✓ Update router firmware regularly");

            if (analysis.Level == SecurityLevel.Excellent)
            {
                analysis.Recommendations.Clear();
                analysis.Recommendations.Add("✅ Excellent security: WPA3 with strong encryption");
                analysis.Recommendations.Add("✓ Maintain regular firmware updates");
                analysis.Recommendations.Add("✓ Monitor connected devices");
            }
        }
    }

    /// <summary>
    /// Security analysis results
    /// </summary>
    public class SecurityAnalysis
    {
        public string? SSID { get; set; }
        public DateTime Timestamp { get; set; }
        public string RawSecurityType { get; set; } = "Unknown";

        public WiFiSecurityAnalyzer.SecurityLevel Level { get; set; }

        // Capability flags
        public bool IsOpen { get; set; }
        public bool SupportsWPA { get; set; }
        public bool SupportsWPA2 { get; set; }
        public bool SupportsWPA3 { get; set; }
        public bool HasWeakEncryption { get; set; }
        public bool HasStrongEncryption { get; set; }

        public List<string> EncryptionTypes { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();

        /// <summary>
        /// Get security level emoji
        /// </summary>
        public string GetLevelEmoji()
        {
            return Level switch
            {
                WiFiSecurityAnalyzer.SecurityLevel.Critical => "🔴",
                WiFiSecurityAnalyzer.SecurityLevel.High => "🟠",
                WiFiSecurityAnalyzer.SecurityLevel.Medium => "🟡",
                WiFiSecurityAnalyzer.SecurityLevel.Good => "🟢",
                WiFiSecurityAnalyzer.SecurityLevel.Excellent => "✅",
                _ => "❓"
            };
        }

        /// <summary>
        /// Format analysis for console display
        /// </summary>
        public override string ToString()
        {
            var builder = new System.Text.StringBuilder();
            builder.AppendLine($"\n{GetLevelEmoji()} Security Analysis: {SSID}");
            builder.AppendLine($"Level: {Level}");
            builder.AppendLine($"Security Type: {RawSecurityType}");

            if (EncryptionTypes.Count > 0)
            {
                builder.AppendLine($"Encryption: {string.Join(", ", EncryptionTypes)}");
            }

            builder.AppendLine();
            builder.AppendLine("Recommendations:");
            foreach (var rec in Recommendations)
            {
                builder.AppendLine($"  {rec}");
            }

            return builder.ToString();
        }
    }
}
