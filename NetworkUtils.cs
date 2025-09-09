using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    public class NetworkCommandResult
    {
        public bool Success { get; set; }
        public string Output { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public int ExitCode { get; set; }
    }

    public static class NetworkUtils
    {
        // 統合定数クラスを使用

        // 統合された高機能コマンド実行メソッド
        public static async Task<NetworkCommandResult> ExecuteAdvancedCommandAsync(string fileName, string arguments, int timeoutMs = 10000, CancellationToken cancellationToken = default)
        {
            using var proc = new Process();
            try
            {
                proc.StartInfo = new ProcessStartInfo(fileName, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };

                if (!proc.Start()) 
                    return new NetworkCommandResult { Success = false, ErrorMessage = "プロセスの開始に失敗しました" };

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeoutMs);

                var outputTask = proc.StandardOutput.ReadToEndAsync();
                var errorTask = proc.StandardError.ReadToEndAsync();

                await proc.WaitForExitAsync(cts.Token);
                
                if (cts.Token.IsCancellationRequested)
                {
                    try { proc.Kill(); } catch { }
                    return new NetworkCommandResult { Success = false, ErrorMessage = "タイムアウトが発生しました" };
                }

                var output = await outputTask;
                var error = await errorTask;
                var success = proc.ExitCode == 0 && string.IsNullOrWhiteSpace(error);

                return new NetworkCommandResult 
                { 
                    Success = success,
                    Output = output,
                    ErrorMessage = success ? null : error,
                    ExitCode = proc.ExitCode
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                try { proc.Kill(); } catch { }
                return new NetworkCommandResult { Success = false, ErrorMessage = "操作がキャンセルされました" };
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(); } catch { }
                return new NetworkCommandResult { Success = false, ErrorMessage = "コマンドがタイムアウトしました" };
            }
            catch (Exception ex)
            {
                return new NetworkCommandResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        public static async Task<NetworkCommandResult> ExecuteNetshCommandAsync(string arguments, int? timeoutMs = null, CancellationToken cancellationToken = default)
        {
            return await ExecuteAdvancedCommandAsync("netsh", arguments, timeoutMs ?? QuickSettingsManager.Constants.NormalTimeoutMs, cancellationToken);
        }

        public static async Task<string> ExecuteCommandAsync(string fileName, string arguments, int timeoutMs = 10000, CancellationToken cancellationToken = default)
        {
            using var proc = new Process();
            try
            {
                proc.StartInfo = new ProcessStartInfo(fileName, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                if (!proc.Start()) return string.Empty;

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeoutMs);

                var outputTask = proc.StandardOutput.ReadToEndAsync();
                var processTask = proc.WaitForExitAsync(cts.Token);

                await processTask;
                
                if (cts.Token.IsCancellationRequested)
                {
                    try { proc.Kill(); } catch { }
                    return string.Empty;
                }

                if (proc.ExitCode != 0) return string.Empty;
                return await outputTask;
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(); } catch { }
                return string.Empty;
            }
            catch
            {
                try { proc.Kill(); } catch { }
                return string.Empty;
            }
        }

        public static async Task<bool> ExecuteNetshCommandWithResultAsync(string arguments, int timeoutMs = 10000, CancellationToken cancellationToken = default)
        {
            return await ExecuteCommandWithResultAsync("netsh", arguments, timeoutMs, cancellationToken);
        }

        public static async Task<bool> ExecuteCommandWithResultAsync(string fileName, string arguments, int timeoutMs = 10000, CancellationToken cancellationToken = default)
        {
            using var proc = new Process();
            try
            {
                proc.StartInfo = new ProcessStartInfo(fileName, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                if (!proc.Start()) return false;

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeoutMs);

                await proc.WaitForExitAsync(cts.Token);
                
                if (cts.Token.IsCancellationRequested)
                {
                    try { proc.Kill(); } catch { }
                    return false;
                }

                return proc.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(); } catch { }
                return false;
            }
            catch
            {
                try { proc.Kill(); } catch { }
                return false;
            }
        }

        public static async Task<bool> ConnectWithRetryAsync(string ssid, string password, int maxRetries = 3, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(ssid)) return false;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                if (cancellationToken.IsCancellationRequested) return false;

                var attemptTimeout = attempt == 1 ? QuickSettingsManager.Constants.QuickTimeoutMs : QuickSettingsManager.Constants.NormalTimeoutMs;

                try
                {
                    // 既存プロファイルで接続試行
                    var success = await ExecuteNetshCommandWithResultAsync(
                        $"wlan connect name=\"{SanitizeSSID(ssid)}\"",
                        attemptTimeout,
                        cancellationToken);

                    if (success)
                    {
                        // 接続成功後、実際に接続されたか確認
                        await Task.Delay(QuickSettingsManager.Constants.NetworkResetDelayMs, cancellationToken);
                        var connectedSsid = await GetCurrentConnectedSSIDAsync(cancellationToken);
                        if (!string.IsNullOrEmpty(connectedSsid) &&
                            connectedSsid.Equals(ssid, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }

                    // パスワードが提供されていて、プロファイルが存在しない場合は作成
                    if (!string.IsNullOrEmpty(password) && attempt == 1)
                    {
                        await CreateAndAddProfileAsync(ssid, password, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
                catch
                {
                    // エラーを無視して次の試行へ
                }

                // 最後の試行でない場合は待機
                if (attempt < maxRetries)
                {
                    await Task.Delay(QuickSettingsManager.Constants.NetworkResetDelayMs, cancellationToken);
                }
            }

            return false;
        }

        public static async Task<string> GetCurrentConnectedSSIDAsync(CancellationToken cancellationToken = default)
        {
            return await ErrorHandler.ExecuteWithRetryAsync(async () =>
            {
                var result = await ExecuteNetshCommandAsync("wlan show interfaces", 3000, cancellationToken);
                if (!result.Success || string.IsNullOrEmpty(result.Output)) 
                    return null;
                
                var output = result.Output;

                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    if (trimmedLine.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) &&
                        !trimmedLine.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
                    {
                        var colonIndex = trimmedLine.IndexOf(':');
                        if (colonIndex > 0 && colonIndex < trimmedLine.Length - 1)
                        {
                            var ssid = trimmedLine.Substring(colonIndex + 1).Trim();
                            return string.IsNullOrWhiteSpace(ssid) ? null : ssid;
                        }
                    }
                }
                return null;
            }, 
            maxRetries: 2,
            context: "NetworkUtils.GetCurrentSSID",
            cancellationToken: cancellationToken) ?? await ErrorHandler.SafeExecute(
                () => null as string, 
                null, 
                "NetworkUtils.GetCurrentSSID.Fallback");
        }

        private static async Task<bool> CreateAndAddProfileAsync(string ssid, string password, CancellationToken cancellationToken = default)
        {
            try
            {
                string safePassword = System.Security.SecurityElement.Escape(password);
                string safeSsid = System.Security.SecurityElement.Escape(ssid);

                string profileXml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
    <name>{safeSsid}</name>
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
            <sharedKey>
                <keyType>passPhrase</keyType>
                <protected>false</protected>
                <keyMaterial>{safePassword}</keyMaterial>
            </sharedKey>
        </security>
    </MSM>
</WLANProfile>";

                var tempPath = Path.Combine(Path.GetTempPath(), $"wifi_{CreateSafeFileName(ssid)}_{Guid.NewGuid():N}.xml");
                await File.WriteAllTextAsync(tempPath, profileXml, cancellationToken);

                var success = await ExecuteNetshCommandWithResultAsync(
                    $"wlan add profile filename=\"{tempPath}\" user=current",
                    10000,
                    cancellationToken);

                try { File.Delete(tempPath); } catch { }

                return success;
            }
            catch (OperationCanceledException)
            {
                // キャンセル時は失敗として処理
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                // 権限不足でプロファイル作成できない
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                // 一時ディレクトリが見つからない
                return false;
            }
            catch (IOException)
            {
                // ファイルI/Oエラー（権限不足、ディスク容量不足等）
                return false;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // netshコマンドが見つからないか実行できない
                return false;
            }
            catch (Exception)
            {
                // その他の予期しないエラー
                return false;
            }
        }

        public static string SanitizeSSID(string ssid)
        {
            if (string.IsNullOrWhiteSpace(ssid)) return string.Empty;

            // 長さ制限（WiFi SSID最大32バイト）
            if (ssid.Length > 32) return string.Empty;

            // 制御文字を除去
            return System.Text.RegularExpressions.Regex.Replace(ssid, @"[\x00-\x1F\x7F]", "");
        }

        public static string CreateSafeFileName(string input, int maxLength = 20)
        {
            if (string.IsNullOrWhiteSpace(input)) return "default";

            var safe = string.Join("", input.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'));
            return safe.Length > maxLength ? safe.Substring(0, maxLength) : safe;
        }

        public static bool IsValidNetworkName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) &&
                   name.Length <= 32 &&
                   !name.Any(c => char.IsControl(c));
        }

        public static int CalculateSignalStrength(int rssi)
        {
            // RSSI を品質パーセンテージに変換
            // -30dBm = 100%, -90dBm = 0% の線形変換
            return (int)Math.Max(0, Math.Min(100, 2 * (rssi + 100)));
        }

        public static string FormatSignalStrength(int signalStrength)
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

        public static TimeSpan GetOptimalScanInterval(int networkCount)
        {
            // ネットワーク数に基づいて最適なスキャン間隔を計算
            return networkCount switch
            {
                < 5 => TimeSpan.FromSeconds(10),
                < 15 => TimeSpan.FromSeconds(15),
                < 30 => TimeSpan.FromSeconds(20),
                _ => TimeSpan.FromSeconds(30)
            };
        }

        
        // WiFiネットワークスキャン（軽量版）
        public static async Task<Dictionary<string, int>> ScanWifiNetworksAsync(CancellationToken cancellationToken = default)
        {
            var networks = new Dictionary<string, int>(20);
            
            try
            {
                // タイムアウトを更に短縮（軽量化）
                var result = await ExecuteNetshCommandAsync("wlan show interfaces", 2000, cancellationToken);
                if (!result.Success) return networks;
                
                // 簡易スキャン - 現在接続中のネットワークの信号強度のみ取得
                var lines = result.Output.Split('\n');
                string? currentSSID = null;
                
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("SSID") && trimmed.Contains(":"))
                    {
                        var colonIndex = trimmed.IndexOf(':');
                        if (colonIndex > 0 && colonIndex < trimmed.Length - 1)
                        {
                            currentSSID = trimmed.Substring(colonIndex + 1).Trim();
                        }
                    }
                    else if (trimmed.StartsWith("Signal") && !string.IsNullOrEmpty(currentSSID))
                    {
                        var signalMatch = System.Text.RegularExpressions.Regex.Match(trimmed, @"(\d+)%");
                        if (signalMatch.Success && int.TryParse(signalMatch.Groups[1].Value, out var signal))
                        {
                            networks[currentSSID] = signal;
                        }
                        break; // 現在の接続のみで十分（軽量化）
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("NetworkUtils.ScanWifiNetworks", ex);
            }
            
            return networks;
        }

        public static string GetSsidFromBss(object bss)
        {
            try
            {
                var managementObj = bss as System.Management.ManagementBaseObject;
                var ssidBytes = (byte[])managementObj?["Ndis80211Ssid"];
                if (ssidBytes == null || ssidBytes.Length == 0) return null;
                
                // 有効なバイトのみを取得
                int validLength = Array.IndexOf(ssidBytes, (byte)0);
                if (validLength == -1) validLength = ssidBytes.Length;
                if (validLength == 0) return null;
                
                // UTF-8でデコード、失敗したらASCIIでリトライ
                try
                {
                    return System.Text.Encoding.UTF8.GetString(ssidBytes, 0, validLength);
                }
                catch
                {
                    return System.Text.Encoding.ASCII.GetString(ssidBytes, 0, validLength);
                }
            }
            catch
            {
                return null;
            }
        }
        
        // 統合されたネットワーク監視機能（旧NetworkMonitor統合）
        public static event EventHandler<NetworkStatusChangedEventArgs>? NetworkStatusChanged;
        private static Timer? _monitorTimer;
        private static string? _lastConnectedSSID;
        private static bool _lastConnectionStatus;
        private static bool _monitoringActive = false;
        
        public static void StartNetworkMonitoring()
        {
            if (_monitoringActive) return;
            
            _monitoringActive = true;
            _monitorTimer = new Timer(CheckNetworkStatusCallback, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }
        
        public static void StopNetworkMonitoring()
        {
            _monitoringActive = false;
            _monitorTimer?.Dispose();
            _monitorTimer = null;
        }
        
        private static async void CheckNetworkStatusCallback(object? state)
        {
            if (!_monitoringActive) return;
            
            try
            {
                var currentSSID = await GetCurrentConnectedSSIDAsync();
                var isConnected = !string.IsNullOrEmpty(currentSSID);
                
                // 状態変更時のみイベント発火
                if (isConnected != _lastConnectionStatus || currentSSID != _lastConnectedSSID)
                {
                    _lastConnectionStatus = isConnected;
                    _lastConnectedSSID = currentSSID;
                    
                    NetworkStatusChanged?.Invoke(null, new NetworkStatusChangedEventArgs
                    {
                        IsConnected = isConnected,
                        ConnectedSSID = currentSSID,
                        Timestamp = DateTime.Now
                    });
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("NetworkUtils.CheckNetworkStatus", ex);
            }
        }
        
        // 統合されたネットワーク速度テスト機能（旧NetworkSpeedTester統合）
        private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler
        {
            MaxConnectionsPerServer = 2,
            UseProxy = false
        })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        
        static NetworkUtils()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "MurtiWiFiConnector/1.0");
            _httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
        }
        
        // 軽量速度テスト（セキュア版）
        public static async Task<SpeedTestResult> RunQuickSpeedTestAsync(CancellationToken cancellationToken = default)
        {
            var result = new SpeedTestResult
            {
                TestType = SpeedTestType.Quick,
                StartTime = DateTime.Now
            };
            
            try
            {
                // セキュアなテストエンドポイント（HTTPSに変更）
                var testEndpoints = new[]
                {
                    "https://httpbin.org/bytes/2048",   // 2KB
                    "https://httpbin.org/bytes/8192",   // 8KB
                };
                
                var downloadTasks = testEndpoints.Select(endpoint => 
                    TestDownloadSpeedAsync(endpoint, cancellationToken)).ToArray();
                
                var downloadResults = await Task.WhenAll(downloadTasks);
                var validResults = downloadResults.Where(r => r.Success && r.SpeedMbps > 0).ToList();
                
                if (validResults.Count > 0)
                {
                    result.Success = true;
                    result.DownloadSpeedMbps = validResults.Average(r => r.SpeedMbps);
                    result.MaxSpeedMbps = validResults.Max(r => r.SpeedMbps);
                    result.MinSpeedMbps = validResults.Min(r => r.SpeedMbps);
                    result.TestCount = validResults.Count;
                    result.Message = $"平均: {result.DownloadSpeedMbps:F1} Mbps";
                }
                else
                {
                    result.Success = false;
                    result.Message = "速度テストに失敗しました";
                }
                
                result.Duration = DateTime.Now - result.StartTime;
                return result;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("NetworkUtils.RunQuickSpeedTest", ex);
                result.Success = false;
                result.Message = $"テストエラー: {ex.Message}";
                result.Duration = DateTime.Now - result.StartTime;
                return result;
            }
        }
        
        private static async Task<DownloadTestResult> TestDownloadSpeedAsync(string url, CancellationToken cancellationToken)
        {
            var testResult = new DownloadTestResult { Url = url };
            
            try
            {
                var stopwatch = Stopwatch.StartNew();
                using var response = await _httpClient.GetAsync(url, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    testResult.ErrorMessage = $"HTTP {(int)response.StatusCode}";
                    return testResult;
                }
                
                var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                stopwatch.Stop();
                
                if (content.Length > 0 && stopwatch.ElapsedMilliseconds > 0)
                {
                    var bytesPerSecond = (double)content.Length / stopwatch.ElapsedMilliseconds * 1000;
                    var mbps = bytesPerSecond * 8 / (1024 * 1024);
                    
                    testResult.Success = true;
                    testResult.BytesDownloaded = content.Length;
                    testResult.DurationMs = (int)stopwatch.ElapsedMilliseconds;
                    testResult.SpeedMbps = mbps;
                }
                else
                {
                    testResult.ErrorMessage = "データなしまたは時間計測失敗";
                }
                
                return testResult;
            }
            catch (TaskCanceledException)
            {
                testResult.ErrorMessage = "タイムアウト";
                return testResult;
            }
            catch (Exception ex)
            {
                testResult.ErrorMessage = SecurityManager.AnonymizeLogData(ex.Message);
                return testResult;
            }
        }
        
        // 接続安定性テスト
        public static async Task<StabilityTestResult> TestConnectionStabilityAsync(string host = "8.8.8.8", int testCount = 5, CancellationToken cancellationToken = default)
        {
            var result = new StabilityTestResult
            {
                Host = host,
                TestCount = testCount,
                StartTime = DateTime.Now
            };
            
            try
            {
                var pingTimes = new List<long>();
                var failedPings = 0;
                
                using var ping = new Ping();
                
                for (int i = 0; i < testCount && !cancellationToken.IsCancellationRequested; i++)
                {
                    try
                    {
                        var reply = await ping.SendPingAsync(host, 2000);
                        
                        if (reply.Status == IPStatus.Success)
                        {
                            pingTimes.Add(reply.RoundtripTime);
                        }
                        else
                        {
                            failedPings++;
                        }
                        
                        if (i < testCount - 1)
                            await Task.Delay(300, cancellationToken);
                    }
                    catch
                    {
                        failedPings++;
                    }
                }
                
                result.Duration = DateTime.Now - result.StartTime;
                
                if (pingTimes.Count > 0)
                {
                    result.Success = true;
                    result.AveragePingMs = (int)pingTimes.Average();
                    result.MinPingMs = (int)pingTimes.Min();
                    result.MaxPingMs = (int)pingTimes.Max();
                    result.PacketLossPercentage = (double)failedPings / testCount * 100;
                    result.IsStable = result.PacketLossPercentage < 20;
                    result.Message = $"平均: {result.AveragePingMs}ms, ロス: {result.PacketLossPercentage:F1}%";
                }
                else
                {
                    result.Success = false;
                    result.Message = "全てのpingが失敗しました";
                }
                
                return result;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("NetworkUtils.TestConnectionStability", ex);
                result.Success = false;
                result.Message = $"安定性テストエラー: {ex.Message}";
                return result;
            }
        }
        
        // リソースクリーンアップ
        public static void DisposeNetworkUtils()
        {
            StopNetworkMonitoring();
            _httpClient?.Dispose();
        }
    }
    
    // イベント引数クラス
    public class NetworkStatusChangedEventArgs : EventArgs
    {
        public bool IsConnected { get; set; }
        public string? ConnectedSSID { get; set; }
        public DateTime Timestamp { get; set; }
    }
    
    // 速度テスト結果クラス
    public class SpeedTestResult
    {
        public SpeedTestType TestType { get; set; }
        public bool Success { get; set; }
        public double DownloadSpeedMbps { get; set; }
        public double MaxSpeedMbps { get; set; }
        public double MinSpeedMbps { get; set; }
        public int TestCount { get; set; }
        public DateTime StartTime { get; set; }
        public TimeSpan Duration { get; set; }
        public string Message { get; set; } = string.Empty;
    }
    
    public class DownloadTestResult
    {
        public bool Success { get; set; }
        public string Url { get; set; } = string.Empty;
        public int BytesDownloaded { get; set; }
        public int DurationMs { get; set; }
        public double SpeedMbps { get; set; }
        public string? ErrorMessage { get; set; }
    }
    
    public class StabilityTestResult
    {
        public bool Success { get; set; }
        public string Host { get; set; } = string.Empty;
        public int TestCount { get; set; }
        public int AveragePingMs { get; set; }
        public int MinPingMs { get; set; }
        public int MaxPingMs { get; set; }
        public double PacketLossPercentage { get; set; }
        public bool IsStable { get; set; }
        public DateTime StartTime { get; set; }
        public TimeSpan Duration { get; set; }
        public string Message { get; set; } = string.Empty;
    }
    
    public enum SpeedTestType
    {
        Quick,
        Standard,
        Comprehensive
    }
}