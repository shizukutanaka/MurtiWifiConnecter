using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// WiFi接続パフォーマンスの最適化を管理するクラス
    /// 接続速度と安定性を向上させる機能を提供
    /// </summary>
    public class WifiConnectionOptimizer
    {
        private readonly ILogger<WifiConnectionOptimizer> _logger;
        private readonly Dictionary<string, NetworkQualityMetrics> _networkMetrics;

        public WifiConnectionOptimizer(ILogger<WifiConnectionOptimizer> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _networkMetrics = new Dictionary<string, NetworkQualityMetrics>();
        }

        /// <summary>
        /// 利用可能なネットワークから最適なものを選択
        /// </summary>
        public async Task<OptimizedNetworkChoice> SelectOptimalNetworkAsync(List<WifiNetwork> availableNetworks)
        {
            try
            {
                var optimizedChoices = new List<NetworkQualityScore>();

                foreach (var network in availableNetworks)
                {
                    var score = await CalculateNetworkScoreAsync(network);
                    optimizedChoices.Add(new NetworkQualityScore
                    {
                        Network = network,
                        Score = score,
                        Reasoning = GetScoreReasoning(score, network)
                    });
                }

                // スコアの高い順にソート
                var bestChoice = optimizedChoices
                    .OrderByDescending(choice => choice.Score.OverallScore)
                    .FirstOrDefault();

                if (bestChoice == null)
                {
                    return new OptimizedNetworkChoice
                    {
                        SelectedNetwork = null,
                        Reason = "利用可能なネットワークが見つかりません"
                    };
                }

                await _logger.LogInformation("最適なネットワークを選択しました", new Dictionary<string, object>
                {
                    ["selectedSSID"] = bestChoice.Network.Ssid,
                    ["score"] = bestChoice.Score.OverallScore,
                    ["signalStrength"] = bestChoice.Network.SignalStrength,
                    ["securityMode"] = bestChoice.Network.SecurityMode.ToString()
                });

                return new OptimizedNetworkChoice
                {
                    SelectedNetwork = bestChoice.Network,
                    Reason = bestChoice.Reasoning,
                    QualityScore = bestChoice.Score
                };
            }
            catch (Exception ex)
            {
                await _logger.LogError("最適ネットワーク選択中にエラーが発生しました", ex);

                // エラーが発生した場合はシグナル強度でソートしたものを返す
                var fallbackNetwork = availableNetworks
                    .OrderByDescending(n => n.SignalStrength)
                    .FirstOrDefault();

                return new OptimizedNetworkChoice
                {
                    SelectedNetwork = fallbackNetwork,
                    Reason = "最適化処理でエラーが発生したため、シグナル強度基準で選択しました"
                };
            }
        }

        /// <summary>
        /// ネットワークの品質スコアを計算
        /// </summary>
        private async Task<NetworkQualityScore> CalculateNetworkScoreAsync(WifiNetwork network)
        {
            var score = new NetworkQualityScore
            {
                Network = network,
                SignalQualityScore = CalculateSignalQualityScore(network.SignalStrength),
                SecurityScore = CalculateSecurityScore(network.SecurityMode),
                StabilityScore = await CalculateStabilityScoreAsync(network),
                PerformanceScore = await CalculatePerformanceScoreAsync(network)
            };

            // 加重平均で総合スコアを計算
            score.OverallScore =
                (score.SignalQualityScore * 0.3) +
                (score.SecurityScore * 0.25) +
                (score.StabilityScore * 0.25) +
                (score.PerformanceScore * 0.2);

            return score;
        }

        /// <summary>
        /// シグナル品質スコアを計算（0-100）
        /// </summary>
        private double CalculateSignalQualityScore(int signalStrength)
        {
            // シグナル強度をスコアに変換
            return Math.Min(100, Math.Max(0, signalStrength));
        }

        /// <summary>
        /// セキュリティスコアを計算（0-100）
        /// </summary>
        private double CalculateSecurityScore(WifiSecurityMode securityMode)
        {
            return securityMode switch
            {
                WifiSecurityMode.Wpa3 => 100,
                WifiSecurityMode.Wpa3Enterprise => 100,
                WifiSecurityMode.Wpa2 => 90,
                WifiSecurityMode.Wpa2Enterprise => 95,
                WifiSecurityMode.Wpa => 70,
                WifiSecurityMode.Wep => 30,
                WifiSecurityMode.Open => 0,
                _ => 50
            };
        }

        /// <summary>
        /// 安定性スコアを計算（0-100）
        /// </summary>
        private async Task<double> CalculateStabilityScoreAsync(WifiNetwork network)
        {
            // 過去の接続履歴から安定性を評価
            if (_networkMetrics.TryGetValue(network.Ssid, out var metrics))
            {
                var successRate = metrics.ConnectionAttempts > 0
                    ? (double)metrics.SuccessfulConnections / metrics.ConnectionAttempts * 100
                    : 50;

                return Math.Min(100, Math.Max(0, successRate));
            }

            return 50; // 履歴がない場合はデフォルト値
        }

        /// <summary>
        /// パフォーマンススコアを計算（0-100）
        /// </summary>
        private async Task<double> CalculatePerformanceScoreAsync(WifiNetwork network)
        {
            // ネットワークの周波数帯と混雑度で評価
            var bandScore = network.FrequencyBand switch
            {
                WifiFrequencyBand.Band5GHz => 90,  // 5GHzは高速で干渉が少ない
                WifiFrequencyBand.Band6GHz => 100, // 6GHzは最新で高速
                WifiFrequencyBand.Band2_4GHz => 70, // 2.4GHzは干渉が多い
                _ => 60
            };

            return bandScore;
        }

        /// <summary>
        /// スコアの理由を説明するテキストを生成
        /// </summary>
        private string GetScoreReasoning(NetworkQualityScore score, WifiNetwork network)
        {
            var reasons = new List<string>();

            if (score.SignalQualityScore >= 80)
                reasons.Add("シグナル強度が良好");
            else if (score.SignalQualityScore >= 60)
                reasons.Add("シグナル強度が中程度");
            else
                reasons.Add("シグナル強度が弱い");

            if (score.SecurityScore >= 90)
                reasons.Add("セキュリティレベルが高い");
            else if (score.SecurityScore >= 70)
                reasons.Add("セキュリティレベルが中程度");
            else
                reasons.Add("セキュリティレベルが低い");

            if (score.StabilityScore >= 80)
                reasons.Add("接続安定性が高い");
            else if (score.StabilityScore >= 60)
                reasons.Add("接続安定性が中程度");
            else
                reasons.Add("接続安定性が低い");

            reasons.Add($"{network.FrequencyBand}帯を使用");

            return string.Join("、", reasons);
        }

        /// <summary>
        /// ネットワークメトリクスを記録
        /// </summary>
        public void RecordNetworkMetrics(string ssid, bool connectionSuccessful)
        {
            if (!_networkMetrics.TryGetValue(ssid, out var metrics))
            {
                metrics = new NetworkQualityMetrics();
                _networkMetrics[ssid] = metrics;
            }

            metrics.ConnectionAttempts++;
            if (connectionSuccessful)
            {
                metrics.SuccessfulConnections++;
            }

            metrics.LastAttemptTime = DateTime.UtcNow;
        }

        /// <summary>
        /// ネットワーク品質メトリクスを取得
        /// </summary>
        public NetworkQualityMetrics? GetNetworkMetrics(string ssid)
        {
            return _networkMetrics.TryGetValue(ssid, out var metrics) ? metrics : null;
        }
    }

    /// <summary>
    /// ネットワーク品質スコア
    /// </summary>
    public class NetworkQualityScore
    {
        public WifiNetwork Network { get; set; } = new();
        public double OverallScore { get; set; }
        public double SignalQualityScore { get; set; }
        public double SecurityScore { get; set; }
        public double StabilityScore { get; set; }
        public double PerformanceScore { get; set; }
        public string Reasoning { get; set; } = "";
    }

    /// <summary>
    /// 最適化されたネットワーク選択結果
    /// </summary>
    public class OptimizedNetworkChoice
    {
        public WifiNetwork? SelectedNetwork { get; set; }
        public string Reason { get; set; } = "";
        public NetworkQualityScore? QualityScore { get; set; }
    }

    /// <summary>
    /// ネットワーク品質メトリクス
    /// </summary>
    public class NetworkQualityMetrics
    {
        public int ConnectionAttempts { get; set; }
        public int SuccessfulConnections { get; set; }
        public DateTime LastAttemptTime { get; set; }
    }
}
