using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MurtiWifiConnecter.Core;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// 包括的なネットワーク診断機能を提供するクラス
    /// </summary>
    public static class NetworkDiagnostics
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// 完全なネットワーク診断を実行
        /// </summary>
        public static async Task<NetworkDiagnosticsReport> PerformFullDiagnosticsAsync(
            string targetHost = "8.8.8.8",
            CancellationToken ct = default)
        {
            var report = new NetworkDiagnosticsReport
            {
                Timestamp = DateTime.Now,
                Tests = new List<DiagnosticTestResult>()
            };

            try
            {
                // 基本接続テスト
                report.Tests.Add(await TestBasicConnectivityAsync(ct));

                // DNS解決テスト
                report.Tests.Add(await TestDnsResolutionAsync(targetHost, ct));

                // 遅延テスト
                report.Tests.Add(await TestLatencyAsync(targetHost, ct));

                // パケット損失テスト
                report.Tests.Add(await TestPacketLossAsync(targetHost, ct));

                // 帯域幅テスト
                report.Tests.Add(await TestBandwidthAsync(ct));

                // WiFi固有のテスト
                report.Tests.Add(await TestWifiSpecificAsync(ct));

                // ネットワークアダプタテスト
                report.Tests.Add(await TestNetworkAdaptersAsync(ct));

                // ファイアウォールテスト
                report.Tests.Add(await TestFirewallAsync(ct));

                report.Success = report.Tests.All(t => t.Success);
                report.OverallScore = CalculateOverallScore(report.Tests);

                await Logger.LogInfo($"Network diagnostics completed with score: {report.OverallScore}",
                    nameof(NetworkDiagnostics), new Dictionary<string, object>
                    {
                        ["testsRun"] = report.Tests.Count,
                        ["successRate"] = report.Tests.Count(t => t.Success) * 100.0 / report.Tests.Count,
                        ["overallScore"] = report.OverallScore
                    });

            }
            catch (Exception ex)
            {
                report.Success = false;
                report.ErrorMessage = ex.Message;
                await Logger.LogError($"Network diagnostics failed: {ex.Message}",
                    nameof(NetworkDiagnostics), null, ex);
            }

            return report;
        }

        /// <summary>
        /// 基本的な接続テスト
        /// </summary>
        private static async Task<DiagnosticTestResult> TestBasicConnectivityAsync(CancellationToken ct)
        {
            var result = new DiagnosticTestResult
            {
                TestName = "Basic Connectivity",
                Description = "インターネット接続の基本テスト"
            };

            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync("8.8.8.8", 1000);

                result.Success = reply.Status == IPStatus.Success;
                result.Details = $"Ping to 8.8.8.8: {reply.RoundtripTime}ms, Status: {reply.Status}";
                result.Score = result.Success ? 100 : 0;
                result.Metrics = new Dictionary<string, object>
                {
                    ["roundTripTime"] = reply.RoundtripTime,
                    ["status"] = reply.Status.ToString()
                };
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.Score = 0;
            }

            return result;
        }

        /// <summary>
        /// DNS解決テスト
        /// </summary>
        private static async Task<DiagnosticTestResult> TestDnsResolutionAsync(string host, CancellationToken ct)
        {
            var result = new DiagnosticTestResult
            {
                TestName = "DNS Resolution",
                Description = "DNS名前解決のテスト"
            };

            try
            {
                var stopwatch = Stopwatch.StartNew();
                var addresses = await Dns.GetHostAddressesAsync(host);
                stopwatch.Stop();

                result.Success = addresses.Length > 0;
                result.Details = $"Resolved {host} to {addresses.Length} address(es) in {stopwatch.ElapsedMilliseconds}ms";
                result.Score = result.Success ? 100 : 0;
                result.Metrics = new Dictionary<string, object>
                {
                    ["resolvedAddresses"] = addresses.Length,
                    ["resolutionTimeMs"] = stopwatch.ElapsedMilliseconds,
                    ["addresses"] = string.Join(", ", addresses.Select(a => a.ToString()))
                };
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.Score = 0;
            }

            return result;
        }

        /// <summary>
        /// 遅延テスト
        /// </summary>
        private static async Task<DiagnosticTestResult> TestLatencyAsync(string host, CancellationToken ct)
        {
            var result = new DiagnosticTestResult
            {
                TestName = "Latency Test",
                Description = "ネットワーク遅延の測定"
            };

            try
            {
                using var ping = new Ping();
                var latencies = new List<long>();

                for (int i = 0; i < 5; i++)
                {
                    var reply = await ping.SendPingAsync(host, 1000);
                    if (reply.Status == IPStatus.Success)
                    {
                        latencies.Add(reply.RoundtripTime);
                    }
                    await Task.Delay(100, ct); // 小さな遅延
                }

                if (latencies.Any())
                {
                    var avgLatency = latencies.Average();
                    var minLatency = latencies.Min();
                    var maxLatency = latencies.Max();
                    var jitter = maxLatency - minLatency;

                    result.Success = true;
                    result.Details = $"平均遅延: {avgLatency:F1}ms, ジッター: {jitter}ms";
                    result.Score = CalculateLatencyScore(avgLatency, jitter);
                    result.Metrics = new Dictionary<string, object>
                    {
                        ["averageLatency"] = avgLatency,
                        ["minLatency"] = minLatency,
                        ["maxLatency"] = maxLatency,
                        ["jitter"] = jitter,
                        ["sampleCount"] = latencies.Count
                    };
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = "すべてのpingが失敗しました";
                    result.Score = 0;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.Score = 0;
            }

            return result;
        }

        /// <summary>
        /// パケット損失テスト
        /// </summary>
        private static async Task<DiagnosticTestResult> TestPacketLossAsync(string host, CancellationToken ct)
        {
            var result = new DiagnosticTestResult
            {
                TestName = "Packet Loss Test",
                Description = "パケット損失率の測定"
            };

            try
            {
                using var ping = new Ping();
                const int totalPings = 10;
                var successfulPings = 0;

                for (int i = 0; i < totalPings; i++)
                {
                    try
                    {
                        var reply = await ping.SendPingAsync(host, 1000);
                        if (reply.Status == IPStatus.Success)
                        {
                            successfulPings++;
                        }
                    }
                    catch
                    {
                        // 個別のping失敗は無視
                    }
                    await Task.Delay(100, ct);
                }

                var lossRate = (double)(totalPings - successfulPings) / totalPings * 100;
                result.Success = lossRate < 5; // 5%未満なら成功
                result.Details = $"パケット損失率: {lossRate:F1}%, 成功: {successfulPings}/{totalPings}";
                result.Score = CalculatePacketLossScore(lossRate);
                result.Metrics = new Dictionary<string, object>
                {
                    ["packetLossRate"] = lossRate,
                    ["successfulPings"] = successfulPings,
                    ["totalPings"] = totalPings
                };
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.Score = 0;
            }

            return result;
        }

        /// <summary>
        /// 帯域幅テスト
        /// </summary>
        private static async Task<DiagnosticTestResult> TestBandwidthAsync(CancellationToken ct)
        {
            var result = new DiagnosticTestResult
            {
                TestName = "Bandwidth Test",
                Description = "ネットワーク帯域幅の測定"
            };

            try
            {
                // 簡易的な帯域幅推定（実際のテストは複雑なので基本的なチェックのみ）
                var speedTest = new EnhancedSpeedTest();
                var speedResult = await speedTest.PerformSpeedTestAsync(ct);

                if (speedResult.Success)
                {
                    result.Success = true;
                    result.Details = $"ダウンロード: {speedResult.DownloadSpeed:F2} Mbps, アップロード: {speedResult.UploadSpeed:F2} Mbps";
                    result.Score = CalculateBandwidthScore(speedResult.DownloadSpeed, speedResult.UploadSpeed);
                    result.Metrics = new Dictionary<string, object>
                    {
                        ["downloadSpeedMbps"] = speedResult.DownloadSpeed,
                        ["uploadSpeedMbps"] = speedResult.UploadSpeed
                    };
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = speedResult.Message ?? "帯域幅テストに失敗しました";
                    result.Score = 0;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.Score = 0;
            }

            return result;
        }

        /// <summary>
        /// WiFi固有のテスト
        /// </summary>
        private static async Task<DiagnosticTestResult> TestWifiSpecificAsync(CancellationToken ct)
        {
            var result = new DiagnosticTestResult
            {
                TestName = "WiFi Specific Tests",
                Description = "WiFi接続固有の診断"
            };

            try
            {
                var issues = new List<string>();
                var networks = await NetworkOperations.GetAvailableNetworksAsync();

                if (networks.Count == 0)
                {
                    issues.Add("利用可能なWiFiネットワークが見つかりません");
                }

                var currentConnection = await NetworkOperations.GetCurrentConnectionAsync();
                if (currentConnection == null)
                {
                    issues.Add("WiFi接続が確立されていません");
                }
                else
                {
                    // 信号強度のチェック
                    if (currentConnection.SignalStrength < 30)
                    {
                        issues.Add($"信号強度が弱いです: {currentConnection.SignalStrength}%");
                    }

                    // セキュリティのチェック
                    if (currentConnection.SecurityType?.Contains("Open") == true)
                    {
                        issues.Add("開かれたネットワークに接続しています（セキュリティリスク）");
                    }
                }

                result.Success = issues.Count == 0;
                result.Details = result.Success
                    ? "WiFi接続は正常です"
                    : $"問題が見つかりました: {string.Join(", ", issues)}";
                result.Score = issues.Count == 0 ? 100 : Math.Max(0, 100 - (issues.Count * 20));
                result.Metrics = new Dictionary<string, object>
                {
                    ["availableNetworks"] = networks.Count,
                    ["currentConnection"] = currentConnection != null,
                    ["signalStrength"] = currentConnection?.SignalStrength ?? 0,
                    ["securityType"] = currentConnection?.SecurityType ?? "None",
                    ["issues"] = issues
                };
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.Score = 0;
            }

            return result;
        }

        /// <summary>
        /// ネットワークアダプタテスト
        /// </summary>
        private static async Task<DiagnosticTestResult> TestNetworkAdaptersAsync(CancellationToken ct)
        {
            var result = new DiagnosticTestResult
            {
                TestName = "Network Adapters",
                Description = "ネットワークアダプタの状態チェック"
            };

            try
            {
                var adapters = NetworkInterface.GetAllNetworkInterfaces();
                var wifiAdapters = adapters.Where(a => a.NetworkInterfaceType == NetworkInterfaceType.Wireless80211).ToList();
                var activeAdapters = adapters.Where(a => a.OperationalStatus == OperationalStatus.Up).ToList();

                var issues = new List<string>();

                if (wifiAdapters.Count == 0)
                {
                    issues.Add("WiFiアダプタが見つかりません");
                }
                else
                {
                    var enabledWifiAdapters = wifiAdapters.Where(a => a.OperationalStatus == OperationalStatus.Up).ToList();
                    if (enabledWifiAdapters.Count == 0)
                    {
                        issues.Add("有効なWiFiアダプタがありません");
                    }
                }

                result.Success = issues.Count == 0;
                result.Details = $"{wifiAdapters.Count} WiFiアダプタ, {activeAdapters.Count} アクティブアダプタ";
                result.Score = CalculateAdapterScore(wifiAdapters, activeAdapters);
                result.Metrics = new Dictionary<string, object>
                {
                    ["totalAdapters"] = adapters.Length,
                    ["wifiAdapters"] = wifiAdapters.Count,
                    ["activeAdapters"] = activeAdapters.Count,
                    ["issues"] = issues
                };
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.Score = 0;
            }

            return result;
        }

        /// <summary>
        /// ファイアウォールテスト
        /// </summary>
        private static async Task<DiagnosticTestResult> TestFirewallAsync(CancellationToken ct)
        {
            var result = new DiagnosticTestResult
            {
                TestName = "Firewall Check",
                Description = "ファイアウォール設定の検証"
            };

            try
            {
                // 基本的なポートテスト（例: HTTP, HTTPS）
                var ports = new[] { 80, 443 };
                var results = new Dictionary<int, bool>();

                foreach (var port in ports)
                {
                    try
                    {
                        using var client = new TcpClient();
                        var connectTask = client.ConnectAsync("google.com", port);
                        var timeoutTask = Task.Delay(5000);

                        var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                        results[port] = completedTask == connectTask && !connectTask.IsFaulted;
                    }
                    catch
                    {
                        results[port] = false;
                    }
                }

                var successCount = results.Count(r => r.Value);
                result.Success = successCount >= ports.Length / 2; // 半分以上成功すればOK
                result.Details = $"{successCount}/{ports.Length} ポートがアクセス可能";
                result.Score = (int)((double)successCount / ports.Length * 100);
                result.Metrics = new Dictionary<string, object>
                {
                    ["portsTested"] = ports,
                    ["results"] = results,
                    ["successCount"] = successCount
                };
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.Score = 0;
            }

            return result;
        }

        // スコア計算ヘルパーメソッド
        private static int CalculateLatencyScore(double avgLatency, double jitter)
        {
            if (avgLatency < 20 && jitter < 10) return 100;
            if (avgLatency < 50 && jitter < 25) return 80;
            if (avgLatency < 100 && jitter < 50) return 60;
            if (avgLatency < 200 && jitter < 100) return 40;
            return 20;
        }

        private static int CalculatePacketLossScore(double lossRate)
        {
            if (lossRate == 0) return 100;
            if (lossRate < 1) return 90;
            if (lossRate < 2) return 80;
            if (lossRate < 5) return 60;
            if (lossRate < 10) return 40;
            return 20;
        }

        private static int CalculateBandwidthScore(double download, double upload)
        {
            // 基本的なスコアリング（実際の要件に合わせて調整）
            var score = 0;
            if (download > 50) score += 50; else if (download > 25) score += 30; else if (download > 10) score += 10;
            if (upload > 10) score += 50; else if (upload > 5) score += 30; else if (upload > 1) score += 10;
            return Math.Min(100, score);
        }

        private static int CalculateAdapterScore(List<NetworkInterface> wifiAdapters, List<NetworkInterface> activeAdapters)
        {
            if (wifiAdapters.Count == 0) return 0;
            var activeWifiAdapters = wifiAdapters.Count(a => a.OperationalStatus == OperationalStatus.Up);
            return (int)((double)activeWifiAdapters / wifiAdapters.Count * 100);
        }

        private static int CalculateOverallScore(List<DiagnosticTestResult> tests)
        {
            if (!tests.Any()) return 0;
            return (int)tests.Average(t => t.Score);
        }
    }

    /// <summary>
    /// ネットワーク診断レポート
    /// </summary>
    public class NetworkDiagnosticsReport
    {
        public DateTime Timestamp { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int OverallScore { get; set; }
        public List<DiagnosticTestResult> Tests { get; set; } = new();
    }

    /// <summary>
    /// 診断テスト結果
    /// </summary>
    public class DiagnosticTestResult
    {
        public string TestName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string Details { get; set; } = string.Empty;
        public int Score { get; set; }
        public Dictionary<string, object>? Metrics { get; set; }
    }
}
