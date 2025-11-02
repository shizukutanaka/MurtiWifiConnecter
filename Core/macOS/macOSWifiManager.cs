using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core.macOS
{
    /// <summary>
    /// macOSプラットフォーム用のWiFiマネージャー実装
    /// </summary>
    [SupportedOSPlatform("macos")]
    public class macOSWifiManager : IWifiManager
    {
        private readonly ProcessExecutor _processExecutor;
        private readonly SemaphoreSlim _operationLock;

        public macOSWifiManager()
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
                // macOSではairportコマンドまたはnetworksetupコマンドを使用
                var connectCmd = $"-setairportnetwork en0 \"{ssid}\" \"{password}\"";
                var result = await _processExecutor.RunAsync("/System/Library/PrivateFrameworks/Apple80211.framework/Versions/Current/Resources/airport", connectCmd, 10000);

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
                // macOSではairportコマンドで切断
                var disconnectCmd = "-disassociate";
                var result = await _processExecutor.RunAsync("/System/Library/PrivateFrameworks/Apple80211.framework/Versions/Current/Resources/airport", disconnectCmd, 5000);

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
                // macOSではairportコマンドでスキャン
                var scanCmd = "-s";
                var result = await _processExecutor.RunAsync("/System/Library/PrivateFrameworks/Apple80211.framework/Versions/Current/Resources/airport", scanCmd, 10000);

                if (result.Success && !string.IsNullOrEmpty(result.Output))
                {
                    var lines = result.Output.Split('\n').Skip(1); // ヘッダーをスキップ

                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            var ssid = parts[0];
                            var security = parts[1];

                            networks.Add(new WifiNetwork
                            {
                                Ssid = ssid,
                                SecurityMode = ParseSecurityMode(security),
                                FrequencyBand = WifiFrequencyBand.Band2_4GHz // デフォルト値
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
                // macOSではairportコマンドで現在のSSIDを取得
                var currentCmd = "-I";
                var result = await _processExecutor.RunAsync("/System/Library/PrivateFrameworks/Apple80211.framework/Versions/Current/Resources/airport", currentCmd, 5000);

                if (result.Success && !string.IsNullOrEmpty(result.Output))
                {
                    var lines = result.Output.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.Contains("SSID:"))
                        {
                            var ssid = line.Substring(line.IndexOf(':') + 1).Trim();
                            return ssid;
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
        /// 保存済みプロファイルを取得する（macOSではサポートされていない）
        /// </summary>
        public async Task<List<string>> GetSavedProfilesAsync(CancellationToken ct = default)
        {
            // macOSではネットワーク設定はシステムレベルで管理され、プロファイル一覧は公開されていない
            return new List<string>();
        }

        /// <summary>
        /// プロファイルを削除する（macOSではサポートされていない）
        /// </summary>
        public async Task<bool> DeleteProfileAsync(string ssid, CancellationToken ct = default)
        {
            // macOSではプロファイル削除はサポートされていない
            return false;
        }

        /// <summary>
        /// 利用可能なアダプタ情報を取得する
        /// </summary>
        public async Task<Result<IReadOnlyList<WifiAdapterInfo>>> GetAvailableAdaptersAsync(CancellationToken ct = default)
        {
            var adapters = new List<WifiAdapterInfo>();

            try
            {
                // macOSではnetworksetupコマンドでインターフェース情報を取得
                var result = await _processExecutor.RunAsync("networksetup", "-listallhardwareports", 5000);

                if (result.Success && !string.IsNullOrEmpty(result.Output))
                {
                    var lines = result.Output.Split('\n');

                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].Contains("Hardware Port: Wi-Fi") && i + 1 < lines.Length)
                        {
                            var deviceName = lines[i + 1].Replace("Device: ", "").Trim();

                            adapters.Add(new WifiAdapterInfo
                            {
                                Id = deviceName,
                                Name = "Wi-Fi",
                                Description = "Wireless Network Adapter",
                                InterfaceType = NetworkInterfaceType.Wireless80211,
                                IsConnected = true
                            });
                            break;
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
        /// 優先アダプタを設定する（macOSではサポートされていない）
        /// </summary>
        public void SetPreferredAdapter(string? adapterName)
        {
            // macOSでは優先アダプタ設定はサポートされていない
        }

        /// <summary>
        /// 優先アダプタを取得する（macOSではサポートされていない）
        /// </summary>
        public string? GetPreferredAdapter()
        {
            // macOSでは優先アダプタ設定はサポートされていない
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
                "none" => WifiSecurityMode.Open,
                "wep" => WifiSecurityMode.Wep,
                "wpa" => WifiSecurityMode.Wpa,
                "wpa2" => WifiSecurityMode.Wpa2,
                "wpa3" => WifiSecurityMode.Wpa3,
                "wpa2 enterprise" => WifiSecurityMode.Wpa2Enterprise,
                "wpa3 enterprise" => WifiSecurityMode.Wpa3Enterprise,
                _ => WifiSecurityMode.Open
            };
        }
    }
}
