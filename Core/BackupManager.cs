using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MurtiWifiConnecter.Core;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// 自動バックアップと復元機能を提供するクラス
    /// 設定、ログ、VPNプロファイルなどの自動バックアップを管理
    /// </summary>
    public static class BackupManager
    {
        private static readonly string BackupDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MurtiWifiConnecter", "backups");

        private static readonly string TempDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MurtiWifiConnecter", "temp");

        private static System.Timers.Timer? _autoBackupTimer;
        private const int MaxBackups = 30;
        private const int AutoBackupIntervalHours = 24;

        static BackupManager()
        {
            Directory.CreateDirectory(BackupDirectory);
            Directory.CreateDirectory(TempDirectory);
        }

        /// <summary>
        /// 完全バックアップを作成
        /// </summary>
        public static async Task<BackupResult> CreateFullBackupAsync(string? name = null, CancellationToken ct = default)
        {
            var result = new BackupResult
            {
                BackupType = BackupType.Full,
                Timestamp = DateTime.Now
            };

            try
            {
                var backupName = name ?? $"backup_{DateTime.Now:yyyyMMddHHmmss}";
                var backupPath = Path.Combine(BackupDirectory, $"{backupName}.zip");

                await Logger.LogInfo($"完全バックアップを開始: {backupName}",
                    nameof(BackupManager), new Dictionary<string, object>
                    {
                        ["backupName"] = backupName,
                        ["backupPath"] = backupPath
                    });

                // バックアップ対象の収集
                var itemsToBackup = await CollectBackupItemsAsync(ct);

                // ZIPファイルの作成
                using (var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create))
                {
                    foreach (var item in itemsToBackup)
                    {
                        if (File.Exists(item.SourcePath))
                        {
                            archive.CreateEntryFromFile(item.SourcePath, item.RelativePath);
                        }
                        else if (Directory.Exists(item.SourcePath))
                        {
                            AddDirectoryToArchive(archive, item.SourcePath, item.RelativePath);
                        }
                    }
                }

                // 整合性チェック
                var integrityHash = await ComputeBackupIntegrityAsync(backupPath);
                var metadata = new BackupMetadata
                {
                    Name = backupName,
                    Type = BackupType.Full,
                    CreatedAt = DateTime.Now,
                    SizeBytes = new FileInfo(backupPath).Length,
                    IntegrityHash = integrityHash,
                    Items = itemsToBackup.Select(i => i.RelativePath).ToList(),
                    Version = GetCurrentVersion()
                };

                // メタデータファイルの作成
                var metadataPath = Path.Combine(BackupDirectory, $"{backupName}.metadata.json");
                var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(metadataPath, metadataJson);

                // セキュリティ適用
                await SecurityManager.EnsureSecureFileAclAsync(backupPath);
                await SecurityManager.EnsureSecureFileAclAsync(metadataPath);

                result.Success = true;
                result.BackupPath = backupPath;
                result.MetadataPath = metadataPath;
                result.SizeBytes = metadata.SizeBytes;

                // 古いバックアップのクリーンアップ
                await CleanupOldBackupsAsync();

                await Logger.LogInfo($"完全バックアップ完了: {backupName}, サイズ: {result.SizeBytes} bytes",
                    nameof(BackupManager), new Dictionary<string, object>
                    {
                        ["backupName"] = backupName,
                        ["sizeBytes"] = result.SizeBytes,
                        ["itemCount"] = itemsToBackup.Count
                    });

            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                await Logger.LogError($"バックアップ作成失敗: {ex.Message}",
                    nameof(BackupManager), null, ex);
            }

            return result;
        }

        /// <summary>
        /// 設定のみのバックアップを作成
        /// </summary>
        public static async Task<BackupResult> CreateConfigBackupAsync(string? name = null, CancellationToken ct = default)
        {
            var result = new BackupResult
            {
                BackupType = BackupType.ConfigOnly,
                Timestamp = DateTime.Now
            };

            try
            {
                var backupName = name ?? $"config_backup_{DateTime.Now:yyyyMMddHHmmss}";
                var backupPath = Path.Combine(BackupDirectory, $"{backupName}.zip");

                await Logger.LogInfo($"設定バックアップを開始: {backupName}",
                    nameof(BackupManager), new Dictionary<string, object> { ["backupName"] = backupName });

                // 設定ファイルのみ収集
                var configFiles = await CollectConfigFilesAsync();

                using (var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create))
                {
                    foreach (var file in configFiles)
                    {
                        if (File.Exists(file.SourcePath))
                        {
                            archive.CreateEntryFromFile(file.SourcePath, file.RelativePath);
                        }
                    }
                }

                var integrityHash = await ComputeBackupIntegrityAsync(backupPath);
                var metadata = new BackupMetadata
                {
                    Name = backupName,
                    Type = BackupType.ConfigOnly,
                    CreatedAt = DateTime.Now,
                    SizeBytes = new FileInfo(backupPath).Length,
                    IntegrityHash = integrityHash,
                    Items = configFiles.Select(i => i.RelativePath).ToList(),
                    Version = GetCurrentVersion()
                };

                var metadataPath = Path.Combine(BackupDirectory, $"{backupName}.metadata.json");
                var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(metadataPath, metadataJson);

                await SecurityManager.EnsureSecureFileAclAsync(backupPath);
                await SecurityManager.EnsureSecureFileAclAsync(metadataPath);

                result.Success = true;
                result.BackupPath = backupPath;
                result.MetadataPath = metadataPath;
                result.SizeBytes = metadata.SizeBytes;

                await Logger.LogInfo($"設定バックアップ完了: {backupName}",
                    nameof(BackupManager), new Dictionary<string, object>
                    {
                        ["backupName"] = backupName,
                        ["configFiles"] = configFiles.Count
                    });

            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                await Logger.LogError($"設定バックアップ失敗: {ex.Message}",
                    nameof(BackupManager), null, ex);
            }

            return result;
        }

        /// <summary>
        /// バックアップから復元
        /// </summary>
        public static async Task<RestoreResult> RestoreFromBackupAsync(string backupPath, RestoreOptions options, CancellationToken ct = default)
        {
            var result = new RestoreResult
            {
                BackupPath = backupPath,
                Timestamp = DateTime.Now
            };

            try
            {
                if (!File.Exists(backupPath))
                {
                    result.Success = false;
                    result.ErrorMessage = "バックアップファイルが見つかりません";
                    return result;
                }

                // メタデータの読み込み
                var metadataPath = Path.ChangeExtension(backupPath, ".metadata.json");
                BackupMetadata? metadata = null;

                if (File.Exists(metadataPath))
                {
                    var metadataJson = await File.ReadAllTextAsync(metadataPath);
                    metadata = JsonSerializer.Deserialize<BackupMetadata>(metadataJson);
                }

                // 整合性チェック
                if (metadata != null)
                {
                    var currentHash = await ComputeBackupIntegrityAsync(backupPath);
                    if (currentHash != metadata.IntegrityHash)
                    {
                        if (!options.IgnoreIntegrityCheck)
                        {
                            result.Success = false;
                            result.ErrorMessage = "バックアップファイルの整合性チェックに失敗しました";
                            return result;
                        }
                        await Logger.LogWarning("バックアップ整合性チェックをスキップします",
                            nameof(BackupManager), new Dictionary<string, object> { ["backupPath"] = backupPath });
                    }
                }

                await Logger.LogInfo($"バックアップ復元を開始: {Path.GetFileName(backupPath)}",
                    nameof(BackupManager), new Dictionary<string, object>
                    {
                        ["backupPath"] = backupPath,
                        ["restoreOptions"] = options
                    });

                // 復元前のバックアップ（オプション）
                if (options.CreatePreRestoreBackup)
                {
                    await CreateFullBackupAsync($"pre_restore_{DateTime.Now:yyyyMMddHHmmss}", ct);
                }

                // 一時ディレクトリに展開
                var extractPath = Path.Combine(TempDirectory, $"restore_{Guid.NewGuid()}");
                Directory.CreateDirectory(extractPath);

                try
                {
                    ZipFile.ExtractToDirectory(backupPath, extractPath, true);

                    // 復元対象の決定
                    var filesToRestore = DetermineFilesToRestore(extractPath, options);

                    // ファイルの復元
                    foreach (var file in filesToRestore)
                    {
                        var targetPath = GetRestoreTargetPath(file, options);
                        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

                        if (options.BackupExistingFiles && File.Exists(targetPath))
                        {
                            var backupPath_ = $"{targetPath}.backup_{DateTime.Now:yyyyMMddHHmmss}";
                            File.Move(targetPath, backupPath_);
                        }

                        File.Copy(file, targetPath, true);
                        result.RestoredFiles.Add(targetPath);
                    }

                    result.Success = true;
                    result.RestoredFileCount = result.RestoredFiles.Count;

                    await Logger.LogInfo($"バックアップ復元完了: {result.RestoredFileCount} ファイルを復元",
                        nameof(BackupManager), new Dictionary<string, object>
                        {
                            ["restoredFiles"] = result.RestoredFileCount,
                            ["backupPath"] = backupPath
                        });

                }
                finally
                {
                    // 一時ディレクトリのクリーンアップ
                    Directory.Delete(extractPath, true);
                }

            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                await Logger.LogError($"バックアップ復元失敗: {ex.Message}",
                    nameof(BackupManager), null, ex);
            }

            return result;
        }

        /// <summary>
        /// 利用可能なバックアップを一覧表示
        /// </summary>
        public static List<BackupInfo> GetAvailableBackups()
        {
            try
            {
                var backupFiles = Directory.GetFiles(BackupDirectory, "*.zip");
                var backups = new List<BackupInfo>();

                foreach (var backupFile in backupFiles)
                {
                    var metadataPath = Path.ChangeExtension(backupFile, ".metadata.json");
                    BackupMetadata? metadata = null;

                    if (File.Exists(metadataPath))
                    {
                        try
                        {
                            var metadataJson = File.ReadAllText(metadataPath);
                            metadata = JsonSerializer.Deserialize<BackupMetadata>(metadataJson);
                        }
                        catch
                        {
                            // メタデータ読み込み失敗時はnullのまま
                        }
                    }

                    var fileInfo = new FileInfo(backupFile);
                    backups.Add(new BackupInfo
                    {
                        FilePath = backupFile,
                        FileName = Path.GetFileName(backupFile),
                        SizeBytes = fileInfo.Length,
                        CreatedAt = fileInfo.CreationTime,
                        Metadata = metadata
                    });
                }

                return backups.OrderByDescending(b => b.CreatedAt).ToList();
            }
            catch (Exception ex)
            {
                Logger.LogError($"バックアップ一覧取得失敗: {ex.Message}", nameof(BackupManager), null, ex);
                return new List<BackupInfo>();
            }
        }

        /// <summary>
        /// 自動バックアップを開始
        /// </summary>
        public static void StartAutoBackup()
        {
            if (_autoBackupTimer != null) return;

            _autoBackupTimer = new System.Timers.Timer(AutoBackupIntervalHours * 60 * 60 * 1000);
            _autoBackupTimer.Elapsed += async (s, e) => await PerformAutoBackupAsync();
            _autoBackupTimer.Start();

            Logger.LogInfo($"自動バックアップを開始しました (間隔: {AutoBackupIntervalHours}時間)",
                nameof(BackupManager));
        }

        /// <summary>
        /// 自動バックアップを停止
        /// </summary>
        public static void StopAutoBackup()
        {
            _autoBackupTimer?.Stop();
            _autoBackupTimer?.Dispose();
            _autoBackupTimer = null;
        }

        // プライベートメソッド
        private static async Task<List<BackupItem>> CollectBackupItemsAsync(CancellationToken ct)
        {
            var items = new List<BackupItem>();
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var basePath = Path.Combine(appData, "MurtiWifiConnecter");

            // 設定ファイル
            var configPath = Path.Combine(basePath, "config.json");
            if (File.Exists(configPath))
            {
                items.Add(new BackupItem { SourcePath = configPath, RelativePath = "config.json" });
            }

            // VPNプロファイル
            var vpnPath = Path.Combine(basePath, "vpn_profiles.json");
            if (File.Exists(vpnPath))
            {
                items.Add(new BackupItem { SourcePath = vpnPath, RelativePath = "vpn_profiles.json" });
            }

            // ログディレクトリ
            var logsPath = Path.Combine(basePath, "logs");
            if (Directory.Exists(logsPath))
            {
                items.Add(new BackupItem { SourcePath = logsPath, RelativePath = "logs" });
            }

            // 分析データ
            var analyticsPath = Path.Combine(basePath, "Analytics");
            if (Directory.Exists(analyticsPath))
            {
                items.Add(new BackupItem { SourcePath = analyticsPath, RelativePath = "Analytics" });
            }

            // 証明書ストア（存在する場合）
            var certsPath = Path.Combine(basePath, "certificates");
            if (Directory.Exists(certsPath))
            {
                items.Add(new BackupItem { SourcePath = certsPath, RelativePath = "certificates" });
            }

            return items;
        }

        private static async Task<List<BackupItem>> CollectConfigFilesAsync()
        {
            var items = new List<BackupItem>();
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var basePath = Path.Combine(appData, "MurtiWifiConnecter");

            // 設定ファイルのみ
            var configPath = Path.Combine(basePath, "config.json");
            if (File.Exists(configPath))
            {
                items.Add(new BackupItem { SourcePath = configPath, RelativePath = "config.json" });
            }

            var userConfigPath = Path.Combine(basePath, "user_config.json");
            if (File.Exists(userConfigPath))
            {
                items.Add(new BackupItem { SourcePath = userConfigPath, RelativePath = "user_config.json" });
            }

            var vpnConfigPath = Path.Combine(basePath, "vpn_profiles.json");
            if (File.Exists(vpnConfigPath))
            {
                items.Add(new BackupItem { SourcePath = vpnConfigPath, RelativePath = "vpn_profiles.json" });
            }

            return items;
        }

        private static void AddDirectoryToArchive(ZipArchive archive, string sourceDir, string relativePath)
        {
            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relativeFilePath = Path.GetRelativePath(sourceDir, file);
                var entryName = Path.Combine(relativePath, relativeFilePath);
                archive.CreateEntryFromFile(file, entryName);
            }
        }

        private static async Task<string> ComputeBackupIntegrityAsync(string backupPath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(backupPath);
            var hash = await sha256.ComputeHashAsync(stream);
            return Convert.ToBase64String(hash);
        }

        private static async Task CleanupOldBackupsAsync()
        {
            try
            {
                var backupFiles = Directory.GetFiles(BackupDirectory, "*.zip")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .Skip(MaxBackups)
                    .ToList();

                foreach (var oldBackup in backupFiles)
                {
                    oldBackup.Delete();

                    // 対応するメタデータファイルも削除
                    var metadataPath = Path.ChangeExtension(oldBackup.FullName, ".metadata.json");
                    if (File.Exists(metadataPath))
                    {
                        File.Delete(metadataPath);
                    }

                    await Logger.LogInfo($"古いバックアップを削除: {oldBackup.Name}",
                        nameof(BackupManager), new Dictionary<string, object>
                        {
                            ["fileName"] = oldBackup.Name,
                            ["fileSize"] = oldBackup.Length
                        });
                }
            }
            catch (Exception ex)
            {
                await Logger.LogError($"古いバックアップクリーンアップ失敗: {ex.Message}",
                    nameof(BackupManager), null, ex);
            }
        }

        private static async Task PerformAutoBackupAsync()
        {
            try
            {
                await CreateFullBackupAsync($"auto_backup_{DateTime.Now:yyyyMMddHHmmss}");
            }
            catch (Exception ex)
            {
                await Logger.LogError($"自動バックアップ失敗: {ex.Message}",
                    nameof(BackupManager), null, ex);
            }
        }

        private static List<string> DetermineFilesToRestore(string extractPath, RestoreOptions options)
        {
            var files = new List<string>();

            if (options.RestoreTypes.HasFlag(RestoreType.Config))
            {
                var configFile = Path.Combine(extractPath, "config.json");
                if (File.Exists(configFile)) files.Add(configFile);

                var userConfigFile = Path.Combine(extractPath, "user_config.json");
                if (File.Exists(userConfigFile)) files.Add(userConfigFile);
            }

            if (options.RestoreTypes.HasFlag(RestoreType.VpnProfiles))
            {
                var vpnFile = Path.Combine(extractPath, "vpn_profiles.json");
                if (File.Exists(vpnFile)) files.Add(vpnFile);
            }

            if (options.RestoreTypes.HasFlag(RestoreType.Logs))
            {
                var logsDir = Path.Combine(extractPath, "logs");
                if (Directory.Exists(logsDir))
                {
                    files.AddRange(Directory.GetFiles(logsDir, "*", SearchOption.AllDirectories));
                }
            }

            if (options.RestoreTypes.HasFlag(RestoreType.Analytics))
            {
                var analyticsDir = Path.Combine(extractPath, "Analytics");
                if (Directory.Exists(analyticsDir))
                {
                    files.AddRange(Directory.GetFiles(analyticsDir, "*", SearchOption.AllDirectories));
                }
            }

            return files;
        }

        private static string GetRestoreTargetPath(string extractedFile, RestoreOptions options)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var basePath = Path.Combine(appData, "MurtiWifiConnecter");

            var relativePath = Path.GetRelativePath(options.RestoreBasePath ?? "", extractedFile);

            // 相対パスの先頭にベースパスを付加
            return Path.Combine(basePath, relativePath);
        }

        private static string GetCurrentVersion()
        {
            // バージョン情報取得（実際の実装ではアセンブリバージョンを使用）
            return "3.1.0";
        }

        // データ構造
        public enum BackupType
        {
            Full,
            ConfigOnly,
            Incremental
        }

        [Flags]
        public enum RestoreType
        {
            None = 0,
            Config = 1,
            VpnProfiles = 2,
            Logs = 4,
            Analytics = 8,
            Certificates = 16,
            All = Config | VpnProfiles | Logs | Analytics | Certificates
        }

        public class BackupResult
        {
            public bool Success { get; set; }
            public string? ErrorMessage { get; set; }
            public string? BackupPath { get; set; }
            public string? MetadataPath { get; set; }
            public long SizeBytes { get; set; }
            public BackupType BackupType { get; set; }
            public DateTime Timestamp { get; set; }
        }

        public class RestoreResult
        {
            public bool Success { get; set; }
            public string? ErrorMessage { get; set; }
            public string BackupPath { get; set; } = string.Empty;
            public List<string> RestoredFiles { get; set; } = new();
            public int RestoredFileCount { get; set; }
            public DateTime Timestamp { get; set; }
        }

        public class BackupInfo
        {
            public string FilePath { get; set; } = string.Empty;
            public string FileName { get; set; } = string.Empty;
            public long SizeBytes { get; set; }
            public DateTime CreatedAt { get; set; }
            public BackupMetadata? Metadata { get; set; }
        }

        public class RestoreOptions
        {
            public RestoreType RestoreTypes { get; set; } = RestoreType.All;
            public bool CreatePreRestoreBackup { get; set; } = true;
            public bool BackupExistingFiles { get; set; } = true;
            public bool IgnoreIntegrityCheck { get; set; } = false;
            public string? RestoreBasePath { get; set; }
        }

        private class BackupItem
        {
            public string SourcePath { get; set; } = string.Empty;
            public string RelativePath { get; set; } = string.Empty;
        }

        private class BackupMetadata
        {
            public string Name { get; set; } = string.Empty;
            public BackupType Type { get; set; }
            public DateTime CreatedAt { get; set; }
            public long SizeBytes { get; set; }
            public string IntegrityHash { get; set; } = string.Empty;
            public List<string> Items { get; set; } = new();
            public string Version { get; set; } = string.Empty;
        }
    }
}
