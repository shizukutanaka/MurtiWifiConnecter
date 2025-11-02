using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// License management and activation system
    /// Supports trial, standard, pro, and enterprise editions
    /// </summary>
    public class LicenseManager
    {
        private static LicenseManager? _instance;
        private static readonly object _lock = new object();
        private LicenseInfo? _currentLicense;
        private readonly string _publicKey;

        // RSA public key for license verification (private key kept secure on server)
        private const string PUBLIC_KEY = @"
<RSAKeyValue>
  <Modulus>xGWqZT9X...</Modulus>
  <Exponent>AQAB</Exponent>
</RSAKeyValue>";

        private LicenseManager()
        {
            _publicKey = PUBLIC_KEY;
            _currentLicense = LoadLicense();
        }

        public static LicenseManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new LicenseManager();
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Initialize license system
        /// </summary>
        public static async Task InitializeAsync()
        {
            var instance = Instance;

            if (instance._currentLicense == null || instance._currentLicense.Edition == LicenseEdition.Trial)
            {
                await instance.CheckTrialStatusAsync();
            }
        }

        /// <summary>
        /// Activate license with key
        /// </summary>
        public async Task<ActivationResult> ActivateAsync(string licenseKey, bool online = true)
        {
            try
            {
                if (online)
                {
                    // Online activation via API
                    return await ActivateOnlineAsync(licenseKey);
                }
                else
                {
                    // Offline activation with activation code
                    Console.Write("Enter activation code: ");
                    var activationCode = Console.ReadLine()?.Trim() ?? "";
                    return await ActivateOfflineAsync(licenseKey, activationCode);
                }
            }
            catch (Exception ex)
            {
                return new ActivationResult
                {
                    Success = false,
                    Message = $"Activation failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Deactivate current license
        /// </summary>
        public async Task<bool> DeactivateAsync()
        {
            if (_currentLicense == null || _currentLicense.Edition == LicenseEdition.Free)
            {
                Console.WriteLine("No license to deactivate.");
                return false;
            }

            try
            {
                // Notify server of deactivation
                await DeactivateOnlineAsync(_currentLicense.LicenseKey);

                // Remove local license
                var licensePath = GetLicenseFilePath();
                if (File.Exists(licensePath))
                {
                    File.Delete(licensePath);
                }

                _currentLicense = CreateFreeLicense();
                await SaveLicenseAsync(_currentLicense);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓ License deactivated successfully");
                Console.ResetColor();

                return true;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✗ Deactivation failed: {ex.Message}");
                Console.ResetColor();
                return false;
            }
        }

        /// <summary>
        /// Check if feature is available in current license
        /// </summary>
        public bool IsFeatureAvailable(string featureName)
        {
            if (_currentLicense == null)
            {
                return false;
            }

            // Check if license is valid
            if (!IsLicenseValid())
            {
                return false;
            }

            return _currentLicense.Edition switch
            {
                LicenseEdition.Free => GetFreeFeaturesSet().Contains(featureName),
                LicenseEdition.Trial => GetProFeaturesSet().Contains(featureName),
                LicenseEdition.Standard => GetStandardFeaturesSet().Contains(featureName),
                LicenseEdition.Pro => GetProFeaturesSet().Contains(featureName),
                LicenseEdition.Enterprise => true, // All features
                _ => false
            };
        }

        /// <summary>
        /// Get current license information
        /// </summary>
        public LicenseInfo GetLicenseInfo()
        {
            return _currentLicense ?? CreateFreeLicense();
        }

        /// <summary>
        /// Check if license is valid
        /// </summary>
        public bool IsLicenseValid()
        {
            if (_currentLicense == null)
            {
                return false;
            }

            // Check expiration
            if (_currentLicense.ExpirationDate.HasValue &&
                _currentLicense.ExpirationDate.Value < DateTime.UtcNow)
            {
                return false;
            }

            // Check activation limit
            if (_currentLicense.MaxActivations > 0 &&
                _currentLicense.ActivationCount >= _currentLicense.MaxActivations)
            {
                return false;
            }

            // Verify hardware fingerprint
            if (!string.IsNullOrEmpty(_currentLicense.HardwareFingerprint))
            {
                var currentFingerprint = GetHardwareFingerprint();
                if (_currentLicense.HardwareFingerprint != currentFingerprint)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Display license information
        /// </summary>
        public void DisplayLicenseInfo()
        {
            var license = GetLicenseInfo();

            Console.WriteLine("\n╔═══════════════════════════════════════════════════════════╗");
            Console.WriteLine("║           MurtiWifi Connector - License Info              ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════════════╝\n");

            Console.Write("Edition:      ");
            Console.ForegroundColor = GetEditionColor(license.Edition);
            Console.WriteLine(license.Edition);
            Console.ResetColor();

            Console.WriteLine($"Licensed To:  {license.LicensedTo}");
            Console.WriteLine($"Company:      {license.Company}");
            Console.WriteLine($"Email:        {license.Email}");

            if (!string.IsNullOrEmpty(license.LicenseKey))
            {
                Console.WriteLine($"License Key:  {MaskLicenseKey(license.LicenseKey)}");
            }

            Console.Write("Status:       ");
            if (IsLicenseValid())
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓ Active");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("✗ Invalid/Expired");
            }
            Console.ResetColor();

            if (license.ExpirationDate.HasValue)
            {
                var daysRemaining = (license.ExpirationDate.Value - DateTime.UtcNow).TotalDays;
                Console.Write("Expires:      ");

                if (daysRemaining < 30)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                }

                Console.WriteLine($"{license.ExpirationDate.Value:yyyy-MM-dd} ({Math.Max(0, (int)daysRemaining)} days remaining)");
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine("Expires:      Never (Perpetual)");
            }

            if (license.Edition == LicenseEdition.Trial)
            {
                var trialDaysRemaining = (license.ExpirationDate!.Value - DateTime.UtcNow).TotalDays;
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"\n⏰ Trial: {Math.Max(0, (int)trialDaysRemaining)} days remaining");
                Console.ResetColor();
                Console.WriteLine("To purchase a license, visit: https://murtisoft.com/buy");
            }

            if (license.Edition != LicenseEdition.Enterprise)
            {
                Console.WriteLine("\n📊 Upgrade Benefits:");
                Console.WriteLine("  • Standard: WiFi 6/7 optimization, fast roaming");
                Console.WriteLine("  • Pro: AI optimization, mesh networking, priority support");
                Console.WriteLine("  • Enterprise: Unlimited devices, API access, SLA support");
                Console.WriteLine("\nUpgrade at: https://murtisoft.com/upgrade");
            }

            Console.WriteLine();
        }

        private async Task<ActivationResult> ActivateOnlineAsync(string licenseKey)
        {
            // In production, this would call an API endpoint
            // For demo purposes, we'll simulate validation

            Console.WriteLine("Contacting activation server...");
            await Task.Delay(1000); // Simulate network call

            // Parse license key (format: EDITION-XXXX-XXXX-XXXX-XXXX)
            var parts = licenseKey.Split('-');
            if (parts.Length != 5)
            {
                return new ActivationResult
                {
                    Success = false,
                    Message = "Invalid license key format"
                };
            }

            var editionCode = parts[0].ToUpperInvariant();
            var edition = editionCode switch
            {
                "STD" => LicenseEdition.Standard,
                "PRO" => LicenseEdition.Pro,
                "ENT" => LicenseEdition.Enterprise,
                _ => LicenseEdition.Free
            };

            if (edition == LicenseEdition.Free)
            {
                return new ActivationResult
                {
                    Success = false,
                    Message = "Invalid edition code"
                };
            }

            // Create license
            var license = new LicenseInfo
            {
                Edition = edition,
                LicenseKey = licenseKey,
                LicensedTo = "User Name", // Would come from server
                Company = "Company Name",
                Email = "user@example.com",
                IssueDate = DateTime.UtcNow,
                ExpirationDate = edition == LicenseEdition.Standard || edition == LicenseEdition.Pro
                    ? DateTime.UtcNow.AddYears(1)
                    : null, // Enterprise is perpetual
                MaxActivations = edition == LicenseEdition.Enterprise ? 0 : 3, // 0 = unlimited
                ActivationCount = 1,
                HardwareFingerprint = GetHardwareFingerprint()
            };

            await SaveLicenseAsync(license);
            _currentLicense = license;

            return new ActivationResult
            {
                Success = true,
                Message = $"✓ Successfully activated {edition} edition",
                License = license
            };
        }

        private async Task<ActivationResult> ActivateOfflineAsync(string licenseKey, string activationCode)
        {
            // Offline activation using activation code
            // In production, activation code would be generated on activation website
            // based on license key + hardware fingerprint

            await Task.Delay(100); // Simulate processing

            // Verify activation code (simplified for demo)
            var expectedCode = GenerateActivationCode(licenseKey, GetHardwareFingerprint());

            if (activationCode != expectedCode)
            {
                return new ActivationResult
                {
                    Success = false,
                    Message = "Invalid activation code"
                };
            }

            // Same as online activation but without server call
            return await ActivateOnlineAsync(licenseKey);
        }

        private async Task DeactivateOnlineAsync(string licenseKey)
        {
            // In production, notify server to decrement activation count
            await Task.Delay(500); // Simulate network call
        }

        private async Task CheckTrialStatusAsync()
        {
            if (_currentLicense == null)
            {
                // Create trial license
                _currentLicense = new LicenseInfo
                {
                    Edition = LicenseEdition.Trial,
                    LicensedTo = "Trial User",
                    IssueDate = DateTime.UtcNow,
                    ExpirationDate = DateTime.UtcNow.AddDays(30),
                    MaxActivations = 1,
                    ActivationCount = 1,
                    HardwareFingerprint = GetHardwareFingerprint()
                };

                await SaveLicenseAsync(_currentLicense);

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("\n🎉 Welcome! You're now using the 30-day trial (Pro features).");
                Console.WriteLine("To purchase a license, visit: https://murtisoft.com/buy\n");
                Console.ResetColor();
            }
            else if (_currentLicense.Edition == LicenseEdition.Trial)
            {
                var daysRemaining = (_currentLicense.ExpirationDate!.Value - DateTime.UtcNow).TotalDays;

                if (daysRemaining <= 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\n⏰ Your trial has expired.");
                    Console.ResetColor();
                    Console.WriteLine("Purchase a license at: https://murtisoft.com/buy");
                    Console.WriteLine("Or continue with free edition (limited features).\n");

                    _currentLicense = CreateFreeLicense();
                    await SaveLicenseAsync(_currentLicense);
                }
                else if (daysRemaining <= 7)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\n⏰ Trial expires in {(int)daysRemaining} days.");
                    Console.ResetColor();
                    Console.WriteLine("Purchase now at: https://murtisoft.com/buy\n");
                }
            }
        }

        private string GetHardwareFingerprint()
        {
            // Generate unique hardware fingerprint
            var components = new[]
            {
                Environment.MachineName,
                Environment.UserName,
                Environment.OSVersion.ToString(),
                Environment.ProcessorCount.ToString()
            };

            var combined = string.Join("|", components);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
            return Convert.ToHexString(hash)[..16];
        }

        private string GenerateActivationCode(string licenseKey, string hardwareFingerprint)
        {
            var combined = licenseKey + hardwareFingerprint;
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
            return Convert.ToHexString(hash)[..12].ToUpperInvariant();
        }

        private LicenseInfo CreateFreeLicense()
        {
            return new LicenseInfo
            {
                Edition = LicenseEdition.Free,
                LicensedTo = "Free User",
                IssueDate = DateTime.UtcNow
            };
        }

        private HashSet<string> GetFreeFeaturesSet()
        {
            return new HashSet<string>
            {
                "scan",
                "connect",
                "disconnect",
                "status",
                "profiles"
            };
        }

        private HashSet<string> GetStandardFeaturesSet()
        {
            var features = GetFreeFeaturesSet();
            features.Add("wifi6-optimize");
            features.Add("fast-roaming");
            features.Add("wpa3");
            features.Add("speed-test");
            return features;
        }

        private HashSet<string> GetProFeaturesSet()
        {
            var features = GetStandardFeaturesSet();
            features.Add("wifi7-mlo");
            features.Add("ai-optimization");
            features.Add("mesh-optimize");
            features.Add("analytics");
            features.Add("priority-support");
            return features;
        }

        private ConsoleColor GetEditionColor(LicenseEdition edition)
        {
            return edition switch
            {
                LicenseEdition.Free => ConsoleColor.Gray,
                LicenseEdition.Trial => ConsoleColor.Cyan,
                LicenseEdition.Standard => ConsoleColor.Green,
                LicenseEdition.Pro => ConsoleColor.Blue,
                LicenseEdition.Enterprise => ConsoleColor.Magenta,
                _ => ConsoleColor.White
            };
        }

        private string MaskLicenseKey(string licenseKey)
        {
            if (licenseKey.Length < 8) return licenseKey;
            var parts = licenseKey.Split('-');
            if (parts.Length < 2) return licenseKey;

            return $"{parts[0]}-****-****-****-{parts[^1]}";
        }

        private string GetLicenseFilePath()
        {
            var dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MurtiWifiConnecter");

            Directory.CreateDirectory(dataDir);
            return Path.Combine(dataDir, ".license");
        }

        private LicenseInfo? LoadLicense()
        {
            try
            {
                var licensePath = GetLicenseFilePath();
                if (File.Exists(licensePath))
                {
                    var json = File.ReadAllText(licensePath);
                    return JsonSerializer.Deserialize<LicenseInfo>(json);
                }
            }
            catch
            {
                // Return null if license can't be loaded
            }

            return null;
        }

        private async Task SaveLicenseAsync(LicenseInfo license)
        {
            try
            {
                var licensePath = GetLicenseFilePath();
                var json = JsonSerializer.Serialize(license, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                await File.WriteAllTextAsync(licensePath, json);
            }
            catch
            {
                // Silent fail
            }
        }
    }

    public class LicenseInfo
    {
        public LicenseEdition Edition { get; set; }
        public string LicenseKey { get; set; } = "";
        public string LicensedTo { get; set; } = "";
        public string Company { get; set; } = "";
        public string Email { get; set; } = "";
        public DateTime IssueDate { get; set; }
        public DateTime? ExpirationDate { get; set; }
        public int MaxActivations { get; set; }
        public int ActivationCount { get; set; }
        public string HardwareFingerprint { get; set; } = "";
    }

    public enum LicenseEdition
    {
        Free,        // Basic features only
        Trial,       // 30-day trial with Pro features
        Standard,    // $79/year - WiFi 6/7, fast roaming, WPA3
        Pro,         // $199/year - AI optimization, mesh, priority support
        Enterprise   // $999/year - Unlimited, API, SLA support
    }

    public class ActivationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public LicenseInfo? License { get; set; }
    }
}
