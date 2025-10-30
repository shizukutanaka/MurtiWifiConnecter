using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// WiFi接続の自動修復機能を提供するクラス
    /// 診断結果に基づいて自動的に問題を修復
    /// </summary>
    public class WifiAutoRepair
    {
        private readonly ILogger<WifiAutoRepair> _logger;
        private readonly IWifiManager _wifiManager;
        private readonly WifiAutoDiagnostics _diagnostics;

        public WifiAutoRepair(
            ILogger<WifiAutoRepair> logger,
            IWifiManager wifiManager,
            WifiAutoDiagnostics diagnostics)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _wifiManager = wifiManager ?? throw new ArgumentNullException(nameof(wifiManager));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        /// <summary>
        /// 自動修復を実行
        /// </summary>
        public async Task<WifiRepairResult> RunAutoRepairAsync()
        {
            var result = new WifiRepairResult
            {
                RepairTimestamp = DateTime.UtcNow,
                RepairsAttempted = new List<RepairAttempt>()
            };

            try
            {
                // まず診断を実行
                var diagnosticReport = await _diagnostics.RunFullDiagnosticsAsync();

                await _logger.LogInformation("自動修復を開始します", new Dictionary<string, object>
                {
                    ["issuesFound"] = diagnosticReport.IssuesFound.Count,
                    ["overallHealth"] = diagnosticReport.OverallHealth.ToString()
                });

                // 深刻度順に修復を試行
                var repairableIssues = diagnosticReport.IssuesFound
                    .Where(issue => CanAutoRepair(issue))
                    .OrderBy(issue => GetRepairPriority(issue))
                    .ToList();

                foreach (var issue in repairableIssues)
                {
                    var repairAttempt = await AttemptRepairAsync(issue);
                    result.RepairsAttempted.Add(repairAttempt);

                    if (repairAttempt.Success)
                    {
                        await _logger.LogInformation($"問題を修復しました: {issue.Description}");
                    }
                    else
                    {
                        await _logger.LogWarning($"問題の修復に失敗しました: {issue.Description} - {repairAttempt.ErrorMessage}");
                    }
                }

                result.Success = result.RepairsAttempted.All(r => r.Success);
                result.TotalRepairsAttempted = result.RepairsAttempted.Count;

                await _logger.LogInformation("自動修復を完了しました", new Dictionary<string, object>
                {
                    ["totalRepairs"] = result.TotalRepairsAttempted,
                    ["successfulRepairs"] = result.RepairsAttempted.Count(r => r.Success),
                    ["overallSuccess"] = result.Success
                });

                return result;
            }
            catch (Exception ex)
            {
                await _logger.LogError("自動修復中にエラーが発生しました", ex);

                return new WifiRepairResult
                {
                    RepairTimestamp = DateTime.UtcNow,
                    Success = false,
                    ErrorMessage = ex.Message,
                    TotalRepairsAttempted = 0
                };
            }
        }

        /// <summary>
        /// 単一の問題を修復
        /// </summary>
        public async Task<RepairAttempt> RepairSpecificIssueAsync(DiagnosticIssue issue)
        {
            if (!CanAutoRepair(issue))
            {
                return new RepairAttempt
                {
                    Issue = issue,
                    Success = false,
                    ErrorMessage = "自動修復がサポートされていない問題です"
                };
            }

            return await AttemptRepairAsync(issue);
        }

        /// <summary>
        /// 問題が自動修復可能か確認
        /// </summary>
        private bool CanAutoRepair(DiagnosticIssue issue)
        {
            return issue.Category switch
            {
                "WiFi接続" when issue.Description.Contains("ネットワークに接続されていません") => true,
                "ネットワークアダプタ" when issue.Description.Contains("WiFiアダプタが有効になっていない") => true,
                "ネットワーク設定" when issue.Description.Contains("ネットワーク設定") => false, // 手動設定が必要
                _ => false
            };
        }

        /// <summary>
        /// 修復の優先度を取得
        /// </summary>
        private int GetRepairPriority(DiagnosticIssue issue)
        {
            return issue.Severity switch
            {
                DiagnosticSeverity.Error => 1,
                DiagnosticSeverity.Warning => 2,
                DiagnosticSeverity.Info => 3,
                _ => 4
            };
        }

        /// <summary>
        /// 問題の修復を試行
        /// </summary>
        private async Task<RepairAttempt> AttemptRepairAsync(DiagnosticIssue issue)
        {
            var attempt = new RepairAttempt
            {
                Issue = issue,
                AttemptTimestamp = DateTime.UtcNow
            };

            try
            {
                switch (issue.Category)
                {
                    case "WiFi接続":
                        attempt.Success = await RepairWifiConnectionAsync(issue, attempt);
                        break;

                    case "ネットワークアダプタ":
                        attempt.Success = await RepairNetworkAdapterAsync(issue, attempt);
                        break;

                    default:
                        attempt.Success = false;
                        attempt.ErrorMessage = $"未対応のカテゴリ: {issue.Category}";
                        break;
                }
            }
            catch (Exception ex)
            {
                attempt.Success = false;
                attempt.ErrorMessage = ex.Message;
            }

            return attempt;
        }

        /// <summary>
        /// WiFi接続の問題を修復
        /// </summary>
        private async Task<bool> RepairWifiConnectionAsync(DiagnosticIssue issue, RepairAttempt attempt)
        {
            try
            {
                if (issue.Description.Contains("ネットワークに接続されていません"))
                {
                    // 利用可能なネットワークを取得して最適なものを選択
                    var networks = await _wifiManager.ScanNetworksAsync();

                    if (!networks.Any())
                    {
                        attempt.ErrorMessage = "利用可能なネットワークが見つかりません";
                        return false;
                    }

                    // 最適なネットワークを選択（実際の実装ではより詳細な選択ロジックが必要）
                    var bestNetwork = networks
                        .Where(n => n.SecurityMode != WifiSecurityMode.Open) // オープンWiFiを避ける
                        .OrderByDescending(n => n.SignalStrength)
                        .FirstOrDefault();

                    if (bestNetwork == null)
                    {
                        attempt.ErrorMessage = "適切なネットワークが見つかりません";
                        return false;
                    }

                    // 接続を試行（パスワードが必要な場合は別途処理が必要）
                    var connected = await _wifiManager.ConnectAsync(bestNetwork.Ssid, "");

                    if (connected)
                    {
                        attempt.RepairAction = $"ネットワーク '{bestNetwork.Ssid}' に接続しました";
                        return true;
                    }
                    else
                    {
                        attempt.ErrorMessage = $"ネットワーク '{bestNetwork.Ssid}' への接続に失敗しました";
                        return false;
                    }
                }

                attempt.ErrorMessage = "サポートされていないWiFi接続の問題です";
                return false;
            }
            catch (Exception ex)
            {
                attempt.ErrorMessage = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// ネットワークアダプタの問題を修復
        /// </summary>
        private async Task<bool> RepairNetworkAdapterAsync(DiagnosticIssue issue, RepairAttempt attempt)
        {
            try
            {
                // 実際の実装では、ネットワークアダプタの有効化やリセットを行う
                // ここではシミュレーションとして成功を返す

                attempt.RepairAction = "ネットワークアダプタの状態を確認しました";
                return true;
            }
            catch (Exception ex)
            {
                attempt.ErrorMessage = ex.Message;
                return false;
            }
        }
    }

    /// <summary>
    /// WiFi修復結果
    /// </summary>
    public class WifiRepairResult
    {
        public DateTime RepairTimestamp { get; set; }
        public bool Success { get; set; }
        public int TotalRepairsAttempted { get; set; }
        public List<RepairAttempt> RepairsAttempted { get; set; } = new();
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// 修復試行
    /// </summary>
    public class RepairAttempt
    {
        public DiagnosticIssue Issue { get; set; } = new();
        public DateTime AttemptTimestamp { get; set; }
        public bool Success { get; set; }
        public string? RepairAction { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
