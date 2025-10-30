using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// 行動ベースアクセス制御マネージャー
    /// ユーザーの行動パターンを学習してゼロトラスト認証を実現
    /// </summary>
    public class BehavioralAccessControlManager
    {
        private readonly ILogger<BehavioralAccessControlManager> _logger;
        private readonly MLContext _mlContext;
        private ITransformer _behaviorModel;
        private readonly Dictionary<string, UserBehaviorProfile> _userProfiles;
        private readonly List<UserActivityEvent> _activityHistory;

        public BehavioralAccessControlManager(ILogger<BehavioralAccessControlManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mlContext = new MLContext(seed: 0);
            _userProfiles = new Dictionary<string, UserBehaviorProfile>();
            _activityHistory = new List<UserActivityEvent>();
            InitializeBehaviorModel();
        }

        /// <summary>
        /// 行動分析モデルを初期化
        /// </summary>
        private void InitializeBehaviorModel()
        {
            // ユーザーの行動パターンを学習する機械学習モデル
            _behaviorModel = _mlContext.Transforms.Concatenate("Features",
                nameof(UserActivityEvent.HourOfDay),
                nameof(UserActivityEvent.DayOfWeek),
                nameof(UserActivityEvent.LocationRisk),
                nameof(UserActivityEvent.DeviceType),
                nameof(UserActivityEvent.NetworkType),
                nameof(UserActivityEvent.AccessPattern))
                .Append(_mlContext.AnomalyDetection.Trainers.RandomizedPcaTrainer())
                .Fit(_mlContext.Data.LoadFromEnumerable(new List<UserActivityEvent>()));
        }

        /// <summary>
        /// ユーザーの行動プロファイルを構築・更新
        /// </summary>
        public async Task<bool> BuildUserProfileAsync(string userId, List<UserActivityEvent> activities)
        {
            try
            {
                if (!_userProfiles.ContainsKey(userId))
                {
                    _userProfiles[userId] = new UserBehaviorProfile
                    {
                        UserId = userId,
                        ProfileCreatedAt = DateTime.UtcNow,
                        LastUpdatedAt = DateTime.UtcNow
                    };
                }

                var profile = _userProfiles[userId];

                // 行動パターンを分析してプロファイルを更新
                profile.NormalHours = CalculateNormalHours(activities);
                profile.NormalLocations = CalculateNormalLocations(activities);
                profile.NormalDevices = CalculateNormalDevices(activities);
                profile.RiskThreshold = CalculateRiskThreshold(activities);
                profile.BehaviorScore = CalculateBehaviorScore(activities);

                // 機械学習モデルで異常検知のベースラインを更新
                await UpdateAnomalyDetectionModelAsync(userId, activities);

                await _logger.LogInformation($"ユーザープロファイルを更新しました: {userId}", new Dictionary<string, object>
                {
                    ["userId"] = userId,
                    ["activityCount"] = activities.Count,
                    ["behaviorScore"] = profile.BehaviorScore
                });

                return true;
            }
            catch (Exception ex)
            {
                await _logger.LogError($"ユーザープロファイルの構築に失敗しました: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// アクセスリクエストを評価して承認・拒否を決定
        /// </summary>
        public async Task<AccessControlDecision> EvaluateAccessRequestAsync(AccessRequest request)
        {
            try
            {
                var decision = new AccessControlDecision
                {
                    RequestId = request.Id,
                    UserId = request.UserId,
                    EvaluatedAt = DateTime.UtcNow,
                    Status = AccessStatus.Pending
                };

                // ユーザープロファイルを取得
                if (!_userProfiles.TryGetValue(request.UserId, out var profile))
                {
                    decision.Status = AccessStatus.Denied;
                    decision.Reason = "ユーザープロファイルが見つかりません";
                    decision.RiskScore = 1.0f;
                    return decision;
                }

                // 行動パターン分析
                var riskScore = await CalculateAccessRiskScoreAsync(request, profile);

                // リスクベースの決定
                if (riskScore < profile.RiskThreshold)
                {
                    decision.Status = AccessStatus.Granted;
                    decision.RiskScore = riskScore;
                    decision.ConfidenceLevel = CalculateConfidenceLevel(riskScore);
                }
                else
                {
                    decision.Status = AccessStatus.Denied;
                    decision.Reason = "リスクスコアが閾値を超えています";
                    decision.RiskScore = riskScore;

                    // 多要素認証を要求
                    decision.Requirements.Add("追加の認証が必要です");
                    if (riskScore > 0.8)
                    {
                        decision.Requirements.Add("管理者承認が必要です");
                    }
                }

                await _logger.LogInformation($"アクセスリクエストを評価しました: {request.Id}", new Dictionary<string, object>
                {
                    ["requestId"] = request.Id,
                    ["userId"] = request.UserId,
                    ["status"] = decision.Status.ToString(),
                    ["riskScore"] = riskScore
                });

                return decision;
            }
            catch (Exception ex)
            {
                await _logger.LogError($"アクセスリクエスト評価に失敗しました: {ex.Message}", ex);

                var decision = new AccessControlDecision
                {
                    RequestId = request.Id,
                    UserId = request.UserId,
                    EvaluatedAt = DateTime.UtcNow,
                    Status = AccessStatus.Denied,
                    Reason = "評価プロセスでエラーが発生しました",
                    RiskScore = 1.0f
                };

                return decision;
            }
        }

        /// <summary>
        /// リアルタイム行動監視を実行
        /// </summary>
        public async Task<List<BehaviorAlert>> MonitorRealTimeBehaviorAsync(string userId)
        {
            var alerts = new List<BehaviorAlert>();

            try
            {
                if (!_userProfiles.TryGetValue(userId, out var profile))
                    return alerts;

                // 最近の活動を取得（実際の実装ではリアルタイムデータソースから）
                var recentActivities = _activityHistory
                    .Where(a => a.UserId == userId)
                    .OrderByDescending(a => a.Timestamp)
                    .Take(10)
                    .ToList();

                // 異常行動を検知
                foreach (var activity in recentActivities)
                {
                    var anomalyScore = await DetectAnomalousBehaviorAsync(activity, profile);

                    if (anomalyScore > 0.7) // 異常閾値
                    {
                        alerts.Add(new BehaviorAlert
                        {
                            Id = Guid.NewGuid().ToString(),
                            UserId = userId,
                            ActivityId = activity.Id,
                            AlertType = DetermineAlertType(activity, profile),
                            AnomalyScore = anomalyScore,
                            DetectedAt = DateTime.UtcNow,
                            Severity = CalculateAlertSeverity(anomalyScore),
                            Description = GenerateAlertDescription(activity, anomalyScore),
                            RecommendedActions = GenerateRecommendedActions(activity, anomalyScore)
                        });
                    }
                }

                await _logger.LogInformation($"リアルタイム行動監視を実行しました: {userId}", new Dictionary<string, object>
                {
                    ["userId"] = userId,
                    ["activityCount"] = recentActivities.Count,
                    ["alertCount"] = alerts.Count
                });

                return alerts;
            }
            catch (Exception ex)
            {
                await _logger.LogError($"リアルタイム行動監視に失敗しました: {ex.Message}", ex);
                return alerts;
            }
        }

        /// <summary>
        /// アクセスリスクスコアを計算
        /// </summary>
        private async Task<float> CalculateAccessRiskScoreAsync(AccessRequest request, UserBehaviorProfile profile)
        {
            var riskScore = 0.0f;

            // 時間帯リスク
            var hourRisk = CalculateTimeBasedRisk(request.Timestamp, profile.NormalHours);
            riskScore += hourRisk * 0.3f;

            // 場所リスク
            var locationRisk = CalculateLocationRisk(request.Location, profile.NormalLocations);
            riskScore += locationRisk * 0.25f;

            // デバイスリスク
            var deviceRisk = CalculateDeviceRisk(request.DeviceInfo, profile.NormalDevices);
            riskScore += deviceRisk * 0.2f;

            // ネットワークリスク
            var networkRisk = CalculateNetworkRisk(request.NetworkInfo);
            riskScore += networkRisk * 0.15f;

            // 行動パターンリスク
            var patternRisk = CalculatePatternRisk(request.AccessPattern, profile);
            riskScore += patternRisk * 0.1f;

            return Math.Min(riskScore, 1.0f);
        }

        /// <summary>
        /// 異常行動を検知
        /// </summary>
        private async Task<float> DetectAnomalousBehaviorAsync(UserActivityEvent activity, UserBehaviorProfile profile)
        {
            // 機械学習モデルで異常スコアを計算
            var predictionEngine = _mlContext.Model.CreatePredictionEngine<UserActivityEvent, AnomalyPrediction>(this._behaviorModel);
            var prediction = predictionEngine.Predict(activity);

            return prediction.AnomalyScore;
        }

        /// <summary>
        /// 通常の活動時間を計算
        /// </summary>
        private List<int> CalculateNormalHours(List<UserActivityEvent> activities)
        {
            return activities
                .GroupBy(a => a.HourOfDay)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => g.Key)
                .ToList();
        }

        /// <summary>
        /// 通常の場所を計算
        /// </summary>
        private List<string> CalculateNormalLocations(List<UserActivityEvent> activities)
        {
            return activities
                .GroupBy(a => a.Location)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .Select(g => g.Key)
                .ToList();
        }

        /// <summary>
        /// 通常のデバイスを計算
        /// </summary>
        private List<string> CalculateNormalDevices(List<UserActivityEvent> activities)
        {
            return activities
                .GroupBy(a => a.DeviceType)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .Select(g => g.Key)
                .ToList();
        }

        /// <summary>
        /// リスク閾値を計算
        /// </summary>
        private float CalculateRiskThreshold(List<UserActivityEvent> activities)
        {
            // 活動の多様性に基づいて閾値を設定
            var uniqueHours = activities.Select(a => a.HourOfDay).Distinct().Count();
            var uniqueLocations = activities.Select(a => a.Location).Distinct().Count();

            var baseThreshold = 0.7f;
            var diversityBonus = (uniqueHours + uniqueLocations) / 20.0f;

            return Math.Min(baseThreshold + diversityBonus * 0.1f, 0.9f);
        }

        /// <summary>
        /// 行動スコアを計算
        /// </summary>
        private float CalculateBehaviorScore(List<UserActivityEvent> activities)
        {
            // 一貫性と予測可能性に基づくスコア
            var consistencyScore = CalculateConsistencyScore(activities);
            var predictabilityScore = CalculatePredictabilityScore(activities);

            return (consistencyScore + predictabilityScore) / 2.0f;
        }

        /// <summary>
        /// 一貫性スコアを計算
        /// </summary>
        private float CalculateConsistencyScore(List<UserActivityEvent> activities)
        {
            // 時間帯と場所の一貫性を評価
            var hourVariance = activities.GroupBy(a => a.HourOfDay).Count();
            var locationVariance = activities.GroupBy(a => a.Location).Count();

            return Math.Max(0, 1.0f - (hourVariance + locationVariance) / 20.0f);
        }

        /// <summary>
        /// 予測可能性スコアを計算
        /// </summary>
        private float CalculatePredictabilityScore(List<UserActivityEvent> activities)
        {
            // パターンの繰り返しを評価
            var patterns = activities
                .OrderBy(a => a.Timestamp)
                .Select(a => $"{a.HourOfDay}-{a.DayOfWeek}-{a.Location}")
                .ToList();

            var uniquePatterns = patterns.Distinct().Count();
            return Math.Max(0, 1.0f - uniquePatterns / (float)patterns.Count);
        }

        /// <summary>
        /// 時間ベースのリスクを計算
        /// </summary>
        private float CalculateTimeBasedRisk(DateTime timestamp, List<int> normalHours)
        {
            var hour = timestamp.Hour;
            return normalHours.Contains(hour) ? 0.1f : 0.8f;
        }

        /// <summary>
        /// 場所リスクを計算
        /// </summary>
        private float CalculateLocationRisk(string location, List<string> normalLocations)
        {
            return normalLocations.Contains(location) ? 0.1f : 0.7f;
        }

        /// <summary>
        /// デバイスリスクを計算
        /// </summary>
        private float CalculateDeviceRisk(string deviceInfo, List<string> normalDevices)
        {
            return normalDevices.Contains(deviceInfo) ? 0.1f : 0.6f;
        }

        /// <summary>
        /// ネットワークリスクを計算
        /// </summary>
        private float CalculateNetworkRisk(string networkInfo)
        {
            // ネットワークタイプに基づくリスク評価
            return networkInfo.Contains("Public") ? 0.8f : 0.2f;
        }

        /// <summary>
        /// パターンリスクを計算
        /// </summary>
        private float CalculatePatternRisk(string accessPattern, UserBehaviorProfile profile)
        {
            // アクセスパターンの異常性を評価
            return 0.3f; // 簡易的な実装
        }

        /// <summary>
        /// 信頼レベルを計算
        /// </summary>
        private ConfidenceLevel CalculateConfidenceLevel(float riskScore)
        {
            if (riskScore < 0.3) return ConfidenceLevel.High;
            if (riskScore < 0.6) return ConfidenceLevel.Medium;
            return ConfidenceLevel.Low;
        }

        /// <summary>
        /// アラートタイプを判定
        /// </summary>
        private BehaviorAlertType DetermineAlertType(UserActivityEvent activity, UserBehaviorProfile profile)
        {
            if (activity.HourOfDay < 6 || activity.HourOfDay > 22)
                return BehaviorAlertType.UnusualTime;

            if (!profile.NormalLocations.Contains(activity.Location))
                return BehaviorAlertType.UnusualLocation;

            if (!profile.NormalDevices.Contains(activity.DeviceType))
                return BehaviorAlertType.UnusualDevice;

            return BehaviorAlertType.AnomalousPattern;
        }

        /// <summary>
        /// アラートの重要度を計算
        /// </summary>
        private AlertSeverity CalculateAlertSeverity(float anomalyScore)
        {
            if (anomalyScore > 0.9) return AlertSeverity.Critical;
            if (anomalyScore > 0.7) return AlertSeverity.High;
            if (anomalyScore > 0.5) return AlertSeverity.Medium;
            return AlertSeverity.Low;
        }

        /// <summary>
        /// アラート説明を生成
        /// </summary>
        private string GenerateAlertDescription(UserActivityEvent activity, float anomalyScore)
        {
            return $"異常な行動パターンを検知しました。異常スコア: {anomalyScore:F2}";
        }

        /// <summary>
        /// 推奨アクションを生成
        /// </summary>
        private List<string> GenerateRecommendedActions(UserActivityEvent activity, float anomalyScore)
        {
            var actions = new List<string> { "ユーザーの行動を確認してください" };

            if (anomalyScore > 0.8)
            {
                actions.Add("アクセスを一時的に制限してください");
                actions.Add("追加の認証を要求してください");
            }

            return actions;
        }

        /// <summary>
        /// 異常検知モデルを更新
        /// </summary>
        private async Task UpdateAnomalyDetectionModelAsync(string userId, List<UserActivityEvent> activities)
        {
            try
            {
                var trainingData = _mlContext.Data.LoadFromEnumerable(activities);
                var pipeline = _mlContext.Transforms.Concatenate("Features",
                    nameof(UserActivityEvent.HourOfDay),
                    nameof(UserActivityEvent.DayOfWeek),
                    nameof(UserActivityEvent.LocationRisk),
                    nameof(UserActivityEvent.DeviceType),
                    nameof(UserActivityEvent.NetworkType),
                    nameof(UserActivityEvent.AccessPattern))
                    .Append(_mlContext.AnomalyDetection.Trainers.RandomizedPcaTrainer());

                _behaviorModel = pipeline.Fit(trainingData);

                await _logger.LogInformation($"異常検知モデルを更新しました: {userId}");
            }
            catch (Exception ex)
            {
                await _logger.LogError($"異常検知モデルの更新に失敗しました: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// ユーザーの行動プロファイル
    /// </summary>
    public class UserBehaviorProfile
    {
        public string UserId { get; set; } = "";
        public DateTime ProfileCreatedAt { get; set; }
        public DateTime LastUpdatedAt { get; set; }
        public List<int> NormalHours { get; set; } = new();
        public List<string> NormalLocations { get; set; } = new();
        public List<string> NormalDevices { get; set; } = new();
        public float RiskThreshold { get; set; } = 0.7f;
        public float BehaviorScore { get; set; } = 0.5f;
        public Dictionary<string, object> AdditionalMetrics { get; set; } = new();
    }

    /// <summary>
    /// ユーザー活動イベント
    /// </summary>
    public class UserActivityEvent
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string UserId { get; set; } = "";
        public int HourOfDay { get; set; }
        public int DayOfWeek { get; set; }
        public string Location { get; set; } = "";
        public float LocationRisk { get; set; }
        public string DeviceType { get; set; } = "";
        public string NetworkType { get; set; } = "";
        public string AccessPattern { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// アクセスリクエスト
    /// </summary>
    public class AccessRequest
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string UserId { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Location { get; set; } = "";
        public string DeviceInfo { get; set; } = "";
        public string NetworkInfo { get; set; } = "";
        public string AccessPattern { get; set; } = "";
        public Dictionary<string, object> Context { get; set; } = new();
    }

    /// <summary>
    /// アクセス制御決定
    /// </summary>
    public class AccessControlDecision
    {
        public string RequestId { get; set; } = "";
        public string UserId { get; set; } = "";
        public DateTime EvaluatedAt { get; set; }
        public AccessStatus Status { get; set; }
        public string Reason { get; set; } = "";
        public float RiskScore { get; set; }
        public ConfidenceLevel ConfidenceLevel { get; set; }
        public List<string> Requirements { get; set; } = new();
    }

    /// <summary>
    /// 行動アラート
    /// </summary>
    public class BehaviorAlert
    {
        public string Id { get; set; } = "";
        public string UserId { get; set; } = "";
        public string ActivityId { get; set; } = "";
        public BehaviorAlertType AlertType { get; set; }
        public float AnomalyScore { get; set; }
        public DateTime DetectedAt { get; set; }
        public AlertSeverity Severity { get; set; }
        public string Description { get; set; } = "";
        public List<string> RecommendedActions { get; set; } = new();
    }

    /// <summary>
    /// アクセス状態
    /// </summary>
    public enum AccessStatus
    {
        Pending,
        Granted,
        Denied,
        RequiresApproval,
        RequiresMFA
    }

    /// <summary>
    /// 信頼レベル
    /// </summary>
    public enum ConfidenceLevel
    {
        Low,
        Medium,
        High
    }

    /// <summary>
    /// 行動アラートタイプ
    /// </summary>
    public enum BehaviorAlertType
    {
        UnusualTime,
        UnusualLocation,
        UnusualDevice,
        AnomalousPattern,
        SuspiciousActivity
    }

    /// <summary>
    /// アラートの重要度
    /// </summary>
    public enum AlertSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    /// <summary>
    /// ML.NET用の異常予測クラス（内部使用）
    /// </summary>
    internal class AnomalyPrediction
    {
        public float AnomalyScore { get; set; }
    }
}
