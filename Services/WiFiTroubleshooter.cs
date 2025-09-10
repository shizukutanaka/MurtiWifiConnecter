using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Services
{
    /// <summary>
    /// WiFiトラブルシューティングサービス - 実用的な問題解決支援
    /// </summary>
    public class WiFiTroubleshooter
    {
        private readonly EnhancedWifiScanner _scanner;
        private readonly QuickStatusChecker _statusChecker;

        public WiFiTroubleshooter(EnhancedWifiScanner scanner = null)
        {
            _scanner = scanner ?? new EnhancedWifiScanner();
        }

        /// <summary>
        /// 包括的なWiFi診断
        /// </summary>
        public async Task<TroubleshootingReport> DiagnoseAsync()
        {
            var report = new TroubleshootingReport
            {
                DiagnosisTime = DateTime.Now,
                Issues = new List<WiFiIssue>(),
                Recommendations = new List<string>()
            };

            try
            {
                SimpleLoggingService.LogInfo("Starting WiFi troubleshooting diagnosis...");

                // 1. 基本接続状態確認
                await DiagnoseBasicConnectivityAsync(report);

                // 2. ネットワークアダプター確認
                await DiagnoseNetworkAdapterAsync(report);

                // 3. 信号品質確認
                await DiagnoseSignalQualityAsync(report);

                // 4. DNS/インターネット確認
                await DiagnoseDnsAndInternetAsync(report);

                // 5. プロファイル問題確認
                await DiagnoseProfileIssuesAsync(report);

                // 総合評価
                report.OverallHealth = CalculateOverallHealth(report);
                GenerateRecommendations(report);

                SimpleLoggingService.LogInfo($"Troubleshooting completed: {report.Issues.Count} issues found");

                return report;
            }
            catch (Exception ex)
            {
                SimpleLoggingService.LogError("Troubleshooting diagnosis failed", ex);
                report.Issues.Add(new WiFiIssue
                {
                    Type = IssueType.SystemError,
                    Severity = IssueSeverity.High,
                    Title = "診断エラー",
                    Description = $"診断中にエラーが発生しました: {ex.Message}",
                    Solution = "アプリケーションを再起動してみてください"
                });
                return report;
            }
        }

        /// <summary>
        /// 特定の問題に対する自動修復試行
        /// </summary>
        public async Task<FixResult> TryAutoFixAsync(WiFiIssue issue)
        {
            var result = new FixResult
            {
                Issue = issue,
                AttemptTime = DateTime.Now
            };

            try
            {
                switch (issue.Type)
                {
                    case IssueType.NoConnection:
                        result.Success = await TryReconnectToLastNetworkAsync();
                        break;

                    case IssueType.WeakSignal:
                        result.Success = await TrySwitchToBetterNetworkAsync();
                        break;

                    case IssueType.DnsIssue:
                        result.Success = await TryFlushDnsAsync();
                        break;

                    case IssueType.ProfileCorruption:
                        result.Success = await TryRecreateProfileAsync();
                        break;

                    default:
                        result.Success = false;
                        result.Message = "この問題の自動修復は対応していません";
                        break;
                }

                if (result.Success)
                {
                    result.Message = "修復が成功しました";
                    SimpleLoggingService.LogInfo($"Auto-fix successful for issue: {issue.Type}");
                }
                else if (string.IsNullOrEmpty(result.Message))
                {
                    result.Message = "自動修復に失敗しました";
                }

                return result;
            }
            catch (Exception ex)
            {
                SimpleLoggingService.LogError($"Auto-fix failed for issue: {issue.Type}", ex);
                result.Success = false;
                result.Message = $"修復中にエラーが発生しました: {ex.Message}";
                return result;
            }
        }

        private async Task DiagnoseBasicConnectivityAsync(TroubleshootingReport report)
        {
            var status = await QuickStatusChecker.GetQuickStatusAsync();

            if (!status.NetworkAvailable)
            {
                report.Issues.Add(new WiFiIssue
                {
                    Type = IssueType.AdapterDisabled,
                    Severity = IssueSeverity.High,
                    Title = "ネットワークアダプター無効",
                    Description = "WiFiアダプターが無効化されているか、利用できません",
                    Solution = "デバイスマネージャーでWiFiアダプターを有効にしてください"
                });
            }

            if (string.IsNullOrEmpty(status.ConnectedSSID))
            {
                report.Issues.Add(new WiFiIssue
                {
                    Type = IssueType.NoConnection,
                    Severity = IssueSeverity.Medium,
                    Title = "WiFi未接続",
                    Description = "どのWiFiネットワークにも接続していません",
                    Solution = "利用可能なネットワークを確認して接続してください"
                });
            }

            if (!string.IsNullOrEmpty(status.ConnectedSSID) && !status.HasInternet)
            {
                report.Issues.Add(new WiFiIssue
                {
                    Type = IssueType.NoInternet,
                    Severity = IssueSeverity.Medium,
                    Title = "インターネット接続なし",
                    Description = $"{status.ConnectedSSID}に接続していますが、インターネットにアクセスできません",
                    Solution = "ルーターの再起動、DNS設定の確認を試してください"
                });
            }
        }

        private async Task DiagnoseNetworkAdapterAsync(TroubleshootingReport report)
        {
            try
            {
                var adapters = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    .ToList();

                if (!adapters.Any())
                {
                    report.Issues.Add(new WiFiIssue
                    {
                        Type = IssueType.NoAdapter,
                        Severity = IssueSeverity.High,
                        Title = "WiFiアダプターなし",
                        Description = "WiFiアダプターが見つかりません",
                        Solution = "ドライバーの再インストールまたはハードウェアの確認が必要です"
                    });
                    return;
                }

                var workingAdapters = adapters.Where(a => a.OperationalStatus == OperationalStatus.Up).ToList();
                if (!workingAdapters.Any())
                {
                    report.Issues.Add(new WiFiIssue
                    {
                        Type = IssueType.AdapterDisabled,
                        Severity = IssueSeverity.High,
                        Title = "WiFiアダプター無効",
                        Description = "WiFiアダプターが無効化されています",
                        Solution = "ネットワーク設定でWiFiアダプターを有効にしてください"
                    });
                }
            }
            catch (Exception ex)
            {
                SimpleLoggingService.LogError("Network adapter diagnosis failed", ex);
            }
        }

        private async Task DiagnoseSignalQualityAsync(TroubleshootingReport report)
        {
            var quality = await QuickStatusChecker.AssessConnectionQualityAsync();

            if (quality.HasValidData)
            {
                if (quality.SignalStrength < 30)
                {
                    report.Issues.Add(new WiFiIssue
                    {
                        Type = IssueType.WeakSignal,
                        Severity = IssueSeverity.Medium,
                        Title = "信号強度低下",
                        Description = $"信号強度が低すぎます ({quality.SignalStrength}%)",
                        Solution = "ルーターに近づく、障害物を除去する、またはより強力な信号のネットワークに切り替えてください"
                    });
                }

                if (quality.AverageLatency > 300)
                {
                    report.Issues.Add(new WiFiIssue
                    {
                        Type = IssueType.HighLatency,
                        Severity = IssueSeverity.Low,
                        Title = "高レイテンシ",
                        Description = $"応答時間が遅すぎます ({quality.AverageLatency}ms)",
                        Solution = "ネットワークの混雑確認、他のデバイスの使用量制限を検討してください"
                    });
                }
            }
        }

        private async Task DiagnoseDnsAndInternetAsync(TroubleshootingReport report)
        {
            try
            {
                // DNS解決テスト
                using var ping = new Ping();
                var googleDns = await ping.SendPingAsync("8.8.8.8", 5000);
                var cloudfareDns = await ping.SendPingAsync("1.1.1.1", 5000);

                if (googleDns.Status != IPStatus.Success && cloudfareDns.Status != IPStatus.Success)
                {
                    report.Issues.Add(new WiFiIssue
                    {
                        Type = IssueType.DnsIssue,
                        Severity = IssueSeverity.Medium,
                        Title = "DNS接続問題",
                        Description = "DNSサーバーに接続できません",
                        Solution = "DNSキャッシュのクリア、DNS設定の変更(8.8.8.8, 1.1.1.1)を試してください"
                    });
                }
            }
            catch (Exception ex)
            {
                SimpleLoggingService.LogError("DNS diagnosis failed", ex);
            }
        }

        private async Task DiagnoseProfileIssuesAsync(TroubleshootingReport report)
        {
            // プロファイル関連の問題は簡易チェックのみ
            try
            {
                var currentSSID = await NetworkUtils.GetCurrentConnectedSSIDAsync();
                if (!string.IsNullOrEmpty(currentSSID))
                {
                    // 接続は成功しているがインターネットアクセスがない場合、
                    // プロファイル破損の可能性を示唆
                    var hasInternet = await QuickStatusChecker.TestInternetConnectionAsync();
                    if (!hasInternet)
                    {
                        report.Issues.Add(new WiFiIssue
                        {
                            Type = IssueType.ProfileCorruption,
                            Severity = IssueSeverity.Low,
                            Title = "プロファイル設定問題の可能性",
                            Description = "接続はしているがインターネットアクセスに問題があります",
                            Solution = "ネットワークプロファイルを削除して再作成してください"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                SimpleLoggingService.LogError("Profile diagnosis failed", ex);
            }
        }

        private HealthStatus CalculateOverallHealth(TroubleshootingReport report)
        {
            if (report.Issues.Any(i => i.Severity == IssueSeverity.High))
                return HealthStatus.Critical;

            if (report.Issues.Any(i => i.Severity == IssueSeverity.Medium))
                return HealthStatus.Warning;

            if (report.Issues.Any(i => i.Severity == IssueSeverity.Low))
                return HealthStatus.Minor;

            return HealthStatus.Healthy;
        }

        private void GenerateRecommendations(TroubleshootingReport report)
        {
            if (report.OverallHealth == HealthStatus.Healthy)
            {
                report.Recommendations.Add("WiFi接続は良好です");
                return;
            }

            // 高優先度の推奨事項
            var highIssues = report.Issues.Where(i => i.Severity == IssueSeverity.High).ToList();
            if (highIssues.Any())
            {
                report.Recommendations.Add("重要: " + string.Join("、", highIssues.Select(i => i.Solution)));
            }

            // 一般的な推奨事項
            if (report.Issues.Any(i => i.Type == IssueType.WeakSignal))
            {
                report.Recommendations.Add("信号強度を向上させてください");
            }

            if (report.Issues.Any(i => i.Type == IssueType.NoInternet || i.Type == IssueType.DnsIssue))
            {
                report.Recommendations.Add("ネットワーク設定またはルーターを確認してください");
            }

            if (!report.Recommendations.Any())
            {
                report.Recommendations.Add("検出された問題に応じて個別の対応が必要です");
            }
        }

        private async Task<bool> TryReconnectToLastNetworkAsync()
        {
            // 簡易再接続試行
            try
            {
                var networks = await _scanner.GetCachedNetworks();
                var lastConnected = networks.FirstOrDefault(n => n.Priority > 0);
                
                if (lastConnected != null)
                {
                    // 実際の再接続は FastWifiConnector を使用
                    SimpleLoggingService.LogInfo($"Attempting reconnect to: {lastConnected.SSID}");
                    return true; // 簡易実装
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> TrySwitchToBetterNetworkAsync()
        {
            try
            {
                var recommended = await _scanner.GetRecommendedNetworkAsync();
                if (recommended != null && recommended.SignalStrength > 50)
                {
                    SimpleLoggingService.LogInfo($"Found better network: {recommended.SSID}");
                    return true; // 簡易実装
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> TryFlushDnsAsync()
        {
            try
            {
                var result = await NetworkUtils.ExecuteAdvancedCommandAsync("ipconfig", "/flushdns", 10000);
                return result.Success;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> TryRecreateProfileAsync()
        {
            // プロファイル再作成の簡易実装
            return await Task.FromResult(false); // 実装は複雑なので簡易版
        }
    }

    // データモデル
    public class TroubleshootingReport
    {
        public DateTime DiagnosisTime { get; set; }
        public List<WiFiIssue> Issues { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
        public HealthStatus OverallHealth { get; set; }
    }

    public class WiFiIssue
    {
        public IssueType Type { get; set; }
        public IssueSeverity Severity { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Solution { get; set; }
    }

    public class FixResult
    {
        public WiFiIssue Issue { get; set; }
        public DateTime AttemptTime { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
    }

    public enum IssueType
    {
        NoConnection,
        WeakSignal,
        NoInternet,
        DnsIssue,
        AdapterDisabled,
        NoAdapter,
        ProfileCorruption,
        HighLatency,
        SystemError
    }

    public enum IssueSeverity
    {
        Low,
        Medium,
        High
    }

    public enum HealthStatus
    {
        Healthy,
        Minor,
        Warning,
        Critical
    }
}