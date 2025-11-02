using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core.Linux
{
    /// <summary>
    /// Linuxプラットフォーム用のWiFiマネージャー実装
    /// </summary>
    [SupportedOSPlatform("linux")]
    public class LinuxWifiManager : IWifiManager
    {
        private readonly ProcessExecutor _processExecutor;
        private readonly SemaphoreSlim _operationLock;

        public LinuxWifiManager()
        {
            _processExecutor = new ProcessExecutor();
            _operationLock = new SemaphoreSlim(1, 1);
        }

        /// <summary>
        /// ネットワークに接続する
        /// </summary>
        public async Task<bool> ConnectAsync(string ssid, string password, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(ssid) || string.IsNullOrWhiteSpace(password))
            {
                return false;
            }

            await _operationLock.WaitAsync(ct);
            try
            {
                // Linuxではnmcliコマンドを使用
                var connectCmd = $"device wifi connect \"{ssid}\" password \"{password}\"";
                var result = await _processExecutor.RunAsync("nmcli", connectCmd, 15000);

                return result.Success;
            }
            finally
            {
                _operationLock.Release();
            }
        }

        /// <summary>
        /// 現在のネットワークから切断する
        /// </summary>
        public async Task<bool> DisconnectAsync(CancellationToken ct = default)
        {
            await _operationLock.WaitAsync(ct);
            try
            {
                // Linuxではnmcliコマンドで切断
                var disconnectCmd = "device disconnect wlan0";
                var result = await _processExecutor.RunAsync("nmcli", disconnectCmd, 5000);

                return result.Success;
            }
            finally
            {
                _operationLock.Release();
            }
        }

        /// <summary>
        /// 利用可能なネットワークをスキャンする
        /// </summary>
        public async Task<List<WifiNetwork>> ScanNetworksAsync(CancellationToken ct = default)
        {
            var networks = new List<WifiNetwork>();

            try
            {
                // Linuxではnmcliコマンドでスキャン
                var scanCmd = "device wifi list";
                var result = await _processExecutor.RunAsync("nmcli", scanCmd, 10000);

                if (result.Success && !string.IsNullOrEmpty(result.Output))
                {
                    var lines = result.Output.Split('\n').Skip(1); // ヘッダーをスキップ

                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 4)
                        {
                            var ssid = parts[0];
                            var security = parts[3];

                            networks.Add(new WifiNetwork
                            {
                                Ssid = ssid,
                                SecurityMode = ParseSecurityMode(security),
                                FrequencyBand = WifiFrequencyBand.Band2_4GHz, // デフォルト値
                                SignalStrength = ParseSignalStrength(parts.Length > 5 ? parts[5] : "")
                            });
                        }
                    }
                }
            }
            catch
            {
                // エラーを無視
            }

            return networks;
        }

        /// <summary>
        /// 現在の接続先SSIDを取得する
        /// </summary>
        public async Task<string?> GetCurrentSSIDAsync(CancellationToken ct = default)
        {
            try
            {
                // Linuxではnmcliコマンドで現在の接続を取得
                var currentCmd = "connection show --active";
                var result = await _processExecutor.RunAsync("nmcli", currentCmd, 5000);

                if (result.Success && !string.IsNullOrEmpty(result.Output))
                {
                    var lines = result.Output.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.Contains("wifi") && line.Contains("wlan0"))
                        {
                            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 4)
                            {
                                return parts[3]; // SSIDは4番目のカラム
                            }
                        }
                    }
                }
            }
            catch
            {
                // エラーを無視
            }

            return null;
        }

        /// <summary>
        /// 保存済みプロファイルを取得する
        /// </summary>
        public async Task<List<string>> GetSavedProfilesAsync(CancellationToken ct = default)
        {
            var profiles = new List<string>();

            try
            {
                // Linuxではnmcliコマンドで保存済み接続を取得
                var profilesCmd = "connection show";
                var result = await _processExecutor.RunAsync("nmcli", profilesCmd, 5000);

                if (result.Success && !string.IsNullOrEmpty(result.Output))
                {
                    var lines = result.Output.Split('\n').Skip(1); // ヘッダーをスキップ

                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 4 && parts[2] == "wifi")
                        {
                            profiles.Add(parts[0]);
                        }
                    }
                }
            }
            catch
            {
                // エラーを無視
            }

            return profiles;
        }

        /// <summary>
        /// プロファイルを削除する
        /// </summary>
        public async Task<bool> DeleteProfileAsync(string ssid, CancellationToken ct = default)
        {
            try
            {
                // Linuxではnmcliコマンドで接続を削除
                var deleteCmd = $"connection delete \"{ssid}\"";
                var result = await _processExecutor.RunAsync("nmcli", deleteCmd, 5000);

                return result.Success;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 利用可能なアダプタ情報を取得する
        /// </summary>
        public async Task<Result<IReadOnlyList<WifiAdapterInfo>>> GetAvailableAdaptersAsync(CancellationToken ct = default)
        {
            var adapters = new List<WifiAdapterInfo>();

            try
            {
                // Linuxではnmcliコマンドでデバイス情報を取得
                var devicesCmd = "device";
                var result = await _processExecutor.RunAsync("nmcli", devicesCmd, 5000);

                if (result.Success && !string.IsNullOrEmpty(result.Output))
                {
                    var lines = result.Output.Split('\n').Skip(1); // ヘッダーをスキップ

                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && parts[1] == "wifi")
                        {
                            adapters.Add(new WifiAdapterInfo
                            {
                                Id = parts[0],
                                Name = parts[0],
                                Description = "Wireless Network Adapter",
                                InterfaceType = NetworkInterfaceType.Wireless80211,
                                IsConnected = parts.Length > 3 ? parts[3] == "connected" : false
                            });
                        }
                    }
                }
            }
            catch
            {
                // エラーを無視
            }

            return Result<IReadOnlyList<WifiAdapterInfo>>.Success(adapters.AsReadOnly());
        }

        /// <summary>
        /// 優先アダプタを設定する（Linuxではサポートされていない）
        /// </summary>
        public void SetPreferredAdapter(string? adapterName)
        {
            // Linuxでは優先アダプタ設定はサポートされていない
        }

        /// <summary>
        /// 優先アダプタを取得する（Linuxではサポートされていない）
        /// </summary>
        public string? GetPreferredAdapter()
        {
            // Linuxでは優先アダプタ設定はサポートされていない
            return null;
        }

        /// <summary>
        /// リソースを解放する
        /// </summary>
        public void Dispose()
        {
            _operationLock?.Dispose();
        }

        private WifiSecurityMode ParseSecurityMode(string security)
        {
            return security.ToLower() switch
            {
                "wep" => WifiSecurityMode.Wep,
                "wpa" => WifiSecurityMode.Wpa,
                "wpa2" => WifiSecurityMode.Wpa2,
                "wpa3" => WifiSecurityMode.Wpa3,
                _ => WifiSecurityMode.Open
            };
        }

        private int ParseSignalStrength(string signal)
        {
            if (int.TryParse(signal.Replace("%", ""), out int strength))
            {
                return strength;
            }
            return 0;
        }
    }
}
