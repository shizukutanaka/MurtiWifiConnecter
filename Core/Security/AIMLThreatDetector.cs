using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace MurtiWifiConnecter.Core.Security
{
    /// <summary>
    /// AI/MLベースのWiFi脅威検知システム
    /// ネットワークトラフィックと動作パターンを分析して脅威を検知
    /// </summary>
    public class AIMLThreatDetector
    {
        private readonly MLContext _mlContext;
        private ITransformer _model;
        private readonly Dictionary<string, NetworkAnomaly> _recentAnomalies;
        private readonly object _anomalyLock = new();

        // 脅威検知の閾値設定
        private const double AnomalyThreshold = 0.7;
        private const int MaxAnomaliesPerHour = 10;
        private const int AnomalyWindowMinutes = 5;

        public AIMLThreatDetector()
        {
            _mlContext = new MLContext(seed: 0);
            _recentAnomalies = new Dictionary<string, NetworkAnomaly>();
            InitializeModel();
        }

        /// <summary>
        /// MLモデルの初期化
        /// </summary>
        private void InitializeModel()
        {
            try
            {
                // 簡易的な異常検知モデル（実際の実装ではより高度なモデルを使用）
                // ここではランダムフォレストベースの異常検知を実装
                var pipeline = _mlContext.Transforms.Concatenate("Features",
                    nameof(NetworkTrafficData.PacketCount),
                    nameof(NetworkTrafficData.ByteCount),
                    nameof(NetworkTrafficData.Duration),
                    nameof(NetworkTrafficData.ProtocolDistribution))
                .Append(_mlContext.AnomalyDetection.Trainers.RandomizedPcaTrainer());

                // サンプルデータでモデルを訓練（実際の実装では大規模なデータセットを使用）
                var sampleData = GenerateSampleTrainingData();
                var trainingData = _mlContext.Data.LoadFromEnumerable(sampleData);

                _model = pipeline.Fit(trainingData);
                Logger.LogInfo("AI/ML脅威検知モデルを初期化しました", nameof(AIMLThreatDetector));
            }
            catch (Exception ex)
            {
                Logger.LogError("AI/MLモデル初期化に失敗しました", nameof(AIMLThreatDetector), null, ex);
            }
        }

        /// <summary>
        /// ネットワークトラフィックを分析して脅威を検知
        /// </summary>
        public async Task<ThreatDetectionResult> AnalyzeNetworkTrafficAsync(NetworkTrafficData trafficData)
        {
            try
            {
                var predictionEngine = _mlContext.Model.CreatePredictionEngine<NetworkTrafficData, AnomalyPrediction>(_model);
                var prediction = predictionEngine.Predict(trafficData);

                var isAnomaly = prediction.Score > AnomalyThreshold;
                var threatLevel = CalculateThreatLevel(prediction.Score, trafficData);

                if (isAnomaly)
                {
                    var anomaly = new NetworkAnomaly
                    {
                        Timestamp = DateTime.UtcNow,
                        ThreatLevel = threatLevel,
                        Description = GenerateAnomalyDescription(trafficData, prediction.Score),
                        TrafficData = trafficData,
                        Confidence = prediction.Score
                    };

                    await RecordAnomalyAsync(anomaly);
                }

                return new ThreatDetectionResult
                {
                    IsThreat = isAnomaly,
                    ThreatLevel = threatLevel,
                    Confidence = prediction.Score,
                    Description = isAnomaly ? "異常なネットワークパターンを検知しました" : "正常なネットワーク動作です",
                    Recommendations = GenerateRecommendations(threatLevel, trafficData)
                };
            }
            catch (Exception ex)
            {
                Logger.LogError("ネットワークトラフィック分析に失敗しました", nameof(AIMLThreatDetector), null, ex);
                return new ThreatDetectionResult { IsThreat = false, ThreatLevel = ThreatLevel.Low };
            }
        }

        /// <summary>
        /// 接続試行を分析して認証攻撃を検知
        /// </summary>
        public async Task<AuthAttackDetectionResult> AnalyzeAuthAttemptsAsync(List<AuthAttemptData> authAttempts)
        {
            try
            {
                // ブルートフォース攻撃検知
                var failedAttempts = authAttempts.Count(a => !a.Success);
                var timeSpan = authAttempts.Max(a => a.Timestamp) - authAttempts.Min(a => a.Timestamp);
                var attemptsPerMinute = failedAttempts / Math.Max(1, timeSpan.TotalMinutes);

                var isBruteForce = attemptsPerMinute > 5 && failedAttempts > 10;

                // パスワードスプレー攻撃検知
                var uniquePasswords = authAttempts.Select(a => a.PasswordHash).Distinct().Count();
                var isPasswordSpray = uniquePasswords > 20 && failedAttempts > 50;

                // 異常な時間帯での攻撃検知
                var unusualHours = authAttempts.Any(a => a.Timestamp.Hour < 6 || a.Timestamp.Hour > 22);
                var isUnusualTime = unusualHours && failedAttempts > 5;

                var threats = new List<string>();
                if (isBruteForce) threats.Add("ブルートフォース攻撃");
                if (isPasswordSpray) threats.Add("パスワードスプレー攻撃");
                if (isUnusualTime) threats.Add("異常時間帯攻撃");

                return new AuthAttackDetectionResult
                {
                    IsAttackDetected = threats.Any(),
                    Threats = threats,
                    RiskLevel = CalculateAuthAttackRisk(threats),
                    BlockedIPs = authAttempts.Where(a => !a.Success).Select(a => a.SourceIP).Distinct().ToList(),
                    Recommendations = GenerateAuthRecommendations(threats)
                };
            }
            catch (Exception ex)
            {
                Logger.LogError("認証試行分析に失敗しました", nameof(AIMLThreatDetector), null, ex);
                return new AuthAttackDetectionResult { IsAttackDetected = false };
            }
        }

        /// <summary>
        /// 異常を記録して統計を更新
        /// </summary>
        private async Task RecordAnomalyAsync(NetworkAnomaly anomaly)
        {
            lock (_anomalyLock)
            {
                var key = $"{anomaly.Timestamp:yyyy-MM-dd-HH-mm}";
                if (!_recentAnomalies.ContainsKey(key))
                {
                    _recentAnomalies[key] = anomaly;

                    // 古いエントリを削除
                    var cutoff = DateTime.UtcNow.AddHours(-1);
                    _recentAnomalies.RemoveAll(kvp => kvp.Value.Timestamp < cutoff);
                }
            }

            // データベースやログに記録
            await Logger.LogSecurity("ネットワーク異常を検知しました", "NetworkAnomalyDetected",
                new Dictionary<string, object>
                {
                    ["threatLevel"] = anomaly.ThreatLevel.ToString(),
                    ["confidence"] = anomaly.Confidence,
                    ["description"] = anomaly.Description,
                    ["timestamp"] = anomaly.Timestamp
                });
        }

        /// <summary>
        /// 脅威レベルを計算
        /// </summary>
        private ThreatLevel CalculateThreatLevel(double score, NetworkTrafficData data)
        {
            if (score > 0.9) return ThreatLevel.Critical;
            if (score > 0.8) return ThreatLevel.High;
            if (score > 0.7) return ThreatLevel.Medium;
            return ThreatLevel.Low;
        }

        /// <summary>
        /// 認証攻撃リスクを計算
        /// </summary>
        private ThreatLevel CalculateAuthAttackRisk(List<string> threats)
        {
            if (threats.Contains("ブルートフォース攻撃") && threats.Contains("パスワードスプレー攻撃"))
                return ThreatLevel.Critical;
            if (threats.Contains("ブルートフォース攻撃") || threats.Contains("パスワードスプレー攻撃"))
                return ThreatLevel.High;
            if (threats.Contains("異常時間帯攻撃"))
                return ThreatLevel.Medium;
            return ThreatLevel.Low;
        }

        /// <summary>
        /// 異常の説明を生成
        /// </summary>
        private string GenerateAnomalyDescription(NetworkTrafficData data, double score)
        {
            var reasons = new List<string>();

            if (data.PacketCount > 10000) reasons.Add("異常なパケット数");
            if (data.ByteCount > 1000000) reasons.Add("異常なバイト数");
            if (data.Duration > 300) reasons.Add("異常な接続時間");
            if (data.ProtocolDistribution.Any(p => p.Value > 0.8)) reasons.Add("異常なプロトコル分布");

            return $"スコア: {score:F2}, 理由: {string.Join(", ", reasons)}";
        }

        /// <summary>
        /// 推奨事項を生成
        /// </summary>
        private List<string> GenerateRecommendations(ThreatLevel threatLevel, NetworkTrafficData data)
        {
            var recommendations = new List<string>();

            switch (threatLevel)
            {
                case ThreatLevel.Critical:
                    recommendations.Add("即時ネットワーク隔離を推奨");
                    recommendations.Add("セキュリティチームに連絡");
                    break;
                case ThreatLevel.High:
                    recommendations.Add("接続を監視");
                    recommendations.Add("ファイアウォール設定を確認");
                    break;
                case ThreatLevel.Medium:
                    recommendations.Add("ログを詳細に確認");
                    recommendations.Add("追加の監視を検討");
                    break;
            }

            return recommendations;
        }

        /// <summary>
        /// 認証攻撃に対する推奨事項を生成
        /// </summary>
        private List<string> GenerateAuthRecommendations(List<string> threats)
        {
            var recommendations = new List<string>();

            if (threats.Contains("ブルートフォース攻撃"))
            {
                recommendations.Add("レート制限を強化");
                recommendations.Add("CAPTCHAを実装");
                recommendations.Add("失敗試行数の制限を設定");
            }

            if (threats.Contains("パスワードスプレー攻撃"))
            {
                recommendations.Add("パスワードポリシーを強化");
                recommendations.Add("多要素認証を必須化");
                recommendations.Add("異常なIPからのアクセスをブロック");
            }

            return recommendations;
        }

        /// <summary>
        /// サンプル訓練データを生成（開発用）
        /// </summary>
        private List<NetworkTrafficData> GenerateSampleTrainingData()
        {
            var data = new List<NetworkTrafficData>();
            var random = new Random(42);

            // 正常なトラフィックデータ
            for (int i = 0; i < 1000; i++)
            {
                data.Add(new NetworkTrafficData
                {
                    PacketCount = random.Next(100, 1000),
                    ByteCount = random.Next(10000, 100000),
                    Duration = random.Next(1, 60),
                    ProtocolDistribution = new[] { 0.6, 0.3, 0.1, 0.0 } // TCP, UDP, ICMP, Other
                });
            }

            // 異常なトラフィックデータ
            for (int i = 0; i < 100; i++)
            {
                data.Add(new NetworkTrafficData
                {
                    PacketCount = random.Next(5000, 20000),
                    ByteCount = random.Next(500000, 2000000),
                    Duration = random.Next(200, 600),
                    ProtocolDistribution = new[] { 0.9, 0.05, 0.05, 0.0 }
                });
            }

            return data;
        }

        /// <summary>
        /// 最近の異常を取得
        /// </summary>
        public List<NetworkAnomaly> GetRecentAnomalies(int count = 50)
        {
            lock (_anomalyLock)
            {
                return _recentAnomalies.Values
                    .OrderByDescending(a => a.Timestamp)
                    .Take(count)
                    .ToList();
            }
        }
    }

    // データ構造定義
    public class NetworkTrafficData
    {
        public int PacketCount { get; set; }
        public long ByteCount { get; set; }
        public int Duration { get; set; }
        public double[] ProtocolDistribution { get; set; } = new double[4]; // TCP, UDP, ICMP, Other
    }

    public class AuthAttemptData
    {
        public DateTime Timestamp { get; set; }
        public string SourceIP { get; set; }
        public string PasswordHash { get; set; }
        public bool Success { get; set; }
    }

    public class AnomalyPrediction
    {
        [ColumnName("Score")]
        public float Score { get; set; }
    }

    public class ThreatDetectionResult
    {
        public bool IsThreat { get; set; }
        public ThreatLevel ThreatLevel { get; set; }
        public double Confidence { get; set; }
        public string Description { get; set; }
        public List<string> Recommendations { get; set; } = new();
    }

    public class AuthAttackDetectionResult
    {
        public bool IsAttackDetected { get; set; }
        public List<string> Threats { get; set; } = new();
        public ThreatLevel RiskLevel { get; set; }
        public List<string> BlockedIPs { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
    }

    public class NetworkAnomaly
    {
        public DateTime Timestamp { get; set; }
        public ThreatLevel ThreatLevel { get; set; }
        public string Description { get; set; }
        public NetworkTrafficData TrafficData { get; set; }
        public double Confidence { get; set; }
    }

    public enum ThreatLevel
    {
        Low,
        Medium,
        High,
        Critical
    }
}
