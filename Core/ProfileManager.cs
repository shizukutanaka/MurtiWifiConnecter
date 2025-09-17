using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// Manages WiFi network profiles with secure storage
    /// </summary>
    public sealed class ProfileManager
    {
        private readonly string _profilesPath;
        private readonly byte[] _entropy;
        private List<NetworkProfile> _profiles;
        private bool _isDirty;

        private const string ProfileFileName = "network_profiles.dat";
        private const int MaxProfiles = 50;

        public event EventHandler<ProfileEventArgs>? ProfileAdded;
        public event EventHandler<ProfileEventArgs>? ProfileRemoved;
        public event EventHandler? ProfilesUpdated;

        public ProfileManager()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MurtiWifiConnecter");

            if (!Directory.Exists(appDataPath))
                Directory.CreateDirectory(appDataPath);

            _profilesPath = Path.Combine(appDataPath, ProfileFileName);
            _entropy = GenerateEntropy();
            _profiles = new List<NetworkProfile>();

            LoadProfiles();
        }

        /// <summary>
        /// Add or update a network profile
        /// </summary>
        public async Task<bool> SaveProfileAsync(string ssid, string password, bool autoConnect = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ssid) || string.IsNullOrWhiteSpace(password))
                    return false;

                // Check if profile already exists
                var existingProfile = _profiles.FirstOrDefault(p =>
                    string.Equals(p.SSID, ssid, StringComparison.OrdinalIgnoreCase));

                if (existingProfile != null)
                {
                    // Update existing profile
                    existingProfile.EncryptedPassword = ProtectPassword(password);
                    existingProfile.AutoConnect = autoConnect;
                    existingProfile.LastModified = DateTime.UtcNow;
                    existingProfile.ConnectionCount++;
                }
                else
                {
                    // Add new profile
                    var profile = new NetworkProfile
                    {
                        SSID = ssid,
                        EncryptedPassword = ProtectPassword(password),
                        AutoConnect = autoConnect,
                        CreatedDate = DateTime.UtcNow,
                        LastModified = DateTime.UtcNow,
                        ConnectionCount = 1
                    };

                    _profiles.Add(profile);

                    // Limit the number of stored profiles
                    while (_profiles.Count > MaxProfiles)
                    {
                        // Remove least recently used profile
                        var lruProfile = _profiles
                            .Where(p => !p.AutoConnect)
                            .OrderBy(p => p.LastUsed)
                            .FirstOrDefault();

                        if (lruProfile != null)
                        {
                            _profiles.Remove(lruProfile);
                            OnProfileRemoved(lruProfile.SSID);
                        }
                        else
                        {
                            break;
                        }
                    }

                    OnProfileAdded(ssid);
                }

                _isDirty = true;
                await SaveProfilesAsync();
                OnProfilesUpdated();

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save profile: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Get a network profile
        /// </summary>
        public NetworkProfile? GetProfile(string ssid)
        {
            if (string.IsNullOrWhiteSpace(ssid))
                return null;

            var profile = _profiles.FirstOrDefault(p =>
                string.Equals(p.SSID, ssid, StringComparison.OrdinalIgnoreCase));

            if (profile != null)
            {
                profile.LastUsed = DateTime.UtcNow;
                _isDirty = true;
            }

            return profile;
        }

        /// <summary>
        /// Get password for a network
        /// </summary>
        public string? GetPassword(string ssid)
        {
            var profile = GetProfile(ssid);

            if (profile != null && !string.IsNullOrEmpty(profile.EncryptedPassword))
            {
                try
                {
                    return UnprotectPassword(profile.EncryptedPassword);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to decrypt password: {ex.Message}", ex);
                }
            }

            return null;
        }

        /// <summary>
        /// Remove a network profile
        /// </summary>
        public async Task<bool> RemoveProfileAsync(string ssid)
        {
            var profile = _profiles.FirstOrDefault(p =>
                string.Equals(p.SSID, ssid, StringComparison.OrdinalIgnoreCase));

            if (profile != null)
            {
                _profiles.Remove(profile);
                _isDirty = true;
                await SaveProfilesAsync();
                OnProfileRemoved(ssid);
                OnProfilesUpdated();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Get all network profiles
        /// </summary>
        public IReadOnlyList<NetworkProfile> GetAllProfiles()
        {
            return _profiles.AsReadOnly();
        }

        /// <summary>
        /// Get auto-connect profiles
        /// </summary>
        public IReadOnlyList<NetworkProfile> GetAutoConnectProfiles()
        {
            return _profiles.Where(p => p.AutoConnect).ToList().AsReadOnly();
        }

        /// <summary>
        /// Clear all profiles
        /// </summary>
        public async Task ClearAllProfilesAsync()
        {
            _profiles.Clear();
            _isDirty = true;
            await SaveProfilesAsync();
            OnProfilesUpdated();
        }

        /// <summary>
        /// Export profiles to file
        /// </summary>
        public async Task<bool> ExportProfilesAsync(string filePath)
        {
            try
            {
                var exportData = new ProfileExportData
                {
                    Version = "1.0",
                    ExportDate = DateTime.UtcNow,
                    Profiles = _profiles.Select(p => new ExportedProfile
                    {
                        SSID = p.SSID,
                        AutoConnect = p.AutoConnect,
                        ConnectionCount = p.ConnectionCount,
                        LastUsed = p.LastUsed
                        // Password is not exported for security
                    }).ToList()
                };

                var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(filePath, json);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to export profiles: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Import profiles from file
        /// </summary>
        public async Task<int> ImportProfilesAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return 0;

                var json = await File.ReadAllTextAsync(filePath);
                var exportData = JsonSerializer.Deserialize<ProfileExportData>(json);

                if (exportData == null || exportData.Profiles == null)
                    return 0;

                int imported = 0;

                foreach (var exportedProfile in exportData.Profiles)
                {
                    if (!_profiles.Any(p => string.Equals(p.SSID, exportedProfile.SSID, StringComparison.OrdinalIgnoreCase)))
                    {
                        var profile = new NetworkProfile
                        {
                            SSID = exportedProfile.SSID,
                            AutoConnect = exportedProfile.AutoConnect,
                            ConnectionCount = exportedProfile.ConnectionCount,
                            LastUsed = exportedProfile.LastUsed,
                            CreatedDate = DateTime.UtcNow,
                            LastModified = DateTime.UtcNow
                        };

                        _profiles.Add(profile);
                        imported++;
                    }
                }

                if (imported > 0)
                {
                    _isDirty = true;
                    await SaveProfilesAsync();
                    OnProfilesUpdated();
                }

                return imported;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to import profiles: {ex.Message}", ex);
                return 0;
            }
        }

        private void LoadProfiles()
        {
            try
            {
                if (File.Exists(_profilesPath))
                {
                    var encryptedData = File.ReadAllBytes(_profilesPath);
                    var decryptedData = ProtectedData.Unprotect(encryptedData, _entropy, DataProtectionScope.CurrentUser);
                    var json = Encoding.UTF8.GetString(decryptedData);

                    _profiles = JsonSerializer.Deserialize<List<NetworkProfile>>(json) ?? new List<NetworkProfile>();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load profiles: {ex.Message}", ex);
                _profiles = new List<NetworkProfile>();
            }
        }

        private async Task SaveProfilesAsync()
        {
            if (!_isDirty)
                return;

            try
            {
                var json = JsonSerializer.Serialize(_profiles);
                var data = Encoding.UTF8.GetBytes(json);
                var encryptedData = ProtectedData.Protect(data, _entropy, DataProtectionScope.CurrentUser);

                await File.WriteAllBytesAsync(_profilesPath, encryptedData);
                _isDirty = false;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save profiles: {ex.Message}", ex);
            }
        }

        private string ProtectPassword(string password)
        {
            try
            {
                var data = Encoding.UTF8.GetBytes(password);
                var encrypted = ProtectedData.Protect(data, _entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encrypted);
            }
            catch
            {
                return string.Empty;
            }
        }

        private string UnprotectPassword(string encryptedPassword)
        {
            try
            {
                var encrypted = Convert.FromBase64String(encryptedPassword);
                var decrypted = ProtectedData.Unprotect(encrypted, _entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                return string.Empty;
            }
        }

        private byte[] GenerateEntropy()
        {
            var entropy = new byte[16];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(entropy);
            return entropy;
        }

        private void OnProfileAdded(string ssid)
        {
            ProfileAdded?.Invoke(this, new ProfileEventArgs { SSID = ssid });
        }

        private void OnProfileRemoved(string ssid)
        {
            ProfileRemoved?.Invoke(this, new ProfileEventArgs { SSID = ssid });
        }

        private void OnProfilesUpdated()
        {
            ProfilesUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    // Supporting classes
    public class NetworkProfile
    {
        public string SSID { get; set; } = string.Empty;
        public string EncryptedPassword { get; set; } = string.Empty;
        public bool AutoConnect { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime LastModified { get; set; }
        public DateTime LastUsed { get; set; }
        public int ConnectionCount { get; set; }
        public int Priority { get; set; }
    }

    public class ProfileEventArgs : EventArgs
    {
        public string SSID { get; set; } = string.Empty;
    }

    public class ProfileExportData
    {
        public string Version { get; set; } = string.Empty;
        public DateTime ExportDate { get; set; }
        public List<ExportedProfile> Profiles { get; set; } = new();
    }

    public class ExportedProfile
    {
        public string SSID { get; set; } = string.Empty;
        public bool AutoConnect { get; set; }
        public int ConnectionCount { get; set; }
        public DateTime LastUsed { get; set; }
    }
}