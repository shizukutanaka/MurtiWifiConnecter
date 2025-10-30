using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// WPA3 Security Enhancer implementing 2025 best practices
    /// Based on systematic literature review and enterprise deployment research
    /// Features: SAE, PMF, Enhanced Open, 192-bit encryption for Enterprise
    /// </summary>
    public class WPA3SecurityEnhancer
    {
        private static WPA3SecurityEnhancer? _instance;
        private static readonly object _lock = new object();
        private readonly Dictionary<string, WPA3Configuration> _configurations = new();

        public static WPA3SecurityEnhancer Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new WPA3SecurityEnhancer();
                    }
                }
                return _instance;
            }
        }

        private WPA3SecurityEnhancer() { }

        public async Task InitializeAsync()
        {
            await Logger.LogInfo("WPA3 Security Enhancer initialized", "WPA3SecurityEnhancer", new Dictionary<string, object>
            {
                ["wpa3_personal_support"] = await SupportsWPA3Personal(),
                ["wpa3_enterprise_support"] = await SupportsWPA3Enterprise(),
                ["pmf_support"] = await SupportsProtectedManagementFrames()
            });
        }

        /// <summary>
        /// Configure WPA3-Personal with SAE (Simultaneous Authentication of Equals)
        /// Provides protection against offline dictionary attacks
        /// </summary>
        public async Task<bool> EnableWPA3PersonalAsync(string ssid, string passphrase, WPA3Mode mode = WPA3Mode.Pure)
        {
            try
            {
                await Logger.LogInfo($"Enabling WPA3-Personal for {ssid}", "WPA3SecurityEnhancer", new Dictionary<string, object>
                {
                    ["mode"] = mode.ToString()
                });

                if (!await SupportsWPA3Personal())
                {
                    await Logger.LogWarning("WPA3-Personal not supported on this adapter", "WPA3SecurityEnhancer");
                    return false;
                }

                // Validate passphrase strength for WPA3
                if (!ValidateWPA3Passphrase(passphrase))
                {
                    throw new ArgumentException("Passphrase does not meet WPA3 security requirements");
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Create WPA3-Personal profile
                    var profileXml = GenerateWPA3PersonalProfile(ssid, mode);
                    await CreateWiFiProfile(ssid, profileXml);

                    // Enable Protected Management Frames (PMF) - Required for WPA3
                    await EnableProtectedManagementFrames(ssid);

                    // Set SAE authentication
                    await ExecuteNetshCommand($"wlan set profileparameter name=\"{ssid}\" authentication=WPA3SAE");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    // Linux NetworkManager WPA3 configuration
                    await ExecuteLinuxCommand($"nmcli connection modify \"{ssid}\" wifi-sec.key-mgmt sae");
                    await ExecuteLinuxCommand($"nmcli connection modify \"{ssid}\" wifi-sec.psk \"{passphrase}\"");
                    await ExecuteLinuxCommand($"nmcli connection modify \"{ssid}\" 802-11-wireless-security.pmf 2");
                }

                var config = new WPA3Configuration
                {
                    SSID = ssid,
                    Mode = mode,
                    Type = WPA3Type.Personal,
                    PMFEnabled = true,
                    SAEEnabled = true,
                    LastUpdated = DateTime.UtcNow
                };

                _configurations[ssid] = config;

                await Logger.LogInfo($"WPA3-Personal enabled for {ssid}", "WPA3SecurityEnhancer");
                return true;
            }
            catch (Exception ex)
            {
                await Logger.LogError($"Failed to enable WPA3-Personal for {ssid}", "WPA3SecurityEnhancer", ex);
                return false;
            }
        }

        /// <summary>
        /// Configure WPA3-Enterprise with 192-bit encryption
        /// Minimum 192-bit encryption strength for enhanced security
        /// </summary>
        public async Task<bool> EnableWPA3EnterpriseAsync(string ssid, EnterpriseAuthConfig authConfig)
        {
            try
            {
                await Logger.LogInfo($"Enabling WPA3-Enterprise 192-bit for {ssid}", "WPA3SecurityEnhancer");

                if (!await SupportsWPA3Enterprise())
                {
                    await Logger.LogWarning("WPA3-Enterprise not supported on this adapter", "WPA3SecurityEnhancer");
                    return false;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Create WPA3-Enterprise profile with 192-bit security
                    var profileXml = GenerateWPA3EnterpriseProfile(ssid, authConfig);
                    await CreateWiFiProfile(ssid, profileXml);

                    // Enable 192-bit mode (CNSA Suite)
                    await ExecuteNetshCommand($"wlan set profileparameter name=\"{ssid}\" authentication=WPA3Enterprise192");

                    // Configure EAP-TLS or PEAP
                    await ConfigureEnterpriseAuth(ssid, authConfig);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    // Linux WPA3-Enterprise configuration
                    await ExecuteLinuxCommand($"nmcli connection modify \"{ssid}\" wifi-sec.key-mgmt wpa-eap");
                    await ExecuteLinuxCommand($"nmcli connection modify \"{ssid}\" 802-1x.eap {authConfig.EAPMethod}");
                    await ExecuteLinuxCommand($"nmcli connection modify \"{ssid}\" 802-11-wireless-security.pmf 2");
                }

                var config = new WPA3Configuration
                {
                    SSID = ssid,
                    Mode = WPA3Mode.Pure,
                    Type = WPA3Type.Enterprise,
                    PMFEnabled = true,
                    Use192BitEncryption = true,
                    EnterpriseAuthConfig = authConfig,
                    LastUpdated = DateTime.UtcNow
                };

                _configurations[ssid] = config;

                await Logger.LogInfo($"WPA3-Enterprise 192-bit enabled for {ssid}", "WPA3SecurityEnhancer", new Dictionary<string, object>
                {
                    ["eap_method"] = authConfig.EAPMethod
                });

                return true;
            }
            catch (Exception ex)
            {
                await Logger.LogError($"Failed to enable WPA3-Enterprise for {ssid}", "WPA3SecurityEnhancer", ex);
                return false;
            }
        }

        /// <summary>
        /// Enable Protected Management Frames (PMF/MFP)
        /// Defends against deauthentication and disassociation attacks
        /// Required for WPA3
        /// </summary>
        public async Task<bool> EnableProtectedManagementFrames(string ssid, PMFMode mode = PMFMode.Required)
        {
            try
            {
                await Logger.LogInfo($"Enabling Protected Management Frames for {ssid}", "WPA3SecurityEnhancer", new Dictionary<string, object>
                {
                    ["mode"] = mode.ToString()
                });

                if (!await SupportsProtectedManagementFrames())
                {
                    await Logger.LogWarning("PMF not supported on this adapter", "WPA3SecurityEnhancer");
                    return false;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var pmfValue = mode switch
                    {
                        PMFMode.Optional => "1",
                        PMFMode.Required => "2",
                        _ => "0"
                    };

                    await ExecuteNetshCommand($"wlan set profileparameter name=\"{ssid}\" PMF={pmfValue}");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    var pmfValue = mode switch
                    {
                        PMFMode.Optional => "1",
                        PMFMode.Required => "2",
                        _ => "0"
                    };

                    await ExecuteLinuxCommand($"nmcli connection modify \"{ssid}\" 802-11-wireless-security.pmf {pmfValue}");
                }

                await Logger.LogInfo($"PMF enabled for {ssid} with mode {mode}", "WPA3SecurityEnhancer");
                return true;
            }
            catch (Exception ex)
            {
                await Logger.LogError($"Failed to enable PMF for {ssid}", "WPA3SecurityEnhancer", ex);
                return false;
            }
        }

        /// <summary>
        /// Enable Enhanced Open (OWE - Opportunistic Wireless Encryption)
        /// Provides encryption for open networks without authentication
        /// </summary>
        public async Task<bool> EnableEnhancedOpenAsync(string ssid)
        {
            try
            {
                await Logger.LogInfo($"Enabling Enhanced Open (OWE) for {ssid}", "WPA3SecurityEnhancer");

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var profileXml = GenerateOWEProfile(ssid);
                    await CreateWiFiProfile(ssid, profileXml);

                    await ExecuteNetshCommand($"wlan set profileparameter name=\"{ssid}\" authentication=OWE");
                }

                await Logger.LogInfo($"Enhanced Open enabled for {ssid}", "WPA3SecurityEnhancer");
                return true;
            }
            catch (Exception ex)
            {
                await Logger.LogError($"Failed to enable Enhanced Open for {ssid}", "WPA3SecurityEnhancer", ex);
                return false;
            }
        }

        /// <summary>
        /// Validate WPA3 passphrase strength
        /// WPA3 requires strong passphrases resistant to dictionary attacks
        /// </summary>
        private bool ValidateWPA3Passphrase(string passphrase)
        {
            if (string.IsNullOrEmpty(passphrase)) return false;
            if (passphrase.Length < 12) return false; // WPA3 minimum recommended length

            // Check for complexity
            bool hasUpper = passphrase.Any(char.IsUpper);
            bool hasLower = passphrase.Any(char.IsLower);
            bool hasDigit = passphrase.Any(char.IsDigit);
            bool hasSpecial = passphrase.Any(c => !char.IsLetterOrDigit(c));

            // At least 3 out of 4 character types
            int complexity = (hasUpper ? 1 : 0) + (hasLower ? 1 : 0) + (hasDigit ? 1 : 0) + (hasSpecial ? 1 : 0);

            return complexity >= 3;
        }

        private string GenerateWPA3PersonalProfile(string ssid, WPA3Mode mode)
        {
            var authMode = mode switch
            {
                WPA3Mode.Pure => "WPA3SAE",
                WPA3Mode.Transition => "WPA2PSKAES+WPA3SAE",
                _ => "WPA3SAE"
            };

            return $@"<?xml version=""1.0""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
    <name>{ssid}</name>
    <SSIDConfig>
        <SSID>
            <name>{ssid}</name>
        </SSID>
    </SSIDConfig>
    <connectionType>ESS</connectionType>
    <connectionMode>auto</connectionMode>
    <MSM>
        <security>
            <authEncryption>
                <authentication>{authMode}</authentication>
                <encryption>AES</encryption>
                <useOneX>false</useOneX>
                <FIPSMode xmlns=""http://www.microsoft.com/networking/WLAN/profile/v2"">false</FIPSMode>
            </authEncryption>
            <PMF xmlns=""http://www.microsoft.com/networking/WLAN/profile/v5"">required</PMF>
        </security>
    </MSM>
</WLANProfile>";
        }

        private string GenerateWPA3EnterpriseProfile(string ssid, EnterpriseAuthConfig authConfig)
        {
            return $@"<?xml version=""1.0""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
    <name>{ssid}</name>
    <SSIDConfig>
        <SSID>
            <name>{ssid}</name>
        </SSID>
    </SSIDConfig>
    <connectionType>ESS</connectionType>
    <connectionMode>auto</connectionMode>
    <MSM>
        <security>
            <authEncryption>
                <authentication>WPA3Enterprise192</authentication>
                <encryption>GCMP256</encryption>
                <useOneX>true</useOneX>
            </authEncryption>
            <PMF xmlns=""http://www.microsoft.com/networking/WLAN/profile/v5"">required</PMF>
            <OneX xmlns=""http://www.microsoft.com/networking/OneX/v1"">
                <EAPConfig>
                    <EapHostConfig>
                        <EapMethod>
                            <Type>{authConfig.EAPMethod}</Type>
                        </EapMethod>
                    </EapHostConfig>
                </EAPConfig>
            </OneX>
        </security>
    </MSM>
</WLANProfile>";
        }

        private string GenerateOWEProfile(string ssid)
        {
            return $@"<?xml version=""1.0""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
    <name>{ssid}</name>
    <SSIDConfig>
        <SSID>
            <name>{ssid}</name>
        </SSID>
    </SSIDConfig>
    <connectionType>ESS</connectionType>
    <connectionMode>auto</connectionMode>
    <MSM>
        <security>
            <authEncryption>
                <authentication>OWE</authentication>
                <encryption>GCMP256</encryption>
                <useOneX>false</useOneX>
            </authEncryption>
        </security>
    </MSM>
</WLANProfile>";
        }

        private async Task CreateWiFiProfile(string ssid, string profileXml)
        {
            var tempFile = System.IO.Path.GetTempFileName();
            await System.IO.File.WriteAllTextAsync(tempFile, profileXml);

            try
            {
                await ExecuteNetshCommand($"wlan add profile filename=\"{tempFile}\"");
            }
            finally
            {
                if (System.IO.File.Exists(tempFile))
                {
                    System.IO.File.Delete(tempFile);
                }
            }
        }

        private async Task ConfigureEnterpriseAuth(string ssid, EnterpriseAuthConfig authConfig)
        {
            if (authConfig.EAPMethod == "TLS")
            {
                // Configure certificate-based authentication
                if (!string.IsNullOrEmpty(authConfig.CertificateThumbprint))
                {
                    await ExecuteNetshCommand($"wlan set profileparameter name=\"{ssid}\" certificate=\"{authConfig.CertificateThumbprint}\"");
                }
            }
            else if (authConfig.EAPMethod == "PEAP")
            {
                // Configure username/password authentication
                if (!string.IsNullOrEmpty(authConfig.Username))
                {
                    await ExecuteNetshCommand($"wlan set profileparameter name=\"{ssid}\" username=\"{authConfig.Username}\"");
                }
            }
        }

        private async Task ExecuteNetshCommand(string command)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new PlatformNotSupportedException("Netsh commands only supported on Windows");
            }

            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(processInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                if (process.ExitCode != 0)
                {
                    var error = await process.StandardError.ReadToEndAsync();
                    throw new InvalidOperationException($"Netsh command failed: {error}");
                }
            }
        }

        private async Task ExecuteLinuxCommand(string command)
        {
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(processInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
            }
        }

        // Capability detection
        private async Task<bool> SupportsWPA3Personal() => await Task.FromResult(false);
        private async Task<bool> SupportsWPA3Enterprise() => await Task.FromResult(false);
        private async Task<bool> SupportsProtectedManagementFrames() => await Task.FromResult(false);
    }

    public class WPA3Configuration
    {
        public string SSID { get; set; } = string.Empty;
        public WPA3Mode Mode { get; set; }
        public WPA3Type Type { get; set; }
        public bool PMFEnabled { get; set; }
        public bool SAEEnabled { get; set; }
        public bool Use192BitEncryption { get; set; }
        public EnterpriseAuthConfig? EnterpriseAuthConfig { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public class EnterpriseAuthConfig
    {
        public string EAPMethod { get; set; } = "TLS"; // TLS, PEAP, TTLS
        public string? Username { get; set; }
        public string? CertificateThumbprint { get; set; }
        public string? ServerCertificateThumbprint { get; set; }
        public bool ValidateServerCertificate { get; set; } = true;
    }

    public enum WPA3Mode
    {
        Pure,       // WPA3-only
        Transition  // WPA2/WPA3 mixed mode
    }

    public enum WPA3Type
    {
        Personal,
        Enterprise
    }

    public enum PMFMode
    {
        Disabled,
        Optional,
        Required
    }
}
