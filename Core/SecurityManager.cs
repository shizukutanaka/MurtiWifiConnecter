using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    public static class SecurityManager
    {
        private static readonly byte[] _entropy = Encoding.UTF8.GetBytes("MurtiWifiConnecterV2.0");
        private static readonly string SecureStoragePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MurtiWifiConnecter", "Secure");
        private static readonly SecurityIdentifier AdministratorsSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);
        private static readonly SecurityIdentifier LocalSystemSid = new(WellKnownSidType.LocalSystemSid, null);
        private static readonly string SecureTempPath = Path.Combine(SecureStoragePath, "Temp");
        private const string CredentialDigestExtension = ".meta";
        private const int CredentialRotationThresholdDays = 90;
        private static bool _isInitialized = false;
        private static readonly object _initLock = new();

        private static readonly Dictionary<string, DateTime> _accessLog = new();
        private static readonly ZeroTrustEvaluator _zeroTrustEvaluator = new ZeroTrustEvaluator();
        private static readonly AdaptivePolicyEngine _adaptivePolicyEngine = new AdaptivePolicyEngine();
        private static readonly SessionManager _sessionManager = new SessionManager();
        private static readonly Dictionary<string, byte[]> _integrityKeys = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _integrityKeyLock = new();
        private static readonly Dictionary<string, RateLimitEntry> _rateLimits = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _rateLimitLock = new();
        private static readonly TimeSpan RateLimitRetentionWindow = TimeSpan.FromHours(1);
        private static readonly TimeSpan PolicyRefreshInterval = TimeSpan.FromSeconds(30);
        private static TimeSpan _commandRateLimitWindow = TimeSpan.FromMinutes(1);
        private static int _commandRateLimitMaxAttempts = 10;
        private static TimeSpan _globalRateLimitWindow = TimeSpan.FromSeconds(10);
        private static int _globalRateLimitMaxAttempts = 200;
        private static DateTime _lastPolicyRefreshUtc = DateTime.MinValue;
        private static int _globalAttemptCount = 0;
        private static int _globalViolationCount = 0;
        private static DateTime _globalWindowStart = DateTime.MinValue;
        private static bool _securityBannerShown = false;
        private static readonly object _bannerLock = new();
        private static int _rateLimitRejectionCount = 0;
        private static int _globalRateLimitRejectionCount = 0;
        private static DateTime _lastRateLimitResetUtc = DateTime.MinValue;

        public readonly struct RateLimitMetrics
        {
            public int CommandRejections { get; init; }
            public int GlobalRejections { get; init; }
            public int TrackedOperations { get; init; }
            public TimeSpan CommandWindow { get; init; }
            public int CommandMaxAttempts { get; init; }
            public TimeSpan GlobalWindow { get; init; }
            public int GlobalMaxAttempts { get; init; }
            public DateTime LastResetUtc { get; init; }
        }

        public static void ResetRateLimitMetrics()
        {
            lock (_rateLimitLock)
            {
                _rateLimitRejectionCount = 0;
                _globalRateLimitRejectionCount = 0;
                _rateLimits.Clear();
                _globalAttemptCount = 0;
                _globalViolationCount = 0;
                _globalWindowStart = DateTime.MinValue;
                _lastRateLimitResetUtc = DateTime.UtcNow;
            }
        }

        public static RateLimitMetrics GetRateLimitMetrics()
        {
            lock (_rateLimitLock)
            {
                return new RateLimitMetrics
                {
                    CommandRejections = _rateLimitRejectionCount,
                    GlobalRejections = _globalRateLimitRejectionCount,
                    TrackedOperations = _rateLimits.Count,
                    CommandWindow = _commandRateLimitWindow,
                    CommandMaxAttempts = _commandRateLimitMaxAttempts,
                    GlobalWindow = _globalRateLimitWindow,
                    GlobalMaxAttempts = _globalRateLimitMaxAttempts,
                    LastResetUtc = _lastRateLimitResetUtc
                };
            }
        }

        /// <summary>
        /// Initialize SecurityManager components (thread-safe, idempotent)
        /// </summary>
        public static async Task InitializeAsync()
        {
            if (_isInitialized)
                return;

            lock (_initLock)
            {
                if (_isInitialized)
                    return;

                _isInitialized = true;
            }

            try
            {
                // Ensure secure storage directories exist with proper ACLs
                await EnsureSecureStorageDirectoryAsync();
                await EnsureSecureTempDirectoryAsync();

                // Clean up old temporary files on startup
                await CleanupOldTempFilesAsync();

                await Logger.LogInfo("SecurityManager initialized", nameof(SecurityManager), new Dictionary<string, object>
                {
                    ["SecureStoragePath"] = SecureStoragePath,
                    ["TempPath"] = SecureTempPath
                });
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Failed to initialize SecurityManager");
                throw;
            }
        }

        /// <summary>
        /// Clean up temporary files older than 24 hours
        /// </summary>
        private static async Task CleanupOldTempFilesAsync()
        {
            try
            {
                if (!Directory.Exists(SecureTempPath))
                    return;

                var cutoffTime = DateTime.Now.AddHours(-24);
                var tempFiles = Directory.GetFiles(SecureTempPath);

                foreach (var file in tempFiles)
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        if (fileInfo.LastAccessTime < cutoffTime)
                        {
                            await SecureDeleteFileAsync(file);
                        }
                    }
                    catch
                    {
                        // Ignore errors for individual files
                    }
                }
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Failed to cleanup old temp files");
            }
        }

        public enum RateLimitScope
        {
            None = 0,
            Command = 1,
            Global = 2
        }

        public readonly struct RateLimitResult
        {
            public bool Allowed { get; init; }
            public RateLimitScope Scope { get; init; }
            public string Operation { get; init; }
            public int CommandViolations { get; init; }
            public DateTime? CommandViolationTime { get; init; }
            public int GlobalViolations { get; init; }
            public DateTime? GlobalViolationTime { get; init; }
            public DateTime Timestamp { get; init; }
        }

        public static async Task<string> EncryptPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
                return password;

            try
            {
                // Use Windows DPAPI for local encryption
                var passwordBytes = Encoding.UTF8.GetBytes(password);
                var encryptedBytes = ProtectedData.Protect(passwordBytes, _entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encryptedBytes);
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Password encryption failed");
                throw new SecurityException("Failed to encrypt password securely", ex);
            }
        }

        public static async Task<string> DecryptPassword(string encryptedPassword)
        {
            if (string.IsNullOrEmpty(encryptedPassword))
                return encryptedPassword;

            try
            {
                var encryptedBytes = Convert.FromBase64String(encryptedPassword);
                var passwordBytes = ProtectedData.Unprotect(encryptedBytes, _entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(passwordBytes);
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Password decryption failed");
                throw new SecurityException("Failed to decrypt password", ex);
            }
        }

        public static async Task<RateLimitResult> CheckRateLimitAsync(string operation)
        {
            var now = DateTime.Now;
            await RefreshRateLimitPolicyAsync(now);

            var result = new RateLimitResult
            {
                Allowed = true,
                Scope = RateLimitScope.None,
                Operation = operation,
                Timestamp = now
            };
            int currentViolations = 0;
            DateTime? violationTime = null;
            int globalViolations = 0;
            DateTime? globalViolationTime = null;

            lock (_rateLimitLock)
            {
                // Remove stale entries to keep the rate limit state lean for long running processes
                if (_rateLimits.Count > 0)
                {
                    var expiredKeys = _rateLimits
                        .Where(kvp => now - kvp.Value.LastAttempt > RateLimitRetentionWindow)
                        .Select(kvp => kvp.Key)
                        .ToList();
                    foreach (var expired in expiredKeys)
                    {
                        _rateLimits.Remove(expired);
                    }
                }

                if (!_rateLimits.TryGetValue(operation, out var entry))
                {
                    entry = new RateLimitEntry
                    {
                        FirstAttempt = now,
                        AttemptCount = 0,
                        LastAttempt = now,
                        TotalViolations = 0
                    };
                    _rateLimits[operation] = entry;
                }

                if (now - entry.FirstAttempt > _commandRateLimitWindow)
                {
                    entry.FirstAttempt = now;
                    entry.AttemptCount = 0;
                }

                if (entry.AttemptCount >= _commandRateLimitMaxAttempts)
                {
                    entry.TotalViolations++;
                    entry.LastViolation = now;
                    entry.LastAttempt = now;
                    result.Allowed = false;
                    result.Scope = RateLimitScope.Command;
                    currentViolations = entry.TotalViolations;
                    violationTime = entry.LastViolation;
                    _rateLimitRejectionCount++;
                }
                else
                {
                    entry.AttemptCount++;
                    entry.LastAttempt = now;
                }

                if (result.Allowed)
                {
                    if (_globalWindowStart == DateTime.MinValue || now - _globalWindowStart > _globalRateLimitWindow)
                    {
                        _globalWindowStart = now;
                        _globalAttemptCount = 0;
                    }

                    _globalAttemptCount++;

                    if (_globalAttemptCount > _globalRateLimitMaxAttempts)
                    {
                        _globalViolationCount++;
                        result.Allowed = false;
                        result.Scope = RateLimitScope.Global;
                        globalViolations = _globalViolationCount;
                        globalViolationTime = now;
                        _globalRateLimitRejectionCount++;
                    }
                }
            }

            if (!result.Allowed)
            {
                if (result.Scope == RateLimitScope.Command)
                {
                    await Logger.LogWarning("Rate limit exceeded", nameof(SecurityManager), new Dictionary<string, object>
                    {
                        ["operation"] = operation,
                        ["maxAttemptsPerWindow"] = _commandRateLimitMaxAttempts,
                        ["windowSeconds"] = _commandRateLimitWindow.TotalSeconds,
                        ["totalViolations"] = currentViolations,
                        ["lastViolation"] = violationTime
                    });
                    await AuditTrail.RecordEventAsync(
                        "Security",
                        "RateLimitViolation",
                        new Dictionary<string, object>
                        {
                            ["operation"] = operation,
                            ["violations"] = currentViolations,
                            ["windowSeconds"] = _commandRateLimitWindow.TotalSeconds
                        },
                        "Warning");
                }
                else if (result.Scope == RateLimitScope.Global)
                {
                    await Logger.LogWarning("Global rate limit exceeded", nameof(SecurityManager), new Dictionary<string, object>
                    {
                        ["operation"] = operation,
                        ["maxGlobalAttemptsPerWindow"] = _globalRateLimitMaxAttempts,
                        ["globalWindowSeconds"] = _globalRateLimitWindow.TotalSeconds,
                        ["globalViolations"] = globalViolations,
                        ["violationTimestamp"] = globalViolationTime
                    });

                    await AuditTrail.RecordEventAsync(
                        "Security",
                        "GlobalRateLimitViolation",
                        new Dictionary<string, object>
                        {
                            ["operation"] = operation,
                            ["violations"] = globalViolations,
                            ["windowSeconds"] = _globalRateLimitWindow.TotalSeconds
                        },
                        "Warning");
                }
            }

            result.CommandViolations = currentViolations;
            result.CommandViolationTime = violationTime;
            result.GlobalViolations = globalViolations;
            result.GlobalViolationTime = globalViolationTime;
            result.Timestamp = now;

            return result;
        }

        private static async Task RefreshRateLimitPolicyAsync(DateTime now)
        {
            var lastRefresh = Volatile.Read(ref _lastPolicyRefreshUtc);
            if (lastRefresh != DateTime.MinValue && now - lastRefresh < PolicyRefreshInterval)
            {
                return;
            }

            var config = await ConfigManager.LoadConfig().ConfigureAwait(false);
            if (config == null)
            {
                return;
            }

            lock (_rateLimitLock)
            {
                lastRefresh = _lastPolicyRefreshUtc;
                if (lastRefresh != DateTime.MinValue && now - lastRefresh < PolicyRefreshInterval)
                {
                    return;
                }

                _commandRateLimitMaxAttempts = Math.Clamp(config.RateLimitCommandMaxAttempts, 1, 1000);
                _commandRateLimitWindow = TimeSpan.FromSeconds(Math.Clamp(config.RateLimitCommandWindowSeconds, 1, 3600));
                _globalRateLimitMaxAttempts = Math.Clamp(config.RateLimitGlobalMaxAttempts, 1, 10000);
                _globalRateLimitWindow = TimeSpan.FromSeconds(Math.Clamp(config.RateLimitGlobalWindowSeconds, 1, 3600));
                Volatile.Write(ref _lastPolicyRefreshUtc, now);
            }
        }

        public static void ShowSecurityBanner()
        {
            lock (_bannerLock)
            {
                if (_securityBannerShown) return;
                _securityBannerShown = true;
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("┌─────────────────────────────────────────────────────────────────┐");
            Console.WriteLine("│                    SECURITY NOTICE                             │");
            Console.WriteLine("│  This system is monitored and audited for security compliance   │");
            Console.WriteLine("│  Unauthorized access is prohibited and will be reported         │");
            Console.WriteLine("│  All activities are logged and may be reviewed by administrators│");
            Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");
            Console.ResetColor();
            Console.WriteLine();
        }

        public static async Task<SecurityAuditReport> GenerateSecurityAuditReport()
        {
            var report = new SecurityAuditReport
            {
                GeneratedAt = DateTime.Now,
                Issues = new List<SecurityIssue>(),
                Recommendations = new List<string>()
            };

            // Check for weak security configurations
            var config = await ConfigManager.LoadConfig();
            var issues = 0;
            var criticalIssues = 0;

            // Check credential rotation
            var overdueCredentialCount = await CountCredentialsPastRotationWindowAsync();
            if (overdueCredentialCount > 0)
            {
                report.Issues.Add(new SecurityIssue
                {
                    Severity = SecuritySeverity.High,
                    Category = "Credential Management",
                    Description = $"{overdueCredentialCount} stored credential(s) exceed the {CredentialRotationThresholdDays}-day rotation window",
                    Recommendation = "Rotate stored credentials and redistribute updated secrets"
                });
                issues++;
                criticalIssues++;
            }

            // Check for unencrypted audit logs
            if (config.LogLevel == "Debug" && !config.VerboseOutput)
            {
                report.Issues.Add(new SecurityIssue
                {
                    Severity = SecuritySeverity.Medium,
                    Category = "Logging",
                    Description = "Debug logging enabled without verbose output restrictions",
                    Recommendation = "Review log configuration for sensitive data exposure"
                });
                issues++;
            }

            // Check rate limiting effectiveness
            var rateLimitViolations = GetRateLimitViolations();
            if (rateLimitViolations > 0)
            {
                report.Issues.Add(new SecurityIssue
                {
                    Severity = SecuritySeverity.Medium,
                    Category = "Rate Limiting",
                    Description = $"{rateLimitViolations} rate limit violations detected",
                    Recommendation = "Review and adjust rate limiting configuration"
                });
                issues++;
            }

            // Calculate security score
            var totalChecks = 10; // Base number of security checks
            var passedChecks = totalChecks - issues;
            report.SecurityScore = (passedChecks / (double)totalChecks) * 100;

            report.TotalIssues = issues;
            report.CriticalCount = criticalIssues;
            report.HighCount = report.Issues.Count(i => i.Severity == SecuritySeverity.High);
            report.MediumCount = report.Issues.Count(i => i.Severity == SecuritySeverity.Medium);
            report.LowCount = report.Issues.Count(i => i.Severity == SecuritySeverity.Low);

            // Add recommendations
            if (report.SecurityScore < 80)
            {
                report.Recommendations.Add("Review security configuration and address identified issues");
            }
            if (report.SecurityScore < 60)
            {
                report.Recommendations.Add("Immediate security review recommended");
            }

            return report;
        }

        private static async Task<int> CountCredentialsPastRotationWindowAsync()
        {
            try
            {
                if (!Directory.Exists(SecureStoragePath))
                {
                    return 0;
                }

                var credentialFiles = Directory.GetFiles(SecureStoragePath, "*.sec");
                if (credentialFiles.Length == 0)
                {
                    return 0;
                }

                var cutoffDate = DateTime.Now.AddDays(-CredentialRotationThresholdDays);
                var overdue = 0;

                foreach (var file in credentialFiles)
                {
                    var credential = await LoadCredentialAsync(file);
                    if (credential == null)
                    {
                        continue;
                    }

                    var lastRotated = credential.LastRotatedAt == DateTime.MinValue
                        ? credential.CreatedAt
                        : credential.LastRotatedAt;

                    if (lastRotated == DateTime.MinValue)
                    {
                        lastRotated = File.GetCreationTime(file);
                    }

                    if (lastRotated < cutoffDate)
                    {
                        overdue++;
                    }
                }

                return overdue;
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Failed to evaluate credential rotation status");
                return 0;
            }
        }

        private static int GetRateLimitViolations()
        {
            lock (_rateLimitLock)
            {
                return _rateLimits.Values.Sum(entry => entry.TotalViolations);
            }
        }

        public static async Task<bool> StoreCredentialAsync(string networkName, string password)
        {
            try
            {
                var encryptedPassword = await EncryptPassword(password);

                await EnsureSecureStorageDirectoryAsync();
                var credentialFile = Path.Combine(SecureStoragePath, $"{GetSafeFileName(networkName)}.sec");

                SecureCredential credential;
                if (File.Exists(credentialFile))
                {
                    credential = await LoadCredentialAsync(credentialFile) ?? new SecureCredential
                    {
                        CreatedAt = DateTime.Now
                    };
                    credential.AccessCount = 0;
                }
                else
                {
                    credential = new SecureCredential
                    {
                        CreatedAt = DateTime.Now,
                        AccessCount = 0
                    };
                }

                credential.NetworkName = networkName;
                credential.EncryptedPassword = encryptedPassword;
                credential.LastAccessedAt = DateTime.Now;
                credential.LastRotatedAt = DateTime.Now;
                credential.StorageProvider = "EncryptedFile";
                credential.CredentialReference = null;

                if (CredentialManager.IsSupported)
                {
                    var target = BuildCredentialManagerTarget(networkName);
                    if (CredentialManager.TryWriteCredential(target, networkName, password, out var credError))
                    {
                        credential.StorageProvider = "WindowsCredentialManager";
                        credential.CredentialReference = target;
                        credential.EncryptedPassword = null;

                        await Logger.LogInfo("Credential stored in Windows Credential Manager", nameof(SecurityManager), new Dictionary<string, object>
                        {
                            ["network"] = networkName,
                            ["target"] = target
                        });
                    }
                    else
                    {
                        await Logger.LogWarning("Credential Manager storage failed, falling back to encrypted file", nameof(SecurityManager), new Dictionary<string, object>
                        {
                            ["network"] = networkName,
                            ["error"] = credError
                        });
                    }
                }

                var json = JsonSerializer.Serialize(credential, new JsonSerializerOptions
                {
                    WriteIndented = false
                });

                // Additional encryption layer for the file itself
                var fileBytes = Encoding.UTF8.GetBytes(json);
                var protectedBytes = ProtectedData.Protect(fileBytes, null, DataProtectionScope.CurrentUser);
                var digest = ComputeSha256(protectedBytes);

                await File.WriteAllBytesAsync(credentialFile, protectedBytes);
                await EnsureSecureFileAclAsync(credentialFile);
                await WriteDigestMetadataAsync(credentialFile, digest);

                LogAccess($"Credential stored for {networkName}");
                return true;
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, $"Failed to store credential for {networkName}");
                return false;
            }
        }

        internal static async Task<string> CreateSecureTempFileAsync(string prefix, string extension, string content)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                prefix = "temp";
            }

            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".tmp";
            }

            if (!extension.StartsWith('.'))
            {
                extension = "." + extension;
            }

            await EnsureSecureStorageDirectoryAsync();
            await EnsureSecureTempDirectoryAsync();

            var fileName = $"{prefix}_{Guid.NewGuid():N}{extension}";
            var fullPath = Path.Combine(SecureTempPath, fileName);

            await File.WriteAllTextAsync(fullPath, content ?? string.Empty, Encoding.UTF8);
            await EnsureSecureFileAclAsync(fullPath);

            return fullPath;
        }

        internal static async Task<string> CreateValidatedProfileAsync(string context, string xmlContent)
        {
            if (!ProfileXmlValidator.ValidateWifiProfile(xmlContent, out var validationError))
            {
                throw new ArgumentException($"Invalid profile for {context}: {validationError}");
            }
            return await CreateSecureTempFileAsync("profile", ".xml", xmlContent);
        }

        public static async Task<string> RetrieveSecureCredential(string networkName)
        {
            try
            {
                await EnsureSecureStorageDirectoryAsync();
                var credentialFile = Path.Combine(SecureStoragePath, $"{GetSafeFileName(networkName)}.sec");
                if (!File.Exists(credentialFile))
                    return null;

                await EnsureSecureFileAclAsync(credentialFile);
                var protectedBytes = await File.ReadAllBytesAsync(credentialFile);
                var expectedDigest = await ReadDigestMetadataAsync(credentialFile);
                var currentDigest = ComputeSha256(protectedBytes);

                if (!string.IsNullOrEmpty(expectedDigest) && !CryptographicEquals(expectedDigest, currentDigest))
                {
                    await Logger.LogError("Credential integrity check failed", nameof(SecurityManager), new Dictionary<string, object>
                    {
                        ["network"] = networkName,
                        ["credentialFile"] = credentialFile
                    });

                    await AuditTrail.RecordEventAsync("Security", "CredentialTamperDetected", new Dictionary<string, object>
                    {
                        ["network"] = networkName,
                        ["credentialFile"] = credentialFile
                    }, "Critical");

                    throw new SecurityException("Credential file integrity verification failed");
                }

                var fileInfo = new FileInfo(credentialFile);
                var fileBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(fileBytes);

                var credential = JsonSerializer.Deserialize<SecureCredential>(json);
                EnsureCredentialMetadataDefaults(credential, fileInfo);

                // Update access information
                credential.LastAccessedAt = DateTime.Now;
                credential.AccessCount++;

                await UpdateCredentialMetadata(networkName, credential);

                LogAccess($"Credential retrieved for {networkName}");
                var decrypted = await ResolveCredentialSecretAsync(credential, networkName);
                if (decrypted == null)
                {
                    return null;
                }

                return decrypted;
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, $"Failed to retrieve credential for {networkName}");
                return null;
            }
        }

        public static async Task<NetworkSecurityValidation> ValidateNetworkSecurity(string ssid, string security, string? password = null)
        {
            var validation = new NetworkSecurityValidation { IsValid = true };
            var issues = new List<string>();

            try
            {
                // Validate SSID
                if (string.IsNullOrWhiteSpace(ssid))
                {
                    issues.Add("SSID cannot be empty");
                    validation.IsValid = false;
                }

                // Validate security type
                if (string.IsNullOrWhiteSpace(security))
                {
                    issues.Add("Security type must be specified");
                    validation.IsValid = false;
                }

                // Validate password for secured networks
                if (!string.IsNullOrEmpty(security) && security != "Open" && string.IsNullOrWhiteSpace(password))
                {
                    issues.Add("Password required for secured networks");
                    validation.IsValid = false;
                }

                validation.Issues = issues;
                return validation;
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Failed to validate network security");
                validation.IsValid = false;
                validation.Issues = new List<string> { "Validation failed due to internal error" };
                return validation;
            }
        }

        public static async Task SecureDeleteFileAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return;
            }

            await DeleteDigestMetadataAsync(filePath);
            await SecureDeleteInternal(filePath);
        }

        public static async Task<SecurityAuditReport> PerformSecurityAudit()
        {
            var report = new SecurityAuditReport
            {
                GeneratedAt = DateTime.Now,
                Issues = new List<SecurityIssue>()
            };

            try
            {
                // Check for weak passwords in saved profiles
                var profiles = await NetworkOperations.GetSavedProfilesAsync();
                foreach (var profile in profiles)
                {
                    var credential = await RetrieveSecureCredential(profile);
                    if (!string.IsNullOrEmpty(credential))
                    {
                        var strength = EvaluatePasswordStrength(credential);
                        if (strength < 50)
                        {
                            report.Issues.Add(new SecurityIssue
                            {
                                Severity = SecuritySeverity.Medium,
                                Category = "Weak Password",
                                Network = profile,
                                Description = "Password strength is below recommended level",
                                Recommendation = "Use a stronger password with mixed case, numbers, and symbols"
                            });
                        }
                    }
                }

                // Check for open networks
                var networks = await AdvancedScanner.PerformDetailedScan();
                foreach (var network in networks.Where(n => n.Security.Contains("Open")))
                {
                    report.Issues.Add(new SecurityIssue
                    {
                        Severity = SecuritySeverity.High,
                        Category = "Open Network",
                        Network = network.Ssid,
                        Description = "Network has no encryption",
                        Recommendation = "Avoid using open networks for sensitive activities"
                    });
                }

                // Check for outdated security protocols
                foreach (var network in networks.Where(n =>
                    n.Security.Contains("WEP") ||
                    (n.Security.Contains("WPA") && !n.Security.Contains("WPA2") && !n.Security.Contains("WPA3"))))
                {
                    report.Issues.Add(new SecurityIssue
                    {
                        Severity = SecuritySeverity.Medium,
                        Category = "Outdated Security",
                        Network = network.Ssid,
                        Description = $"Network uses outdated security: {network.Security}",
                        Recommendation = "Upgrade to WPA2 or WPA3 if possible"
                    });
                }

                // Check for suspicious SSIDs
                var suspiciousPatterns = new[] { "free", "public", "open", "guest", "default" };
                foreach (var network in networks.Where(n =>
                    suspiciousPatterns.Any(p => n.Ssid.Contains(p, StringComparison.OrdinalIgnoreCase))))
                {
                    report.Issues.Add(new SecurityIssue
                    {
                        Severity = SecuritySeverity.Low,
                        Category = "Suspicious SSID",
                        Network = network.Ssid,
                        Description = "Network name suggests public or unsecured access",
                        Recommendation = "Verify network legitimacy before connecting"
                    });
                }

                // Check system security
                if (!IsRunningAsAdmin())
                {
                    report.Issues.Add(new SecurityIssue
                    {
                        Severity = SecuritySeverity.Low,
                        Category = "Privileges",
                        Description = "Not running with administrator privileges",
                        Recommendation = "Some security features require admin access"
                    });
                }

                // Check for credential file permissions
                if (Directory.Exists(SecureStoragePath))
                {
                    var dirInfo = new DirectoryInfo(SecureStoragePath);
                    var acl = dirInfo.GetAccessControl();
                    // Additional ACL checks could be added here
                }

                report.TotalIssues = report.Issues.Count;
                report.CriticalCount = report.Issues.Count(i => i.Severity == SecuritySeverity.Critical);
                report.HighCount = report.Issues.Count(i => i.Severity == SecuritySeverity.High);
                report.MediumCount = report.Issues.Count(i => i.Severity == SecuritySeverity.Medium);
                report.LowCount = report.Issues.Count(i => i.Severity == SecuritySeverity.Low);

                report.SecurityScore = CalculateSecurityScore(report);
                report.Recommendations = GenerateSecurityRecommendations(report);
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Security audit failed");
            }

            return report;
        }

        private static int EvaluatePasswordStrength(string password)
        {
            if (string.IsNullOrEmpty(password))
                return 0;

            int score = 0;
            var analysis = AnalyzePasswordComplexity(password);

            // Length scoring (enhanced)
            if (password.Length >= 8) score += 15;
            if (password.Length >= 12) score += 15;
            if (password.Length >= 16) score += 10;
            if (password.Length >= 20) score += 10;

            // Character variety scoring (enhanced)
            if (analysis.HasLowercase) score += 10;
            if (analysis.HasUppercase) score += 10;
            if (analysis.HasDigits) score += 10;
            if (analysis.HasSpecialChars) score += 15;
            if (analysis.UniqueCharRatio > 0.7) score += 10; // High character diversity

            // Complexity patterns (enhanced penalties)
            if (analysis.HasSequentialNumbers) score -= 15;
            if (analysis.HasSequentialLetters) score -= 15;
            if (analysis.HasRepeatedChars) score -= 10;
            if (analysis.IsDictionaryWord) score -= 25;
            if (analysis.IsCommonPattern) score -= 20;

            // Entropy bonus
            if (analysis.EntropyBits >= 50) score += 10;
            if (analysis.EntropyBits >= 70) score += 10;
            if (analysis.EntropyBits >= 90) score += 5;

            // Length bonus for very long passwords
            if (password.Length >= 24) score += 5;

            // Apply reasonable bounds
            score = Math.Max(0, Math.Min(100, score));

            // Additional security checks
            if (analysis.IsCompromised)
            {
                score = Math.Min(score, 20); // Cap score for known compromised passwords
            }

            return score;
        }

        private static PasswordAnalysis AnalyzePasswordComplexity(string password)
        {
            var analysis = new PasswordAnalysis();

            // Basic character checks
            analysis.HasLowercase = password.Any(char.IsLower);
            analysis.HasUppercase = password.Any(char.IsUpper);
            analysis.HasDigits = password.Any(char.IsDigit);
            analysis.HasSpecialChars = password.Any(c => !char.IsLetterOrDigit(c));

            // Unique character ratio
            var uniqueChars = new HashSet<char>(password);
            analysis.UniqueCharRatio = (double)uniqueChars.Count / password.Length;

            // Sequential patterns
            analysis.HasSequentialNumbers = HasSequentialPattern(password, c => char.IsDigit(c));
            analysis.HasSequentialLetters = HasSequentialPattern(password, c => char.IsLetter(c));

            // Repeated characters
            analysis.HasRepeatedChars = password.GroupBy(c => c).Any(g => g.Count() >= 3);

            // Dictionary word check (basic implementation)
            analysis.IsDictionaryWord = IsCommonDictionaryWord(password);

            // Common patterns
            analysis.IsCommonPattern = HasCommonWeakPatterns(password);

            // Entropy calculation
            analysis.EntropyBits = CalculatePasswordEntropy(password);

            // Compromised check (would integrate with external service in production)
            analysis.IsCompromised = IsKnownCompromisedPassword(password);

            return analysis;
        }

        private static bool HasSequentialPattern(string password, Func<char, bool> charFilter)
        {
            for (int i = 0; i < password.Length - 2; i++)
            {
                if (charFilter(password[i]) && charFilter(password[i + 1]) && charFilter(password[i + 2]))
                {
                    if ((password[i + 1] == password[i] + 1 && password[i + 2] == password[i] + 2) ||
                        (password[i + 1] == password[i] - 1 && password[i + 2] == password[i] - 2))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IsCommonDictionaryWord(string password)
        {
            // Basic dictionary check - in production, use a proper word list
            var commonWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "password", "admin", "user", "login", "welcome", "letmein", "monkey", "dragon",
                "passw0rd", "p@ssword", "qwerty", "abc123", "iloveyou", "sunshine", "princess",
                "flower", "superman", "batman", "trustno1", "ninja", "summer", "winter"
            };

            return commonWords.Contains(password.ToLowerInvariant());
        }

        private static bool HasCommonWeakPatterns(string password)
        {
            var patterns = new[]
            {
                @"^\d{4,}$",           // All digits
                @"^[a-zA-Z]{4,}$",     // All letters
                @"^(.)\1{3,}$",        // Repeated single character
                @"^(012|123|234|345|456|567|678|789|890|abc|bcd|cde|def|efg|fgh|ghi|hij|ijk|jkl|klm|lmn|mno|nop|opq|pqr|qrs|rst|stu|tuv|uvw|vwx|wxy|xyz)",
                @"^(qwer|asdf|zxcv|yuio|hjkl|vbnm)"
            };

            return patterns.Any(pattern => System.Text.RegularExpressions.Regex.IsMatch(password, pattern, RegexOptions.IgnoreCase));
        }

        private static double CalculatePasswordEntropy(string password)
        {
            if (string.IsNullOrEmpty(password)) return 0;

            int charsetSize = 0;
            if (password.Any(char.IsLower)) charsetSize += 26;
            if (password.Any(char.IsUpper)) charsetSize += 26;
            if (password.Any(char.IsDigit)) charsetSize += 10;
            if (password.Any(c => !char.IsLetterOrDigit(c))) charsetSize += 32; // Approximate special chars

            if (charsetSize == 0) return 0;

            return Math.Log2(Math.Pow(charsetSize, password.Length));
        }

        private static bool IsKnownCompromisedPassword(string password)
        {
            // In production, this would check against HaveIBeenPwned API or local database
            // For now, return false - this is a placeholder for future implementation
            return false;
        }

        private class PasswordAnalysis
        {
            public bool HasLowercase { get; set; }
            public bool HasUppercase { get; set; }
            public bool HasDigits { get; set; }
            public bool HasSpecialChars { get; set; }
            public double UniqueCharRatio { get; set; }
            public bool HasSequentialNumbers { get; set; }
            public bool HasSequentialLetters { get; set; }
            public bool HasRepeatedChars { get; set; }
            public bool IsDictionaryWord { get; set; }
            public bool IsCommonPattern { get; set; }
            public double EntropyBits { get; set; }
            public bool IsCompromised { get; set; }
        }

        private static double CalculateSecurityScore(SecurityAuditReport report)
        {
            var baseScore = 100.0;

            baseScore -= report.CriticalCount * 25;
            baseScore -= report.HighCount * 15;
            baseScore -= report.MediumCount * 10;
            baseScore -= report.LowCount * 5;

            return Math.Max(0, Math.Min(100, baseScore));
        }

        private static List<string> GenerateSecurityRecommendations(SecurityAuditReport report)
        {
            var recommendations = new List<string>();

            if (report.CriticalCount > 0)
            {
                recommendations.Add($"CRITICAL: Address {report.CriticalCount} critical security issues immediately");
            }

            if (report.HighCount > 0)
            {
                recommendations.Add($"HIGH: Resolve {report.HighCount} high-priority security concerns");
            }

            if (report.Issues.Any(i => i.Category == "Open Network"))
            {
                recommendations.Add("Avoid connecting to open networks without VPN protection");
            }

            if (report.Issues.Any(i => i.Category == "Weak Password"))
            {
                recommendations.Add("Update weak passwords to use 12+ characters with mixed case, numbers, and symbols");
            }

            if (report.Issues.Any(i => i.Category == "Outdated Security"))
            {
                recommendations.Add("Upgrade network security protocols to WPA2 or WPA3");
            }

            if (report.SecurityScore >= 80)
            {
                recommendations.Add("Security posture is good. Continue regular security audits");
            }
            else if (report.SecurityScore < 50)
            {
                recommendations.Add("Security posture needs immediate attention");
            }

            return recommendations;
        }

        private static async Task SecureDeleteInternal(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                var fileLength = fileInfo.Length;

                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    var random = new Random();
                    var buffer = new byte[4096];

                    for (int pass = 0; pass < 3; pass++)
                    {
                        stream.Seek(0, SeekOrigin.Begin);
                        for (long pos = 0; pos < fileLength; pos += buffer.Length)
                        {
                            random.NextBytes(buffer);
                            var writeSize = (int)Math.Min(buffer.Length, fileLength - pos);
                            await stream.WriteAsync(buffer, 0, writeSize);
                        }
                        await stream.FlushAsync();
                    }
                }

                File.Delete(filePath);
            }
            catch
            {
                try
                {
                    File.Delete(filePath);
                }
                catch
                {
                }
            }
        }

        private static async Task UpdateCredentialMetadata(string networkName, SecureCredential credential)
        {
            try
            {
                var credentialFile = Path.Combine(SecureStoragePath, $"{GetSafeFileName(networkName)}.sec");

                var json = JsonSerializer.Serialize(credential, new JsonSerializerOptions
                {
                    WriteIndented = false
                });

                var fileBytes = Encoding.UTF8.GetBytes(json);
                var protectedBytes = ProtectedData.Protect(fileBytes, null, DataProtectionScope.CurrentUser);
                var digest = ComputeSha256(protectedBytes);

                await File.WriteAllBytesAsync(credentialFile, protectedBytes);
                await EnsureSecureFileAclAsync(credentialFile);
                await WriteDigestMetadataAsync(credentialFile, digest);
            }
            catch
            {
                // Ignore metadata update errors
            }
        }

        private static async Task<SecureCredential> LoadCredentialAsync(string credentialFilePath)
        {
            try
            {
                var fileInfo = new FileInfo(credentialFilePath);
                if (!fileInfo.Exists)
                {
                    return null;
                }

                var protectedBytes = await File.ReadAllBytesAsync(credentialFilePath);
                var expectedDigest = await ReadDigestMetadataAsync(credentialFilePath);
                if (!string.IsNullOrEmpty(expectedDigest))
                {
                    var currentDigest = ComputeSha256(protectedBytes);
                    if (!CryptographicEquals(expectedDigest, currentDigest))
                    {
                        await Logger.LogError("Credential integrity check failed during metadata evaluation", nameof(SecurityManager), new Dictionary<string, object>
                        {
                            ["credentialFile"] = Path.GetFileName(credentialFilePath)
                        });
                        return null;
                    }
                }

                var fileBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(fileBytes);
                var credential = JsonSerializer.Deserialize<SecureCredential>(json);
                EnsureCredentialMetadataDefaults(credential, fileInfo);
                return credential;
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, $"Failed to load credential metadata from {credentialFilePath}");
                return null;
            }
        }

        private static void EnsureCredentialMetadataDefaults(SecureCredential credential, FileInfo sourceInfo)
        {
            if (credential == null)
            {
                return;
            }

            var now = DateTime.Now;

            if (credential.CreatedAt == DateTime.MinValue)
            {
                credential.CreatedAt = sourceInfo?.CreationTime ?? now;
            }

            if (credential.LastRotatedAt == DateTime.MinValue)
            {
                credential.LastRotatedAt = credential.CreatedAt;
            }

            if (credential.LastAccessedAt == DateTime.MinValue)
            {
                credential.LastAccessedAt = credential.CreatedAt;
            }

            if (credential.AccessCount < 0)
            {
                credential.AccessCount = 0;
            }

            if (string.IsNullOrWhiteSpace(credential.StorageProvider))
            {
                credential.StorageProvider = !string.IsNullOrEmpty(credential.EncryptedPassword)
                    ? "EncryptedFile"
                    : (CredentialManager.IsSupported ? "WindowsCredentialManager" : "EncryptedFile");
            }
        }

        private static string BuildCredentialManagerTarget(string networkName)
        {
            var safeName = GetSafeFileName(networkName);
            return $"MurtiWifiConnecter/{safeName}";
        }

        private static async Task<string> ResolveCredentialSecretAsync(SecureCredential credential, string networkName)
        {
            if (credential == null)
            {
                return null;
            }

            // Prefer Windows Credential Manager if available
            if (!string.IsNullOrWhiteSpace(credential.StorageProvider) &&
                credential.StorageProvider.Equals("WindowsCredentialManager", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(credential.CredentialReference) &&
                CredentialManager.IsSupported)
            {
                if (CredentialManager.TryReadCredential(credential.CredentialReference, out var secret, out var readError))
                {
                    return secret;
                }

                await Logger.LogWarning("Credential Manager retrieval failed, attempting encrypted fallback", nameof(SecurityManager), new Dictionary<string, object>
                {
                    ["network"] = networkName,
                    ["target"] = credential.CredentialReference,
                    ["error"] = readError
                });
            }

            if (!string.IsNullOrEmpty(credential.EncryptedPassword))
            {
                return await DecryptPassword(credential.EncryptedPassword);
            }

            return null;
        }

        private static string ComputeSha256(byte[] data)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(data);
            return Convert.ToBase64String(hash);
        }

        private static bool CryptographicEquals(string expected, string actual)
        {
            if (expected == null || actual == null || expected.Length != actual.Length)
            {
                return false;
            }

            var expectedBytes = Encoding.UTF8.GetBytes(expected);
            var actualBytes = Encoding.UTF8.GetBytes(actual);

            if (expectedBytes.Length != actualBytes.Length)
            {
                return false;
            }

            var diff = 0;
            for (int i = 0; i < expectedBytes.Length; i++)
            {
                diff |= expectedBytes[i] ^ actualBytes[i];
            }

            return diff == 0;
        }

        private static async Task WriteDigestMetadataAsync(string credentialFilePath, string digest)
        {
            try
            {
                if (string.IsNullOrEmpty(digest))
                {
                    return;
                }

                var metadataPath = credentialFilePath + CredentialDigestExtension;
                await File.WriteAllTextAsync(metadataPath, digest, Encoding.UTF8);
                await EnsureSecureFileAclAsync(metadataPath);
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Failed to write credential digest metadata");
            }
        }

        private static async Task<string> ReadDigestMetadataAsync(string credentialFilePath)
        {
            try
            {
                var metadataPath = credentialFilePath + CredentialDigestExtension;
                if (!File.Exists(metadataPath))
                {
                    return null;
                }

                return (await File.ReadAllTextAsync(metadataPath, Encoding.UTF8)).Trim();
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Failed to read credential digest metadata");
                return null;
            }
        }

        private static async Task DeleteDigestMetadataAsync(string credentialFilePath)
        {
            try
            {
                var metadataPath = credentialFilePath + CredentialDigestExtension;
                if (File.Exists(metadataPath))
                {
                    File.Delete(metadataPath);
                }
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Failed to delete credential digest metadata");
            }
        }

        private static async Task EnsureSecureStorageDirectoryAsync()
        {
            try
            {
                var directoryInfo = new DirectoryInfo(SecureStoragePath);
                if (!directoryInfo.Exists)
                {
                    directoryInfo.Create();
                }

                var security = BuildSecureDirectorySecurity();
                directoryInfo.SetAccessControl(security);
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Failed to enforce secure storage directory permissions");
            }
        }

        private static async Task EnsureSecureTempDirectoryAsync()
        {
            try
            {
                var directoryInfo = new DirectoryInfo(SecureTempPath);
                if (!directoryInfo.Exists)
                {
                    directoryInfo.Create();
                }

                var security = BuildSecureDirectorySecurity();
                directoryInfo.SetAccessControl(security);
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Failed to enforce secure temp directory permissions");
            }
        }

        internal static async Task EnsureSecureDirectoryAclAsync(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                throw new ArgumentException("Directory path cannot be null or whitespace", nameof(directoryPath));
            }

            try
            {
                var directoryInfo = new DirectoryInfo(directoryPath);
                if (!directoryInfo.Exists)
                {
                    directoryInfo.Create();
                }

                var security = BuildSecureDirectorySecurity();
                directoryInfo.SetAccessControl(security);
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, $"Failed to enforce secure directory permissions for {directoryPath}");
            }
        }

        internal static async Task EnsureSecureFileAclAsync(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists)
                {
                    return;
                }

                var security = BuildSecureFileSecurity();
                fileInfo.SetAccessControl(security);
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, $"Failed to enforce secure storage file permissions for {filePath}");
            }
        }

        internal static async Task<byte[]> GetIntegrityKeyAsync(string keyName, int keySize = 64)
        {
            if (string.IsNullOrWhiteSpace(keyName))
            {
                throw new ArgumentException("Key name cannot be null or whitespace", nameof(keyName));
            }

            if (keySize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(keySize), "Key size must be positive");
            }

            lock (_integrityKeyLock)
            {
                if (_integrityKeys.TryGetValue(keyName, out var cached))
                {
                    return cached;
                }
            }

            await EnsureSecureStorageDirectoryAsync().ConfigureAwait(false);

            var keyFileName = $"{GetSafeFileName(keyName)}.key";
            var keyPath = Path.Combine(SecureStoragePath, keyFileName);

            byte[] keyBytes;
            try
            {
                if (File.Exists(keyPath))
                {
                    keyBytes = await File.ReadAllBytesAsync(keyPath).ConfigureAwait(false);
                    if (keyBytes == null || keyBytes.Length == 0)
                    {
                        keyBytes = new byte[keySize];
                        RandomNumberGenerator.Fill(keyBytes);
                        await File.WriteAllBytesAsync(keyPath, keyBytes).ConfigureAwait(false);
                    }
                }
                else
                {
                    keyBytes = new byte[keySize];
                    RandomNumberGenerator.Fill(keyBytes);
                    await File.WriteAllBytesAsync(keyPath, keyBytes).ConfigureAwait(false);
                }

                await EnsureSecureFileAclAsync(keyPath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, $"Failed to access integrity key {keyName}");
                keyBytes = new byte[keySize];
                RandomNumberGenerator.Fill(keyBytes);
            }

            lock (_integrityKeyLock)
            {
                _integrityKeys[keyName] = keyBytes;
            }

            return keyBytes;
        }

        private static DirectorySecurity BuildSecureDirectorySecurity()
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);

            var currentUserSid = GetCurrentUserSid();

            security.AddAccessRule(new FileSystemAccessRule(
                currentUserSid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            security.AddAccessRule(new FileSystemAccessRule(
                AdministratorsSid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            security.AddAccessRule(new FileSystemAccessRule(
                LocalSystemSid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            return security;
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static FileSecurity BuildSecureFileSecurity()
        {
#if WINDOWS
            var security = new FileSecurity();
            security.SetAccessRuleProtection(true, false);

            var currentUserSid = GetCurrentUserSid();

            security.AddAccessRule(new FileSystemAccessRule(
                currentUserSid,
                FileSystemRights.FullControl,
                AccessControlType.Allow));

            security.AddAccessRule(new FileSystemAccessRule(
                AdministratorsSid,
                FileSystemRights.FullControl,
                AccessControlType.Allow));

            security.AddAccessRule(new FileSystemAccessRule(
                LocalSystemSid,
                FileSystemRights.FullControl,
                AccessControlType.Allow));

            return security;
#else
            throw new PlatformNotSupportedException("Advanced file security is only supported on Windows.");
#endif
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static SecurityIdentifier GetCurrentUserSid()
        {
#if WINDOWS
            using var identity = WindowsIdentity.GetCurrent();
            if (identity?.User == null)
            {
                throw new SecurityException("Current user identity could not be determined");
            }

            return identity.User;
#else
            throw new PlatformNotSupportedException("Windows identity features are only supported on Windows.");
#endif
        }

        private static string GetSafeFileName(string networkName)
        {
            if (string.IsNullOrWhiteSpace(networkName))
                return "unnamed_network";

            var invalid = Path.GetInvalidFileNameChars();
            var safe = new StringBuilder();

            foreach (var c in networkName)
            {
                if (!invalid.Contains(c) && c != ':' && c != '*' && c != '?' && c != '"' && c != '<' && c != '>' && c != '|')
                    safe.Append(c);
                else
                    safe.Append('_');
            }

            var result = safe.ToString().Trim('_', ' ', '.');

            // Ensure the filename isn't empty or just underscores
            if (string.IsNullOrWhiteSpace(result) || result.All(c => c == '_'))
                return "unnamed_network";

            // Limit length to prevent filesystem issues
            if (result.Length > 200)
                result = result.Substring(0, 200);

            return result;
        }

        private static void LogAccess(string action)
        {
            lock (_lockObject)
            {
                _accessLog[action] = DateTime.Now;

                // Keep only last 100 entries
                if (_accessLog.Count > 100)
                {
                    var oldest = _accessLog.OrderBy(kvp => kvp.Value).First();
                    _accessLog.Remove(oldest.Key);
                }
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static bool IsRunningAsAdmin()
        {
#if WINDOWS
            try
            {
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
#else
            // On non-Windows platforms, assume no elevated privileges
            return false;
#endif
        }

        private class RateLimitEntry
        {
            public DateTime FirstAttempt { get; set; }
            public int AttemptCount { get; set; }
            public DateTime LastAttempt { get; set; }
            public int TotalViolations { get; set; }
            public DateTime? LastViolation { get; set; }
        }

        public class SecureCredential
        {
            public string NetworkName { get; set; }
            public string EncryptedPassword { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime LastAccessedAt { get; set; }
            public int AccessCount { get; set; }
            public DateTime LastRotatedAt { get; set; }
            public string StorageProvider { get; set; }
            public string CredentialReference { get; set; }
        }

        public class SecurityAuditReport
        {
            public DateTime GeneratedAt { get; set; }
            public int TotalIssues { get; set; }
            public int CriticalCount { get; set; }
            public int HighCount { get; set; }
            public int MediumCount { get; set; }
            public int LowCount { get; set; }
            public double SecurityScore { get; set; }
            public List<SecurityIssue> Issues { get; set; }
            public List<string> Recommendations { get; set; }
        }

        public class SecurityIssue
        {
            public SecuritySeverity Severity { get; set; }
            public string Category { get; set; }
            public string Network { get; set; }
            public string Description { get; set; }
            public string Recommendation { get; set; }
        }

        public enum SecuritySeverity
        {
            Low,
            Medium,
            High,
            Critical
        }
    }

        /// <summary>
        /// Evaluate access using zero-trust framework with continuous threat monitoring and adaptive policy evaluation (Security-001)
        /// </summary>
        public static async Task<ZeroTrustDecision> EvaluateZeroTrustAccessAsync(string operation, Dictionary<string, object> context)
        {
            // Phase 1: 基本的な零トラスト評価
            var baseDecision = await _zeroTrustEvaluator.EvaluateAccessAsync(operation, context);

            // Phase 2: 適応型ポリシー評価とリアルタイム適応
            var adaptiveDecision = await _adaptivePolicyEngine.EvaluatePolicyAsync(operation, context, baseDecision.RiskScore);

            // Phase 3: 統合決定の生成
            var finalDecision = new ZeroTrustDecision
            {
                Operation = operation,
                Context = context,
                RiskScore = adaptiveDecision.AdjustedRiskScore,
                RequiredAuthentications = baseDecision.RequiredAuthentications.Concat(adaptiveDecision.RequiredActions).Distinct().ToList(),
                MonitoringLevel = DetermineIntegratedMonitoringLevel(baseDecision.MonitoringLevel, adaptiveDecision.RiskLevel),
                TimestampUtc = DateTime.UtcNow,
                IsAllowed = baseDecision.IsAllowed && adaptiveDecision.IsAllowed
            };

            // Log comprehensive zero-trust evaluation results with adaptive policy information
            await Logger.LogInfo("Zero-trust access evaluation with adaptive policy", nameof(SecurityManager), new Dictionary<string, object>
            {
                ["operation"] = operation,
                ["baseRiskScore"] = baseDecision.RiskScore,
                ["adjustedRiskScore"] = adaptiveDecision.AdjustedRiskScore,
                ["isAllowed"] = finalDecision.IsAllowed,
                ["monitoringLevel"] = finalDecision.MonitoringLevel.ToString(),
                ["requiredAuthentications"] = string.Join(", ", finalDecision.RequiredAuthentications),
                ["policyName"] = adaptiveDecision.PolicyName,
                ["riskLevel"] = adaptiveDecision.RiskLevel.ToString()
            });

            // Record comprehensive audit event for zero-trust decisions with adaptive policy context
            await AuditTrail.RecordEventAsync(
                "Security",
                "ZeroTrustEvaluation",
                new Dictionary<string, object>
                {
                    ["operation"] = operation,
                    ["baseRiskScore"] = baseDecision.RiskScore,
                    ["adjustedRiskScore"] = adaptiveDecision.AdjustedRiskScore,
                    ["isAllowed"] = finalDecision.IsAllowed,
                    ["monitoringLevel"] = finalDecision.MonitoringLevel.ToString(),
                    ["policyName"] = adaptiveDecision.PolicyName,
                    ["riskLevel"] = adaptiveDecision.RiskLevel.ToString()
                },
                finalDecision.IsAllowed ? "Info" : "Warning");

            return finalDecision;
        }

        /// <summary>
        /// Determine integrated monitoring level based on base monitoring and adaptive risk level
        /// </summary>
        private static MonitoringLevel DetermineIntegratedMonitoringLevel(MonitoringLevel baseLevel, RiskLevel adaptiveRisk)
        {
            // Base monitoring levels mapped to risk levels
            var baseToRisk = baseLevel switch
            {
                MonitoringLevel.None => RiskLevel.Low,
                MonitoringLevel.Basic => RiskLevel.Low,
                MonitoringLevel.Standard => RiskLevel.Medium,
                MonitoringLevel.Enhanced => RiskLevel.High,
                MonitoringLevel.Maximum => RiskLevel.Critical,
                _ => RiskLevel.Medium
            };

            // Take the higher risk level between base and adaptive
            var integratedRisk = (RiskLevel)Math.Max((int)baseToRisk, (int)adaptiveRisk);

            // Convert back to monitoring level
            return integratedRisk switch
            {
                RiskLevel.Low => MonitoringLevel.Basic,
                RiskLevel.Medium => MonitoringLevel.Standard,
                RiskLevel.High => MonitoringLevel.Enhanced,
                RiskLevel.Critical => MonitoringLevel.Maximum,
                _ => MonitoringLevel.Standard
            };
        }

        /// <summary>
        /// Get current threat indicators from zero-trust evaluator
        /// </summary>
        public static async Task<List<ThreatIndicator>> GetActiveThreatIndicatorsAsync()
        {
            // This would need to be implemented in ZeroTrustEvaluator to expose threat indicators
            return new List<ThreatIndicator>();
        }

        /// <summary>
        /// Update threat intelligence feed
        /// </summary>
        public static async Task UpdateThreatIntelligenceAsync()
        {
            await _zeroTrustEvaluator.EvaluateAccessAsync("threat_update", new Dictionary<string, object>());
        }

        /// <summary>
        /// Security-004: セッション封鎖の実装
        /// CLIセッションごとのコンテキスト隔離を管理
        /// </summary>
        public static class SessionManager
        {
            private static readonly Dictionary<string, SessionContext> _activeSessions = new();
            private static readonly object _sessionLock = new();
            private static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(30);
            private static readonly TimeSpan MaxSessionDuration = TimeSpan.FromHours(8);

            /// <summary>
            /// 新しいセッションを作成
            /// </summary>
            public static async Task<SessionContext> CreateSessionAsync()
            {
                var sessionId = Guid.NewGuid().ToString("N");
                var context = new SessionContext
                {
                    SessionId = sessionId,
                    CreatedAt = DateTime.UtcNow,
                    LastActivity = DateTime.UtcNow,
                    UserIdentity = await GetCurrentUserIdentityAsync(),
                    SecurityContext = await CreateSecurityContextAsync(),
                    IsActive = true
                };

                lock (_sessionLock)
                {
                    _activeSessions[sessionId] = context;
                }

                await Logger.LogInfo("Session created", nameof(SessionManager), new Dictionary<string, object>
                {
                    ["sessionId"] = sessionId,
                    ["userIdentity"] = context.UserIdentity
                });

                return context;
            }

            /// <summary>
            /// セッションを取得して活動を更新
            /// </summary>
            public static SessionContext GetSession(string sessionId)
            {
                lock (_sessionLock)
                {
                    if (_activeSessions.TryGetValue(sessionId, out var session))
                    {
                        // セッションの有効性をチェック
                        if (!session.IsActive || IsSessionExpired(session))
                        {
                            session.IsActive = false;
                            return null;
                        }

                        // 最終活動時間を更新
                        session.LastActivity = DateTime.UtcNow;
                        return session;
                    }
                }

                return null;
            }

            /// <summary>
            /// セッションを終了
            /// </summary>
            public static async Task<bool> TerminateSessionAsync(string sessionId)
            {
                SessionContext session = null;

                lock (_sessionLock)
                {
                    if (_activeSessions.TryGetValue(sessionId, out session))
                    {
                        _activeSessions.Remove(sessionId);
                    }
                }

                if (session != null)
                {
                    // セッション固有のリソースをクリーンアップ
                    await CleanupSessionResourcesAsync(session);

                    await Logger.LogInfo("Session terminated", nameof(SessionManager), new Dictionary<string, object>
                    {
                        ["sessionId"] = sessionId,
                        ["duration"] = DateTime.UtcNow - session.CreatedAt
                    });

                    return true;
                }

                return false;
            }

            /// <summary>
            /// 期限切れのセッションをクリーンアップ
            /// </summary>
            public static async Task CleanupExpiredSessionsAsync()
            {
                var expiredSessions = new List<string>();
                var currentTime = DateTime.UtcNow;

                lock (_sessionLock)
                {
                    foreach (var kvp in _activeSessions)
                    {
                        if (IsSessionExpired(kvp.Value))
                        {
                            expiredSessions.Add(kvp.Key);
                        }
                    }

                    foreach (var sessionId in expiredSessions)
                    {
                        _activeSessions.Remove(sessionId);
                    }
                }

                // 非同期クリーンアップを実行
                foreach (var sessionId in expiredSessions)
                {
                    await Logger.LogWarning("Expired session cleaned up", nameof(SessionManager), new Dictionary<string, object>
                    {
                        ["sessionId"] = sessionId
                    });
                }
            }

            /// <summary>
            /// セッションのセキュリティコンテキストを検証
            /// </summary>
            public static async Task<bool> ValidateSessionContextAsync(string sessionId, Dictionary<string, object> currentContext)
            {
                var session = GetSession(sessionId);
                if (session == null || !session.IsActive)
                {
                    return false;
                }

                // 現在のコンテキストとセッションコンテキストを比較
                var currentIdentity = await GetCurrentUserIdentityAsync();

                if (session.UserIdentity != currentIdentity)
                {
                    await Logger.LogSecurity("Session identity mismatch detected", "SessionViolation", new Dictionary<string, object>
                    {
                        ["sessionId"] = sessionId,
                        ["sessionIdentity"] = session.UserIdentity,
                        ["currentIdentity"] = currentIdentity
                    });

                    await TerminateSessionAsync(sessionId);
                    return false;
                }

                // セキュリティコンテキストの整合性を検証
                if (!await ValidateSecurityContextAsync(session.SecurityContext))
                {
                    await Logger.LogSecurity("Session security context compromised", "SessionViolation", new Dictionary<string, object>
                    {
                        ["sessionId"] = sessionId
                    });

                    await TerminateSessionAsync(sessionId);
                    return false;
                }

                return true;
            }

            /// <summary>
            /// セッション統計を取得
            /// </summary>
            public static SessionStatistics GetSessionStatistics()
            {
                lock (_sessionLock)
                {
                    var activeSessions = _activeSessions.Values.Where(s => s.IsActive).ToList();
                    var expiredSessions = _activeSessions.Values.Where(s => !s.IsActive || IsSessionExpired(s)).Count();

                    return new SessionStatistics
                    {
                        ActiveSessionCount = activeSessions.Count,
                        TotalSessionCount = _activeSessions.Count,
                        ExpiredSessionCount = expiredSessions,
                        AverageSessionDuration = activeSessions.Any()
                            ? TimeSpan.FromTicks((long)activeSessions.Average(s => (DateTime.UtcNow - s.CreatedAt).Ticks))
                            : TimeSpan.Zero,
                        OldestSessionAge = activeSessions.Any()
                            ? DateTime.UtcNow - activeSessions.Min(s => s.CreatedAt)
                            : TimeSpan.Zero
                    };
                }
            }

            private static bool IsSessionExpired(SessionContext session)
            {
                var currentTime = DateTime.UtcNow;
                var timeSinceActivity = currentTime - session.LastActivity;
                var sessionDuration = currentTime - session.CreatedAt;

                return timeSinceActivity > SessionTimeout || sessionDuration > MaxSessionDuration;
            }

            private static async Task<string> GetCurrentUserIdentityAsync()
            {
                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                        return identity?.User?.Value ?? "Unknown";
                    }
                    else
                    {
                        return Environment.UserName ?? "Unknown";
                    }
                }
                catch
                {
                    return "Error";
                }
            }

            private static async Task<SessionSecurityContext> CreateSecurityContextAsync()
            {
                return new SessionSecurityContext
                {
                    ProcessId = Environment.ProcessId,
                    ThreadId = Environment.CurrentManagedThreadId,
                    StartedAt = DateTime.UtcNow,
                    IntegrityLevel = await GetCurrentIntegrityLevelAsync()
                };
            }

            private static async Task<string> GetCurrentIntegrityLevelAsync()
            {
                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        // Windowsの整合性レベルを取得
                        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                        var principal = new System.Security.Principal.WindowsPrincipal(identity);

                        if (principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
                            return "High";
                        else
                            return "Medium";
                    }
                    else
                    {
                        // Unix系では基本的な権限情報
                        return Environment.UserName == "root" ? "High" : "Medium";
                    }
                }
                catch
                {
                    return "Unknown";
                }
            }

            private static async Task<bool> ValidateSecurityContextAsync(SessionSecurityContext originalContext)
            {
                // 現在のセキュリティコンテキストと比較
                var currentIntegrityLevel = await GetCurrentIntegrityLevelAsync();

                return originalContext.ProcessId == Environment.ProcessId &&
                       originalContext.IntegrityLevel == currentIntegrityLevel;
            }

            private static async Task CleanupSessionResourcesAsync(SessionContext session)
            {
                // セッション固有の一時ファイルやリソースをクリーンアップ
                // 実際の実装では、セッション中に作成された一時ファイルを削除

                await Task.CompletedTask; // プレースホルダー
            }
        }

        public class SessionContext
        {
            public string SessionId { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime LastActivity { get; set; }
            public string UserIdentity { get; set; }
            public SessionSecurityContext SecurityContext { get; set; }
            public bool IsActive { get; set; }
        }

        public class SessionSecurityContext
        {
            public int ProcessId { get; set; }
            public int ThreadId { get; set; }
            public DateTime StartedAt { get; set; }
            public string IntegrityLevel { get; set; }
        }

        public class SessionStatistics
        {
            public int ActiveSessionCount { get; set; }
            public int TotalSessionCount { get; set; }
            public int ExpiredSessionCount { get; set; }
            public TimeSpan AverageSessionDuration { get; set; }
            public TimeSpan OldestSessionAge { get; set; }
        }

    public class SecurityException : Exception
    {
        public SecurityException(string message) : base(message) { }
        public SecurityException(string message, Exception innerException) : base(message, innerException) { }
    }
}
}