using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using static MurtiWifiConnecter.Services.Log;

namespace MurtiWifiConnecter.Services
{
    /// <summary>
    /// WiFiプロファイルのエクスポート/インポート機能
    /// </summary>
    public class ProfileExportImportService
    {
        private readonly string _profileBackupPath;
        private readonly byte[] _encryptionKey;

        public ProfileExportImportService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MurtiWifiConnecter",
                "Backups"
            );
            
            Directory.CreateDirectory(appDataPath);
            _profileBackupPath = appDataPath;
            
            // シンプルな暗号化キー（実際にはユーザー固有のキーを使用すべき）
            _encryptionKey = GenerateKeyFromMachineId();
        }

        /// <summary>
        /// 全プロファイルをファイルにエクスポート
        /// </summary>
        public async Task<ExportResult> ExportProfilesToFileAsync(string filePath = null)
        {
            try
            {
                // エクスポートファイルパス
                if (string.IsNullOrEmpty(filePath))
                {
                    filePath = Path.Combine(_profileBackupPath, 
                        $"wifi_profiles_{DateTime.Now:yyyyMMdd_HHmmss}.wfp");
                }

                // netshコマンドで全プロファイルを取得
                var profiles = await GetAllProfilesAsync();
                
                if (profiles.Count == 0)
                {
                    return new ExportResult 
                    { 
                        Success = false, 
                        Message = "No WiFi profiles found to export" 
                    };
                }

                // エクスポートデータ作成
                var exportData = new ProfileExportData
                {
                    ExportDate = DateTime.Now,
                    MachineName = Environment.MachineName,
                    ProfileCount = profiles.Count,
                    Profiles = profiles
                };

                // JSON化
                var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                // 暗号化して保存
                var encrypted = EncryptData(json);
                await File.WriteAllBytesAsync(filePath, encrypted);

                Log.Info($"Exported {profiles.Count} WiFi profiles to {filePath}");

                return new ExportResult
                {
                    Success = true,
                    FilePath = filePath,
                    ProfileCount = profiles.Count,
                    Message = $"Successfully exported {profiles.Count} profiles"
                };
            }
            catch (Exception ex)
            {
                Log.Error("Failed to export WiFi profiles", ex);
                return new ExportResult
                {
                    Success = false,
                    Message = $"Export failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// ファイルからプロファイルをインポート
        /// </summary>
        public async Task<ImportResult> ImportProfilesFromFileAsync(string filePath, bool overwriteExisting = false)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return new ImportResult
                    {
                        Success = false,
                        Message = "Import file not found"
                    };
                }

                // ファイル読み込みと復号化
                var encrypted = await File.ReadAllBytesAsync(filePath);
                var json = DecryptData(encrypted);
                
                if (string.IsNullOrEmpty(json))
                {
                    return new ImportResult
                    {
                        Success = false,
                        Message = "Failed to decrypt import file"
                    };
                }

                // デシリアライズ
                var exportData = JsonSerializer.Deserialize<ProfileExportData>(json);
                
                if (exportData == null || exportData.Profiles == null)
                {
                    return new ImportResult
                    {
                        Success = false,
                        Message = "Invalid import file format"
                    };
                }

                // 既存プロファイル取得
                var existingProfiles = await GetAllProfilesAsync();
                var existingNames = existingProfiles.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

                var imported = 0;
                var skipped = 0;
                var failed = 0;

                // プロファイルインポート
                foreach (var profile in exportData.Profiles)
                {
                    try
                    {
                        // 既存チェック
                        if (existingNames.Contains(profile.Name) && !overwriteExisting)
                        {
                            skipped++;
                            Log.Info($"Skipped existing profile: {profile.Name}");
                            continue;
                        }

                        // プロファイル追加
                        if (await AddProfileAsync(profile))
                        {
                            imported++;
                            Log.Info($"Imported profile: {profile.Name}");
                        }
                        else
                        {
                            failed++;
                            Log.Warning($"Failed to import profile: {profile.Name}");
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Log.Error($"Error importing profile {profile.Name}", ex);
                    }
                }

                var message = $"Import complete: {imported} imported, {skipped} skipped, {failed} failed";
                Log.Info(message);

                return new ImportResult
                {
                    Success = imported > 0,
                    ImportedCount = imported,
                    SkippedCount = skipped,
                    FailedCount = failed,
                    Message = message
                };
            }
            catch (Exception ex)
            {
                Log.Error("Failed to import WiFi profiles", ex);
                return new ImportResult
                {
                    Success = false,
                    Message = $"Import failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 自動バックアップ
        /// </summary>
        public async Task<bool> CreateAutoBackupAsync()
        {
            try
            {
                // 古いバックアップを削除
                CleanOldBackups(7); // 7日以上前のバックアップを削除

                var backupPath = Path.Combine(_profileBackupPath, 
                    $"auto_backup_{DateTime.Now:yyyyMMdd}.wfp");

                var result = await ExportProfilesToFileAsync(backupPath);
                return result.Success;
            }
            catch (Exception ex)
            {
                Log.Error("Auto backup failed", ex);
                return false;
            }
        }

        private async Task<List<WifiProfileData>> GetAllProfilesAsync()
        {
            var profiles = new List<WifiProfileData>();

            try
            {
                // プロファイル一覧取得
                var result = await NetworkUtils.ExecuteNetshCommandAsync("wlan show profiles");
                if (!result.Success) return profiles;

                var lines = result.Output.Split('\n');
                var profileNames = new List<string>();

                foreach (var line in lines)
                {
                    if (line.Contains("All User Profile") && line.Contains(":"))
                    {
                        var parts = line.Split(':');
                        if (parts.Length > 1)
                        {
                            var name = parts[1].Trim();
                            if (!string.IsNullOrEmpty(name))
                                profileNames.Add(name);
                        }
                    }
                }

                // 各プロファイルの詳細取得
                foreach (var name in profileNames)
                {
                    var profileData = await GetProfileDetailsAsync(name);
                    if (profileData != null)
                        profiles.Add(profileData);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Failed to get WiFi profiles", ex);
            }

            return profiles;
        }

        private async Task<WifiProfileData> GetProfileDetailsAsync(string profileName)
        {
            try
            {
                var result = await NetworkUtils.ExecuteNetshCommandAsync($"wlan show profile \"{profileName}\" key=clear");
                if (!result.Success) return null;

                var profile = new WifiProfileData
                {
                    Name = profileName,
                    SSID = profileName // デフォルトは同じ
                };

                var lines = result.Output.Split('\n');
                
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    
                    if (trimmed.StartsWith("SSID name") && trimmed.Contains(":"))
                    {
                        var parts = trimmed.Split(':');
                        if (parts.Length > 1)
                        {
                            var ssid = parts[1].Trim().Trim('"');
                            if (!string.IsNullOrEmpty(ssid))
                                profile.SSID = ssid;
                        }
                    }
                    else if (trimmed.StartsWith("Authentication") && trimmed.Contains(":"))
                    {
                        var parts = trimmed.Split(':');
                        if (parts.Length > 1)
                            profile.Authentication = parts[1].Trim();
                    }
                    else if (trimmed.StartsWith("Key Content") && trimmed.Contains(":"))
                    {
                        var parts = trimmed.Split(':');
                        if (parts.Length > 1)
                        {
                            // パスワードは暗号化して保存
                            var password = parts[1].Trim();
                            if (!string.IsNullOrEmpty(password))
                                profile.KeyMaterial = Convert.ToBase64String(EncryptData(password));
                        }
                    }
                }

                return profile;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to get profile details for {profileName}", ex);
                return null;
            }
        }

        private async Task<bool> AddProfileAsync(WifiProfileData profile)
        {
            try
            {
                // パスワード復号化
                string password = "";
                if (!string.IsNullOrEmpty(profile.KeyMaterial))
                {
                    try
                    {
                        var encrypted = Convert.FromBase64String(profile.KeyMaterial);
                        password = DecryptData(encrypted);
                    }
                    catch
                    {
                        // 復号化失敗（異なるマシンからのインポートなど）
                        Log.Warning($"Could not decrypt password for {profile.Name}");
                        return false;
                    }
                }

                // XML プロファイル作成
                var profileXml = CreateProfileXml(profile.SSID, password, profile.Authentication);
                
                // 一時ファイル作成
                var tempFile = Path.GetTempFileName();
                await File.WriteAllTextAsync(tempFile, profileXml);

                try
                {
                    // プロファイル追加
                    var result = await NetworkUtils.ExecuteNetshCommandAsync(
                        $"wlan add profile filename=\"{tempFile}\" user=current");
                    
                    return result.Success;
                }
                finally
                {
                    // 一時ファイル削除
                    try { File.Delete(tempFile); } catch { }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to add profile {profile.Name}", ex);
                return false;
            }
        }

        private string CreateProfileXml(string ssid, string password, string authentication = "WPA2PSK")
        {
            var safeSSID = System.Security.SecurityElement.Escape(ssid);
            var safePassword = System.Security.SecurityElement.Escape(password);

            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
    <name>{safeSSID}</name>
    <SSIDConfig>
        <SSID>
            <name>{safeSSID}</name>
        </SSID>
    </SSIDConfig>
    <connectionType>ESS</connectionType>
    <connectionMode>auto</connectionMode>
    <MSM>
        <security>
            <authEncryption>
                <authentication>{authentication}</authentication>
                <encryption>AES</encryption>
                <useOneX>false</useOneX>
            </authEncryption>
            <sharedKey>
                <keyType>passPhrase</keyType>
                <protected>false</protected>
                <keyMaterial>{safePassword}</keyMaterial>
            </sharedKey>
        </security>
    </MSM>
</WLANProfile>";
        }

        private byte[] EncryptData(string data)
        {
            try
            {
                using var aes = Aes.Create();
                aes.Key = _encryptionKey;
                aes.GenerateIV();

                using var encryptor = aes.CreateEncryptor();
                var dataBytes = Encoding.UTF8.GetBytes(data);
                var encrypted = encryptor.TransformFinalBlock(dataBytes, 0, dataBytes.Length);

                // IV + 暗号化データ
                var result = new byte[aes.IV.Length + encrypted.Length];
                aes.IV.CopyTo(result, 0);
                encrypted.CopyTo(result, aes.IV.Length);

                return result;
            }
            catch
            {
                return Encoding.UTF8.GetBytes(data); // 暗号化失敗時は平文
            }
        }

        private string DecryptData(byte[] encrypted)
        {
            try
            {
                using var aes = Aes.Create();
                aes.Key = _encryptionKey;

                // IVを抽出
                var iv = new byte[aes.IV.Length];
                Array.Copy(encrypted, 0, iv, 0, iv.Length);
                aes.IV = iv;

                // データ部分を復号化
                using var decryptor = aes.CreateDecryptor();
                var dataLength = encrypted.Length - iv.Length;
                var decrypted = decryptor.TransformFinalBlock(encrypted, iv.Length, dataLength);

                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                // 復号化失敗（平文の可能性）
                return Encoding.UTF8.GetString(encrypted);
            }
        }

        private byte[] GenerateKeyFromMachineId()
        {
            // マシン固有のキー生成（簡易版）
            var machineId = Environment.MachineName + Environment.UserName;
            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(Encoding.UTF8.GetBytes(machineId));
        }

        private void CleanOldBackups(int daysToKeep)
        {
            try
            {
                var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
                var backupFiles = Directory.GetFiles(_profileBackupPath, "auto_backup_*.wfp");

                foreach (var file in backupFiles)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.CreationTime < cutoffDate)
                    {
                        File.Delete(file);
                        Log.Info($"Deleted old backup: {fileInfo.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Failed to clean old backups", ex);
            }
        }
    }

    // データモデル
    public class ProfileExportData
    {
        public DateTime ExportDate { get; set; }
        public string MachineName { get; set; }
        public int ProfileCount { get; set; }
        public List<WifiProfileData> Profiles { get; set; } = new();
    }

    public class WifiProfileData
    {
        public string Name { get; set; }
        public string SSID { get; set; }
        public string Authentication { get; set; }
        public string KeyMaterial { get; set; } // 暗号化されたパスワード
    }

    public class ExportResult
    {
        public bool Success { get; set; }
        public string FilePath { get; set; }
        public int ProfileCount { get; set; }
        public string Message { get; set; }
    }

    public class ImportResult
    {
        public bool Success { get; set; }
        public int ImportedCount { get; set; }
        public int SkippedCount { get; set; }
        public int FailedCount { get; set; }
        public string Message { get; set; }
    }
}