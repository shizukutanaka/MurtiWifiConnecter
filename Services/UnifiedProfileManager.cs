using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Services
{
    /// <summary>
    /// 統合WiFiプロファイル管理サービス
    /// AutoConnectManager、WifiProfileManager、ProfileExportImportServiceの機能を統合
    /// </summary>
    public class UnifiedProfileManager : IDisposable
    {
        private readonly string _profilesPath;
        private readonly string _backupDirectory;
        private readonly Dictionary<string, SavedProfile> _profiles = new();
        private readonly Dictionary<string, WifiProfileInfo> _profileCache = new();
        private readonly SemaphoreSlim _saveLock = new(1, 1);
        private readonly Timer _cacheCleanupTimer;
        private readonly ConnectionLogger _logger;
        private DateTime _lastCacheRefresh = DateTime.MinValue;
        private bool _disposed = false;

        private const int CacheValidityMinutes = 3;
        private const int MaxCacheSize = 20;

        public bool AutoConnectEnabled { get; set; } = true;
        public bool AutoSavePasswords { get; set; } = true;

        public UnifiedProfileManager(ConnectionLogger logger = null)
        {
            _logger = logger ?? new ConnectionLogger();
            
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MurtiWifiConnecter");
            
            Directory.CreateDirectory(appDataPath);
            
            _profilesPath = Path.Combine(appDataPath, "profiles.json");
            _backupDirectory = Path.Combine(appDataPath, "ProfileBackups");
            Directory.CreateDirectory(_backupDirectory);
            
            LoadProfiles();
            
            _cacheCleanupTimer = new Timer(CacheCleanupCallback, null,
                TimeSpan.FromMinutes(CacheValidityMinutes),
                TimeSpan.FromMinutes(CacheValidityMinutes));
        }

        /// <summary>
        /// WiFiプロファイルを保存
        /// </summary>
        public async Task SaveProfileAsync(string ssid, string password)
        {
            if (!AutoSavePasswords || string.IsNullOrEmpty(ssid))
                return;

            await _saveLock.WaitAsync();
            try
            {
                var existingProfile = _profiles.GetValueOrDefault(ssid);
                
                var profile = new SavedProfile
                {
                    SSID = ssid,
                    EncryptedPassword = EncryptPassword(password),
                    LastConnected = DateTime.Now,
                    AutoConnect = existingProfile?.AutoConnect ?? true,
                    Priority = existingProfile?.Priority ?? 0
                };

                _profiles[ssid] = profile;
                await SaveProfilesToFileAsync();
                
                _logger.Log(LogLevel.Info, "Profile", $"Saved profile for {ssid}");
            }
            finally
            {
                _saveLock.Release();
            }
        }

        /// <summary>
        /// 保存されたパスワードを取得
        /// </summary>
        public string GetSavedPassword(string ssid)
        {
            if (_profiles.TryGetValue(ssid, out var profile))
            {
                try
                {
                    return DecryptPassword(profile.EncryptedPassword);
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        /// <summary>
        /// 自動接続を試行
        /// </summary>
        public async Task<bool> TryAutoConnectAsync(string ssid, CancellationToken cancellationToken = default)
        {
            if (!AutoConnectEnabled || !_profiles.ContainsKey(ssid))
                return false;

            var password = GetSavedPassword(ssid);
            if (string.IsNullOrEmpty(password))
                return false;

            try
            {
                var result = await FastWifiConnector.ConnectAsync(ssid, password, cancellationToken);
                return result.Success;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 保存されたプロファイル一覧を取得
        /// </summary>
        public List<string> GetSavedProfiles()
        {
            return _profiles.Keys.ToList();
        }

        /// <summary>
        /// プロファイルを削除
        /// </summary>
        public async Task RemoveProfileAsync(string ssid)
        {
            await _saveLock.WaitAsync();
            try
            {
                if (_profiles.Remove(ssid))
                {
                    await SaveProfilesToFileAsync();
                    _logger.Log(LogLevel.Info, "Profile", $"Removed profile for {ssid}");
                }
            }
            finally
            {
                _saveLock.Release();
            }
        }

        /// <summary>
        /// プロファイルをバックアップ
        /// </summary>
        public async Task<bool> BackupProfilesToFileAsync(string filePath)
        {
            try
            {
                var backupData = new
                {
                    Timestamp = DateTime.Now,
                    Profiles = _profiles.Values.Select(p => new
                    {
                        p.SSID,
                        p.LastConnected,
                        p.AutoConnect,
                        p.Priority
                    })
                };

                var json = JsonSerializer.Serialize(backupData, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void LoadProfiles()
        {
            try
            {
                if (!File.Exists(_profilesPath))
                    return;

                var json = File.ReadAllText(_profilesPath, Encoding.UTF8);
                var profiles = JsonSerializer.Deserialize<Dictionary<string, SavedProfile>>(json);
                
                if (profiles != null)
                {
                    foreach (var kvp in profiles)
                    {
                        _profiles[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch
            {
                _profiles.Clear();
            }
        }

        private async Task SaveProfilesToFileAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(_profiles, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                await File.WriteAllTextAsync(_profilesPath, json, Encoding.UTF8);
            }
            catch
            {
                // エラーハンドリング（ログのみ）
            }
        }

        private void CacheCleanupCallback(object state)
        {
            if (_disposed) return;

            lock (_profileCache)
            {
                var expiredKeys = _profileCache
                    .Where(kvp => DateTime.Now - _lastCacheRefresh > TimeSpan.FromMinutes(CacheValidityMinutes))
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in expiredKeys)
                {
                    _profileCache.Remove(key);
                }
            }
        }

        private string EncryptPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
                return string.Empty;

            try
            {
                var data = Encoding.UTF8.GetBytes(password);
                var encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encrypted);
            }
            catch
            {
                return password; // フォールバック（暗号化失敗時）
            }
        }

        private string DecryptPassword(string encryptedPassword)
        {
            if (string.IsNullOrEmpty(encryptedPassword))
                return string.Empty;

            try
            {
                var data = Convert.FromBase64String(encryptedPassword);
                var decrypted = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                return encryptedPassword; // フォールバック（復号化失敗時）
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cacheCleanupTimer?.Dispose();
            _saveLock?.Dispose();
            _logger?.Dispose();
        }
    }

    /// <summary>
    /// WiFiプロファイル情報
    /// </summary>
    public class WifiProfileInfo
    {
        public string Name { get; set; }
        public string Authentication { get; set; }
        public string Encryption { get; set; }
        public bool IsConnected { get; set; }
        public DateTime LastUsed { get; set; }
    }
}