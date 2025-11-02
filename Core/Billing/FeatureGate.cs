using System;
using System.Collections.Generic;
using System.Linq;

namespace MurtiWifiConnecter.Core.Billing
{
    /// <summary>
    /// Feature gating system that restricts functionality based on billing edition.
    /// </summary>
    public static class FeatureGate
    {
        // Feature names mapped to minimum required edition
        private static readonly Dictionary<string, BillingEdition> FeatureRequirements = new(StringComparer.OrdinalIgnoreCase)
        {
            // Free tier features (everyone)
            ["scan"] = BillingEdition.Free,
            ["connect"] = BillingEdition.Free,
            ["disconnect"] = BillingEdition.Free,
            ["status"] = BillingEdition.Free,
            ["help"] = BillingEdition.Free,
            ["version"] = BillingEdition.Free,
            ["profiles"] = BillingEdition.Free,

            // Professional tier features
            ["automation"] = BillingEdition.Professional,
            ["monitor"] = BillingEdition.Professional,
            ["realtime"] = BillingEdition.Professional,
            ["predict"] = BillingEdition.Professional,
            ["analytics"] = BillingEdition.Professional,
            ["speed"] = BillingEdition.Professional,
            ["backup"] = BillingEdition.Professional,
            ["restore"] = BillingEdition.Professional,
            ["history"] = BillingEdition.Professional,

            // Enterprise tier features
            ["audit-trail"] = BillingEdition.Enterprise,
            ["security-scan"] = BillingEdition.Enterprise,
            ["security-audit"] = BillingEdition.Enterprise,
            ["compliance"] = BillingEdition.Enterprise,
            ["report"] = BillingEdition.Enterprise,
            ["command-anomalies"] = BillingEdition.Enterprise,
            ["command-metrics"] = BillingEdition.Enterprise,
            ["security-metrics"] = BillingEdition.Enterprise,
            ["health"] = BillingEdition.Enterprise,
            ["performance"] = BillingEdition.Enterprise
        };

        // Resource limits per edition
        private static readonly Dictionary<BillingEdition, ResourceLimits> EditionLimits = new()
        {
            [BillingEdition.Free] = new ResourceLimits
            {
                MaxPreferredNetworks = 5,
                MaxBackupRetention = 7,
                MaxLogRetentionDays = 7,
                MaxHistoryEntries = 50,
                AllowAutomation = false,
                AllowAdvancedSecurity = false
            },
            [BillingEdition.Professional] = new ResourceLimits
            {
                MaxPreferredNetworks = 50,
                MaxBackupRetention = 30,
                MaxLogRetentionDays = 90,
                MaxHistoryEntries = 1000,
                AllowAutomation = true,
                AllowAdvancedSecurity = false
            },
            [BillingEdition.Enterprise] = new ResourceLimits
            {
                MaxPreferredNetworks = int.MaxValue,
                MaxBackupRetention = 365,
                MaxLogRetentionDays = 365,
                MaxHistoryEntries = int.MaxValue,
                AllowAutomation = true,
                AllowAdvancedSecurity = true
            }
        };

        public sealed class ResourceLimits
        {
            public int MaxPreferredNetworks { get; init; }
            public int MaxBackupRetention { get; init; }
            public int MaxLogRetentionDays { get; init; }
            public int MaxHistoryEntries { get; init; }
            public bool AllowAutomation { get; init; }
            public bool AllowAdvancedSecurity { get; init; }
        }

        /// <summary>
        /// Get the minimum required edition for a feature.
        /// </summary>
        public static BillingEdition GetRequiredEdition(string featureName)
        {
            if (string.IsNullOrWhiteSpace(featureName))
            {
                return BillingEdition.Free;
            }

            // Normalize feature name (remove aliases)
            var normalized = NormalizeFeatureName(featureName);

            if (FeatureRequirements.TryGetValue(normalized, out var required))
            {
                return required;
            }

            // Default to Free if feature not explicitly gated
            return BillingEdition.Free;
        }

        /// <summary>
        /// Get resource limits for a billing edition.
        /// </summary>
        public static ResourceLimits GetLimits(BillingEdition edition)
        {
            if (EditionLimits.TryGetValue(edition, out var limits))
            {
                return limits;
            }
            return EditionLimits[BillingEdition.Free];
        }

        /// <summary>
        /// Get all features available for a specific edition.
        /// </summary>
        public static IReadOnlyList<string> GetAvailableFeatures(BillingEdition edition)
        {
            return FeatureRequirements
                .Where(kv => kv.Value <= edition)
                .Select(kv => kv.Key)
                .OrderBy(f => f)
                .ToArray();
        }

        /// <summary>
        /// Get features locked behind a specific edition.
        /// </summary>
        public static IReadOnlyList<string> GetLockedFeatures(BillingEdition currentEdition)
        {
            return FeatureRequirements
                .Where(kv => kv.Value > currentEdition)
                .Select(kv => kv.Key)
                .OrderBy(f => f)
                .ToArray();
        }

        /// <summary>
        /// Get upgrade benefits when moving from one edition to another.
        /// </summary>
        public static UpgradeBenefits GetUpgradeBenefits(BillingEdition from, BillingEdition to)
        {
            var currentLimits = GetLimits(from);
            var newLimits = GetLimits(to);
            var unlockedFeatures = FeatureRequirements
                .Where(kv => kv.Value <= to && kv.Value > from)
                .Select(kv => kv.Key)
                .ToArray();

            return new UpgradeBenefits
            {
                FromEdition = from,
                ToEdition = to,
                UnlockedFeatures = unlockedFeatures,
                NetworkLimitIncrease = newLimits.MaxPreferredNetworks == int.MaxValue
                    ? "Unlimited"
                    : $"{currentLimits.MaxPreferredNetworks} → {newLimits.MaxPreferredNetworks}",
                LogRetentionIncrease = $"{currentLimits.MaxLogRetentionDays} → {newLimits.MaxLogRetentionDays} days",
                NewCapabilities = BuildCapabilityList(from, to)
            };
        }

        public sealed class UpgradeBenefits
        {
            public BillingEdition FromEdition { get; init; }
            public BillingEdition ToEdition { get; init; }
            public string[] UnlockedFeatures { get; init; }
            public string NetworkLimitIncrease { get; init; }
            public string LogRetentionIncrease { get; init; }
            public string[] NewCapabilities { get; init; }
        }

        private static string NormalizeFeatureName(string featureName)
        {
            // Map aliases to canonical names
            var aliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["c"] = "connect",
                ["d"] = "disconnect",
                ["s"] = "status",
                ["h"] = "help",
                ["v"] = "version",
                ["p"] = "profiles",
                ["q"] = "quick",
                ["a"] = "available",
                ["i"] = "info",
                ["r"] = "reset",
                ["cmd-anomalies"] = "command-anomalies",
                ["cmd-metrics"] = "command-metrics",
                ["metrics"] = "security-metrics",
                ["diag"] = "diagnostics"
            };

            if (aliasMap.TryGetValue(featureName, out var canonical))
            {
                return canonical;
            }

            return featureName;
        }

        private static string[] BuildCapabilityList(BillingEdition from, BillingEdition to)
        {
            var capabilities = new List<string>();
            var fromLimits = GetLimits(from);
            var toLimits = GetLimits(to);

            if (!fromLimits.AllowAutomation && toLimits.AllowAutomation)
            {
                capabilities.Add("Network automation and scheduled tasks");
            }

            if (!fromLimits.AllowAdvancedSecurity && toLimits.AllowAdvancedSecurity)
            {
                capabilities.Add("Advanced security scanning and audit trails");
                capabilities.Add("Compliance reporting and policy enforcement");
            }

            if (to == BillingEdition.Professional && from == BillingEdition.Free)
            {
                capabilities.Add("Real-time monitoring and alerts");
                capabilities.Add("Predictive network quality analysis");
                capabilities.Add("Speed testing and performance metrics");
            }

            if (to == BillingEdition.Enterprise)
            {
                capabilities.Add("Priority support and custom policies");
                capabilities.Add("Command anomaly detection");
                capabilities.Add("Comprehensive security metrics");
            }

            return capabilities.ToArray();
        }
    }
}
