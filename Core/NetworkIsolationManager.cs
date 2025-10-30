using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// Manages network segmentation between guest and internal networks
    /// </summary>
    public static class NetworkIsolationManager
    {
        private static readonly string IsolationConfigPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MurtiWifiConnecter", "NetworkIsolation");

        private static NetworkIsolationConfig _config;
        private static readonly object _configLock = new();
        private static DateTime _lastConfigLoad = DateTime.MinValue;
        private static readonly TimeSpan ConfigRefreshInterval = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Initialize the network isolation manager
        /// </summary>
        public static async Task InitializeAsync()
        {
            await LoadIsolationConfigAsync();
            await Logger.LogInfo("Network isolation manager initialized", nameof(NetworkIsolationManager));
        }

        /// <summary>
        /// Classify a network as guest or internal based on SSID patterns and security
        /// </summary>
        public static async Task<NetworkClassification> ClassifyNetworkAsync(string ssid, string security, string band = null)
        {
            var config = await GetIsolationConfigAsync();

            var classification = new NetworkClassification
            {
                Ssid = ssid,
                Security = security,
                Band = band ?? "Unknown",
                IsGuestNetwork = false,
                IsInternalNetwork = false,
                IsolationLevel = NetworkIsolationLevel.None,
                AccessRestrictions = new List<string>(),
                Recommendations = new List<string>()
            };

            // Check guest network patterns
            if (config.GuestNetworkPatterns.Any(pattern =>
                ssid.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
            {
                classification.IsGuestNetwork = true;
                classification.IsolationLevel = NetworkIsolationLevel.Guest;
                classification.AccessRestrictions.Add("Limited to internet access only");
                classification.AccessRestrictions.Add("No access to internal resources");
                classification.Recommendations.Add("Use separate VLAN for guest traffic");
                classification.Recommendations.Add("Implement captive portal for guest authentication");
            }

            // Check internal network patterns
            if (config.InternalNetworkPatterns.Any(pattern =>
                ssid.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
            {
                classification.IsInternalNetwork = true;
                if (classification.IsGuestNetwork)
                {
                    // Conflict - prefer internal classification for security
                    classification.IsGuestNetwork = false;
                    classification.IsolationLevel = NetworkIsolationLevel.Internal;
                    classification.AccessRestrictions.Clear();
                    classification.Recommendations.Clear();
                    classification.Recommendations.Add("Internal network detected - ensure proper security policies");
                }
                else
                {
                    classification.IsolationLevel = NetworkIsolationLevel.Internal;
                    classification.Recommendations.Add("Ensure WPA3-Enterprise or equivalent security");
                    classification.Recommendations.Add("Implement network access control (NAC)");
                }
            }

            // Security-based classification
            if (!classification.IsGuestNetwork && !classification.IsInternalNetwork)
            {
                if (security.Contains("WPA3-Enterprise", StringComparison.OrdinalIgnoreCase))
                {
                    classification.IsInternalNetwork = true;
                    classification.IsolationLevel = NetworkIsolationLevel.Internal;
                    classification.Recommendations.Add("Enterprise-grade security detected");
                }
                else if (security.Contains("Open", StringComparison.OrdinalIgnoreCase) ||
                         security.Contains("WEP", StringComparison.OrdinalIgnoreCase))
                {
                    classification.IsGuestNetwork = true;
                    classification.IsolationLevel = NetworkIsolationLevel.Guest;
                    classification.AccessRestrictions.Add("High security risk - avoid sensitive activities");
                    classification.Recommendations.Add("Implement WPA3-Personal minimum security");
                }
                else if (security.Contains("WPA3-Personal", StringComparison.OrdinalIgnoreCase))
                {
                    classification.IsInternalNetwork = true;
                    classification.IsolationLevel = NetworkIsolationLevel.Internal;
                    classification.Recommendations.Add("Good security for small office/home office");
                }
            }

            // Band-based recommendations
            if (!string.IsNullOrEmpty(band))
            {
                if (band.Contains("5GHz", StringComparison.OrdinalIgnoreCase) ||
                    band.Contains("6GHz", StringComparison.OrdinalIgnoreCase))
                {
                    classification.Recommendations.Add("High-bandwidth network suitable for demanding applications");
                }
                else if (band.Contains("2.4GHz", StringComparison.OrdinalIgnoreCase))
                {
                    classification.Recommendations.Add("2.4GHz band may have interference from other devices");
                    if (classification.IsInternalNetwork)
                    {
                        classification.Recommendations.Add("Consider using 5GHz or 6GHz for internal networks when possible");
                    }
                }
            }

            await Logger.LogDebug("Network classified", nameof(NetworkIsolationManager), new Dictionary<string, object>
            {
                ["ssid"] = ssid,
                ["classification"] = classification.IsolationLevel.ToString(),
                ["isGuest"] = classification.IsGuestNetwork,
                ["isInternal"] = classification.IsInternalNetwork
            });

            return classification;
        }

        /// <summary>
        /// Get isolation recommendations for network setup
        /// </summary>
        public static async Task<List<IsolationRecommendation>> GetIsolationRecommendationsAsync()
        {
            var recommendations = new List<IsolationRecommendation>();
            var config = await GetIsolationConfigAsync();

            // Basic isolation recommendations
            recommendations.Add(new IsolationRecommendation
            {
                Priority = RecommendationPriority.High,
                Category = "Network Segmentation",
                Title = "Implement Guest/Internal Network Separation",
                Description = "Separate guest and internal networks using different SSIDs and VLANs to prevent lateral movement in case of compromise.",
                ImplementationSteps = new List<string>
                {
                    "Create separate SSID for guest access (e.g., 'Company-Guest')",
                    "Configure guest network with internet-only access",
                    "Use WPA3-Personal for guest network with strong password",
                    "Implement captive portal for guest authentication",
                    "Configure firewall rules to block guest-to-internal traffic"
                },
                Benefits = new List<string>
                {
                    "Prevents malware spread from guest devices",
                    "Protects internal resources from unauthorized access",
                    "Improves network performance by isolating guest traffic",
                    "Complies with security best practices"
                }
            });

            recommendations.Add(new IsolationRecommendation
            {
                Priority = RecommendationPriority.Medium,
                Category = "Security Enhancement",
                Title = "Implement WPA3-Enterprise for Internal Networks",
                Description = "Use WPA3-Enterprise with RADIUS authentication for maximum security on internal networks.",
                ImplementationSteps = new List<string>
                {
                    "Set up RADIUS server (Microsoft NPS, FreeRADIUS, etc.)",
                    "Configure certificates for 802.1X authentication",
                    "Enable Protected Management Frames (PMF)",
                    "Test with enterprise devices before full deployment"
                },
                Benefits = new List<string>
                {
                    "Individual user authentication instead of shared passwords",
                    "Enhanced protection against attacks",
                    "Better audit trail of network access",
                    "Compliance with enterprise security standards"
                }
            });

            recommendations.Add(new IsolationRecommendation
            {
                Priority = RecommendationPriority.Medium,
                Category = "Monitoring & Control",
                Title = "Deploy Network Access Control (NAC)",
                Description = "Implement NAC to automatically segment and control network access based on device type and user identity.",
                ImplementationSteps = new List<string>
                {
                    "Assess current network infrastructure compatibility",
                    "Choose NAC solution (Cisco ISE, Microsoft NPS, third-party)",
                    "Define access policies for different user/device types",
                    "Implement gradual rollout with testing"
                },
                Benefits = new List<string>
                {
                    "Automated network segmentation",
                    "Real-time security policy enforcement",
                    "Improved visibility into network usage",
                    "Reduced manual configuration overhead"
                }
            });

            // Custom recommendations based on configuration
            if (config.GuestNetworkPatterns.Count == 0)
            {
                recommendations.Add(new IsolationRecommendation
                {
                    Priority = RecommendationPriority.High,
                    Category = "Configuration",
                    Title = "Configure Guest Network Detection",
                    Description = "Define patterns to automatically identify guest networks for proper isolation.",
                    ImplementationSteps = new List<string>
                    {
                        "Identify common guest network naming patterns",
                        "Configure guest network patterns in isolation settings",
                        "Test automatic classification with sample networks"
                    }
                });
            }

            return recommendations.OrderByDescending(r => r.Priority).ToList();
        }

        /// <summary>
        /// Validate network isolation configuration
        /// </summary>
        public static async Task<List<IsolationValidationResult>> ValidateIsolationSetupAsync()
        {
            var results = new List<IsolationValidationResult>();
            var config = await GetIsolationConfigAsync();

            // Check guest network patterns
            if (config.GuestNetworkPatterns.Count == 0)
            {
                results.Add(new IsolationValidationResult
                {
                    CheckType = "Guest Network Detection",
                    Status = ValidationStatus.Warning,
                    Message = "No guest network patterns configured",
                    Recommendation = "Configure patterns to identify guest networks (e.g., 'Guest', 'Visitor', 'Public')"
                });
            }
            else
            {
                results.Add(new IsolationValidationResult
                {
                    CheckType = "Guest Network Detection",
                    Status = ValidationStatus.Pass,
                    Message = $"Configured {config.GuestNetworkPatterns.Count} guest network patterns"
                });
            }

            // Check internal network patterns
            if (config.InternalNetworkPatterns.Count == 0)
            {
                results.Add(new IsolationValidationResult
                {
                    CheckType = "Internal Network Detection",
                    Status = ValidationStatus.Warning,
                    Message = "No internal network patterns configured",
                    Recommendation = "Configure patterns to identify internal networks (e.g., 'Corporate', 'Office', 'Secure')"
                });
            }
            else
            {
                results.Add(new IsolationValidationResult
                {
                    CheckType = "Internal Network Detection",
                    Status = ValidationStatus.Pass,
                    Message = $"Configured {config.InternalNetworkPatterns.Count} internal network patterns"
                });
            }

            // Check for conflicting patterns
            var conflicts = config.GuestNetworkPatterns.Intersect(
                config.InternalNetworkPatterns, StringComparer.OrdinalIgnoreCase).ToList();

            if (conflicts.Any())
            {
                results.Add(new IsolationValidationResult
                {
                    CheckType = "Pattern Conflicts",
                    Status = ValidationStatus.Error,
                    Message = $"Conflicting patterns detected: {string.Join(", ", conflicts)}",
                    Recommendation = "Remove overlapping patterns between guest and internal network definitions"
                });
            }
            else
            {
                results.Add(new IsolationValidationResult
                {
                    CheckType = "Pattern Conflicts",
                    Status = ValidationStatus.Pass,
                    Message = "No conflicting patterns detected"
                });
            }

            return results;
        }

        /// <summary>
        /// Get current isolation configuration
        /// </summary>
        private static async Task<NetworkIsolationConfig> GetIsolationConfigAsync()
        {
            var now = DateTime.Now;
            if (now - _lastConfigLoad > ConfigRefreshInterval)
            {
                await LoadIsolationConfigAsync();
            }

            lock (_configLock)
            {
                return _config ?? CreateDefaultConfig();
            }
        }

        /// <summary>
        /// Load isolation configuration from storage
        /// </summary>
        private static async Task LoadIsolationConfigAsync()
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(IsolationConfigPath)!);
                var configFile = System.IO.Path.Combine(IsolationConfigPath, "isolation_config.json");

                if (System.IO.File.Exists(configFile))
                {
                    var json = await System.IO.File.ReadAllTextAsync(configFile);
                    var config = System.Text.Json.JsonSerializer.Deserialize<NetworkIsolationConfig>(json, new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                    });

                    lock (_configLock)
                    {
                        _config = config ?? CreateDefaultConfig();
                        _lastConfigLoad = DateTime.Now;
                    }
                }
                else
                {
                    lock (_configLock)
                    {
                        _config = CreateDefaultConfig();
                        _lastConfigLoad = DateTime.Now;
                    }
                    await SaveIsolationConfigAsync(_config);
                }
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Failed to load isolation config");
                lock (_configLock)
                {
                    _config = CreateDefaultConfig();
                    _lastConfigLoad = DateTime.Now;
                }
            }
        }

        /// <summary>
        /// Save isolation configuration to storage
        /// </summary>
        private static async Task SaveIsolationConfigAsync(NetworkIsolationConfig config)
        {
            try
            {
                var configFile = System.IO.Path.Combine(IsolationConfigPath, "isolation_config.json");
                var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });

                await System.IO.File.WriteAllTextAsync(configFile, json);
                await SecurityManager.EnsureSecureFileAclAsync(configFile);
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Failed to save isolation config");
            }
        }

        /// <summary>
        /// Create default isolation configuration
        /// </summary>
        private static NetworkIsolationConfig CreateDefaultConfig()
        {
            return new NetworkIsolationConfig
            {
                GuestNetworkPatterns = new List<string>
                {
                    "guest",
                    "visitor",
                    "public",
                    "wifi",
                    "free",
                    "customer"
                },
                InternalNetworkPatterns = new List<string>
                {
                    "corporate",
                    "office",
                    "internal",
                    "secure",
                    "private",
                    "admin",
                    "staff"
                },
                LastModified = DateTime.Now,
                Version = "1.0"
            };
        }

        /// <summary>
        /// Network classification result
        /// </summary>
        public class NetworkClassification
        {
            public string Ssid { get; set; }
            public string Security { get; set; }
            public string Band { get; set; }
            public bool IsGuestNetwork { get; set; }
            public bool IsInternalNetwork { get; set; }
            public NetworkIsolationLevel IsolationLevel { get; set; }
            public List<string> AccessRestrictions { get; set; } = new();
            public List<string> Recommendations { get; set; } = new();
        }

        /// <summary>
        /// Network isolation levels
        /// </summary>
        public enum NetworkIsolationLevel
        {
            None,
            Guest,
            Internal
        }

        /// <summary>
        /// Isolation recommendation
        /// </summary>
        public class IsolationRecommendation
        {
            public RecommendationPriority Priority { get; set; }
            public string Category { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public List<string> ImplementationSteps { get; set; } = new();
            public List<string> Benefits { get; set; } = new();
        }

        /// <summary>
        /// Recommendation priority levels
        /// </summary>
        public enum RecommendationPriority
        {
            Low,
            Medium,
            High
        }

        /// <summary>
        /// Isolation validation result
        /// </summary>
        public class IsolationValidationResult
        {
            public string CheckType { get; set; }
            public ValidationStatus Status { get; set; }
            public string Message { get; set; }
            public string Recommendation { get; set; }
        }

        /// <summary>
        /// Validation status
        /// </summary>
        public enum ValidationStatus
        {
            Pass,
            Warning,
            Error
        }

        /// <summary>
        /// Network isolation configuration
        /// </summary>
        private class NetworkIsolationConfig
        {
            public List<string> GuestNetworkPatterns { get; set; } = new();
            public List<string> InternalNetworkPatterns { get; set; } = new();
            public DateTime LastModified { get; set; }
            public string Version { get; set; }
        }
    }
}
