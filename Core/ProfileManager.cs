using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// WiFi profile management system
    /// Stores and manages saved network profiles for quick reconnection
    /// </summary>
    public class ProfileManager
    {
        private readonly string _profilesDirectory;
        private readonly string _profilesFile;
        private List<WiFiProfile> _profiles;

        public ProfileManager()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MurtiWifiConnecter"
            );

            _profilesDirectory = appDataPath;
            _profilesFile = Path.Combine(appDataPath, "profiles.json");
            _profiles = new List<WiFiProfile>();

            // Ensure directory exists
            Directory.CreateDirectory(_profilesDirectory);
        }

        /// <summary>
        /// Load profiles from disk
        /// </summary>
        public async Task LoadProfilesAsync()
        {
            try
            {
                if (!File.Exists(_profilesFile))
                {
                    _profiles = new List<WiFiProfile>();
                    return;
                }

                var json = await File.ReadAllTextAsync(_profilesFile);
                _profiles = JsonSerializer.Deserialize<List<WiFiProfile>>(json) ?? new List<WiFiProfile>();
            }
            catch
            {
                _profiles = new List<WiFiProfile>();
            }
        }

        /// <summary>
        /// Save profiles to disk
        /// </summary>
        public async Task SaveProfilesAsync()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_profiles, options);
                await File.WriteAllTextAsync(_profilesFile, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not save profiles: {ex.Message}");
            }
        }

        /// <summary>
        /// Add or update a profile
        /// </summary>
        public async Task<bool> AddOrUpdateProfileAsync(string ssid, string? password = null, string? band = null)
        {
            try
            {
                // Check if profile exists
                var existing = _profiles.FirstOrDefault(p => p.SSID == ssid);

                if (existing != null)
                {
                    existing.LastConnected = DateTime.UtcNow;
                    existing.ConnectionCount++;
                    if (!string.IsNullOrEmpty(password))
                        existing.PasswordHash = HashPassword(password);
                    if (!string.IsNullOrEmpty(band))
                        existing.PreferredBand = band;
                }
                else
                {
                    var profile = new WiFiProfile
                    {
                        Id = Guid.NewGuid().ToString(),
                        SSID = ssid,
                        PasswordHash = !string.IsNullOrEmpty(password) ? HashPassword(password) : null,
                        PreferredBand = band ?? "Auto",
                        CreatedAt = DateTime.UtcNow,
                        LastConnected = DateTime.UtcNow,
                        ConnectionCount = 1
                    };

                    _profiles.Add(profile);
                }

                await SaveProfilesAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get all profiles
        /// </summary>
        public List<WiFiProfile> GetAllProfiles()
        {
            return _profiles.OrderByDescending(p => p.LastConnected).ToList();
        }

        /// <summary>
        /// Get profile by SSID
        /// </summary>
        public WiFiProfile? GetProfile(string ssid)
        {
            return _profiles.FirstOrDefault(p => p.SSID == ssid);
        }

        /// <summary>
        /// Delete a profile
        /// </summary>
        public async Task<bool> DeleteProfileAsync(string ssid)
        {
            var profile = _profiles.FirstOrDefault(p => p.SSID == ssid);
            if (profile != null)
            {
                _profiles.Remove(profile);
                await SaveProfilesAsync();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Get most recently used profiles
        /// </summary>
        public List<WiFiProfile> GetRecentProfiles(int count = 5)
        {
            return _profiles
                .OrderByDescending(p => p.LastConnected)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// Get most frequently used profiles
        /// </summary>
        public List<WiFiProfile> GetFrequentProfiles(int count = 5)
        {
            return _profiles
                .OrderByDescending(p => p.ConnectionCount)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// Simple password hashing (not cryptographically secure)
        /// For production, use proper encryption (DPAPI)
        /// </summary>
        private static string HashPassword(string password)
        {
            // Simple hash - should use DPAPI in production
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var hashedBytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hashedBytes);
            }
        }

        /// <summary>
        /// Clear all profiles
        /// </summary>
        public async Task ClearAllProfilesAsync()
        {
            _profiles.Clear();
            await SaveProfilesAsync();
        }
    }

    /// <summary>
    /// WiFi profile data
    /// </summary>
    public class WiFiProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string SSID { get; set; } = string.Empty;
        public string? PasswordHash { get; set; }
        public string PreferredBand { get; set; } = "Auto";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastConnected { get; set; } = DateTime.UtcNow;
        public int ConnectionCount { get; set; } = 0;

        /// <summary>
        /// Format profile for display
        /// </summary>
        public override string ToString()
        {
            var lastConnected = LastConnected == default ? "Never" : LastConnected.ToString("yyyy-MM-dd HH:mm");
            return $"{SSID} (Connected {ConnectionCount}x, Last: {lastConnected})";
        }
    }
}
