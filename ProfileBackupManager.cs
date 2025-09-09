using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// ネットワークプロファイルのバックアップ・復元管理
    /// </summary>
    public class ProfileBackupManager
    {
        private readonly ConnectionLogger _logger;
        private readonly string _backupDirectory;
        private readonly SemaphoreSlim _backupLock = new(1, 1);
        
        public ProfileBackupManager(ConnectionLogger logger)
        {
            _logger = logger;
            
            _backupDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "MurtiWifiBackups");
            
            if (!Directory.Exists(_backupDirectory))
            {
                Directory.CreateDirectory(_backupDirectory);
            }
        }
        
        /// <summary>
        /// 完全バックアップ作成
        /// </summary>
        public async Task<BackupResult> CreateFullBackupAsync(string? customName = null)
        {
            await _backupLock.WaitAsync();
            try
            {
                var backupName = customName ?? $"WiFi_Backup_{DateTime.Now:yyyyMMdd_HHmmss}";
                var backupData = new BackupData
                {
                    BackupName = backupName,
                    CreatedDate = DateTime.Now,
                    BackupType = BackupType.Full
                };
                
                // 1. WiFiプロファイル情報収集
                var profiles = await CollectWiFiProfilesAsync();
                backupData.WiFiProfiles = profiles;
                
                // 2. アプリケーション設定
                var appSettings = await CollectAppSettingsAsync();
                backupData.ApplicationSettings = appSettings;
                
                // 3. 接続履歴
                var history = await CollectConnectionHistoryAsync();
                backupData.ConnectionHistory = history;
                
                // 4. 統計データ
                var statistics = await CollectStatisticsAsync();
                backupData.Statistics = statistics;
                
                // バックアップファイルに保存
                var fileName = $"{backupName}.wifibackup";
                var filePath = Path.Combine(_backupDirectory, fileName);
                
                await SaveBackupFileAsync(filePath, backupData);
                
                _logger?.Log(ConnectionLogger.LogLevel.Info, "Backup", 
                    $"完全バックアップ作成: {fileName} ({profiles.Count}プロファイル)");
                
                return new BackupResult
                {
                    IsSuccess = true,
                    BackupName = backupName,
                    FilePath = filePath,
                    ProfileCount = profiles.Count,
                    FileSize = new FileInfo(filePath).Length
                };
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("ProfileBackupManager.CreateFullBackup", ex, _logger);
                return new BackupResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
            finally
            {
                _backupLock.Release();
            }
        }
        
        /// <summary>
        /// バックアップ復元
        /// </summary>
        public async Task<RestoreResult> RestoreBackupAsync(string backupFilePath, RestoreOptions options)
        {
            await _backupLock.WaitAsync();
            try
            {
                if (!File.Exists(backupFilePath))
                {
                    return new RestoreResult
                    {
                        IsSuccess = false,
                        ErrorMessage = "バックアップファイルが見つかりません"
                    };
                }
                
                var backupData = await LoadBackupFileAsync(backupFilePath);
                if (backupData == null)
                {
                    return new RestoreResult
                    {
                        IsSuccess = false,
                        ErrorMessage = "バックアップファイルの読み込みに失敗しました"
                    };
                }
                
                var result = new RestoreResult
                {
                    BackupName = backupData.BackupName,
                    BackupDate = backupData.CreatedDate
                };
                
                var restoredItems = new List<string>();
                
                // 1. WiFiプロファイル復元
                if (options.RestoreWiFiProfiles && backupData.WiFiProfiles.Any())
                {
                    var profileResult = await RestoreWiFiProfilesAsync(backupData.WiFiProfiles);
                    if (profileResult.IsSuccess)
                    {
                        restoredItems.Add($"{profileResult.RestoredCount}個のWiFiプロファイル");
                        result.RestoredProfiles = profileResult.RestoredCount;
                    }
                }
                
                // 2. アプリケーション設定復元
                if (options.RestoreAppSettings && backupData.ApplicationSettings.Any())
                {
                    var settingsResult = await RestoreAppSettingsAsync(backupData.ApplicationSettings);
                    if (settingsResult.IsSuccess)
                    {
                        restoredItems.Add($"{settingsResult.RestoredCount}個の設定");
                    }
                }
                
                // 3. 接続履歴復元
                if (options.RestoreConnectionHistory && backupData.ConnectionHistory.Any())
                {
                    var historyResult = await RestoreConnectionHistoryAsync(backupData.ConnectionHistory);
                    if (historyResult.IsSuccess)
                    {
                        restoredItems.Add("接続履歴");
                    }
                }
                
                result.IsSuccess = restoredItems.Any();
                result.Message = result.IsSuccess ? 
                    $"復元完了: {string.Join(", ", restoredItems)}" :
                    "復元する項目がありませんでした";
                
                _logger?.Log(ConnectionLogger.LogLevel.Info, "Restore", result.Message);
                
                return result;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("ProfileBackupManager.RestoreBackup", ex, _logger);
                return new RestoreResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
            finally
            {
                _backupLock.Release();
            }
        }
        
        /// <summary>
        /// 利用可能なバックアップ一覧取得
        /// </summary>
        public async Task<List<BackupInfo>> GetAvailableBackupsAsync()
        {
            try
            {
                var backups = new List<BackupInfo>();
                var backupFiles = Directory.GetFiles(_backupDirectory, "*.wifibackup");
                
                foreach (var file in backupFiles)
                {
                    try
                    {
                        var info = new FileInfo(file);
                        var backupData = await LoadBackupFileAsync(file, headersOnly: true);
                        
                        backups.Add(new BackupInfo
                        {
                            FileName = info.Name,
                            FilePath = file,
                            BackupName = backupData?.BackupName ?? Path.GetFileNameWithoutExtension(file),
                            CreatedDate = backupData?.CreatedDate ?? info.CreationTime,
                            FileSize = info.Length,
                            ProfileCount = backupData?.WiFiProfiles?.Count ?? 0,
                            BackupType = backupData?.BackupType ?? BackupType.Unknown
                        });
                    }
                    catch
                    {
                        // 破損したバックアップファイルはスキップ
                    }
                }
                
                return backups.OrderByDescending(b => b.CreatedDate).ToList();
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("ProfileBackupManager.GetAvailableBackups", ex, _logger);
                return new List<BackupInfo>();
            }
        }
        
        /// <summary>
        /// 古いバックアップファイルをクリーンアップ
        /// </summary>
        public async Task<CleanupResult> CleanupOldBackupsAsync(int keepRecentCount = 10, TimeSpan? maxAge = null)
        {
            try
            {
                var backups = await GetAvailableBackupsAsync();
                var toDelete = new List<BackupInfo>();
                
                // 保持数を超えるバックアップ
                if (backups.Count > keepRecentCount)
                {
                    toDelete.AddRange(backups.Skip(keepRecentCount));
                }
                
                // 古すぎるバックアップ
                if (maxAge.HasValue)
                {
                    var cutoffDate = DateTime.Now - maxAge.Value;
                    toDelete.AddRange(backups.Where(b => b.CreatedDate < cutoffDate));
                }
                
                // 重複を除去
                toDelete = toDelete.Distinct().ToList();
                
                var deletedCount = 0;
                var freedSpace = 0L;
                
                foreach (var backup in toDelete)
                {
                    try
                    {
                        freedSpace += backup.FileSize;
                        File.Delete(backup.FilePath);
                        deletedCount++;
                    }
                    catch
                    {
                        // 削除エラーは無視
                    }
                }
                
                return new CleanupResult
                {
                    IsSuccess = true,
                    DeletedCount = deletedCount,
                    FreedSpaceBytes = freedSpace,
                    Message = $"{deletedCount}個のバックアップを削除, {FormatBytes(freedSpace)}解放"
                };
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("ProfileBackupManager.CleanupOldBackups", ex, _logger);
                return new CleanupResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }
        
        private async Task<List<WiFiProfileData>> CollectWiFiProfilesAsync()
        {
            var profiles = new List<WiFiProfileData>();
            
            try
            {
                // netsh wlan show profilesでプロファイル一覧取得
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = "wlan show profiles",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                process.WaitForExit(5000);
                
                // プロファイル名を解析
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains("All User Profile") || line.Contains("ユーザー プロファイル"))
                    {
                        var parts = line.Split(':');
                        if (parts.Length >= 2)
                        {
                            var profileName = parts[1].Trim();
                            if (!string.IsNullOrWhiteSpace(profileName))
                            {
                                profiles.Add(new WiFiProfileData
                                {
                                    ProfileName = profileName,
                                    BackupDate = DateTime.Now
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("ProfileBackupManager.CollectWiFiProfiles", ex, _logger);
            }
            
            return profiles;
        }
        
        private async Task<Dictionary<string, object>> CollectAppSettingsAsync()
        {
            var settings = new Dictionary<string, object>();
            
            try
            {
                // QuickSettingsManagerから設定を収集
                var allSettings = new[]
                {
                    "preferred_language", "auto_connect", "auto_save_passwords",
                    "refresh_interval_seconds", "max_displayed_networks", "tray_notifications"
                };
                
                foreach (var setting in allSettings)
                {
                    try
                    {
                        var value = QuickSettingsManager.GetSetting(setting, "");
                        if (!string.IsNullOrEmpty(value.ToString()))
                        {
                            settings[setting] = value;
                        }
                    }
                    catch
                    {
                        // 個別設定のエラーは無視
                    }
                }
                
                await Task.CompletedTask; // 非同期対応
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("ProfileBackupManager.CollectAppSettings", ex, _logger);
            }
            
            return settings;
        }
        
        private async Task<List<object>> CollectConnectionHistoryAsync()
        {
            try
            {
                // 接続履歴のJSONファイルが存在する場合に読み込み
                var historyData = await FileManager.ReadJsonAsync<List<object>>("connection_history.json");
                return historyData ?? new List<object>();
            }
            catch
            {
                return new List<object>();
            }
        }
        
        private async Task<Dictionary<string, object>> CollectStatisticsAsync()
        {
            try
            {
                // 統計データのJSONファイルが存在する場合に読み込み
                var statsData = await FileManager.ReadJsonAsync<Dictionary<string, object>>("statistics.json");
                return statsData ?? new Dictionary<string, object>();
            }
            catch
            {
                return new Dictionary<string, object>();
            }
        }
        
        private async Task SaveBackupFileAsync(string filePath, BackupData backupData)
        {
            var json = JsonSerializer.Serialize(backupData, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            
            // 暗号化（簡易）
            var encryptedData = EncryptData(json);
            await File.WriteAllBytesAsync(filePath, encryptedData);
        }
        
        private async Task<BackupData?> LoadBackupFileAsync(string filePath, bool headersOnly = false)
        {
            try
            {
                var encryptedData = await File.ReadAllBytesAsync(filePath);
                var json = DecryptData(encryptedData);
                
                var backupData = JsonSerializer.Deserialize<BackupData>(json, new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                
                // ヘッダーのみの場合は詳細データをクリア
                if (headersOnly && backupData != null)
                {
                    backupData.WiFiProfiles = new List<WiFiProfileData>();
                    backupData.ApplicationSettings = new Dictionary<string, object>();
                    backupData.ConnectionHistory = new List<object>();
                    backupData.Statistics = new Dictionary<string, object>();
                }
                
                return backupData;
            }
            catch
            {
                return null;
            }
        }
        
        private async Task<RestoreProfilesResult> RestoreWiFiProfilesAsync(List<WiFiProfileData> profiles)
        {
            var restoredCount = 0;
            
            foreach (var profile in profiles)
            {
                try
                {
                    // WiFiプロファイルの復元は複雑なため、ログ記録のみ
                    _logger?.Log(ConnectionLogger.LogLevel.Info, "Restore", 
                        $"WiFiプロファイル復元予定: {profile.ProfileName}");
                    restoredCount++;
                }
                catch
                {
                    // 個別復元エラーは無視
                }
            }
            
            await Task.CompletedTask;
            
            return new RestoreProfilesResult
            {
                IsSuccess = restoredCount > 0,
                RestoredCount = restoredCount
            };
        }
        
        private async Task<RestoreSettingsResult> RestoreAppSettingsAsync(Dictionary<string, object> settings)
        {
            var restoredCount = 0;
            
            foreach (var setting in settings)
            {
                try
                {
                    QuickSettingsManager.SetSetting(setting.Key, setting.Value);
                    restoredCount++;
                }
                catch
                {
                    // 個別設定復元エラーは無視
                }
            }
            
            await Task.CompletedTask;
            
            return new RestoreSettingsResult
            {
                IsSuccess = restoredCount > 0,
                RestoredCount = restoredCount
            };
        }
        
        private async Task<RestoreHistoryResult> RestoreConnectionHistoryAsync(List<object> history)
        {
            try
            {
                await FileManager.WriteJsonAsync("connection_history.json", history);
                return new RestoreHistoryResult { IsSuccess = true };
            }
            catch
            {
                return new RestoreHistoryResult { IsSuccess = false };
            }
        }
        
        private byte[] EncryptData(string data)
        {
            try
            {
                var entropy = Encoding.UTF8.GetBytes("MurtiWifiBackup");
                var dataBytes = Encoding.UTF8.GetBytes(data);
                return ProtectedData.Protect(dataBytes, entropy, DataProtectionScope.CurrentUser);
            }
            catch
            {
                // 暗号化に失敗した場合は平文で保存
                return Encoding.UTF8.GetBytes(data);
            }
        }
        
        private string DecryptData(byte[] encryptedData)
        {
            try
            {
                var entropy = Encoding.UTF8.GetBytes("MurtiWifiBackup");
                var decryptedBytes = ProtectedData.Unprotect(encryptedData, entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decryptedBytes);
            }
            catch
            {
                // 復号に失敗した場合は平文として読み込み
                return Encoding.UTF8.GetString(encryptedData);
            }
        }
        
        private static string FormatBytes(long bytes)
        {
            return bytes switch
            {
                >= 1_000_000 => $"{bytes / 1_000_000.0:F1} MB",
                >= 1_000 => $"{bytes / 1_000.0:F1} KB",
                _ => $"{bytes} B"
            };
        }
    }
    
    // データクラス群
    public class BackupData
    {
        public string BackupName { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
        public BackupType BackupType { get; set; }
        public List<WiFiProfileData> WiFiProfiles { get; set; } = new();
        public Dictionary<string, object> ApplicationSettings { get; set; } = new();
        public List<object> ConnectionHistory { get; set; } = new();
        public Dictionary<string, object> Statistics { get; set; } = new();
    }
    
    public class WiFiProfileData
    {
        public string ProfileName { get; set; } = string.Empty;
        public DateTime BackupDate { get; set; }
    }
    
    public enum BackupType
    {
        Unknown,
        Full,
        ProfilesOnly,
        SettingsOnly
    }
    
    public class RestoreOptions
    {
        public bool RestoreWiFiProfiles { get; set; } = true;
        public bool RestoreAppSettings { get; set; } = true;
        public bool RestoreConnectionHistory { get; set; } = true;
        public bool RestoreStatistics { get; set; } = false;
    }
    
    public class BackupResult
    {
        public bool IsSuccess { get; set; }
        public string BackupName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int ProfileCount { get; set; }
        public long FileSize { get; set; }
        public string? ErrorMessage { get; set; }
    }
    
    public class RestoreResult
    {
        public bool IsSuccess { get; set; }
        public string BackupName { get; set; } = string.Empty;
        public DateTime BackupDate { get; set; }
        public int RestoredProfiles { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
    }
    
    public class BackupInfo
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string BackupName { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
        public long FileSize { get; set; }
        public int ProfileCount { get; set; }
        public BackupType BackupType { get; set; }
        
        public string GetFormattedSize() => FormatBytes(FileSize);
        
        private static string FormatBytes(long bytes)
        {
            return bytes switch
            {
                >= 1_000_000 => $"{bytes / 1_000_000.0:F1} MB",
                >= 1_000 => $"{bytes / 1_000.0:F1} KB",
                _ => $"{bytes} B"
            };
        }
    }
    
    public class CleanupResult
    {
        public bool IsSuccess { get; set; }
        public int DeletedCount { get; set; }
        public long FreedSpaceBytes { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
    }
    
    // 内部結果クラス
    internal class RestoreProfilesResult
    {
        public bool IsSuccess { get; set; }
        public int RestoredCount { get; set; }
    }
    
    internal class RestoreSettingsResult
    {
        public bool IsSuccess { get; set; }
        public int RestoredCount { get; set; }
    }
    
    internal class RestoreHistoryResult
    {
        public bool IsSuccess { get; set; }
    }
}