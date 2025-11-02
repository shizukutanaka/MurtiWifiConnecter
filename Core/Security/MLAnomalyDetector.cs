using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core.Security
{
    /// <summary>
    /// 機械学習ベースの異常検知システム
    /// ユーザーの行動パターンを学習し、異常を検知する
    /// </summary>
    public class MLAnomalyDetector
    {
        private readonly Dictionary<string, UserBehaviorProfile> _userProfiles = new();
        private readonly List<SecurityEvent> _globalEvents = new();
        private readonly object _lockObject = new();
        private readonly int _maxEventsPerUser = 1000;
        private readonly double _anomalyThreshold = 0.7;

        /// <summary>
        /// ユーザーの行動を記録する
        /// </summary>
        public void RecordUserActivity(string userId, string operation, Dictionary<string, object> context)
        {
            lock (_lockObject)
            {
                if (!_userProfiles.ContainsKey(userId))
                {
                    _userProfiles[userId] = new UserBehaviorProfile(userId);
                }

                var profile = _userProfiles[userId];
                var activity = new UserActivity
                {
                    Operation = operation,
                    Context = context,
                    TimestampUtc = DateTime.UtcNow,
                    DayOfWeek = DateTime.UtcNow.DayOfWeek,
                    HourOfDay = DateTime.UtcNow.Hour
                };

                profile.Activities.Add(activity);

                // 古い活動を削除（最大保持数を超えた場合）
                if (profile.Activities.Count > _maxEventsPerUser)
                {
                    profile.Activities.RemoveRange(0, profile.Activities.Count - _maxEventsPerUser);
                }

                // グローバルイベントリストにも追加
                _globalEvents.Add(new SecurityEvent
                {
                    TimestampUtc = DateTime.UtcNow,
                    Operation = operation,
                    UserId = userId,
                    Context = context
                });

                // グローバルイベントリストも制限
                if (_globalEvents.Count > 10000)
                {
                    _globalEvents.RemoveRange(0, _globalEvents.Count - 5000);
                }
            }
        }

        /// <summary>
        /// 指定された活動が異常かどうかを判定する
        /// </summary>
        public async Task<AnomalyDetectionResult> DetectAnomalyAsync(string userId, string operation, Dictionary<string, object> context)
        {
            lock (_lockObject)
            {
                if (!_userProfiles.ContainsKey(userId) || !_userProfiles[userId].Activities.Any())
                {
                    return new AnomalyDetectionResult
                    {
                        IsAnomalous = false,
                        ConfidenceScore = 0.0,
                        Reason = "Insufficient data for analysis"
                    };
                }

                var profile = _userProfiles[userId];
                var currentActivity = new UserActivity
                {
                    Operation = operation,
                    Context = context,
                    TimestampUtc = DateTime.UtcNow,
                    DayOfWeek = DateTime.UtcNow.DayOfWeek,
                    HourOfDay = DateTime.UtcNow.Hour
                };

                // 複数の異常検知アルゴリズムを適用
                var scores = new List<double>
                {
                    CalculateTimeBasedAnomalyScore(profile, currentActivity),
                    CalculateFrequencyBasedAnomalyScore(profile, currentActivity),
                    CalculateContextBasedAnomalyScore(profile, currentActivity),
                    CalculateGlobalAnomalyScore(currentActivity)
                };

                var averageScore = scores.Average();
                var isAnomalous = averageScore > _anomalyThreshold;

                return new AnomalyDetectionResult
                {
                    IsAnomalous = isAnomalous,
                    ConfidenceScore = averageScore,
                    Reason = isAnomalous ? $"Anomaly detected with score {averageScore:F2}" : "Normal activity",
                    ContributingFactors = GetContributingFactors(scores)
                };
            }
        }

        /// <summary>
        /// 行動プロファイルを更新する
        /// </summary>
        public async Task UpdateBehaviorProfilesAsync()
        {
            lock (_lockObject)
            {
                foreach (var profile in _userProfiles.Values)
                {
                    profile.UpdateProfile();
                }
            }
        }

        private double CalculateTimeBasedAnomalyScore(UserBehaviorProfile profile, UserActivity activity)
        {
            // 時間帯ベースの異常スコア計算
            var hourActivities = profile.Activities.Where(a => a.HourOfDay == activity.HourOfDay);
            var dayActivities = profile.Activities.Where(a => a.DayOfWeek == activity.DayOfWeek);

            if (!hourActivities.Any() || !dayActivities.Any())
            {
                return 0.8; // データ不足時は高いスコアを返す
            }

            // 時間帯の頻度を計算
            var hourFrequency = (double)hourActivities.Count() / profile.Activities.Count;
            var dayFrequency = (double)dayActivities.Count() / profile.Activities.Count;

            // 通常とは異なる時間帯の活動は異常
            return Math.Max(0, 1.0 - (hourFrequency + dayFrequency) / 2.0);
        }

        private double CalculateFrequencyBasedAnomalyScore(UserBehaviorProfile profile, UserActivity activity)
        {
            // 操作頻度ベースの異常スコア計算
            var operationActivities = profile.Activities.Where(a => a.Operation == activity.Operation);
            var operationFrequency = (double)operationActivities.Count() / profile.Activities.Count;

            // 頻度が極端に高いまたは低い場合は異常
            if (operationFrequency < 0.01 || operationFrequency > 0.5)
            {
                return Math.Min(operationFrequency * 10, 1.0);
            }

            return 0.0;
        }

        private double CalculateContextBasedAnomalyScore(UserBehaviorProfile profile, UserActivity activity)
        {
            // コンテキストベースの異常スコア計算
            var similarActivities = profile.Activities.Where(a =>
                a.Operation == activity.Operation &&
                Math.Abs((a.TimestampUtc - activity.TimestampUtc).TotalHours) < 24);

            if (!similarActivities.Any())
            {
                return 0.5; // 類似活動がない場合は中程度のスコア
            }

            // コンテキストの類似性を計算
            var contextSimilarity = CalculateContextSimilarity(activity.Context, similarActivities);
            return Math.Max(0, 1.0 - contextSimilarity);
        }

        private double CalculateGlobalAnomalyScore(UserActivity activity)
        {
            // グローバルな異常スコア計算
            var recentGlobalEvents = _globalEvents.Where(e =>
                e.TimestampUtc > DateTime.UtcNow.AddMinutes(-30));

            var similarGlobalEvents = recentGlobalEvents.Where(e =>
                e.Operation == activity.Operation);

            if (similarGlobalEvents.Count() > 10) // グローバルで頻発している場合
            {
                return 0.8;
            }

            return 0.0;
        }

        private double CalculateContextSimilarity(Dictionary<string, object> context1, IEnumerable<UserActivity> activities)
        {
            var similarities = new List<double>();

            foreach (var activity in activities)
            {
                var similarity = 0.0;
                var commonKeys = context1.Keys.Intersect(activity.Context.Keys);

                foreach (var key in commonKeys)
                {
                    if (context1[key].Equals(activity.Context[key]))
                    {
                        similarity += 1.0;
                    }
                }

                similarity /= Math.Max(context1.Count, activity.Context.Count);
                similarities.Add(similarity);
            }

            return similarities.Any() ? similarities.Average() : 0.0;
        }

        private List<string> GetContributingFactors(List<double> scores)
        {
            var factors = new List<string>();

            if (scores[0] > 0.5) factors.Add("Unusual time pattern");
            if (scores[1] > 0.5) factors.Add("Unusual frequency");
            if (scores[2] > 0.5) factors.Add("Unusual context");
            if (scores[3] > 0.5) factors.Add("Global anomaly pattern");

            return factors;
        }

        /// <summary>
        /// ユーザーの行動プロファイル
        /// </summary>
        private class UserBehaviorProfile
        {
            public string UserId { get; }
            public List<UserActivity> Activities { get; } = new();
            public Dictionary<string, double> OperationFrequencies { get; } = new();
            public Dictionary<int, double> HourFrequencies { get; } = new();
            public Dictionary<DayOfWeek, double> DayFrequencies { get; } = new();

            public UserBehaviorProfile(string userId)
            {
                UserId = userId;
            }

            public void UpdateProfile()
            {
                if (!Activities.Any()) return;

                // 操作頻度の更新
                OperationFrequencies.Clear();
                foreach (var activity in Activities)
                {
                    if (!OperationFrequencies.ContainsKey(activity.Operation))
                    {
                        OperationFrequencies[activity.Operation] = 0;
                    }
                    OperationFrequencies[activity.Operation]++;
                }

                // 正規化
                var totalOperations = OperationFrequencies.Sum(kvp => kvp.Value);
                foreach (var key in OperationFrequencies.Keys.ToList())
                {
                    OperationFrequencies[key] /= totalOperations;
                }

                // 時間帯頻度の更新
                HourFrequencies.Clear();
                foreach (var activity in Activities)
                {
                    if (!HourFrequencies.ContainsKey(activity.HourOfDay))
                    {
                        HourFrequencies[activity.HourOfDay] = 0;
                    }
                    HourFrequencies[activity.HourOfDay]++;
                }

                // 正規化
                var totalHours = HourFrequencies.Sum(kvp => kvp.Value);
                foreach (var key in HourFrequencies.Keys.ToList())
                {
                    HourFrequencies[key] /= totalHours;
                }

                // 曜日頻度の更新
                DayFrequencies.Clear();
                foreach (var activity in Activities)
                {
                    if (!DayFrequencies.ContainsKey(activity.DayOfWeek))
                    {
                        DayFrequencies[activity.DayOfWeek] = 0;
                    }
                    DayFrequencies[activity.DayOfWeek]++;
                }

                // 正規化
                var totalDays = DayFrequencies.Sum(kvp => kvp.Value);
                foreach (var key in DayFrequencies.Keys.ToList())
                {
                    DayFrequencies[key] /= totalDays;
                }
            }
        }

        /// <summary>
        /// ユーザーの活動記録
        /// </summary>
        private class UserActivity
        {
            public string Operation { get; set; } = "";
            public Dictionary<string, object> Context { get; set; } = new();
            public DateTime TimestampUtc { get; set; }
            public DayOfWeek DayOfWeek { get; set; }
            public int HourOfDay { get; set; }
        }

        /// <summary>
        /// セキュリティイベント
        /// </summary>
        private class SecurityEvent
        {
            public DateTime TimestampUtc { get; set; }
            public string Operation { get; set; } = "";
            public string UserId { get; set; } = "";
            public Dictionary<string, object> Context { get; set; } = new();
        }

        /// <summary>
        /// 異常検知結果
        /// </summary>
        public class AnomalyDetectionResult
        {
            public bool IsAnomalous { get; set; }
            public double ConfidenceScore { get; set; }
            public string Reason { get; set; } = "";
            public List<string> ContributingFactors { get; set; } = new();
        }
    }
}
