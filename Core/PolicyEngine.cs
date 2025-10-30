using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// Enterprise policy enforcement engine for national-level deployment
    /// </summary>
    public static class PolicyEngine
    {
        private static readonly string PolicyStoragePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MurtiWifiConnecter", "Policies");

        private static SecurityPolicy _activePolicy;
        private static readonly object _policyLock = new();
        private static DateTime _lastPolicyLoad = DateTime.MinValue;
        private static readonly TimeSpan PolicyRefreshInterval = TimeSpan.FromMinutes(5);

        public static async Task InitializeAsync()
        {
            await LoadSecurityPolicyAsync();
            await Logger.LogInfo("Policy engine initialized", nameof(PolicyEngine), new Dictionary<string, object>
            {
                ["policyLevel"] = _activePolicy?.PolicyLevel ?? "None",
                ["enforcementMode"] = _activePolicy?.EnforcementMode ?? "Permissive"
            });
        }

        public static async Task<SecurityPolicy> GetActivePolicyAsync()
        {
            var now = DateTime.Now;
            if (now - _lastPolicyLoad > PolicyRefreshInterval)
            {
                await LoadSecurityPolicyAsync();
            }

            lock (_policyLock)
            {
                return _activePolicy ?? CreateDefaultPolicy();
            }
        }

        public static async Task<PolicyValidationResult> ValidateOperationAsync(string operation, Dictionary<string, object> context)
        {
            var policy = await GetActivePolicyAsync();
            var result = new PolicyValidationResult { Allowed = true, Operation = operation };

            // Check blacklisted operations
            if (policy.BlacklistedOperations.Contains(operation, StringComparer.OrdinalIgnoreCase))
            {
                result.Allowed = false;
                result.Reason = $"Operation '{operation}' is blacklisted by policy";
                result.Severity = PolicyViolationSeverity.High;

                await AuditTrail.RecordEventAsync(
                    "Policy",
                    "OperationBlocked",
                    new Dictionary<string, object>
                    {
                        ["operation"] = operation,
                        ["reason"] = result.Reason
                    },
                    "Warning");

                return result;
            }

            // Check network security requirements
            if (context != null && context.TryGetValue("ssid", out var ssidObj))
            {
                var ssid = ssidObj?.ToString() ?? string.Empty;

                if (policy.BlockedSSIDPatterns.Any(pattern =>
                    ssid.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Allowed = false;
                    result.Reason = $"SSID matches blocked pattern";
                    result.Severity = PolicyViolationSeverity.Medium;

                    await AuditTrail.RecordEventAsync(
                        "Policy",
                        "SSIDBlocked",
                        new Dictionary<string, object>
                        {
                            ["ssid"] = ssid,
                            ["reason"] = result.Reason
                        },
                        "Warning");

                    return result;
                }
            }

            // Check security level requirements
            if (context != null && context.TryGetValue("security", out var securityObj))
            {
                var security = securityObj?.ToString() ?? string.Empty;

                if (policy.MinimumSecurityLevel == "WPA2" || policy.MinimumSecurityLevel == "WPA3")
                {
                    if (security.Contains("Open", StringComparison.OrdinalIgnoreCase) ||
                        security.Contains("WEP", StringComparison.OrdinalIgnoreCase) ||
                        (policy.MinimumSecurityLevel == "WPA3" && !security.Contains("WPA3", StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Allowed = policy.EnforcementMode == "Permissive";
                        result.Reason = $"Security level '{security}' below required '{policy.MinimumSecurityLevel}'";
                        result.Severity = PolicyViolationSeverity.High;

                        await AuditTrail.RecordEventAsync(
                            "Policy",
                            "WeakSecurityDetected",
                            new Dictionary<string, object>
                            {
                                ["security"] = security,
                                ["required"] = policy.MinimumSecurityLevel,
                                ["allowed"] = result.Allowed
                            },
                            result.Allowed ? "Warning" : "Error");

                        return result;
                    }
                }
            }

            // Check credential storage policy
            if (operation.Contains("store", StringComparison.OrdinalIgnoreCase) && !policy.AllowCredentialStorage)
            {
                result.Allowed = false;
                result.Reason = "Credential storage is disabled by policy";
                result.Severity = PolicyViolationSeverity.Medium;

                await AuditTrail.RecordEventAsync(
                    "Policy",
                    "CredentialStorageBlocked",
                    new Dictionary<string, object>
                    {
                        ["operation"] = operation
                    },
                    "Warning");

                return result;
            }

            return result;
        }

        public static async Task<bool> SaveSecurityPolicyAsync(SecurityPolicy policy)
        {
            try
            {
                Directory.CreateDirectory(PolicyStoragePath);
                var policyFile = Path.Combine(PolicyStoragePath, "security_policy.json");

                var json = JsonSerializer.Serialize(policy, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(policyFile, json);
                await SecurityManager.EnsureSecureFileAclAsync(policyFile);

                lock (_policyLock)
                {
                    _activePolicy = policy;
                    _lastPolicyLoad = DateTime.Now;
                }

                await Logger.LogInfo("Security policy saved", nameof(PolicyEngine), new Dictionary<string, object>
                {
                    ["policyLevel"] = policy.PolicyLevel,
                    ["enforcementMode"] = policy.EnforcementMode
                });

                await AuditTrail.RecordEventAsync(
                    "Policy",
                    "PolicyUpdated",
                    new Dictionary<string, object>
                    {
                        ["policyLevel"] = policy.PolicyLevel,
                        ["enforcementMode"] = policy.EnforcementMode
                    },
                    "Info");

                return true;
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Failed to save security policy");
                return false;
            }
        }

        private static async Task LoadSecurityPolicyAsync()
        {
            try
            {
                var policyFile = Path.Combine(PolicyStoragePath, "security_policy.json");

                if (!File.Exists(policyFile))
                {
                    lock (_policyLock)
                    {
                        _activePolicy = CreateDefaultPolicy();
                        _lastPolicyLoad = DateTime.Now;
                    }
                    return;
                }

                var json = await File.ReadAllTextAsync(policyFile);
                var policy = JsonSerializer.Deserialize<SecurityPolicy>(json);

                lock (_policyLock)
                {
                    _activePolicy = policy ?? CreateDefaultPolicy();
                    _lastPolicyLoad = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Failed to load security policy, using defaults");
                lock (_policyLock)
                {
                    _activePolicy = CreateDefaultPolicy();
                    _lastPolicyLoad = DateTime.Now;
                }
            }
        }

        private static SecurityPolicy CreateDefaultPolicy()
        {
            return new SecurityPolicy
            {
                PolicyLevel = "Standard",
                EnforcementMode = "Permissive",
                MinimumSecurityLevel = "WPA2",
                AllowCredentialStorage = true,
                RequireAuditLogging = true,
                MaxConnectionRetries = 3,
                BlacklistedOperations = new List<string>(),
                BlockedSSIDPatterns = new List<string> { "TEST", "FAKE", "PWNED" },
                AllowedSecurityTypes = new List<string> { "WPA2PSK", "WPA3SAE", "WPA2-Enterprise", "WPA3-Enterprise" },
                ComplianceStandards = new List<string> { "Generic" }
            };
        }

        public class SecurityPolicy
        {
            public string PolicyLevel { get; set; } // Basic, Standard, Strict, Enterprise
            public string EnforcementMode { get; set; } // Permissive, Enforcing, Blocking
            public string MinimumSecurityLevel { get; set; } // Open, WPA2, WPA3
            public bool AllowCredentialStorage { get; set; }
            public bool RequireAuditLogging { get; set; }
            public int MaxConnectionRetries { get; set; }
            public List<string> BlacklistedOperations { get; set; } = new();
            public List<string> BlockedSSIDPatterns { get; set; } = new();
            public List<string> AllowedSecurityTypes { get; set; } = new();
            public List<string> ComplianceStandards { get; set; } = new(); // NIST, GDPR, HIPAA, etc.
        }

        public class PolicyValidationResult
        {
            public bool Allowed { get; set; }
            public string Operation { get; set; }
            public string Reason { get; set; }
            public PolicyViolationSeverity Severity { get; set; }
        }

        public enum PolicyViolationSeverity
        {
            Low,
            Medium,
            High,
            Critical
        }
    }
}
