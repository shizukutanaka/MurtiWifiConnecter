using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// 軽量なネットワーク診断ツール
    /// </summary>
    public static class NetworkDiagnostics
    {
        private static readonly string[] TestHosts = { "8.8.8.8", "1.1.1.1", "208.67.222.222" };
        
        /// <summary>
        /// 基本的なネットワーク診断
        /// </summary>
        public static async Task<DiagnosticSummary> RunBasicDiagnosticsAsync(CancellationToken cancellationToken = default)
        {
            var summary = new DiagnosticSummary
            {
                TestTime = DateTime.Now,
                Results = new List<DiagnosticTestResult>()
            };
            
            try
            {
                // 1. アダプター状態チェック
                var adapterResult = await CheckWifiAdapterAsync(cancellationToken).ConfigureAwait(false);
                summary.Results.Add(adapterResult);
                
                // 2. 接続状態チェック
                var connectionResult = await CheckConnectionStatusAsync(cancellationToken).ConfigureAwait(false);
                summary.Results.Add(connectionResult);
                
                // 3. 基本的な接続テスト
                var pingResult = await CheckInternetConnectivityAsync(cancellationToken);
                summary.Results.Add(pingResult);
                
                // 4. DNS解決テスト
                var dnsResult = await CheckDnsResolutionAsync(cancellationToken);
                summary.Results.Add(dnsResult);
                
                // 総合評価
                var passedTests = 0;
                var totalTests = summary.Results.Count;
                
                foreach (var result in summary.Results)
                {
                    if (result.IsSuccess) passedTests++;
                }
                
                summary.OverallStatus = passedTests == totalTests ? DiagnosticStatus.Healthy :
                                       passedTests >= totalTests / 2 ? DiagnosticStatus.Warning :
                                       DiagnosticStatus.Critical;
                
                summary.Summary = $"{passedTests}/{totalTests} テスト合格";
            }
            catch (Exception ex)
            {
                summary.Results.Add(new DiagnosticTestResult
                {
                    TestName = "診断実行",
                    IsSuccess = false,
                    Message = $"診断中にエラー: {ex.Message}"
                });
                summary.OverallStatus = DiagnosticStatus.Error;
            }
            
            return summary;
        }
        
        /// <summary>
        /// WiFiアダプターの状態をチェック
        /// </summary>
        private static async Task<DiagnosticTestResult> CheckWifiAdapterAsync(CancellationToken cancellationToken)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = "interface show interface",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                process.WaitForExit(2000);
                
                var hasEnabledWifi = output.Contains("Wi-Fi") && output.Contains("Connected");
                
                return new DiagnosticTestResult
                {
                    TestName = "WiFiアダプター",
                    IsSuccess = hasEnabledWifi,
                    Message = hasEnabledWifi ? "WiFiアダプターは正常に動作しています" : "WiFiアダプターが無効または未接続です"
                };
            }
            catch (Exception ex)
            {
                return new DiagnosticTestResult
                {
                    TestName = "WiFiアダプター",
                    IsSuccess = false,
                    Message = $"アダプター確認エラー: {ex.Message}"
                };
            }
        }
        
        /// <summary>
        /// 現在の接続状態をチェック
        /// </summary>
        private static async Task<DiagnosticTestResult> CheckConnectionStatusAsync(CancellationToken cancellationToken)
        {
            try
            {
                var currentSSID = await NetworkUtils.GetCurrentConnectedSSIDAsync();
                
                return new DiagnosticTestResult
                {
                    TestName = "接続状態",
                    IsSuccess = !string.IsNullOrEmpty(currentSSID),
                    Message = !string.IsNullOrEmpty(currentSSID) ? $"接続中: {currentSSID}" : "WiFiに接続されていません"
                };
            }
            catch (Exception ex)
            {
                return new DiagnosticTestResult
                {
                    TestName = "接続状態",
                    IsSuccess = false,
                    Message = $"接続状態確認エラー: {ex.Message}"
                };
            }
        }
        
        /// <summary>
        /// インターネット接続をチェック
        /// </summary>
        private static async Task<DiagnosticTestResult> CheckInternetConnectivityAsync(CancellationToken cancellationToken)
        {
            var successfulPings = 0;
            var totalPings = TestHosts.Length;
            var avgLatency = 0L;
            
            try
            {
                using var ping = new Ping();
                
                foreach (var host in TestHosts)
                {
                    try
                    {
                        var reply = await ping.SendPingAsync(host, 3000);
                        if (reply.Status == IPStatus.Success)
                        {
                            successfulPings++;
                            avgLatency += reply.RoundtripTime;
                        }
                    }
                    catch
                    {
                        // 個別のpingエラーは無視
                    }
                    
                    if (cancellationToken.IsCancellationRequested)
                        break;
                }
                
                if (successfulPings > 0)
                    avgLatency /= successfulPings;
                
                var isSuccess = successfulPings > 0;
                var message = isSuccess ? 
                    $"インターネット接続OK ({successfulPings}/{totalPings} 成功, 平均 {avgLatency}ms)" :
                    "インターネット接続に問題があります";
                
                return new DiagnosticTestResult
                {
                    TestName = "インターネット接続",
                    IsSuccess = isSuccess,
                    Message = message
                };
            }
            catch (Exception ex)
            {
                return new DiagnosticTestResult
                {
                    TestName = "インターネット接続",
                    IsSuccess = false,
                    Message = $"接続テストエラー: {ex.Message}"
                };
            }
        }
        
        /// <summary>
        /// DNS解決をチェック
        /// </summary>
        private static async Task<DiagnosticTestResult> CheckDnsResolutionAsync(CancellationToken cancellationToken)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "nslookup",
                        Arguments = "google.com",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                process.WaitForExit(3000);
                
                var isSuccess = output.Contains("Name:") || output.Contains("Address");
                
                return new DiagnosticTestResult
                {
                    TestName = "DNS解決",
                    IsSuccess = isSuccess,
                    Message = isSuccess ? "DNS解決は正常に動作しています" : "DNS解決に問題があります"
                };
            }
            catch (Exception ex)
            {
                return new DiagnosticTestResult
                {
                    TestName = "DNS解決",
                    IsSuccess = false,
                    Message = $"DNS解決テストエラー: {ex.Message}"
                };
            }
        }
        
        /// <summary>
        /// ネットワーク速度の簡易テスト
        /// </summary>
        public static async Task<SpeedTestResult> RunSimpleSpeedTestAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var result = new SpeedTestResult
                {
                    TestTime = DateTime.Now
                };
                
                // 小さなファイルでの簡易スピードテスト
                using var ping = new Ping();
                var latencyTests = new List<long>();
                
                for (int i = 0; i < 5; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                        
                    var reply = await ping.SendPingAsync("8.8.8.8", 2000);
                    if (reply.Status == IPStatus.Success)
                    {
                        latencyTests.Add(reply.RoundtripTime);
                    }
                    await Task.Delay(200, cancellationToken);
                }
                
                if (latencyTests.Count > 0)
                {
                    latencyTests.Sort();
                    result.AverageLatency = latencyTests[latencyTests.Count / 2]; // 中央値
                    result.IsSuccess = true;
                    
                    result.Quality = result.AverageLatency switch
                    {
                        <= 30 => "優秀",
                        <= 60 => "良好",
                        <= 120 => "普通",
                        <= 300 => "低速",
                        _ => "非常に低速"
                    };
                }
                else
                {
                    result.IsSuccess = false;
                    result.Quality = "測定失敗";
                }
                
                return result;
            }
            catch (Exception ex)
            {
                return new SpeedTestResult
                {
                    TestTime = DateTime.Now,
                    IsSuccess = false,
                    Quality = $"エラー: {ex.Message}"
                };
            }
        }
    }
    
    public class DiagnosticSummary
    {
        public DateTime TestTime { get; set; }
        public DiagnosticStatus OverallStatus { get; set; }
        public string Summary { get; set; } = string.Empty;
        public List<DiagnosticTestResult> Results { get; set; } = new();
        
        public string GetStatusDescription()
        {
            return OverallStatus switch
            {
                DiagnosticStatus.Healthy => "正常",
                DiagnosticStatus.Warning => "注意",
                DiagnosticStatus.Critical => "問題あり",
                DiagnosticStatus.Error => "エラー",
                _ => "不明"
            };
        }
    }
    
    public class DiagnosticTestResult
    {
        public string TestName { get; set; } = string.Empty;
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
    }
    
    public class SpeedTestResult
    {
        public DateTime TestTime { get; set; }
        public bool IsSuccess { get; set; }
        public long AverageLatency { get; set; }
        public string Quality { get; set; } = string.Empty;
    }
    
    public enum DiagnosticStatus
    {
        Healthy,
        Warning,
        Critical,
        Error
    }
}