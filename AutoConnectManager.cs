using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// WiFi自動接続管理
    /// </summary>
    public class AutoConnectManager : IDisposable
    {
        private readonly string _savedProfilesPath;
        private readonly Dictionary<string, SavedProfile> _savedProfiles;
        private readonly ConnectionLogger _connectionLogger;
        private readonly SemaphoreSlim _saveLock = new(1, 1);
        private bool _disposed = false;

        public bool AutoConnectEnabled { get; set; } = true;
        public bool AutoSavePasswords { get; set; } = true;
        
        public AutoConnectManager(ConnectionLogger connectionLogger)
        {
            _connectionLogger = connectionLogger;
            
            // プロファイル保存先
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MurtiWifiConnecter");
            
            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }
            
            _savedProfilesPath = Path.Combine(appDataPath, "profiles.json");
            _savedProfiles = LoadProfiles();
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
                var encryptedPassword = EncryptPassword(password);
                _savedProfiles[ssid] = new SavedProfile
                {
                    SSID = ssid,
                    EncryptedPassword = encryptedPassword,
                    LastConnected = DateTime.Now,
                    ConnectionCount = _savedProfiles.ContainsKey(ssid) 
                        ? _savedProfiles[ssid].ConnectionCount + 1 : 1
                };

                await SaveProfilesToFileAsync();
            }
            finally
            {
                _saveLock.Release();
            }
        }

        /// <summary>
        /// 保存されたパスワードを取得
        /// </summary>
        public string? GetSavedPassword(string ssid)
        {
            if (_savedProfiles.TryGetValue(ssid, out var profile))
            {
                return DecryptPassword(profile.EncryptedPassword);
            }
            return null;
        }

        /// <summary>
        /// 自動接続を試行
        /// </summary>
        public async Task<bool> TryAutoConnectAsync(string ssid, CancellationToken cancellationToken = default)
        {
            if (!AutoConnectEnabled)
                return false;

            var password = GetSavedPassword(ssid);
            if (string.IsNullOrEmpty(password))
                return false;

            try
            {
                _connectionLogger?.Log(ConnectionLogger.LogLevel.Info, "AutoConnect", 
                    $"Attempting auto-connect to {ssid}");

                var result = await FastWifiConnector.ConnectAsync(ssid, password, cancellationToken);
                
                if (result.Success)
                {
                    _connectionLogger?.Log(ConnectionLogger.LogLevel.Info, "AutoConnect", 
                        $"Successfully auto-connected to {ssid}");
                    
                    // 接続成功時にプロファイルを更新
                    await UpdateProfileLastConnectedAsync(ssid);
                }
                
                return result.Success;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError($"AutoConnectManager.TryAutoConnect({ssid})", ex, _connectionLogger);
                return false;
            }
        }

        /// <summary>
        /// 最適なネットワークに自動接続
        /// </summary>
        public async Task<string?> AutoConnectToBestNetworkAsync(
            IEnumerable<WifiNetwork> availableNetworks,
            CancellationToken cancellationToken = default)
        {
            if (!AutoConnectEnabled)
                return null;

            // 保存済みプロファイルがあるネットワークを信号強度順にソート
            var candidateNetworks = availableNetworks
                .Where(n => _savedProfiles.ContainsKey(n.SSID) && !n.IsConnected)
                .OrderByDescending(n => n.SignalStrength)
                .ThenByDescending(n => _savedProfiles[n.SSID].ConnectionCount)
                .ToList();

            foreach (var network in candidateNetworks)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (await TryAutoConnectAsync(network.SSID, cancellationToken))
                {
                    return network.SSID;
                }

                // 失敗した場合は少し待つ
                await Task.Delay(1000, cancellationToken);
            }

            return null;
        }

        /// <summary>
        /// プロファイルを削除
        /// </summary>
        public async Task RemoveProfileAsync(string ssid)
        {
            await _saveLock.WaitAsync();
            try
            {
                if (_savedProfiles.Remove(ssid))
                {
                    await SaveProfilesToFileAsync();
                }
            }
            finally
            {
                _saveLock.Release();
            }
        }

        /// <summary>
        /// すべてのプロファイルをクリア
        /// </summary>
        public async Task ClearAllProfilesAsync()
        {
            await _saveLock.WaitAsync();
            try
            {
                _savedProfiles.Clear();
                await SaveProfilesToFileAsync();
            }
            finally
            {
                _saveLock.Release();
            }
        }

        private async Task UpdateProfileLastConnectedAsync(string ssid)
        {
            await _saveLock.WaitAsync();
            try
            {
                if (_savedProfiles.TryGetValue(ssid, out var profile))
                {
                    profile.LastConnected = DateTime.Now;
                    profile.ConnectionCount++;
                    await SaveProfilesToFileAsync();
                }
            }
            finally
            {
                _saveLock.Release();
            }
        }

        private Dictionary<string, SavedProfile> LoadProfiles()
        {
            try
            {
                if (File.Exists(_savedProfilesPath))
                {
                    var json = File.ReadAllText(_savedProfilesPath);
                    return JsonSerializer.Deserialize<Dictionary<string, SavedProfile>>(json) 
                        ?? new Dictionary<string, SavedProfile>();
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("AutoConnectManager.LoadProfiles", ex, _connectionLogger);
            }
            
            return new Dictionary<string, SavedProfile>();
        }

        private async Task SaveProfilesToFileAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(_savedProfiles, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                await File.WriteAllTextAsync(_savedProfilesPath, json);
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("AutoConnectManager.SaveProfiles", ex, _connectionLogger);
            }
        }

        private string EncryptPassword(string password)
        {
            try
            {
                var entropy = Encoding.UTF8.GetBytes("MurtiWifi");
                var data = Encoding.UTF8.GetBytes(password);
                var encrypted = ProtectedData.Protect(data, entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encrypted);
            }
            catch
            {
                // 暗号化に失敗した場合は空文字を返す
                return string.Empty;
            }
        }

        private string DecryptPassword(string encryptedPassword)
        {
            try
            {
                var entropy = Encoding.UTF8.GetBytes("MurtiWifi");
                var encrypted = Convert.FromBase64String(encryptedPassword);
                var decrypted = ProtectedData.Unprotect(encrypted, entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                // 復号に失敗した場合は空文字を返す
                return string.Empty;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _saveLock?.Dispose();
                _disposed = true;
            }
        }

        private class SavedProfile
        {
            public string SSID { get; set; } = string.Empty;
            public string EncryptedPassword { get; set; } = string.Empty;
            public DateTime LastConnected { get; set; }
            public int ConnectionCount { get; set; }
        }
    }
}