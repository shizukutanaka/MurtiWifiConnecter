using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// 統合ファイル管理 - 複数のファイルアクセスを最適化
    /// </summary>
    public static class FileManager
    {
        private static readonly string AppDataPath;
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new();
        private static readonly SemaphoreSlim GlobalLock = new(5, 5); // 最大5つの同時ファイル操作
        
        static FileManager()
        {
            AppDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MurtiWifiConnecter");
            
            if (!Directory.Exists(AppDataPath))
            {
                Directory.CreateDirectory(AppDataPath);
            }
        }
        
        /// <summary>
        /// JSONファイルを安全に読み込み
        /// </summary>
        public static async Task<T?> ReadJsonAsync<T>(string fileName, CancellationToken cancellationToken = default) where T : class
        {
            var filePath = Path.Combine(AppDataPath, fileName);
            var fileLock = GetFileLock(filePath);
            
            await GlobalLock.WaitAsync(cancellationToken);
            try
            {
                await fileLock.WaitAsync(cancellationToken);
                try
                {
                    if (!File.Exists(filePath))
                        return null;
                    
                    var json = await File.ReadAllTextAsync(filePath, cancellationToken);
                    if (string.IsNullOrWhiteSpace(json))
                        return null;
                        
                    return JsonSerializer.Deserialize<T>(json);
                }
                finally
                {
                    fileLock.Release();
                }
            }
            finally
            {
                GlobalLock.Release();
            }
        }
        
        /// <summary>
        /// JSONファイルを安全に書き込み
        /// </summary>
        public static async Task WriteJsonAsync<T>(string fileName, T data, CancellationToken cancellationToken = default) where T : class
        {
            var filePath = Path.Combine(AppDataPath, fileName);
            var fileLock = GetFileLock(filePath);
            
            await GlobalLock.WaitAsync(cancellationToken);
            try
            {
                await fileLock.WaitAsync(cancellationToken);
                try
                {
                    var json = JsonSerializer.Serialize(data, new JsonSerializerOptions 
                    { 
                        WriteIndented = false // 軽量化
                    });
                    
                    // 原子的書き込み（一時ファイル経由）
                    var tempPath = filePath + ".tmp";
                    await File.WriteAllTextAsync(tempPath, json, cancellationToken);
                    
                    // ファイルを置き換え
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                    File.Move(tempPath, filePath);
                }
                finally
                {
                    fileLock.Release();
                }
            }
            finally
            {
                GlobalLock.Release();
            }
        }
        
        /// <summary>
        /// テキストファイルを安全に読み込み
        /// </summary>
        public static async Task<string?> ReadTextAsync(string fileName, CancellationToken cancellationToken = default)
        {
            var filePath = Path.Combine(AppDataPath, fileName);
            var fileLock = GetFileLock(filePath);
            
            await GlobalLock.WaitAsync(cancellationToken);
            try
            {
                await fileLock.WaitAsync(cancellationToken);
                try
                {
                    if (!File.Exists(filePath))
                        return null;
                    
                    return await File.ReadAllTextAsync(filePath, cancellationToken);
                }
                finally
                {
                    fileLock.Release();
                }
            }
            finally
            {
                GlobalLock.Release();
            }
        }
        
        /// <summary>
        /// テキストファイルを安全に書き込み
        /// </summary>
        public static async Task WriteTextAsync(string fileName, string content, CancellationToken cancellationToken = default)
        {
            var filePath = Path.Combine(AppDataPath, fileName);
            var fileLock = GetFileLock(filePath);
            
            await GlobalLock.WaitAsync(cancellationToken);
            try
            {
                await fileLock.WaitAsync(cancellationToken);
                try
                {
                    var tempPath = filePath + ".tmp";
                    await File.WriteAllTextAsync(tempPath, content, cancellationToken);
                    
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                    File.Move(tempPath, filePath);
                }
                finally
                {
                    fileLock.Release();
                }
            }
            finally
            {
                GlobalLock.Release();
            }
        }
        
        /// <summary>
        /// テキストファイルに追記
        /// </summary>
        public static async Task AppendTextAsync(string fileName, string content, CancellationToken cancellationToken = default)
        {
            var filePath = Path.Combine(AppDataPath, fileName);
            var fileLock = GetFileLock(filePath);
            
            await GlobalLock.WaitAsync(cancellationToken);
            try
            {
                await fileLock.WaitAsync(cancellationToken);
                try
                {
                    await File.AppendAllTextAsync(filePath, content, cancellationToken);
                }
                finally
                {
                    fileLock.Release();
                }
            }
            finally
            {
                GlobalLock.Release();
            }
        }
        
        /// <summary>
        /// ファイル存在確認
        /// </summary>
        public static bool Exists(string fileName)
        {
            var filePath = Path.Combine(AppDataPath, fileName);
            return File.Exists(filePath);
        }
        
        /// <summary>
        /// ファイル削除
        /// </summary>
        public static async Task DeleteAsync(string fileName, CancellationToken cancellationToken = default)
        {
            var filePath = Path.Combine(AppDataPath, fileName);
            var fileLock = GetFileLock(filePath);
            
            await GlobalLock.WaitAsync(cancellationToken);
            try
            {
                await fileLock.WaitAsync(cancellationToken);
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                }
                finally
                {
                    fileLock.Release();
                }
            }
            finally
            {
                GlobalLock.Release();
            }
        }
        
        /// <summary>
        /// 古いファイルをクリーンアップ
        /// </summary>
        public static async Task CleanupOldFilesAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
        {
            await GlobalLock.WaitAsync(cancellationToken);
            try
            {
                var cutoffTime = DateTime.Now - maxAge;
                
                foreach (var file in Directory.GetFiles(AppDataPath))
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                        
                    var info = new FileInfo(file);
                    if (info.LastWriteTime < cutoffTime && !IsSystemFile(info.Name))
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch
                        {
                            // ファイル削除エラーは無視
                        }
                    }
                }
            }
            finally
            {
                GlobalLock.Release();
            }
        }
        
        /// <summary>
        /// ディスク容量使用量を取得
        /// </summary>
        public static long GetTotalSizeBytes()
        {
            try
            {
                var totalSize = 0L;
                foreach (var file in Directory.GetFiles(AppDataPath))
                {
                    var info = new FileInfo(file);
                    totalSize += info.Length;
                }
                return totalSize;
            }
            catch
            {
                return 0;
            }
        }
        
        /// <summary>
        /// アプリデータパスを取得
        /// </summary>
        public static string GetAppDataPath() => AppDataPath;
        
        private static SemaphoreSlim GetFileLock(string filePath)
        {
            return FileLocks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));
        }
        
        private static bool IsSystemFile(string fileName)
        {
            // システムファイルは削除しない
            var systemFiles = new[] { "settings.json", "profiles.json", "connection_history.json" };
            foreach (var systemFile in systemFiles)
            {
                if (fileName.EndsWith(systemFile, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        
        /// <summary>
        /// バックアップファイルを作成
        /// </summary>
        public static async Task CreateBackupAsync(string fileName, CancellationToken cancellationToken = default)
        {
            var filePath = Path.Combine(AppDataPath, fileName);
            if (!File.Exists(filePath))
                return;
                
            var backupPath = Path.Combine(AppDataPath, $"{fileName}.backup");
            var fileLock = GetFileLock(filePath);
            
            await fileLock.WaitAsync(cancellationToken);
            try
            {
                File.Copy(filePath, backupPath, true);
            }
            finally
            {
                fileLock.Release();
            }
        }
    }
}