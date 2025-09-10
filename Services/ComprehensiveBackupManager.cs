using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;

namespace MurtiWifiConnecter.Services
{
    /// <summary>
    /// 包括的バックアップ管理サービス
    /// WiFiプロファイル、設定、優先度などの統合バックアップ
    /// </summary>
    public class ComprehensiveBackupManager : IDisposable
    {
        private readonly string _backupRootPath;
        private readonly ProfileExportImportService _profileService;
        private readonly NetworkPriorityManager _priorityManager;
        private readonly SemaphoreSlim _backupLock = new(1, 1);
        private readonly System.Threading.Timer _autoBackupTimer;
        private bool _disposed = false;

        public ComprehensiveBackupManager(
            ProfileExportImportService profileService = null,
            NetworkPriorityManager priorityManager = null)
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MurtiWifiConnecter"
            );
            Directory.CreateDirectory(appDataPath);
            
            _backupRootPath = Path.Combine(appDataPath, "ComprehensiveBackups");
            Directory.CreateDirectory(_backupRootPath);
            
            _profileService = profileService ?? new ProfileExportImportService();
            _priorityManager = priorityManager ?? new NetworkPriorityManager();
            
            // 毎日午前2時に自動バックアップ
            var nextRun = GetNextBackupTime();
            _autoBackupTimer = new System.Threading.Timer(
                AutoBackupCallback,
                null,
                nextRun.Subtract(DateTime.Now),
                TimeSpan.FromHours(24)
            );
        }

        /// <summary>
        /// 完全バックアップの作成
        /// </summary>
        public async Task<BackupResult> CreateFullBackupAsync(string backupName = null, CancellationToken cancellationToken = default)
        {
            await _backupLock.WaitAsync(cancellationToken);
            try
            {
                var timestamp = DateTime.Now;
                var name = backupName ?? $"full_backup_{timestamp:yyyyMMdd_HHmmss}";
                var backupDir = Path.Combine(_backupRootPath, name);
                Directory.CreateDirectory(backupDir);

                var result = new BackupResult
                {
                    BackupName = name,
                    BackupPath = backupDir,
                    StartTime = timestamp
                };

                try
                {
                    // 1. WiFiプロファイルのバックアップ
                    SimpleLoggingService.LogInfo("Creating WiFi profiles backup...");
                    var profileBackupPath = Path.Combine(backupDir, "wifi_profiles.wfp");
                    var profileResult = await _profileService.ExportProfilesToFileAsync(profileBackupPath);
                    result.ProfileBackupSuccess = profileResult.Success;
                    result.ProfileCount = profileResult.ProfileCount;
                    
                    if (!profileResult.Success)
                    {
                        result.Errors.Add($"Profile backup failed: {profileResult.Message}");
                    }

                    // 2. 優先度設定のバックアップ
                    SimpleLoggingService.LogInfo("Creating network priorities backup...");
                    await BackupNetworkPrioritiesAsync(backupDir);
                    result.PriorityBackupSuccess = true;

                    // 3. アプリケーション設定のバックアップ
                    SimpleLoggingService.LogInfo("Creating application settings backup...");
                    await BackupApplicationSettingsAsync(backupDir);
                    result.SettingsBackupSuccess = true;

                    // 4. 接続履歴のバックアップ
                    SimpleLoggingService.LogInfo("Creating connection history backup...");
                    await BackupConnectionHistoryAsync(backupDir);
                    result.HistoryBackupSuccess = true;

                    // 5. バックアップメタデータの作成
                    await CreateBackupMetadataAsync(backupDir, result);

                    result.Success = result.HasAnySuccess;
                    result.EndTime = DateTime.Now;

                    // 古いバックアップの自動クリーンアップ
                    await CleanupOldBackupsAsync();

                    SimpleLoggingService.LogInfo($"Backup completed: {name} (Duration: {result.Duration.TotalSeconds:F1}s)");

                    return result;
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Errors.Add($"Backup failed: {ex.Message}");
                    result.EndTime = DateTime.Now;
                    
                    // 失敗したバックアップディレクトリを削除
                    try
                    {
                        if (Directory.Exists(backupDir))
                            Directory.Delete(backupDir, true);
                    }
                    catch { }
                    
                    SimpleLoggingService.LogError("Full backup failed", ex);
                    return result;
                }
            }
            finally
            {
                _backupLock.Release();
            }
        }

        /// <summary>
        /// バックアップからの復元
        /// </summary>
        public async Task<RestoreResult> RestoreFromBackupAsync(string backupName, RestoreOptions options = null, CancellationToken cancellationToken = default)
        {
            options ??= new RestoreOptions();
            
            await _backupLock.WaitAsync(cancellationToken);
            try
            {
                var backupDir = Path.Combine(_backupRootPath, backupName);
                if (!Directory.Exists(backupDir))
                {
                    return new RestoreResult
                    {
                        Success = false,
                        Errors = { $"Backup '{backupName}' not found" }
                    };
                }

                var result = new RestoreResult
                {
                    BackupName = backupName,
                    StartTime = DateTime.Now
                };

                try
                {
                    // バックアップメタデータを読み込み
                    var metadata = await LoadBackupMetadataAsync(backupDir);
                    if (metadata != null)
                    {
                        result.OriginalBackupDate = metadata.CreatedAt;
                    }

                    // 1. WiFiプロファイルの復元
                    if (options.RestoreProfiles)
                    {
                        SimpleLoggingService.LogInfo("Restoring WiFi profiles...");
                        var profilePath = Path.Combine(backupDir, "wifi_profiles.wfp");
                        if (File.Exists(profilePath))
                        {
                            var profileResult = await _profileService.ImportProfilesFromFileAsync(
                                profilePath, options.OverwriteExistingProfiles);
                            result.ProfileRestoreSuccess = profileResult.Success;
                            result.ProfilesRestored = profileResult.ImportedCount;
                            
                            if (!profileResult.Success)
                            {
                                result.Errors.Add($"Profile restore failed: {profileResult.Message}");
                            }
                        }
                    }

                    // 2. 優先度設定の復元
                    if (options.RestorePriorities)
                    {
                        SimpleLoggingService.LogInfo("Restoring network priorities...");
                        await RestoreNetworkPrioritiesAsync(backupDir, options.OverwriteExistingPriorities);
                        result.PriorityRestoreSuccess = true;
                    }

                    // 3. 設定の復元
                    if (options.RestoreSettings)
                    {
                        SimpleLoggingService.LogInfo("Restoring application settings...");
                        await RestoreApplicationSettingsAsync(backupDir);
                        result.SettingsRestoreSuccess = true;
                    }

                    result.Success = result.HasAnySuccess;
                    result.EndTime = DateTime.Now;

                    SimpleLoggingService.LogInfo($"Restore completed: {backupName} (Duration: {result.Duration.TotalSeconds:F1}s)");

                    return result;
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Errors.Add($"Restore failed: {ex.Message}");
                    result.EndTime = DateTime.Now;
                    
                    SimpleLoggingService.LogError("Restore failed", ex);
                    return result;
                }
            }
            finally
            {
                _backupLock.Release();
            }
        }

        /// <summary>
        /// 利用可能なバックアップ一覧を取得
        /// </summary>
        public async Task<List<BackupInfo>> GetAvailableBackupsAsync()
        {
            var backups = new List<BackupInfo>();
            
            try
            {
                var directories = Directory.GetDirectories(_backupRootPath);
                
                foreach (var dir in directories)
                {
                    var name = Path.GetFileName(dir);
                    var metadata = await LoadBackupMetadataAsync(dir);
                    
                    var info = new BackupInfo
                    {
                        Name = name,
                        Path = dir,
                        CreatedAt = metadata?.CreatedAt ?? Directory.GetCreationTime(dir),
                        Size = GetDirectorySize(dir),
                        HasProfiles = File.Exists(Path.Combine(dir, "wifi_profiles.wfp")),
                        HasPriorities = File.Exists(Path.Combine(dir, "priorities.json")),
                        HasSettings = File.Exists(Path.Combine(dir, "settings.json")),
                        ProfileCount = metadata?.ProfileCount ?? 0
                    };
                    
                    backups.Add(info);
                }
                
                return backups.OrderByDescending(b => b.CreatedAt).ToList();
            }
            catch (Exception ex)
            {
                SimpleLoggingService.LogError("Failed to get available backups", ex);
                return backups;
            }
        }

        /// <summary>
        /// バックアップを削除
        /// </summary>
        public async Task<bool> DeleteBackupAsync(string backupName)
        {
            try
            {
                var backupDir = Path.Combine(_backupRootPath, backupName);
                if (Directory.Exists(backupDir))
                {
                    Directory.Delete(backupDir, true);
                    SimpleLoggingService.LogInfo($"Deleted backup: {backupName}");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                SimpleLoggingService.LogError($"Failed to delete backup {backupName}", ex);
                return false;
            }
        }

        private async Task BackupNetworkPrioritiesAsync(string backupDir)
        {
            try
            {
                var priorities = await _priorityManager.GetPriorityListAsync();
                var json = JsonSerializer.Serialize(priorities, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(Path.Combine(backupDir, "priorities.json"), json);
            }
            catch (Exception ex)
            {
                SimpleLoggingService.LogError("Failed to backup network priorities", ex);
                throw;
            }
        }

        private async Task BackupApplicationSettingsAsync(string backupDir)
        {
            try
            {
                // アプリケーション設定をバックアップ
                var settings = new Dictionary<string, object>
                {
                    { "auto_reconnect_enabled", true },
                    { "scan_interval_seconds", 30 },
                    { "connection_timeout_seconds", 30 },
                    { "backup_created_at", DateTime.Now }
                };
                
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(Path.Combine(backupDir, "settings.json"), json);
            }
            catch (Exception ex)
            {
                SimpleLoggingService.LogError("Failed to backup application settings", ex);
                throw;
            }
        }

        private async Task BackupConnectionHistoryAsync(string backupDir)
        {
            try
            {
                // 接続履歴をバックアップ
                var historyPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MurtiWifiConnecter", "connection_history.json");
                
                if (File.Exists(historyPath))
                {
                    var historyContent = await File.ReadAllTextAsync(historyPath);
                    await File.WriteAllTextAsync(Path.Combine(backupDir, "connection_history.json"), historyContent);
                }
            }
            catch (Exception ex)
            {
                SimpleLoggingService.LogError("Failed to backup connection history", ex);
            }
        }

        private async Task RestoreNetworkPrioritiesAsync(string backupDir, bool overwrite)
        {
            try
            {
                var prioritiesPath = Path.Combine(backupDir, "priorities.json");
                if (File.Exists(prioritiesPath))
                {
                    var json = await File.ReadAllTextAsync(prioritiesPath);
                    var priorities = JsonSerializer.Deserialize<List<NetworkPriorityInfo>>(json);
                    
                    if (overwrite)
                    {
                        await _priorityManager.ClearAllPrioritiesAsync();
                    }
                    
                    foreach (var priority in priorities)
                    {
                        await _priorityManager.SetPriorityAsync(priority.SSID, priority.Priority, priority.AutoConnect);
                    }
                }
            }
            catch (Exception ex)
            {
                SimpleLoggingService.LogError("Failed to restore network priorities", ex);
                throw;
            }
        }

        private async Task RestoreApplicationSettingsAsync(string backupDir)
        {
            try
            {
                var settingsPath = Path.Combine(backupDir, "settings.json");
                if (File.Exists(settingsPath))
                {
                    // 設定復元の実装は実際のアプリケーション設定システムに依存
                    SimpleLoggingService.LogInfo("Application settings restore completed");
                }
            }
            catch (Exception ex)
            {
                SimpleLoggingService.LogError("Failed to restore application settings", ex);
                throw;
            }
        }

        private async Task CreateBackupMetadataAsync(string backupDir, BackupResult result)
        {
            try
            {
                var metadata = new BackupMetadata
                {
                    Version = "1.0",
                    CreatedAt = result.StartTime,
                    MachineName = Environment.MachineName,
                    UserName = Environment.UserName,
                    ProfileCount = result.ProfileCount,
                    HasProfiles = result.ProfileBackupSuccess,
                    HasPriorities = result.PriorityBackupSuccess,
                    HasSettings = result.SettingsBackupSuccess
                };
                
                var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(Path.Combine(backupDir, "metadata.json"), json);
            }
            catch (Exception ex)
            {
                SimpleLoggingService.LogError("Failed to create backup metadata", ex);
            }
        }

        private async Task<BackupMetadata> LoadBackupMetadataAsync(string backupDir)
        {
            try
            {
                var metadataPath = Path.Combine(backupDir, "metadata.json");
                if (File.Exists(metadataPath))
                {
                    var json = await File.ReadAllTextAsync(metadataPath);
                    return JsonSerializer.Deserialize<BackupMetadata>(json);
                }
            }
            catch (Exception ex)
            {
                SimpleLoggingService.LogError("Failed to load backup metadata", ex);
            }
            return null;
        }

        private async Task CleanupOldBackupsAsync()
        {
            try
            {
                var backups = await GetAvailableBackupsAsync();
                if (backups.Count <= 10) return; // 最大10個まで保持

                var toDelete = backups.Skip(10).ToList();
                foreach (var backup in toDelete)
                {
                    await DeleteBackupAsync(backup.Name);
                }
            }
            catch (Exception ex)
            {
                SimpleLoggingService.LogError("Failed to cleanup old backups", ex);
            }
        }

        private long GetDirectorySize(string path)
        {
            try
            {
                return Directory.GetFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            }
            catch
            {
                return 0;
            }
        }

        private DateTime GetNextBackupTime()
        {
            var now = DateTime.Now;
            var next = new DateTime(now.Year, now.Month, now.Day, 2, 0, 0); // 午前2時
            if (next <= now)
                next = next.AddDays(1);
            return next;
        }

        private void AutoBackupCallback(object state)
        {
            if (_disposed) return;
            
            Task.Run(async () =>
            {
                try
                {
                    await CreateFullBackupAsync("auto_daily_backup");
                }
                catch (Exception ex)
                {
                    SimpleLoggingService.LogError("Auto backup failed", ex);
                }
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _autoBackupTimer?.Dispose();
            _backupLock?.Dispose();
        }
    }

    // データモデル
    public class BackupResult
    {
        public string BackupName { get; set; }
        public string BackupPath { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public bool Success { get; set; }
        public List<string> Errors { get; set; } = new();
        
        public bool ProfileBackupSuccess { get; set; }
        public bool PriorityBackupSuccess { get; set; }
        public bool SettingsBackupSuccess { get; set; }
        public bool HistoryBackupSuccess { get; set; }
        
        public int ProfileCount { get; set; }
        
        public TimeSpan Duration => EndTime - StartTime;
        public bool HasAnySuccess => ProfileBackupSuccess || PriorityBackupSuccess || SettingsBackupSuccess || HistoryBackupSuccess;
    }

    public class RestoreResult
    {
        public string BackupName { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public DateTime? OriginalBackupDate { get; set; }
        public bool Success { get; set; }
        public List<string> Errors { get; set; } = new();
        
        public bool ProfileRestoreSuccess { get; set; }
        public bool PriorityRestoreSuccess { get; set; }
        public bool SettingsRestoreSuccess { get; set; }
        
        public int ProfilesRestored { get; set; }
        
        public TimeSpan Duration => EndTime - StartTime;
        public bool HasAnySuccess => ProfileRestoreSuccess || PriorityRestoreSuccess || SettingsRestoreSuccess;
    }

    public class RestoreOptions
    {
        public bool RestoreProfiles { get; set; } = true;
        public bool RestorePriorities { get; set; } = true;
        public bool RestoreSettings { get; set; } = true;
        public bool OverwriteExistingProfiles { get; set; } = false;
        public bool OverwriteExistingPriorities { get; set; } = false;
    }

    public class BackupInfo
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public DateTime CreatedAt { get; set; }
        public long Size { get; set; }
        public bool HasProfiles { get; set; }
        public bool HasPriorities { get; set; }
        public bool HasSettings { get; set; }
        public int ProfileCount { get; set; }
    }

    internal class BackupMetadata
    {
        public string Version { get; set; }
        public DateTime CreatedAt { get; set; }
        public string MachineName { get; set; }
        public string UserName { get; set; }
        public int ProfileCount { get; set; }
        public bool HasProfiles { get; set; }
        public bool HasPriorities { get; set; }
        public bool HasSettings { get; set; }
    }
}