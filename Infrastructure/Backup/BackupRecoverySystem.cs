using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Infrastructure.Backup
{
    /// <summary>
    /// バックアップ・リカバリシステム
    /// </summary>
    public interface IBackupRecoverySystem
    {
        Task<BackupResult> CreateBackupAsync(BackupOptions options = null);
        Task<RestoreResult> RestoreBackupAsync(string backupPath, RestoreOptions options = null);
        Task<List<BackupInfo>> GetAvailableBackupsAsync();
        Task<bool> DeleteBackupAsync(string backupId);
        Task<BackupValidationResult> ValidateBackupAsync(string backupPath);
        Task ScheduleAutomaticBackupAsync(TimeSpan interval, BackupOptions options = null);
        void StopAutomaticBackup();
        event Action<BackupProgressEventArgs> BackupProgress;
        event Action<RestoreProgressEventArgs> RestoreProgress;
    }

    /// <summary>
    /// バックアップ・リカバリシステムの実装
    /// </summary>
    public class BackupRecoverySystem : IBackupRecoverySystem, IDisposable
    {
        private readonly string _backupDirectory;
        private readonly int _maxBackups;
        private readonly long _maxBackupSizeMB;
        private System.Timers.Timer _automaticBackupTimer;
        private BackupOptions _automaticBackupOptions;

        public event Action<BackupProgressEventArgs> BackupProgress;
        public event Action<RestoreProgressEventArgs> RestoreProgress;

        public BackupRecoverySystem(string backupDirectory = "Backups", int maxBackups = 10, long maxBackupSizeMB = 100)
        {
            _backupDirectory = backupDirectory;
            _maxBackups = maxBackups;
            _maxBackupSizeMB = maxBackupSizeMB;

            Directory.CreateDirectory(_backupDirectory);
        }

        /// <summary>
        /// バックアップを作成
        /// </summary>
        public async Task<BackupResult> CreateBackupAsync(BackupOptions options = null)
        {
            options ??= new BackupOptions();
            var backupId = GenerateBackupId();
            var backupPath = Path.Combine(_backupDirectory, $"backup_{backupId}.zip");

            var result = new BackupResult
            {
                BackupId = backupId,
                BackupPath = backupPath,
                StartTime = DateTime.Now,
                Success = false
            };

            try
            {
                ReportProgress(new BackupProgressEventArgs("バックアップを開始しています...", 0));

                var tempDirectory = Path.Combine(Path.GetTempPath(), $"backup_temp_{backupId}");
                Directory.CreateDirectory(tempDirectory);

                try
                {
                    // バックアップするファイルを収集
                    var filesToBackup = await CollectFilesToBackupAsync(options);
                    result.TotalFiles = filesToBackup.Count;

                    ReportProgress(new BackupProgressEventArgs($"{filesToBackup.Count}個のファイルをバックアップ中...", 10));

                    // ファイルを一時ディレクトリにコピー
                    var copiedFiles = 0;
                    foreach (var file in filesToBackup)
                    {
                        await CopyFileForBackupAsync(file, tempDirectory, options);
                        copiedFiles++;

                        var progress = 10 + (int)((double)copiedFiles / filesToBackup.Count * 70);
                        ReportProgress(new BackupProgressEventArgs($"ファイルをコピー中... ({copiedFiles}/{filesToBackup.Count})", progress));
                    }

                    // メタデータを作成
                    var metadata = CreateBackupMetadata(options, filesToBackup);
                    var metadataPath = Path.Combine(tempDirectory, "backup_metadata.json");
                    await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));

                    ReportProgress(new BackupProgressEventArgs("アーカイブを作成中...", 85));

                    // ZIPアーカイブを作成
                    ZipFile.CreateFromDirectory(tempDirectory, backupPath, CompressionLevel.Optimal, false);

                    ReportProgress(new BackupProgressEventArgs("バックアップを完了しています...", 95));

                    // バックアップ情報を更新
                    var backupInfo = new FileInfo(backupPath);
                    result.BackupSizeMB = backupInfo.Length / (1024.0 * 1024.0);
                    result.FilesBackedUp = filesToBackup.Count;
                    result.Success = true;

                    ReportProgress(new BackupProgressEventArgs("バックアップが完了しました", 100));

                    // 古いバックアップを清理
                    await CleanupOldBackupsAsync();
                }
                finally
                {
                    // 一時ディレクトリを削除
                    if (Directory.Exists(tempDirectory))
                    {
                        Directory.Delete(tempDirectory, true);
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                ReportProgress(new BackupProgressEventArgs($"バックアップに失敗しました: {ex.Message}", 0));
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        /// <summary>
        /// バックアップを復元
        /// </summary>
        public async Task<RestoreResult> RestoreBackupAsync(string backupPath, RestoreOptions options = null)
        {
            options ??= new RestoreOptions();

            var result = new RestoreResult
            {
                BackupPath = backupPath,
                StartTime = DateTime.Now,
                Success = false
            };

            try
            {
                ReportRestoreProgress(new RestoreProgressEventArgs("復元を開始しています...", 0));

                // バックアップファイルの存在確認
                if (!File.Exists(backupPath))
                {
                    throw new FileNotFoundException($"バックアップファイルが見つかりません: {backupPath}");
                }

                // バックアップを検証
                var validationResult = await ValidateBackupAsync(backupPath);
                if (!validationResult.IsValid)
                {
                    throw new InvalidOperationException($"バックアップファイルが無効です: {validationResult.ErrorMessage}");
                }

                ReportRestoreProgress(new RestoreProgressEventArgs("バックアップファイルを展開中...", 10));

                var tempDirectory = Path.Combine(Path.GetTempPath(), $"restore_temp_{Guid.NewGuid()}");
                Directory.CreateDirectory(tempDirectory);

                try
                {
                    // ZIPアーカイブを展開
                    ZipFile.ExtractToDirectory(backupPath, tempDirectory);

                    // メタデータを読み込み
                    var metadataPath = Path.Combine(tempDirectory, "backup_metadata.json");
                    var metadata = JsonSerializer.Deserialize<BackupMetadata>(await File.ReadAllTextAsync(metadataPath));

                    ReportRestoreProgress(new RestoreProgressEventArgs($"{metadata.Files.Count}個のファイルを復元中...", 20));

                    // ファイルを復元
                    var restoredFiles = 0;
                    foreach (var fileInfo in metadata.Files)
                    {
                        await RestoreFileAsync(fileInfo, tempDirectory, options);
                        restoredFiles++;

                        var progress = 20 + (int)((double)restoredFiles / metadata.Files.Count * 70);
                        ReportRestoreProgress(new RestoreProgressEventArgs($"ファイルを復元中... ({restoredFiles}/{metadata.Files.Count})", progress));
                    }

                    result.FilesRestored = restoredFiles;
                    result.Success = true;

                    ReportRestoreProgress(new RestoreProgressEventArgs("復元が完了しました", 100));
                }
                finally
                {
                    // 一時ディレクトリを削除
                    if (Directory.Exists(tempDirectory))
                    {
                        Directory.Delete(tempDirectory, true);
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                ReportRestoreProgress(new RestoreProgressEventArgs($"復元に失敗しました: {ex.Message}", 0));
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        /// <summary>
        /// 利用可能なバックアップ一覧を取得
        /// </summary>
        public async Task<List<BackupInfo>> GetAvailableBackupsAsync()
        {
            var backups = new List<BackupInfo>();

            try
            {
                var backupFiles = Directory.GetFiles(_backupDirectory, "backup_*.zip");

                foreach (var file in backupFiles)
                {
                    try
                    {
                        var info = await GetBackupInfoAsync(file);
                        if (info != null)
                        {
                            backups.Add(info);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to read backup info for {file}: {ex.Message}");
                    }
                }

                // 作成日時で降順ソート
                backups.Sort((a, b) => b.CreatedDate.CompareTo(a.CreatedDate));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get available backups: {ex.Message}");
            }

            return backups;
        }

        /// <summary>
        /// バックアップを削除
        /// </summary>
        public async Task<bool> DeleteBackupAsync(string backupId)
        {
            try
            {
                var backupPath = Path.Combine(_backupDirectory, $"backup_{backupId}.zip");
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to delete backup {backupId}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// バックアップを検証
        /// </summary>
        public async Task<BackupValidationResult> ValidateBackupAsync(string backupPath)
        {
            var result = new BackupValidationResult { IsValid = false };

            try
            {
                if (!File.Exists(backupPath))
                {
                    result.ErrorMessage = "バックアップファイルが存在しません";
                    return result;
                }

                // ZIPファイルとして開けるかテスト
                using var archive = ZipFile.OpenRead(backupPath);
                
                // メタデータファイルの存在確認
                var metadataEntry = archive.GetEntry("backup_metadata.json");
                if (metadataEntry == null)
                {
                    result.ErrorMessage = "メタデータファイルが見つかりません";
                    return result;
                }

                // メタデータを読み込み
                using var metadataStream = metadataEntry.Open();
                using var reader = new StreamReader(metadataStream);
                var metadataJson = await reader.ReadToEndAsync();
                var metadata = JsonSerializer.Deserialize<BackupMetadata>(metadataJson);

                // ファイル数の検証
                var actualFileCount = archive.Entries.Count - 1; // メタデータファイルを除く
                if (metadata.Files.Count != actualFileCount)
                {
                    result.ErrorMessage = $"ファイル数が一致しません。期待値: {metadata.Files.Count}, 実際: {actualFileCount}";
                    return result;
                }

                result.IsValid = true;
                result.Metadata = metadata;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"バックアップ検証エラー: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// 自動バックアップをスケジュール
        /// </summary>
        public async Task ScheduleAutomaticBackupAsync(TimeSpan interval, BackupOptions options = null)
        {
            _automaticBackupOptions = options ?? new BackupOptions();
            
            _automaticBackupTimer?.Dispose();
            _automaticBackupTimer = new System.Timers.Timer(interval.TotalMilliseconds);
            _automaticBackupTimer.Elapsed += async (sender, e) =>
            {
                try
                {
                    await CreateBackupAsync(_automaticBackupOptions);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Automatic backup failed: {ex.Message}");
                }
            };
            _automaticBackupTimer.Start();
        }

        /// <summary>
        /// 自動バックアップを停止
        /// </summary>
        public void StopAutomaticBackup()
        {
            _automaticBackupTimer?.Stop();
            _automaticBackupTimer?.Dispose();
            _automaticBackupTimer = null;
        }

        /// <summary>
        /// バックアップするファイルを収集
        /// </summary>
        private async Task<List<string>> CollectFilesToBackupAsync(BackupOptions options)
        {
            var files = new List<string>();

            // 設定ファイル
            if (options.IncludeConfiguration)
            {
                AddFilesIfExist(files, "config.json", "settings.json", "user.config");
            }

            // ログファイル
            if (options.IncludeLogs)
            {
                var logDirectory = "Logs";
                if (Directory.Exists(logDirectory))
                {
                    files.AddRange(Directory.GetFiles(logDirectory, "*.log"));
                }
            }

            // ユーザーデータ
            if (options.IncludeUserData)
            {
                AddFilesIfExist(files, "wifi_profiles.json", "connection_history.json", "user_preferences.json");
            }

            // カスタムファイル
            if (options.CustomPaths != null)
            {
                foreach (var path in options.CustomPaths)
                {
                    if (File.Exists(path))
                    {
                        files.Add(path);
                    }
                    else if (Directory.Exists(path))
                    {
                        files.AddRange(Directory.GetFiles(path, "*", SearchOption.AllDirectories));
                    }
                }
            }

            return files.Distinct().ToList();
        }

        /// <summary>
        /// ファイルをバックアップ用にコピー
        /// </summary>
        private async Task CopyFileForBackupAsync(string sourceFile, string backupDirectory, BackupOptions options)
        {
            var relativePath = GetRelativePath(sourceFile);
            var destPath = Path.Combine(backupDirectory, relativePath);
            var destDir = Path.GetDirectoryName(destPath);

            Directory.CreateDirectory(destDir);
            await File.CopyAsync(sourceFile, destPath);
        }

        /// <summary>
        /// ファイルを復元
        /// </summary>
        private async Task RestoreFileAsync(BackupFileInfo fileInfo, string tempDirectory, RestoreOptions options)
        {
            var sourcePath = Path.Combine(tempDirectory, fileInfo.RelativePath);
            var destPath = fileInfo.OriginalPath;

            if (options.OverwriteExisting || !File.Exists(destPath))
            {
                var destDir = Path.GetDirectoryName(destPath);
                Directory.CreateDirectory(destDir);
                await File.CopyAsync(sourcePath, destPath);
            }
        }

        /// <summary>
        /// バックアップメタデータを作成
        /// </summary>
        private BackupMetadata CreateBackupMetadata(BackupOptions options, List<string> files)
        {
            return new BackupMetadata
            {
                BackupId = GenerateBackupId(),
                CreatedDate = DateTime.Now,
                Version = "1.0",
                Options = options,
                Files = files.Select(f => new BackupFileInfo
                {
                    OriginalPath = f,
                    RelativePath = GetRelativePath(f),
                    Size = new FileInfo(f).Length,
                    LastModified = File.GetLastWriteTime(f)
                }).ToList()
            };
        }

        /// <summary>
        /// 相対パスを取得
        /// </summary>
        private string GetRelativePath(string fullPath)
        {
            var currentDir = Directory.GetCurrentDirectory();
            var relativePath = Path.GetRelativePath(currentDir, fullPath);
            return relativePath.Replace(Path.DirectorySeparatorChar, '/');
        }

        /// <summary>
        /// ファイルが存在する場合リストに追加
        /// </summary>
        private void AddFilesIfExist(List<string> files, params string[] paths)
        {
            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    files.Add(path);
                }
            }
        }

        /// <summary>
        /// 古いバックアップを清理
        /// </summary>
        private async Task CleanupOldBackupsAsync()
        {
            try
            {
                var backups = await GetAvailableBackupsAsync();
                if (backups.Count > _maxBackups)
                {
                    var backupsToDelete = backups.Skip(_maxBackups);
                    foreach (var backup in backupsToDelete)
                    {
                        await DeleteBackupAsync(backup.BackupId);
                    }
                }

                // サイズ制限チェック
                var totalSize = backups.Sum(b => b.SizeMB);
                if (totalSize > _maxBackupSizeMB)
                {
                    var sortedBackups = backups.OrderBy(b => b.CreatedDate);
                    foreach (var backup in sortedBackups)
                    {
                        await DeleteBackupAsync(backup.BackupId);
                        totalSize -= backup.SizeMB;
                        if (totalSize <= _maxBackupSizeMB)
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Cleanup failed: {ex.Message}");
            }
        }

        /// <summary>
        /// バックアップ情報を取得
        /// </summary>
        private async Task<BackupInfo> GetBackupInfoAsync(string backupPath)
        {
            try
            {
                using var archive = ZipFile.OpenRead(backupPath);
                var metadataEntry = archive.GetEntry("backup_metadata.json");
                if (metadataEntry == null)
                    return null;

                using var stream = metadataEntry.Open();
                using var reader = new StreamReader(stream);
                var metadataJson = await reader.ReadToEndAsync();
                var metadata = JsonSerializer.Deserialize<BackupMetadata>(metadataJson);

                var fileInfo = new FileInfo(backupPath);
                return new BackupInfo
                {
                    BackupId = metadata.BackupId,
                    BackupPath = backupPath,
                    CreatedDate = metadata.CreatedDate,
                    SizeMB = fileInfo.Length / (1024.0 * 1024.0),
                    FileCount = metadata.Files.Count,
                    Description = $"Backup created on {metadata.CreatedDate:yyyy-MM-dd HH:mm:ss}"
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// バックアップIDを生成
        /// </summary>
        private string GenerateBackupId()
        {
            return DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..8];
        }

        /// <summary>
        /// バックアップ進捗を報告
        /// </summary>
        private void ReportProgress(BackupProgressEventArgs args)
        {
            BackupProgress?.Invoke(args);
        }

        /// <summary>
        /// 復元進捗を報告
        /// </summary>
        private void ReportRestoreProgress(RestoreProgressEventArgs args)
        {
            RestoreProgress?.Invoke(args);
        }

        public void Dispose()
        {
            StopAutomaticBackup();
        }
    }

    #region Data Models

    public class BackupOptions
    {
        public bool IncludeConfiguration { get; set; } = true;
        public bool IncludeLogs { get; set; } = false;
        public bool IncludeUserData { get; set; } = true;
        public List<string> CustomPaths { get; set; } = new();
        public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;
    }

    public class RestoreOptions
    {
        public bool OverwriteExisting { get; set; } = false;
        public bool RestoreConfiguration { get; set; } = true;
        public bool RestoreUserData { get; set; } = true;
        public string TargetDirectory { get; set; }
    }

    public class BackupResult
    {
        public string BackupId { get; set; }
        public string BackupPath { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public int TotalFiles { get; set; }
        public int FilesBackedUp { get; set; }
        public double BackupSizeMB { get; set; }
    }

    public class RestoreResult
    {
        public string BackupPath { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public int FilesRestored { get; set; }
    }

    public class BackupInfo
    {
        public string BackupId { get; set; }
        public string BackupPath { get; set; }
        public DateTime CreatedDate { get; set; }
        public double SizeMB { get; set; }
        public int FileCount { get; set; }
        public string Description { get; set; }
    }

    public class BackupValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; }
        public BackupMetadata Metadata { get; set; }
    }

    public class BackupMetadata
    {
        public string BackupId { get; set; }
        public DateTime CreatedDate { get; set; }
        public string Version { get; set; }
        public BackupOptions Options { get; set; }
        public List<BackupFileInfo> Files { get; set; } = new();
    }

    public class BackupFileInfo
    {
        public string OriginalPath { get; set; }
        public string RelativePath { get; set; }
        public long Size { get; set; }
        public DateTime LastModified { get; set; }
    }

    public class BackupProgressEventArgs
    {
        public string Message { get; set; }
        public int ProgressPercentage { get; set; }

        public BackupProgressEventArgs(string message, int progressPercentage)
        {
            Message = message;
            ProgressPercentage = progressPercentage;
        }
    }

    public class RestoreProgressEventArgs
    {
        public string Message { get; set; }
        public int ProgressPercentage { get; set; }

        public RestoreProgressEventArgs(string message, int progressPercentage)
        {
            Message = message;
            ProgressPercentage = progressPercentage;
        }
    }

    #endregion
}