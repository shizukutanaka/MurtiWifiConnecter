using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MurtiWifiConnecter.Core;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// VPN接続管理機能を提供するクラス
    /// OpenVPN, WireGuard, IKEv2などのVPNプロトコルをサポート
    /// </summary>
    public static class VpnManager
    {
        private static readonly Dictionary<string, VpnConnection> _activeConnections = new();
        private static readonly object _lockObject = new();

        /// <summary>
        /// 利用可能なVPNプロバイダーを列挙
        /// </summary>
        public static async Task<List<VpnProvider>> GetAvailableProvidersAsync()
        {
            var providers = new List<VpnProvider>();

            // OpenVPNのチェック
            if (await IsOpenVpnAvailableAsync())
            {
                providers.Add(new VpnProvider
                {
                    Name = "OpenVPN",
                    Type = VpnType.OpenVPN,
                    IsAvailable = true,
                    SupportedProtocols = new[] { "UDP", "TCP" }
                });
            }

            // WireGuardのチェック
            if (await IsWireGuardAvailableAsync())
            {
                providers.Add(new VpnProvider
                {
                    Name = "WireGuard",
                    Type = VpnType.WireGuard,
                    IsAvailable = true,
                    SupportedProtocols = new[] { "UDP" }
                });
            }

            // IKEv2/IPsecのチェック（Windows組み込み）
            if (OperatingSystem.IsWindows())
            {
                providers.Add(new VpnProvider
                {
                    Name = "Windows IKEv2",
                    Type = VpnType.IKEv2,
                    IsAvailable = true,
                    SupportedProtocols = new[] { "IKEv2" }
                });
            }

            // SSTPのチェック（Windows）
            if (OperatingSystem.IsWindows())
            {
                providers.Add(new VpnProvider
                {
                    Name = "Windows SSTP",
                    Type = VpnType.SSTP,
                    IsAvailable = true,
                    SupportedProtocols = new[] { "SSTP" }
                });
            }

            return providers;
        }

        /// <summary>
        /// VPN接続を確立
        /// </summary>
        public static async Task<VpnConnectionResult> ConnectAsync(VpnConnectionProfile profile, CancellationToken ct = default)
        {
            var result = new VpnConnectionResult { Profile = profile };

            try
            {
                await Logger.LogInfo($"VPN接続を開始: {profile.Name} ({profile.Provider})",
                    nameof(VpnManager), new Dictionary<string, object>
                    {
                        ["profileName"] = profile.Name,
                        ["provider"] = profile.Provider.ToString(),
                        ["server"] = profile.Server
                    });

                switch (profile.Provider)
                {
                    case VpnType.OpenVPN:
                        result = await ConnectOpenVpnAsync(profile, ct);
                        break;
                    case VpnType.WireGuard:
                        result = await ConnectWireGuardAsync(profile, ct);
                        break;
                    case VpnType.IKEv2:
                        result = await ConnectIkev2Async(profile, ct);
                        break;
                    case VpnType.SSTP:
                        result = await ConnectSstpAsync(profile, ct);
                        break;
                    default:
                        result.Success = false;
                        result.ErrorMessage = $"未対応のVPNタイプ: {profile.Provider}";
                        break;
                }

                if (result.Success)
                {
                    lock (_lockObject)
                    {
                        _activeConnections[profile.Id] = new VpnConnection
                        {
                            Profile = profile,
                            ConnectedAt = DateTime.Now,
                            Status = VpnConnectionStatus.Connected
                        };
                    }

                    await Logger.LogInfo($"VPN接続成功: {profile.Name}",
                        nameof(VpnManager), new Dictionary<string, object>
                        {
                            ["profileName"] = profile.Name,
                            ["connectionId"] = result.ConnectionId
                        });
                }
                else
                {
                    await Logger.LogError($"VPN接続失敗: {profile.Name} - {result.ErrorMessage}",
                        nameof(VpnManager), new Dictionary<string, object>
                        {
                            ["profileName"] = profile.Name,
                            ["error"] = result.ErrorMessage
                        });
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                await Logger.LogError($"VPN接続中に例外発生: {profile.Name}",
                    nameof(VpnManager), null, ex);
            }

            return result;
        }

        /// <summary>
        /// VPN接続を切断
        /// </summary>
        public static async Task<bool> DisconnectAsync(string connectionId, CancellationToken ct = default)
        {
            VpnConnection connection;
            lock (_lockObject)
            {
                if (!_activeConnections.TryGetValue(connectionId, out connection))
                {
                    return false;
                }
            }

            try
            {
                await Logger.LogInfo($"VPN切断を開始: {connection.Profile.Name}",
                    nameof(VpnManager), new Dictionary<string, object>
                    {
                        ["profileName"] = connection.Profile.Name,
                        ["connectionId"] = connectionId
                    });

                var success = connection.Profile.Provider switch
                {
                    VpnType.OpenVPN => await DisconnectOpenVpnAsync(connectionId, ct),
                    VpnType.WireGuard => await DisconnectWireGuardAsync(connectionId, ct),
                    VpnType.IKEv2 => await DisconnectIkev2Async(connectionId, ct),
                    VpnType.SSTP => await DisconnectSstpAsync(connectionId, ct),
                    _ => false
                };

                if (success)
                {
                    lock (_lockObject)
                    {
                        _activeConnections.Remove(connectionId);
                    }

                    await Logger.LogInfo($"VPN切断成功: {connection.Profile.Name}",
                        nameof(VpnManager), new Dictionary<string, object>
                        {
                            ["profileName"] = connection.Profile.Name,
                            ["connectionId"] = connectionId,
                            ["duration"] = DateTime.Now - connection.ConnectedAt
                        });
                }

                return success;
            }
            catch (Exception ex)
            {
                await Logger.LogError($"VPN切断中に例外発生: {connection.Profile.Name}",
                    nameof(VpnManager), null, ex);
                return false;
            }
        }

        /// <summary>
        /// アクティブなVPN接続を取得
        /// </summary>
        public static List<VpnConnection> GetActiveConnections()
        {
            lock (_lockObject)
            {
                return _activeConnections.Values.ToList();
            }
        }

        /// <summary>
        /// VPN接続のステータスを取得
        /// </summary>
        public static async Task<VpnConnectionStatus> GetConnectionStatusAsync(string connectionId)
        {
            lock (_lockObject)
            {
                if (!_activeConnections.TryGetValue(connectionId, out var connection))
                {
                    return VpnConnectionStatus.Disconnected;
                }
                return connection.Status;
            }
        }

        /// <summary>
        /// VPN設定を保存
        /// </summary>
        public static async Task<bool> SaveProfileAsync(VpnConnectionProfile profile)
        {
            try
            {
                var profiles = await LoadProfilesAsync();
                profiles[profile.Id] = profile;

                var json = System.Text.Json.JsonSerializer.Serialize(profiles, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

                var configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MurtiWifiConnecter",
                    "vpn_profiles.json");

                Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
                await SecurityManager.EnsureSecureDirectoryAclAsync(Path.GetDirectoryName(configPath)!);
                await File.WriteAllTextAsync(configPath, json);
                await SecurityManager.EnsureSecureFileAclAsync(configPath);

                return true;
            }
            catch (Exception ex)
            {
                await Logger.LogError($"VPNプロファイル保存失敗: {ex.Message}", nameof(VpnManager), null, ex);
                return false;
            }
        }

        /// <summary>
        /// VPN設定を読み込み
        /// </summary>
        public static async Task<Dictionary<string, VpnConnectionProfile>> LoadProfilesAsync()
        {
            try
            {
                var configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MurtiWifiConnecter",
                    "vpn_profiles.json");

                if (!File.Exists(configPath))
                {
                    return new Dictionary<string, VpnConnectionProfile>();
                }

                var json = await File.ReadAllTextAsync(configPath);
                var profiles = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, VpnConnectionProfile>>(json);

                return profiles ?? new Dictionary<string, VpnConnectionProfile>();
            }
            catch (Exception ex)
            {
                await Logger.LogError($"VPNプロファイル読み込み失敗: {ex.Message}", nameof(VpnManager), null, ex);
                return new Dictionary<string, VpnConnectionProfile>();
            }
        }

        /// <summary>
        /// VPN速度テスト
        /// </summary>
        public static async Task<VpnSpeedTestResult> TestVpnSpeedAsync(string connectionId, CancellationToken ct = default)
        {
            var result = new VpnSpeedTestResult { ConnectionId = connectionId };

            try
            {
                // VPN接続中のみテスト可能
                var status = await GetConnectionStatusAsync(connectionId);
                if (status != VpnConnectionStatus.Connected)
                {
                    result.Success = false;
                    result.ErrorMessage = "VPN接続が確立されていません";
                    return result;
                }

                // 速度テスト実行
                var speedTest = new EnhancedSpeedTest();
                var speedResult = await speedTest.PerformSpeedTestAsync(ct);

                result.Success = speedResult.Success;
                result.DownloadSpeed = speedResult.DownloadSpeed;
                result.UploadSpeed = speedResult.UploadSpeed;
                result.Latency = speedResult.Latency;
                result.Timestamp = DateTime.Now;

                if (!result.Success)
                {
                    result.ErrorMessage = speedResult.Message;
                }

                await Logger.LogInfo($"VPN速度テスト完了: {result.DownloadSpeed:F2} Mbps DL, {result.UploadSpeed:F2} Mbps UL",
                    nameof(VpnManager), new Dictionary<string, object>
                    {
                        ["connectionId"] = connectionId,
                        ["downloadSpeed"] = result.DownloadSpeed,
                        ["uploadSpeed"] = result.UploadSpeed,
                        ["latency"] = result.Latency
                    });
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                await Logger.LogError($"VPN速度テスト失敗: {ex.Message}", nameof(VpnManager), null, ex);
            }

            return result;
        }

        // 個別VPNタイプの実装（スタブ実装）
        private static async Task<VpnConnectionResult> ConnectOpenVpnAsync(VpnConnectionProfile profile, CancellationToken ct)
        {
            // OpenVPN接続の実装（実際の環境に合わせて調整）
            // この部分は実際のOpenVPNクライアントとの統合が必要
            await Task.Delay(1000, ct); // シミュレーション

            return new VpnConnectionResult
            {
                Profile = profile,
                Success = true,
                ConnectionId = Guid.NewGuid().ToString(),
                ConnectedAt = DateTime.Now
            };
        }

        private static async Task<VpnConnectionResult> ConnectWireGuardAsync(VpnConnectionProfile profile, CancellationToken ct)
        {
            // WireGuard接続の実装
            await Task.Delay(800, ct); // シミュレーション

            return new VpnConnectionResult
            {
                Profile = profile,
                Success = true,
                ConnectionId = Guid.NewGuid().ToString(),
                ConnectedAt = DateTime.Now
            };
        }

        private static async Task<VpnConnectionResult> ConnectIkev2Async(VpnConnectionProfile profile, CancellationToken ct)
        {
            // IKEv2接続の実装（Windowsのrasphone.exeを使用）
            if (!OperatingSystem.IsWindows())
            {
                return new VpnConnectionResult
                {
                    Profile = profile,
                    Success = false,
                    ErrorMessage = "IKEv2はWindowsでのみサポートされます"
                };
            }

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "rasphone.exe",
                        Arguments = $"-d \"{profile.Name}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync(ct);

                return new VpnConnectionResult
                {
                    Profile = profile,
                    Success = process.ExitCode == 0,
                    ConnectionId = Guid.NewGuid().ToString(),
                    ConnectedAt = DateTime.Now,
                    ErrorMessage = process.ExitCode != 0 ? "IKEv2接続に失敗しました" : null
                };
            }
            catch (Exception ex)
            {
                return new VpnConnectionResult
                {
                    Profile = profile,
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private static async Task<VpnConnectionResult> ConnectSstpAsync(VpnConnectionProfile profile, CancellationToken ct)
        {
            // SSTP接続の実装（Windowsのrasphone.exeを使用）
            if (!OperatingSystem.IsWindows())
            {
                return new VpnConnectionResult
                {
                    Profile = profile,
                    Success = false,
                    ErrorMessage = "SSTPはWindowsでのみサポートされます"
                };
            }

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "rasphone.exe",
                        Arguments = $"-d \"{profile.Name}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync(ct);

                return new VpnConnectionResult
                {
                    Profile = profile,
                    Success = process.ExitCode == 0,
                    ConnectionId = Guid.NewGuid().ToString(),
                    ConnectedAt = DateTime.Now,
                    ErrorMessage = process.ExitCode != 0 ? "SSTP接続に失敗しました" : null
                };
            }
            catch (Exception ex)
            {
                return new VpnConnectionResult
                {
                    Profile = profile,
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        // 切断メソッドの実装も同様に...

        private static async Task<bool> DisconnectOpenVpnAsync(string connectionId, CancellationToken ct)
        {
            await Task.Delay(500, ct);
            return true;
        }

        private static async Task<bool> DisconnectWireGuardAsync(string connectionId, CancellationToken ct)
        {
            await Task.Delay(400, ct);
            return true;
        }

        private static async Task<bool> DisconnectIkev2Async(string connectionId, CancellationToken ct)
        {
            if (!OperatingSystem.IsWindows()) return false;

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "rasphone.exe",
                        Arguments = "-h",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync(ct);
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> DisconnectSstpAsync(string connectionId, CancellationToken ct)
        {
            // SSTP切断もIKEv2と同じ
            return await DisconnectIkev2Async(connectionId, ct);
        }

        // 利用可能性チェック
        private static async Task<bool> IsOpenVpnAvailableAsync()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = OperatingSystem.IsWindows() ? "openvpn.exe" : "openvpn",
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> IsWireGuardAvailableAsync()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = OperatingSystem.IsWindows() ? "wg.exe" : "wg",
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }

    // VPN関連のデータ構造
    public enum VpnType
    {
        OpenVPN,
        WireGuard,
        IKEv2,
        SSTP,
        PPTP,
        L2TP
    }

    public enum VpnConnectionStatus
    {
        Disconnected,
        Connecting,
        Connected,
        Disconnecting,
        Error
    }

    public class VpnProvider
    {
        public string Name { get; set; } = string.Empty;
        public VpnType Type { get; set; }
        public bool IsAvailable { get; set; }
        public string[] SupportedProtocols { get; set; } = Array.Empty<string>();
    }

    public class VpnConnectionProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public VpnType Provider { get; set; }
        public string Server { get; set; } = string.Empty;
        public int Port { get; set; } = 1194;
        public string Protocol { get; set; } = "UDP";
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ConfigFile { get; set; } = string.Empty; // OpenVPN用
        public Dictionary<string, string> AdvancedSettings { get; set; } = new();
    }

    public class VpnConnection
    {
        public VpnConnectionProfile Profile { get; set; } = new();
        public DateTime ConnectedAt { get; set; }
        public VpnConnectionStatus Status { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class VpnConnectionResult
    {
        public VpnConnectionProfile Profile { get; set; } = new();
        public bool Success { get; set; }
        public string? ConnectionId { get; set; }
        public DateTime ConnectedAt { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class VpnSpeedTestResult
    {
        public string ConnectionId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public double DownloadSpeed { get; set; }
        public double UploadSpeed { get; set; }
        public double Latency { get; set; }
        public DateTime Timestamp { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
