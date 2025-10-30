using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// WiFi接続の自動診断機能を提供するクラス
    /// 接続問題を自動的に検知して解決策を提案
    /// </summary>
    public class WifiAutoDiagnostics
    {
        private readonly ILogger<WifiAutoDiagnostics> _logger;
        private readonly IWifiManager _wifiManager;

        public WifiAutoDiagnostics(ILogger<WifiAutoDiagnostics> logger, IWifiManager wifiManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _wifiManager = wifiManager ?? throw new ArgumentNullException(nameof(wifiManager));
        }

        /// <summary>
        /// 包括的なWiFi診断を実行
        /// </summary>
        public async Task<WifiDiagnosticReport> RunFullDiagnosticsAsync()
        {
            var report = new WifiDiagnosticReport
            {
                DiagnosticTimestamp = DateTime.UtcNow,
                IssuesFound = new List<DiagnosticIssue>()
            };

            try
            {
                // ネットワークアダプタの診断
                await DiagnoseNetworkAdaptersAsync(report);

                // WiFi接続状態の診断
                await DiagnoseWifiConnectionAsync(report);

                // ネットワーク設定の診断
                await DiagnoseNetworkConfigurationAsync(report);

                // パフォーマンスの診断
                await DiagnosePerformanceAsync(report);

                // セキュリティの診断
                await DiagnoseSecurityAsync(report);

                // 診断結果を評価
                report.OverallHealth = EvaluateOverallHealth(report.IssuesFound);

                await _logger.LogInformation("WiFi診断を完了しました", new Dictionary<string, object>
                {
                    ["issuesFound"] = report.IssuesFound.Count,
                    ["overallHealth"] = report.OverallHealth.ToString()
                });

                return report;
            }
            catch (Exception ex)
            {
                await _logger.LogError("WiFi診断中にエラーが発生しました", ex);

                return new WifiDiagnosticReport
                {
                    DiagnosticTimestamp = DateTime.UtcNow,
                    IssuesFound = new List<DiagnosticIssue>
                    {
                        new DiagnosticIssue
                        {
                            Severity = DiagnosticSeverity.Error,
                            Category = "診断エラー",
                            Description = $"診断実行中にエラーが発生しました: {ex.Message}",
                            Recommendation = "システム管理者にお問い合わせください"
                        }
                    },
                    OverallHealth = HealthStatus.Unhealthy
                };
            }
        }

        /// <summary>
        /// ネットワークアダプタの診断
        /// </summary>
        private async Task DiagnoseNetworkAdaptersAsync(WifiDiagnosticReport report)
        {
            try
            {
                var adapters = await _wifiManager.GetAvailableAdaptersAsync();

                if (!adapters.IsSuccess || !adapters.Value.Any())
                {
                    report.IssuesFound.Add(new DiagnosticIssue
                    {
                        Severity = DiagnosticSeverity.Error,
                        Category = "ネットワークアダプタ",
                        Description = "WiFiアダプタが見つかりません",
                        Recommendation = "WiFiアダプタが正しくインストールされているか確認してください"
                    });
                    return;
                }

                var wifiAdapters = adapters.Value
                    .Where(a => a.InterfaceType == NetworkInterfaceType.Wireless80211)
                    .ToList();

                if (!wifiAdapters.Any())
                {
                    report.IssuesFound.Add(new DiagnosticIssue
                    {
                        Severity = DiagnosticSeverity.Warning,
                        Category = "ネットワークアダプタ",
                        Description = "WiFiアダプタが検出されませんでした",
                        Recommendation = "WiFiアダプタが有効になっているか確認してください"
                    });
                }

                foreach (var adapter in wifiAdapters)
                {
                    if (!adapter.IsConnected)
                    {
                        report.IssuesFound.Add(new DiagnosticIssue
                        {
                            Severity = DiagnosticSeverity.Info,
                            Category = "ネットワークアダプタ",
                            Description = $"アダプタ '{adapter.Name}' が接続されていません",
                            Recommendation = "WiFiネットワークに接続してください"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                await _logger.LogWarning("ネットワークアダプタ診断中にエラーが発生しました", ex.Message);
            }
        }

        /// <summary>
        /// WiFi接続状態の診断
        /// </summary>
        private async Task DiagnoseWifiConnectionAsync(WifiDiagnosticReport report)
        {
            try
            {
                var currentSSID = await _wifiManager.GetCurrentSSIDAsync();

                if (string.IsNullOrEmpty(currentSSID))
                {
                    report.IssuesFound.Add(new DiagnosticIssue
                    {
                        Severity = DiagnosticSeverity.Warning,
                        Category = "WiFi接続",
                        Description = "WiFiネットワークに接続されていません",
                        Recommendation = "利用可能なWiFiネットワークに接続してください"
                    });
                    return;
                }

                // 利用可能なネットワークを取得して現在のネットワークを確認
                var networks = await _wifiManager.ScanNetworksAsync();
                var currentNetwork = networks.FirstOrDefault(n => n.Ssid == currentSSID);

                if (currentNetwork == null)
                {
                    report.IssuesFound.Add(new DiagnosticIssue
                    {
                        Severity = DiagnosticSeverity.Warning,
                        Category = "WiFi接続",
                        Description = $"接続中のネットワーク '{currentSSID}' がスキャン結果に見つかりません",
                        Recommendation = "ネットワークが利用可能か確認してください"
                    });
                    return;
                }

                // シグナル強度の診断
                if (currentNetwork.SignalStrength < 30)
                {
                    report.IssuesFound.Add(new DiagnosticIssue
                    {
                        Severity = DiagnosticSeverity.Warning,
                        Category = "WiFi接続",
                        Description = $"シグナル強度が弱いです ({currentNetwork.SignalStrength}%)",
                        Recommendation = "アクセスポイントに近づくか、より良い場所に移動してください"
                    });
                }

                // セキュリティモードの診断
                if (currentNetwork.SecurityMode == WifiSecurityMode.Open)
                {
                    report.IssuesFound.Add(new DiagnosticIssue
                    {
                        Severity = DiagnosticSeverity.Info,
                        Category = "セキュリティ",
                        Description = "オープンWiFiネットワークに接続しています",
                        Recommendation = "セキュリティ保護されたネットワークの使用を検討してください"
                    });
                }
            }
            catch (Exception ex)
            {
                await _logger.LogWarning("WiFi接続診断中にエラーが発生しました", ex.Message);
            }
        }

        /// <summary>
        /// ネットワーク設定の診断
        /// </summary>
        private async Task DiagnoseNetworkConfigurationAsync(WifiDiagnosticReport report)
        {
            try
            {
                // IP設定の確認
                var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();

                foreach (var ni in networkInterfaces)
                {
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                        ni.OperationalStatus == OperationalStatus.Up)
                    {
                        var ipProps = ni.GetIPProperties();

                        // DNSサーバーの確認
                        if (!ipProps.DnsAddresses.Any())
                        {
                            report.IssuesFound.Add(new DiagnosticIssue
                            {
                                Severity = DiagnosticSeverity.Warning,
                                Category = "ネットワーク設定",
                                Description = "DNSサーバーが設定されていません",
                                Recommendation = "ネットワーク設定でDNSサーバーを確認してください"
                            });
                        }

                        // ゲートウェイの確認
                        if (!ipProps.GatewayAddresses.Any())
                        {
                            report.IssuesFound.Add(new DiagnosticIssue
                            {
                                Severity = DiagnosticSeverity.Warning,
                                Category = "ネットワーク設定",
                                Description = "デフォルトゲートウェイが設定されていません",
                                Recommendation = "ネットワーク設定でゲートウェイを確認してください"
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await _logger.LogWarning("ネットワーク設定診断中にエラーが発生しました", ex.Message);
            }
        }

        /// <summary>
        /// パフォーマンスの診断
        /// </summary>
        private async Task DiagnosePerformanceAsync(WifiDiagnosticReport report)
        {
            try
            {
                // ネットワークインターフェースの統計情報を確認
                var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();

                foreach (var ni in networkInterfaces)
                {
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                        ni.OperationalStatus == OperationalStatus.Up)
                    {
                        // エラーパケットの確認
                        var stats = ni.GetIPv4Statistics();

                        if (stats.IncomingPacketsDiscarded > 0 || stats.OutgoingPacketsDiscarded > 0)
                        {
                            report.IssuesFound.Add(new DiagnosticIssue
                            {
                                Severity = DiagnosticSeverity.Warning,
                                Category = "パフォーマンス",
                                Description = "パケット廃棄が発生しています",
                                Recommendation = "ネットワーク負荷を減らすか、ルーターを再起動してください"
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await _logger.LogWarning("パフォーマンス診断中にエラーが発生しました", ex.Message);
            }
        }

        /// <summary>
        /// セキュリティの診断
        /// </summary>
        private async Task DiagnoseSecurityAsync(WifiDiagnosticReport report)
        {
            try
            {
                // 保存されたプロファイルのセキュリティ確認
                var profiles = await _wifiManager.GetSavedProfilesAsync();

                foreach (var profile in profiles)
                {
                    // プロファイルのセキュリティ情報を取得（実際の実装ではより詳細なチェックが必要）
                    var networks = await _wifiManager.ScanNetworksAsync();
                    var network = networks.FirstOrDefault(n => n.Ssid == profile);

                    if (network != null && network.SecurityMode == WifiSecurityMode.Open)
                    {
                        report.IssuesFound.Add(new DiagnosticIssue
                        {
                            Severity = DiagnosticSeverity.Info,
                            Category = "セキュリティ",
                            Description = $"保存されたプロファイル '{profile}' はオープンWiFiです",
                            Recommendation = "セキュリティ保護されたネットワークの使用を検討してください"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                await _logger.LogWarning("セキュリティ診断中にエラーが発生しました", ex.Message);
            }
        }

        /// <summary>
        /// 全体的なヘルス状態を評価
        /// </summary>
        private HealthStatus EvaluateOverallHealth(List<DiagnosticIssue> issues)
        {
            if (!issues.Any())
                return HealthStatus.Excellent;

            var errorCount = issues.Count(i => i.Severity == DiagnosticSeverity.Error);
            var warningCount = issues.Count(i => i.Severity == DiagnosticSeverity.Warning);

            if (errorCount > 0)
                return HealthStatus.Unhealthy;
            else if (warningCount > 2)
                return HealthStatus.Fair;
            else if (warningCount > 0)
                return HealthStatus.Good;
            else
                return HealthStatus.Excellent;
        }
    }

    /// <summary>
    /// WiFi診断レポート
    /// </summary>
    public class WifiDiagnosticReport
    {
        public DateTime DiagnosticTimestamp { get; set; }
        public HealthStatus OverallHealth { get; set; }
        public List<DiagnosticIssue> IssuesFound { get; set; } = new();
        public string? Summary => $"{OverallHealth} - {IssuesFound.Count}個の問題が見つかりました";
    }

    /// <summary>
    /// 診断問題
    /// </summary>
    public class DiagnosticIssue
    {
        public DiagnosticSeverity Severity { get; set; }
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
        public string Recommendation { get; set; } = "";
    }

    /// <summary>
    /// 診断の深刻度
    /// </summary>
    public enum DiagnosticSeverity
    {
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// ヘルス状態
    /// </summary>
    public enum HealthStatus
    {
        Excellent,
        Good,
        Fair,
        Unhealthy
    }
}
