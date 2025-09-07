using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    public class WifiProfileManager
    {
        private readonly string _backupDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MurtiWifiConnecter", "Backups");
        public async Task<List<string>> GetSavedProfilesAsync()
        {
            try
            {
                return await Task.Run(() =>
                {
                    var profiles = new List<string>();
                    Process proc = null;

                    try
                    {
                        var psi = new ProcessStartInfo("netsh", "wlan show profiles")
                        {
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };

                        proc = Process.Start(psi);
                        if (proc == null) return profiles;

                        if (!proc.WaitForExit(10000))
                        {
                            proc.Kill();
                            return profiles;
                        }

                        string output = proc.StandardOutput.ReadToEnd();
                        if (proc.ExitCode != 0) return profiles;

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
                    }
                    catch { }
                    finally
                    {
                        try { proc?.Kill(); } catch { }
                        proc?.Dispose();
                    }

                    return profiles.Distinct().OrderBy(p => p).ToList();
                });
            }
            catch
            {
                return new List<string>();
            }
        }

        public async Task<bool> DeleteProfileAsync(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName)) return false;

            try
            {
                return await Task.Run(() =>
                {
                    Process proc = null;
                    try
                    {
                        var safeProfileName = System.Security.SecurityElement.Escape(profileName);
                        var psi = new ProcessStartInfo("netsh", $"wlan delete profile name=\"{safeProfileName}\"")
                        {
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };

                        proc = Process.Start(psi);
                        if (proc == null) return false;

                        if (!proc.WaitForExit(10000))
                        {
                            proc.Kill();
                            return false;
                        }

                        return proc.ExitCode == 0;
                    }
                    catch
                    {
                        return false;
                    }
                    finally
                    {
                        try { proc?.Kill(); } catch { }
                        proc?.Dispose();
                    }
                });
            }
            catch
            {
                return false;
            }
        }

        public async Task<WifiProfileInfo> GetProfileInfoAsync(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName)) 
                return new WifiProfileInfo { ProfileName = profileName };

            try
            {
                return await Task.Run(() =>
                {
                    var info = new WifiProfileInfo { ProfileName = profileName };
                    Process proc = null;

                    try
                    {
                        var safeProfileName = System.Security.SecurityElement.Escape(profileName);
                        var psi = new ProcessStartInfo("netsh", $"wlan show profile name=\"{safeProfileName}\" key=clear")
                        {
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };

                        proc = Process.Start(psi);
                        if (proc == null) return info;

                        if (!proc.WaitForExit(10000))
                        {
                            proc.Kill();
                            return info;
                        }

                        if (proc.ExitCode != 0) return info;

                        string output = proc.StandardOutput.ReadToEnd();
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
                    catch { }
                    finally
                    {
                        try { proc?.Kill(); } catch { }
                        proc?.Dispose();
                    }

                    return info;
                });
            }
            catch
            {
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
                
                // 古いバックアップを削除（最新10個を保持）
                var backupFiles = Directory.GetFiles(_backupDirectory, "*.txt")
                    .OrderByDescending(f => new FileInfo(f).CreationTime)
                    .Skip(10)
                    .ToList();
                    
                foreach (var oldFile in backupFiles)
                {
                    try { File.Delete(oldFile); } catch { }
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
                    return Directory.GetFiles(_backupDirectory, "*.txt")
                        .Select(f => Path.GetFileNameWithoutExtension(f))
                        .OrderByDescending(f => f)
                        .ToList();
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

                var tempPath = Path.Combine(Path.GetTempPath(), $"wifi_restore_{Guid.NewGuid():N}.xml");
                await File.WriteAllTextAsync(tempPath, profileXml);
                
                var success = await NetworkUtils.ExecuteNetshCommandWithResultAsync(
                    $"wlan add profile filename=\"{tempPath}\" user=current",
                    10000);
                    
                try { File.Delete(tempPath); } catch { }
                
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
    }

    public class WifiProfileInfo
    {
        public string ProfileName { get; set; } = string.Empty;
        public string SSID { get; set; } = string.Empty;
        public string Authentication { get; set; } = string.Empty;
        public bool AutoConnect { get; set; }
    }
}