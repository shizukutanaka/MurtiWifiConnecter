using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// 最適化されたWiFiスキャナー
    /// </summary>
    public static class OptimizedWifiScanner
    {
        private static readonly SemaphoreSlim _scanLock = new(1, 1);
        private static DateTime _lastScanTime = DateTime.MinValue;
        private static List<WifiNetwork>? _cachedNetworks;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(5);
        
        /// <summary>
        /// WiFiネットワークをスキャン（最適化版）
        /// </summary>
        public static async Task<List<WifiNetwork>> ScanNetworksAsync(
            ConnectionHistory? connectionHistory = null,
            CancellationToken cancellationToken = default)
        {
            // キャッシュが有効な場合は返す
            if (_cachedNetworks != null && 
                DateTime.Now - _lastScanTime < CacheDuration)
            {
                return new List<WifiNetwork>(_cachedNetworks);
            }
            
            if (!await _scanLock.WaitAsync(100, cancellationToken))
            {
                // 他のスキャンが実行中の場合はキャッシュを返す
                return _cachedNetworks ?? new List<WifiNetwork>();
            }
            
            try
            {
                var networks = new List<WifiNetwork>();
                
                // netsh wlan show networksコマンドを使用（軽量）
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = "wlan show networks mode=bssid",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = System.Text.Encoding.GetEncoding(932) // 日本語対応
                    }
                };
                
                process.Start();
                
                // タイムアウト設定
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var timeoutTask = Task.Delay(3000, cancellationToken);
                
                var completedTask = await Task.WhenAny(outputTask, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    try { process.Kill(); } catch { }
                    return _cachedNetworks ?? new List<WifiNetwork>();
                }
                
                var output = await outputTask;
                process.WaitForExit(1000);
                
                // 出力を解析
                networks = ParseNetshOutput(output, connectionHistory);
                
                // 現在の接続を確認
                var currentSSID = await GetCurrentSSIDAsync(cancellationToken);
                if (!string.IsNullOrEmpty(currentSSID))
                {
                    var connectedNetwork = networks.FirstOrDefault(n => 
                        string.Equals(n.SSID, currentSSID, StringComparison.OrdinalIgnoreCase));
                    if (connectedNetwork != null)
                    {
                        connectedNetwork.IsConnected = true;
                    }
                }
                
                // ソート: 接続中 -> 履歴あり -> 信号強度
                networks = networks
                    .OrderByDescending(n => n.IsConnected)
                    .ThenByDescending(n => n.HasConnectedBefore)
                    .ThenByDescending(n => n.SignalStrength)
                    .ThenBy(n => n.SSID)
                    .ToList();
                
                // キャッシュを更新
                _cachedNetworks = networks;
                _lastScanTime = DateTime.Now;
                
                return networks;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("OptimizedWifiScanner.ScanNetworks", ex);
                return _cachedNetworks ?? new List<WifiNetwork>();
            }
            finally
            {
                _scanLock.Release();
            }
        }
        
        private static List<WifiNetwork> ParseNetshOutput(string output, ConnectionHistory? connectionHistory)
        {
            var networks = new Dictionary<string, WifiNetwork>();
            var lines = output.Split('\n');
            
            string? currentSSID = null;
            int currentSignal = 0;
            
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                
                if (trimmed.StartsWith("SSID"))
                {
                    var parts = trimmed.Split(':');
                    if (parts.Length >= 2)
                    {
                        var ssid = string.Join(":", parts.Skip(1)).Trim();
                        if (!string.IsNullOrWhiteSpace(ssid) && ssid.Length <= 32)
                        {
                            currentSSID = ssid;
                        }
                    }
                }
                else if (trimmed.Contains("Signal") && trimmed.Contains("%"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(trimmed, @"(\d+)%");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out var signal))
                    {
                        currentSignal = signal;
                        
                        if (!string.IsNullOrEmpty(currentSSID) && !networks.ContainsKey(currentSSID))
                        {
                            var hasHistory = connectionHistory?.HasConnectedTo(currentSSID) ?? false;
                            
                            networks[currentSSID] = new WifiNetwork
                            {
                                SSID = currentSSID,
                                SignalStrength = currentSignal,
                                IsConnected = false,
                                HasConnectedBefore = hasHistory,
                                SignalQuality = GetSignalQuality(currentSignal)
                            };
                        }
                    }
                }
            }
            
            return networks.Values.ToList();
        }
        
        private static async Task<string?> GetCurrentSSIDAsync(CancellationToken cancellationToken)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = "wlan show interfaces",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var timeoutTask = Task.Delay(2000, cancellationToken);
                
                var completedTask = await Task.WhenAny(outputTask, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    try { process.Kill(); } catch { }
                    return null;
                }
                
                var output = await outputTask;
                process.WaitForExit(500);
                
                // SSID行を探す
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("SSID") && trimmed.Contains(":"))
                    {
                        var parts = trimmed.Split(':');
                        if (parts.Length >= 2)
                        {
                            var ssid = string.Join(":", parts.Skip(1)).Trim();
                            if (!string.IsNullOrWhiteSpace(ssid))
                            {
                                return ssid;
                            }
                        }
                    }
                }
            }
            catch
            {
                // エラーは無視
            }
            
            return null;
        }
        
        private static string GetSignalQuality(int signalStrength)
        {
            return signalStrength switch
            {
                >= 80 => "優秀",
                >= 60 => "良好",
                >= 40 => "普通",
                >= 20 => "弱い",
                _ => "非常に弱い"
            };
        }
        
        /// <summary>
        /// キャッシュをクリア
        /// </summary>
        public static void ClearCache()
        {
            _cachedNetworks = null;
            _lastScanTime = DateTime.MinValue;
        }
    }
}