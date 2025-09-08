using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    public class WifiProfileManager : IDisposable
    {
        private readonly string _backupDirectory;
        private readonly Dictionary<string, WifiProfileInfo> _profileCache;
        private readonly object _lockObject = new object();
        private DateTime _lastCacheRefresh = DateTime.MinValue;
        private readonly System.Threading.Timer _cacheCleanupTimer;
        private bool _disposed = false;
        private const int CacheValidityMinutes = 3; // 5分から3分に短縮
        private const int MaxCacheSize = 20; // キャッシュサイズ制限
        
        public WifiProfileManager()
        {
            // QuickSettingsManagerからアプリケーションデータパスを取得
            var appDataPath = string.IsNullOrEmpty(QuickSettingsManager.AppDataPath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MurtiWifiConnecter")
                : QuickSettingsManager.AppDataPath;
            
            _backupDirectory = Path.Combine(appDataPath, "Backups");
            _profileCache = new Dictionary<string, WifiProfileInfo>(MaxCacheSize);
            
            // 定期的にキャッシュをクリーンアップ
            _cacheCleanupTimer = new System.Threading.Timer(CacheCleanupCallback, null, 
                TimeSpan.FromMinutes(CacheValidityMinutes), TimeSpan.FromMinutes(CacheValidityMinutes));
        }
        public async Task<List<string>> GetSavedProfilesAsync()
        {
            try
            {
                var output = await NetworkUtils.ExecuteNetshCommandAsync("wlan show profiles", 10000);
                if (string.IsNullOrEmpty(output)) return new List<string>();

                var profiles = new List<string>();
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var line in lines)
                {
                    if (line.Contains(":") && (line.Contains("All User Profile") || line.Contains("User Profile")))
                    {
                        var colonIndex = line.LastIndexOf(':');
                        if (colonIndex > 0 && colonIndex < line.Length - 1)
                        {
                            var profileName = line.Substring(colonIndex + 1).Trim();
                            if (!string.IsNullOrWhiteSpace(profileName))
                                profiles.Add(profileName);
                        }
                    }
                }

                return profiles.Distinct().OrderBy(p => p).ToList();
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("WifiProfileManager.GetSavedProfilesAsync", ex);
                return new List<string>();
            }
        }

        public async Task<bool> DeleteProfileAsync(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName)) return false;

            try
            {
                var safeProfileName = System.Security.SecurityElement.Escape(profileName);
                var success = await NetworkUtils.ExecuteNetshCommandWithResultAsync(
                    $"wlan delete profile name=\"{safeProfileName}\"", 10000);
                
                // キャッシュからも削除
                if (success)
                {
                    lock (_lockObject)
                    {
                        _profileCache.Remove(profileName);
                    }
                }
                
                return success;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("WifiProfileManager.DeleteProfileAsync", ex);
                return false;
            }
        }

        public async Task<WifiProfileInfo> GetProfileInfoAsync(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName)) 
                return new WifiProfileInfo { ProfileName = profileName };

            // キャッシュチェック（5分間有効）
            lock (_lockObject)
            {
                if (_profileCache.TryGetValue(profileName, out var cachedInfo) &&
                    (DateTime.Now - _lastCacheRefresh).TotalMinutes < CacheValidityMinutes)
                {
                    return cachedInfo;
                }
            }

            try
            {
                var safeProfileName = System.Security.SecurityElement.Escape(profileName);
                var output = await NetworkUtils.ExecuteNetshCommandAsync(
                    $"wlan show profile name=\"{safeProfileName}\" key=clear", 10000);

                var info = new WifiProfileInfo { ProfileName = profileName };
                
                if (!string.IsNullOrEmpty(output))
                {
                    var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                    foreach (var line in lines)
                    {
                        var trimmedLine = line.Trim();
                        if (trimmedLine.StartsWith("SSID name", StringComparison.OrdinalIgnoreCase))
                        {
                            var colonIndex = trimmedLine.IndexOf(':');
                            if (colonIndex > 0)
                            {
                                info.SSID = trimmedLine.Substring(colonIndex + 1).Trim().Trim('"');
                            }
                        }
                        else if (trimmedLine.StartsWith("Authentication", StringComparison.OrdinalIgnoreCase))
                        {
                            var colonIndex = trimmedLine.IndexOf(':');
                            if (colonIndex > 0)
                            {
                                info.Authentication = trimmedLine.Substring(colonIndex + 1).Trim();
                            }
                        }
                        else if (trimmedLine.StartsWith("Connection mode", StringComparison.OrdinalIgnoreCase))
                        {
                            var colonIndex = trimmedLine.IndexOf(':');
                            if (colonIndex > 0)
                            {
                                info.AutoConnect = trimmedLine.Substring(colonIndex + 1).Trim()
                                    .Equals("Connect automatically", StringComparison.OrdinalIgnoreCase);
                            }
                        }
                    }
                }

                // キャッシュに保存
                lock (_lockObject)
                {
                    _profileCache[profileName] = info;
                    _lastCacheRefresh = DateTime.Now;
                }

                return info;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("WifiProfileManager.GetProfileInfoAsync", ex);
                return new WifiProfileInfo { ProfileName = profileName };
            }
        }

        public async Task<bool> BackupProfilesAsync(string backupName = null)
        {
            try
            {
                if (!Directory.Exists(_backupDirectory))
                    Directory.CreateDirectory(_backupDirectory);

                var profiles = await GetSavedProfilesAsync();
                if (!profiles.Any()) return false;

                var backupFileName = string.IsNullOrEmpty(backupName) 
                    ? $"profiles_backup_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                    : $"{NetworkUtils.CreateSafeFileName(backupName, 50)}.txt";
                    
                var backupPath = Path.Combine(_backupDirectory, backupFileName);
                
                var backupContent = new List<string>();
                backupContent.Add($"# WiFi Profiles Backup - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                backupContent.Add($"# Total Profiles: {profiles.Count}");
                backupContent.Add("");
                
                foreach (var profile in profiles)
                {
                    var info = await GetProfileInfoAsync(profile);
                    backupContent.Add($"Profile: {profile}");
                    backupContent.Add($"  SSID: {info.SSID}");
                    backupContent.Add($"  Authentication: {info.Authentication}");
                    backupContent.Add($"  AutoConnect: {info.AutoConnect}");
                    backupContent.Add("");
                }
                
                await File.WriteAllLinesAsync(backupPath, backupContent);
                
                // メモリ効率化: LINQ回避でバックアップクリーンアップ
                var backupFiles = Directory.GetFiles(_backupDirectory, "*.txt");
                if (backupFiles.Length > 5) // 10個から5個に削減
                {
                    var fileInfos = new (string Path, DateTime Created)[backupFiles.Length];
                    for (int i = 0; i < backupFiles.Length; i++)
                    {
                        fileInfos[i] = (backupFiles[i], new FileInfo(backupFiles[i]).CreationTime);
                    }
                    
                    Array.Sort(fileInfos, (a, b) => b.Created.CompareTo(a.Created));
                    
                    for (int i = 5; i < fileInfos.Length; i++)
                    {
                        try 
                        { 
                            // セキュリティ強化: バックアップファイルの安全な削除
                            SecurityManager.SecureDeleteFile(fileInfos[i].Path); 
                        } 
                        catch (Exception ex)
                        {
                            ErrorHandler.LogError("WifiProfileManager.BackupCleanup", ex);
                        }
                    }
                }
                
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<List<string>> GetBackupsAsync()
        {
            try
            {
                if (!Directory.Exists(_backupDirectory))
                    return new List<string>();
                    
                return await Task.Run(() =>
                {
                    // メモリ効率化: LINQ回避
                    var files = Directory.GetFiles(_backupDirectory, "*.txt");
                    var result = new List<string>(files.Length);
                    
                    for (int i = 0; i < files.Length; i++)
                    {
                        result.Add(Path.GetFileNameWithoutExtension(files[i]));
                    }
                    
                    result.Sort((a, b) => string.Compare(b, a, StringComparison.Ordinal));
                    return result;
                });
            }
            catch
            {
                return new List<string>();
            }
        }

        public async Task<bool> RestoreProfileAsync(string profileName, string ssid, string password)
        {
            if (string.IsNullOrWhiteSpace(profileName) || string.IsNullOrWhiteSpace(ssid))
                return false;
                
            try
            {
                // XMLプロファイル作成
                string safePassword = System.Security.SecurityElement.Escape(password ?? "");
                string safeSsid = System.Security.SecurityElement.Escape(ssid);
                string safeProfileName = System.Security.SecurityElement.Escape(profileName);
                
                string profileXml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
    <name>{safeProfileName}</name>
    <SSIDConfig>
        <SSID>
            <name>{safeSsid}</name>
        </SSID>
    </SSIDConfig>
    <connectionType>ESS</connectionType>
    <connectionMode>auto</connectionMode>
    <MSM>
        <security>
            <authEncryption>
                <authentication>WPA2PSK</authentication>
                <encryption>AES</encryption>
                <useOneX>false</useOneX>
            </authEncryption>
            {(string.IsNullOrEmpty(password) ? "" : $@"<sharedKey>
                <keyType>passPhrase</keyType>
                <protected>false</protected>
                <keyMaterial>{safePassword}</keyMaterial>
            </sharedKey>")}
        </security>
    </MSM>
</WLANProfile>";

                // セキュリティ強化: 安全な一時ファイル作成
                var tempPath = SecurityManager.CreateSecureTempFile(".xml");
                await File.WriteAllTextAsync(tempPath, profileXml);
                
                var success = await NetworkUtils.ExecuteNetshCommandWithResultAsync(
                    $"wlan add profile filename=\"{tempPath}\" user=current",
                    10000);
                
                // セキュリティ強化: 機密データの安全な削除
                try 
                { 
                    SecurityManager.SecureDeleteFile(tempPath); 
                } 
                catch (Exception ex)
                {
                    ErrorHandler.LogError("WifiProfileManager.SecureFileDelete", ex);
                }
                
                return success;
            }
            catch
            {
                return false;
            }
        }

        public async Task<int> CleanupOldProfilesAsync(int keepCount = 20)
        {
            try
            {
                var profiles = await GetSavedProfilesAsync();
                if (profiles.Count <= keepCount) return 0;

                var profilesToDelete = profiles.Skip(keepCount).ToList();
                int deletedCount = 0;

                foreach (var profile in profilesToDelete)
                {
                    if (await DeleteProfileAsync(profile))
                        deletedCount++;
                    
                    await Task.Delay(100); // 負荷軽減
                }

                return deletedCount;
            }
            catch
            {
                return 0;
            }
        }
        
        private void CacheCleanupCallback(object state)
        {
            if (_disposed) return;
            
            lock (_lockObject)
            {
                try
                {
                    if (_profileCache.Count > MaxCacheSize)
                    {
                        _profileCache.Clear(); // 簡単なクリーンアップ
                        _lastCacheRefresh = DateTime.MinValue;
                    }
                    
                    // 古いキャッシュエントリのクリア
                    if ((DateTime.Now - _lastCacheRefresh).TotalMinutes > CacheValidityMinutes * 2)
                    {
                        _profileCache.Clear();
                        _lastCacheRefresh = DateTime.MinValue;
                    }
                }
                catch { }
            }
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _cacheCleanupTimer?.Dispose();
            
            lock (_lockObject)
            {
                _profileCache.Clear();
            }
            
            GC.SuppressFinalize(this);
        }
    }

    public class WifiProfileInfo
    {
        public string ProfileName { get; set; } = string.Empty;
        public string SSID { get; set; } = string.Empty;
        public string Authentication { get; set; } = string.Empty;
        public bool AutoConnect { get; set; }
    }
}