/**
 * 現在のC#実装の問題点と改善策
 *
 * 現在のWindowsNetworkOperations.csの問題点：
 * 1. 過度な複雑さ（1500行超、複数のキャッシュ、ネイティブAPI）
 * 2. 依存関係の多さ（MemoryCache、SecurityManager、ConfigManager等）
 * 3. パフォーマンスオーバーヘッド（頻繁なログ、監視、セキュリティチェック）
 * 4. 保守性の低下（プラットフォーム重複、テストの複雑さ）
 *
 * 改善方針：
 * 1. コア機能に特化（スキャン、接続、切断、ステータス）
 * 2. 依存関係を最小限に（標準ライブラリのみ）
 * 3. 軽量な実装（シンプルキャッシュ、基本エラーハンドリング）
 * 4. クロスプラットフォーム対応（netsh, nmcli, airportコマンド）
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// シンプルで効率的なネットワーク操作実装
    /// 現在の複雑な実装に対する軽量版
    /// </summary>
    public class SimpleNetworkOperations : INetworkOperations
    {
        // 軽量キャッシュ - 静的Dictionaryで十分
        private static readonly Dictionary<string, (object data, DateTime expiry)> _cache = new();
        private static readonly object _cacheLock = new();
        private static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromSeconds(30);

        // 基本的なレート制限 - シンプルなカウンター
        private static readonly Dictionary<string, DateTime> _lastOperation = new();
        private static readonly TimeSpan MinOperationInterval = TimeSpan.FromMilliseconds(100);

        // プラットフォーム判定
        private readonly PlatformType _platform = DetectPlatform();

        public PlatformType Platform => _platform;

        private static PlatformType DetectPlatform()
        {
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                    return PlatformType.Windows;
                case PlatformID.Unix:
                    return PlatformType.Linux; // WSLを含む
                case PlatformID.MacOSX:
                    return PlatformType.MacOS;
                default:
                    return PlatformType.Windows;
            }
        }

        public async Task<List<NetworkInfo>> ScanNetworksAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            const string cacheKey = "scan_networks";

            // 軽量なレート制限チェック
            if (!CheckRateLimit("scan"))
            {
                throw new InvalidOperationException("Rate limit exceeded. Please wait before scanning again.");
            }

            // キャッシュチェック（シンプル版）
            if (!forceRefresh && GetCachedData<List<NetworkInfo>>(cacheKey) is List<NetworkInfo> cached)
            {
                return cached;
            }

            var networks = await ExecuteWithRetry(async () =>
            {
                string command, arguments;
                GetScanCommand(out command, out arguments);

                var output = await ExecuteCommandAsync(command, arguments, cancellationToken);

                if (string.IsNullOrWhiteSpace(output))
                    return new List<NetworkInfo>();

                return ParseNetworkScanOutput(output);
            }, new List<NetworkInfo>());

            // キャッシュ保存（シンプル版）
            SetCachedData(cacheKey, networks, DefaultCacheDuration);

            return networks.OrderByDescending(n => n.Signal).ToList();
        }

        public async Task<bool> ConnectAsync(string ssid, string password = null, CancellationToken cancellationToken = default)
        {
            if (!CheckRateLimit("connect"))
            {
                throw new InvalidOperationException("Rate limit exceeded. Please wait before connecting.");
            }

            // 入力検証（軽量版）
            ssid = ssid?.Trim();
            if (string.IsNullOrEmpty(ssid) || ssid.Length > 32)
                throw new ArgumentException("Invalid SSID");

            return await ExecuteWithRetry(async () =>
            {
                // 既存プロファイル確認
                var profiles = await GetSavedProfilesAsync(cancellationToken);
                if (profiles.Any(p => p.Equals(ssid, StringComparison.OrdinalIgnoreCase)))
                {
                    return await TryConnectExisting(ssid, cancellationToken);
                }

                // 新規プロファイル作成と接続
                if (!string.IsNullOrEmpty(password))
                {
                    return await CreateProfileAndConnect(ssid, password, cancellationToken);
                }

                return false;
            }, false);
        }

        public async Task<bool> DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (!CheckRateLimit("disconnect"))
            {
                throw new InvalidOperationException("Rate limit exceeded.");
            }

            string command, arguments;
            GetDisconnectCommand(out command, out arguments);

            var output = await ExecuteCommandAsync(command, arguments, cancellationToken);
            ClearCache();

            return output.Contains("successfully", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("disconnected", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<ConnectionStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            const string cacheKey = "connection_status";

            // ステータスは短い間隔でキャッシュ
            if (GetCachedData<ConnectionStatus>(cacheKey) is ConnectionStatus cached)
            {
                return cached;
            }

            string command, arguments;
            GetStatusCommand(out command, out arguments);

            var output = await ExecuteCommandAsync(command, arguments, cancellationToken);
            var status = ParseConnectionStatus(output);

            SetCachedData(cacheKey, status, TimeSpan.FromSeconds(5));
            return status;
        }

        public async Task<List<string>> GetSavedProfilesAsync(CancellationToken cancellationToken = default)
        {
            const string cacheKey = "saved_profiles";

            if (GetCachedData<List<string>>(cacheKey) is List<string> cached)
            {
                return cached;
            }

            string command, arguments;
            GetProfilesCommand(out command, out arguments);

            var output = await ExecuteCommandAsync(command, arguments, cancellationToken);
            var profiles = ParseProfilesOutput(output);

            SetCachedData(cacheKey, profiles, TimeSpan.FromMinutes(1));
            return profiles;
        }

        public async Task<bool> DeleteProfileAsync(string ssid, CancellationToken cancellationToken = default)
        {
            ssid = ssid?.Trim();
            if (string.IsNullOrEmpty(ssid))
                throw new ArgumentException("Invalid SSID");

            string command, arguments;
            GetDeleteProfileCommand(ssid, out command, out arguments);

            var output = await ExecuteCommandAsync(command, arguments, cancellationToken);
            ClearCache();

            return output.Contains("deleted", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("successfully", StringComparison.OrdinalIgnoreCase);
        }

        // プラットフォーム固有コマンド取得
        private void GetScanCommand(out string command, out string arguments)
        {
            switch (_platform)
            {
                case PlatformType.Windows:
                    command = "netsh";
                    arguments = "wlan show networks mode=bssid";
                    break;
                case PlatformType.Linux:
                    command = "nmcli";
                    arguments = "-t -f SSID,SIGNAL,SECURITY,CHAN,FREQ device wifi list";
                    break;
                case PlatformType.MacOS:
                    command = "/System/Library/PrivateFrameworks/Apple80211.framework/Versions/Current/Resources/airport";
                    arguments = "-s";
                    break;
                default:
                    command = "netsh";
                    arguments = "wlan show networks mode=bssid";
                    break;
            }
        }

        private void GetDisconnectCommand(out string command, out string arguments)
        {
            switch (_platform)
            {
                case PlatformType.Windows:
                    command = "netsh";
                    arguments = "wlan disconnect";
                    break;
                case PlatformType.Linux:
                    command = "nmcli";
                    arguments = "device disconnect wlan0";
                    break;
                case PlatformType.MacOS:
                    command = "networksetup";
                    arguments = "-setairportpower en0 off";
                    break;
                default:
                    command = "netsh";
                    arguments = "wlan disconnect";
                    break;
            }
        }

        private void GetStatusCommand(out string command, out string arguments)
        {
            switch (_platform)
            {
                case PlatformType.Windows:
                    command = "netsh";
                    arguments = "wlan show interfaces";
                    break;
                case PlatformType.Linux:
                    command = "nmcli";
                    arguments = "-t -f STATE,CONNECTION general";
                    break;
                case PlatformType.MacOS:
                    command = "/System/Library/PrivateFrameworks/Apple80211.framework/Versions/Current/Resources/airport";
                    arguments = "-I";
                    break;
                default:
                    command = "netsh";
                    arguments = "wlan show interfaces";
                    break;
            }
        }

        private void GetProfilesCommand(out string command, out string arguments)
        {
            switch (_platform)
            {
                case PlatformType.Windows:
                    command = "netsh";
                    arguments = "wlan show profiles";
                    break;
                case PlatformType.Linux:
                    command = "nmcli";
                    arguments = "-t -f NAME connection show";
                    break;
                case PlatformType.MacOS:
                    command = "networksetup";
                    arguments = "-listpreferredwirelessnetworks en0";
                    break;
                default:
                    command = "netsh";
                    arguments = "wlan show profiles";
                    break;
            }
        }

        private void GetDeleteProfileCommand(string ssid, out string command, out string arguments)
        {
            switch (_platform)
            {
                case PlatformType.Windows:
                    command = "netsh";
                    arguments = $"wlan delete profile name=\"{ssid}\"";
                    break;
                case PlatformType.Linux:
                    command = "nmcli";
                    arguments = $"connection delete \"{ssid}\"";
                    break;
                case PlatformType.MacOS:
                    command = "networksetup";
                    arguments = $"-removepreferredwirelessnetwork en0 \"{ssid}\"";
                    break;
                default:
                    command = "netsh";
                    arguments = $"wlan delete profile name=\"{ssid}\"";
                    break;
            }
        }

        // コマンド実行（シンプル版）
        private async Task<string> ExecuteCommandAsync(string command, string arguments, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            cancellationToken.Register(() => process.Kill());

            if (!process.Start())
                throw new InvalidOperationException("Failed to start process");

            await process.WaitForExitAsync(cancellationToken);

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Command failed: {error}");
            }

            return output;
        }

        // パース処理（プラットフォーム統合版）
        private List<NetworkInfo> ParseNetworkScanOutput(string output)
        {
            var networks = new List<NetworkInfo>();

            switch (_platform)
            {
                case PlatformType.Windows:
                    return ParseWindowsNetworks(output);
                case PlatformType.Linux:
                    return ParseLinuxNetworks(output);
                case PlatformType.MacOS:
                    return ParseMacOSNetworks(output);
                default:
                    return ParseWindowsNetworks(output);
            }
        }

        private List<NetworkInfo> ParseWindowsNetworks(string output)
        {
            var networks = new List<NetworkInfo>();
            var lines = output.Split('\n');
            NetworkInfo current = null;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                // SSID行
                if (trimmed.StartsWith("SSID", StringComparison.OrdinalIgnoreCase))
                {
                    if (current != null && !string.IsNullOrEmpty(current.Ssid))
                    {
                        networks.Add(current);
                    }

                    var parts = trimmed.Split(':');
                    if (parts.Length >= 2)
                    {
                        var ssid = string.Join(":", parts.Skip(1)).Trim();
                        if (!string.IsNullOrEmpty(ssid))
                        {
                            current = new NetworkInfo { Ssid = ssid };
                        }
                    }
                }
                else if (current != null)
                {
                    // シグナル
                    if (trimmed.Contains("Signal", StringComparison.OrdinalIgnoreCase))
                    {
                        var match = Regex.Match(trimmed, @"(\d+)%");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out var signal))
                        {
                            current.Signal = signal;
                        }
                    }
                    // 認証方式
                    else if (trimmed.Contains("Authentication", StringComparison.OrdinalIgnoreCase))
                    {
                        current.Security = trimmed.Split(':').Last().Trim();
                    }
                    // バンド
                    else if (trimmed.Contains("Band", StringComparison.OrdinalIgnoreCase))
                    {
                        current.Band = trimmed.Contains("5GHz") ? "5GHz" : "2.4GHz";
                    }
                }
            }

            if (current != null && !string.IsNullOrEmpty(current.Ssid))
            {
                networks.Add(current);
            }

            return networks;
        }

        private List<NetworkInfo> ParseLinuxNetworks(string output)
        {
            var networks = new List<NetworkInfo>();

            foreach (var line in output.Trim().Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split(':');
                if (parts.Length >= 5)
                {
                    var ssid = parts[0] == "--" ? "Hidden Network" : parts[0];
                    var signal = 0;
                    if (int.TryParse(parts[1], out var s)) signal = s;

                    var security = parts[2];
                    var band = "Unknown";

                    // 周波数からバンド判定
                    if (parts[4] != "")
                    {
                        if (double.TryParse(parts[4], out var freq))
                        {
                            band = (freq >= 2400 && freq <= 2500) ? "2.4GHz" :
                                   (freq >= 5000 && freq <= 6000) ? "5GHz" : "Unknown";
                        }
                    }

                    networks.Add(new NetworkInfo
                    {
                        Ssid = ssid,
                        Signal = signal,
                        Security = security,
                        Band = band
                    });
                }
            }

            return networks;
        }

        private List<NetworkInfo> ParseMacOSNetworks(string output)
        {
            var networks = new List<NetworkInfo>();
            var lines = output.Trim().Split('\n');

            // ヘッダー行をスキップ
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = Regex.Split(line, @"\s+");
                if (parts.Length >= 7)
                {
                    networks.Add(new NetworkInfo
                    {
                        Ssid = parts[0],
                        Signal = 80, // 簡易推定
                        Security = line.Contains("WPA") ? "WPA2" : "Open",
                        Band = line.Contains("5GHz") ? "5GHz" : "2.4GHz"
                    });
                }
            }

            return networks;
        }

        private ConnectionStatus ParseConnectionStatus(string output)
        {
            var status = new ConnectionStatus { Status = "Disconnected" };

            switch (_platform)
            {
                case PlatformType.Windows:
                    return ParseWindowsStatus(output);
                case PlatformType.Linux:
                    return ParseLinuxStatus(output);
                case PlatformType.MacOS:
                    return ParseMacOSStatus(output);
                default:
                    return ParseWindowsStatus(output);
            }
        }

        private ConnectionStatus ParseWindowsStatus(string output)
        {
            var status = new ConnectionStatus { Status = "Disconnected" };
            var lines = output.Split('\n');

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                if (trimmed.StartsWith("State", StringComparison.OrdinalIgnoreCase))
                {
                    status.Status = trimmed.Contains("connected", StringComparison.OrdinalIgnoreCase) ? "Connected" : "Disconnected";
                }
                else if (trimmed.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) && !trimmed.Contains("BSSID"))
                {
                    var parts = trimmed.Split(':');
                    if (parts.Length >= 2)
                    {
                        status.Ssid = string.Join(":", parts.Skip(1)).Trim();
                    }
                }
                else if (trimmed.Contains("Signal", StringComparison.OrdinalIgnoreCase))
                {
                    var match = Regex.Match(trimmed, @"(\d+)%");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out var signal))
                    {
                        status.Signal = signal;
                    }
                }
            }

            return status;
        }

        private ConnectionStatus ParseLinuxStatus(string output)
        {
            var status = new ConnectionStatus { Status = "Disconnected" };

            if (output.Contains("connected", StringComparison.OrdinalIgnoreCase))
            {
                status.Status = "Connected";

                // 追加情報を取得（簡易版）
                try
                {
                    var wifiOutput = ExecuteCommandAsync("nmcli", "-t -f SSID,SIGNAL device wifi", CancellationToken.None).Result;
                    var parts = wifiOutput.Trim().Split('\n').FirstOrDefault()?.Split(':');
                    if (parts?.Length >= 2)
                    {
                        status.Ssid = parts[0];
                        if (int.TryParse(parts[1], out var signal))
                        {
                            status.Signal = signal;
                        }
                    }
                }
                catch { /* 無視 */ }
            }

            return status;
        }

        private ConnectionStatus ParseMacOSStatus(string output)
        {
            var status = new ConnectionStatus { Status = "Disconnected" };
            var lines = output.Split('\n');

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                if (trimmed.StartsWith("SSID:", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = trimmed.Split(':');
                    if (parts.Length >= 2)
                    {
                        var ssid = string.Join(":", parts.Skip(1)).Trim();
                        if (!string.IsNullOrEmpty(ssid))
                        {
                            status.Status = "Connected";
                            status.Ssid = ssid;
                        }
                    }
                }
                else if (trimmed.StartsWith("agrCtlRSSI:", StringComparison.OrdinalIgnoreCase))
                {
                    var match = Regex.Match(trimmed, @"(-?\d+)");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out var rssi))
                    {
                        status.Signal = Math.Max(0, Math.Min(100, (rssi + 100) * 2));
                    }
                }
            }

            return status;
        }

        private List<string> ParseProfilesOutput(string output)
        {
            var profiles = new List<string>();

            switch (_platform)
            {
                case PlatformType.Windows:
                    return ParseWindowsProfiles(output);
                case PlatformType.Linux:
                    return ParseLinuxProfiles(output);
                case PlatformType.MacOS:
                    return ParseMacOSProfiles(output);
                default:
                    return ParseWindowsProfiles(output);
            }
        }

        private List<string> ParseWindowsProfiles(string output)
        {
            var profiles = new List<string>();

            foreach (var line in output.Split('\n'))
            {
                if (line.Contains("All User Profile", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(':');
                    if (parts.Length >= 2)
                    {
                        var profile = parts[1].Trim();
                        if (!string.IsNullOrEmpty(profile))
                        {
                            profiles.Add(profile);
                        }
                    }
                }
            }

            return profiles.Distinct(StringComparer.OrdinalIgnoreCase)
                          .OrderBy(p => p)
                          .ToList();
        }

        private List<string> ParseLinuxProfiles(string output)
        {
            return output.Trim()
                        .Split('\n')
                        .Where(line => !string.IsNullOrWhiteSpace(line))
                        .Select(line => line.Trim())
                        .ToList();
        }

        private List<string> ParseMacOSProfiles(string output)
        {
            return output.Trim()
                        .Split('\n')
                        .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("Preferred"))
                        .Select(line => line.Trim())
                        .ToList();
        }

        private async Task<bool> TryConnectExisting(string ssid, CancellationToken cancellationToken)
        {
            string command, arguments;
            GetConnectCommand(ssid, out command, out arguments);

            var output = await ExecuteCommandAsync(command, arguments, cancellationToken);
            ClearCache();

            return output.Contains("successfully", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<bool> CreateProfileAndConnect(string ssid, string password, CancellationToken cancellationToken)
        {
            // プロファイル作成
            if (!await CreateWifiProfile(ssid, password, cancellationToken))
                return false;

            // 接続実行
            return await TryConnectExisting(ssid, cancellationToken);
        }

        private async Task<bool> CreateWifiProfile(string ssid, string password, CancellationToken cancellationToken)
        {
            if (_platform != PlatformType.Windows)
                return true; // Linux/macOSではプロファイル自動作成

            var profileXml = GenerateWifiProfile(ssid, password);
            var tempFile = $"wifi_profile_{ssid.Replace(" ", "_")}.xml";

            try
            {
                await File.WriteAllTextAsync(tempFile, profileXml, cancellationToken);

                string command, arguments;
                GetAddProfileCommand(tempFile, out command, out arguments);

                var output = await ExecuteCommandAsync(command, arguments, cancellationToken);
                return output.Contains("added", StringComparison.OrdinalIgnoreCase) ||
                       output.Contains("updated", StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                try { File.Delete(tempFile); } catch { }
            }
        }

        private void GetConnectCommand(string ssid, out string command, out string arguments)
        {
            switch (_platform)
            {
                case PlatformType.Windows:
                    command = "netsh";
                    arguments = $"wlan connect name=\"{ssid}\"";
                    break;
                case PlatformType.Linux:
                    command = "nmcli";
                    arguments = $"device wifi connect \"{ssid}\"";
                    break;
                case PlatformType.MacOS:
                    command = "networksetup";
                    arguments = $"-setairportnetwork en0 \"{ssid}\"";
                    break;
                default:
                    command = "netsh";
                    arguments = $"wlan connect name=\"{ssid}\"";
                    break;
            }
        }

        private void GetAddProfileCommand(string filename, out string command, out string arguments)
        {
            switch (_platform)
            {
                case PlatformType.Windows:
                    command = "netsh";
                    arguments = $"wlan add profile filename=\"{filename}\" user=all";
                    break;
                default:
                    command = "echo";
                    arguments = "profile created";
                    break;
            }
        }

        private string GenerateWifiProfile(string ssid, string password)
        {
            return $@"<?xml version=""1.0""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
    <name>{ssid}</name>
    <SSIDConfig>
        <SSID>
            <name>{ssid}</name>
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
            <sharedKey>
                <keyType>passPhrase</keyType>
                <protected>false</protected>
                <keyMaterial>{password}</keyMaterial>
            </sharedKey>
        </security>
    </MSM>
</WLANProfile>";
        }

        // キャッシュ操作（シンプル版）
        private T GetCachedData<T>(string key) where T : class
        {
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(key, out var entry))
                {
                    if (entry.expiry > DateTime.UtcNow)
                    {
                        return entry.data as T;
                    }
                    else
                    {
                        _cache.Remove(key);
                    }
                }
                return null;
            }
        }

        private void SetCachedData(string key, object data, TimeSpan duration)
        {
            lock (_cacheLock)
            {
                _cache[key] = (data, DateTime.UtcNow.Add(duration));
            }
        }

        private void ClearCache()
        {
            lock (_cacheLock)
            {
                _cache.Clear();
            }
        }

        // レート制限（シンプル版）
        private bool CheckRateLimit(string operation)
        {
            lock (_lastOperation)
            {
                if (_lastOperation.TryGetValue(operation, out var lastTime))
                {
                    if (DateTime.UtcNow - lastTime < MinOperationInterval)
                    {
                        return false;
                    }
                }
                _lastOperation[operation] = DateTime.UtcNow;
                return true;
            }
        }

        // リトライ処理（シンプル版）
        private async Task<T> ExecuteWithRetry<T>(Func<Task<T>> operation, T defaultValue, int maxRetries = 3)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    return await operation();
                }
                catch
                {
                    if (attempt == maxRetries)
                        return defaultValue;

                    await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt));
                }
            }
            return defaultValue;
        }
    }
}
