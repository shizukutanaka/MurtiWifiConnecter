using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// 動的ポリシー適応マネージャー - リアルタイム脅威状況に応じたポリシー自動調整
    /// </summary>
    public static class DynamicPolicyManager
    {
        private static readonly Dictionary<string, DynamicPolicy> _dynamicPolicies = new();
        private static readonly List<PolicyAdaptation> _adaptationHistory = new();
        private static readonly Timer _adaptationTimer;
        private static readonly object _policyLock = new();
        private static readonly object _historyLock = new();

        static DynamicPolicyManager()
        {
            InitializeDefaultPolicies();
            _adaptationTimer = new Timer(AdaptPoliciesCallback, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        }

        /// <summary>
        /// 動的ポリシーを評価・適用
        /// </summary>
        public static async Task<PolicyAdaptationResult> EvaluateAndAdaptPolicyAsync(
            string operationType,
            Dictionary<string, object> context,
            double currentRiskScore)
        {
            var evaluationId = Guid.NewGuid().ToString();
            var startTime = DateTime.UtcNow;

            try
            {
                // 現在の脅威状況を評価
                var threatSituation = await EvaluateThreatSituationAsync(context);

                // ポリシーを取得または作成
                var policy = GetOrCreateDynamicPolicy(operationType);

                // 適応が必要か判断
                var adaptationNeeded = IsAdaptationNeeded(policy, threatSituation, currentRiskScore);

                PolicyAdaptationResult result;

                if (adaptationNeeded)
                {
                    // ポリシーを適応
                    var adaptation = await AdaptPolicyAsync(policy, threatSituation, context);
                    result = new PolicyAdaptationResult
                    {
                        EvaluationId = evaluationId,
                        OperationType = operationType,
                        Adapted = true,
                        OriginalRiskThreshold = policy.OriginalRiskThreshold,
                        NewRiskThreshold = adaptation.NewThreshold,
                        AdaptationReason = adaptation.Reason,
                        AdaptationLevel = adaptation.Level,
                        ExpectedImprovement = CalculateExpectedImprovement(policy, adaptation)
                    };

                    // 適応を記録
                    RecordAdaptation(adaptation, context, evaluationId);
                }
                else
                {
                    result = new PolicyAdaptationResult
                    {
                        EvaluationId = evaluationId,
                        OperationType = operationType,
                        Adapted = false,
                        OriginalRiskThreshold = policy.CurrentRiskThreshold,
                        NewRiskThreshold = policy.CurrentRiskThreshold
                    };
                }

                result.EvaluationTime = DateTime.UtcNow - startTime;
                return result;
            }
            catch (Exception ex)
            {
                await Logger.LogError($"Dynamic policy adaptation failed: {ex.Message}",
                    nameof(DynamicPolicyManager), null, ex);

                return new PolicyAdaptationResult
                {
                    EvaluationId = evaluationId,
                    OperationType = operationType,
                    Adapted = false,
                    ErrorMessage = ex.Message,
                    EvaluationTime = DateTime.UtcNow - startTime
                };
            }
        }

        /// <summary>
        /// 高度な適応 - 複数の適応戦略を組み合わせ
        /// </summary>
        public static async Task<AdvancedAdaptationResult> PerformAdvancedAdaptationAsync(
            string operationType,
            Dictionary<string, object> context,
            ThreatSituation threatSituation,
            AdaptationStrategy strategy = AdaptationStrategy.Balanced)
        {
            var result = new AdvancedAdaptationResult
            {
                OperationType = operationType,
                Strategy = strategy
            };

            // 複数の適応メカニズムを適用
            var adaptations = new List<PolicyAdaptation>();

            // 1. リスクベース適応
            if (strategy.HasFlag(AdaptationStrategy.RiskBased))
            {
                var riskAdaptation = await AdaptBasedOnRiskAsync(operationType, threatSituation);
                if (riskAdaptation != null) adaptations.Add(riskAdaptation);
            }

            // 2. パターンベース適応
            if (strategy.HasFlag(AdaptationStrategy.PatternBased))
            {
                var patternAdaptation = await AdaptBasedOnPatternsAsync(operationType, context);
                if (patternAdaptation != null) adaptations.Add(patternAdaptation);
            }

            // 3. リソースベース適応
            if (strategy.HasFlag(AdaptationStrategy.ResourceBased))
            {
                var resourceAdaptation = await AdaptBasedOnResourcesAsync(operationType);
                if (resourceAdaptation != null) adaptations.Add(resourceAdaptation);
            }

            // 適応結果を統合
            result.Adaptations = adaptations;
            result.OverallAdaptationLevel = adaptations.Any() ?
                adaptations.Max(a => (int)a.Level) : AdaptationLevel.None;

            // 適応の有効性を評価
            result.ExpectedEffectiveness = CalculateAdaptationEffectiveness(adaptations, threatSituation);

            return result;
        }

        /// <summary>
        /// ポリシー適応をロールバック
        /// </summary>
        public static bool RollbackAdaptation(string adaptationId)
        {
            lock (_policyLock)
            {
                var adaptation = _adaptationHistory.FirstOrDefault(a => a.AdaptationId == adaptationId);
                if (adaptation == null) return false;

                var policy = GetOrCreateDynamicPolicy(adaptation.OperationType);
                policy.CurrentRiskThreshold = adaptation.OldThreshold;

                adaptation.RolledBack = true;
                adaptation.RollbackTime = DateTime.UtcNow;

                return true;
            }
        }

        /// <summary>
        /// 適応履歴を取得
        /// </summary>
        public static IReadOnlyList<PolicyAdaptation> GetAdaptationHistory(string operationType = null, int maxEntries = 50)
        {
            lock (_historyLock)
            {
                var query = _adaptationHistory.AsQueryable();

                if (!string.IsNullOrEmpty(operationType))
                {
                    query = query.Where(a => a.OperationType == operationType);
                }

                return query
                    .OrderByDescending(a => a.Timestamp)
                    .Take(maxEntries)
                    .ToList();
            }
        }

        /// <summary>
        /// ポリシー適応統計を取得
        /// </summary>
        public static PolicyAdaptationStatistics GetAdaptationStatistics(TimeSpan timeWindow)
        {
            var cutoffTime = DateTime.UtcNow - timeWindow;

            lock (_historyLock)
            {
                var relevantAdaptations = _adaptationHistory
                    .Where(a => a.Timestamp >= cutoffTime)
                    .ToList();

                return new PolicyAdaptationStatistics
                {
                    TimeWindow = timeWindow,
                    TotalAdaptations = relevantAdaptations.Count,
                    SuccessfulAdaptations = relevantAdaptations.Count(a => a.Success),
                    FailedAdaptations = relevantAdaptations.Count(a => !a.Success),
                    AverageEffectiveness = relevantAdaptations.Any() ?
                        relevantAdaptations.Average(a => a.Effectiveness) : 0,
                    MostAdaptedOperation = relevantAdaptations
                        .GroupBy(a => a.OperationType)
                        .OrderByDescending(g => g.Count())
                        .FirstOrDefault()?.Key
                };
            }
        }

        private static void InitializeDefaultPolicies()
        {
            // デフォルトの動的ポリシーを初期化
            var defaultPolicies = new[]
            {
                new { Type = "network_connect", BaseThreshold = 0.3, Adaptability = 0.7 },
                new { Type = "credential_access", BaseThreshold = 0.2, Adaptability = 0.8 },
                new { Type = "command_execution", BaseThreshold = 0.4, Adaptability = 0.6 },
                new { Type = "file_operation", BaseThreshold = 0.3, Adaptability = 0.5 },
                new { Type = "config_change", BaseThreshold = 0.25, Adaptability = 0.9 }
            };

            foreach (var policy in defaultPolicies)
            {
                _dynamicPolicies[policy.Type] = new DynamicPolicy
                {
                    OperationType = policy.Type,
                    OriginalRiskThreshold = policy.BaseThreshold,
                    CurrentRiskThreshold = policy.BaseThreshold,
                    AdaptabilityFactor = policy.Adaptability,
                    LastAdapted = DateTime.MinValue,
                    AdaptationCount = 0
                };
            }
        }

        private static async Task<ThreatSituation> EvaluateThreatSituationAsync(Dictionary<string, object> context)
        {
            var situation = new ThreatSituation();

            // 脅威レベルを評価
            situation.OverallThreatLevel = await CalculateThreatLevelAsync(context);

            // 脅威カテゴリを特定
            situation.ThreatCategories = await IdentifyThreatCategoriesAsync(context);

            // 脅威の持続時間を評価
            situation.ThreatDuration = await EstimateThreatDurationAsync(context);

            // 影響範囲を評価
            situation.ImpactScope = await AssessImpactScopeAsync(context);

            return situation;
        }

        private static bool IsAdaptationNeeded(DynamicPolicy policy, ThreatSituation threatSituation, double currentRiskScore)
        {
            // 適応が必要な条件をチェック
            var timeSinceLastAdaptation = DateTime.UtcNow - policy.LastAdapted;

            // 脅威レベルが高い場合
            if (threatSituation.OverallThreatLevel >= ThreatLevel.High)
                return true;

            // リスクスコアが閾値を超えている場合
            if (currentRiskScore > policy.CurrentRiskThreshold * 1.2)
                return true;

            // 最後の適応から一定時間が経過し、脅威状況が変化している場合
            if (timeSinceLastAdaptation > TimeSpan.FromMinutes(10) &&
                threatSituation.OverallThreatLevel >= ThreatLevel.Medium)
                return true;

            // 適応回数が少なすぎる場合（学習不足）
            if (policy.AdaptationCount < 3 && timeSinceLastAdaptation > TimeSpan.FromHours(1))
                return true;

            return false;
        }

        private static async Task<PolicyAdaptation> AdaptPolicyAsync(
            DynamicPolicy policy,
            ThreatSituation threatSituation,
            Dictionary<string, object> context)
        {
            var oldThreshold = policy.CurrentRiskThreshold;
            var adaptation = new PolicyAdaptation
            {
                AdaptationId = Guid.NewGuid().ToString(),
                OperationType = policy.OperationType,
                Timestamp = DateTime.UtcNow,
                OldThreshold = oldThreshold,
                Reason = GenerateAdaptationReason(threatSituation),
                ThreatLevel = threatSituation.OverallThreatLevel
            };

            // 適応レベルを決定
            adaptation.Level = DetermineAdaptationLevel(threatSituation, context);

            // 新しい閾値を計算
            var thresholdAdjustment = CalculateThresholdAdjustment(policy, threatSituation, adaptation.Level);
            adaptation.NewThreshold = Math.Max(0.1, Math.Min(0.9, oldThreshold + thresholdAdjustment));

            // ポリシーを更新
            policy.CurrentRiskThreshold = adaptation.NewThreshold;
            policy.LastAdapted = DateTime.UtcNow;
            policy.AdaptationCount++;

            // 適応の有効性を評価
            adaptation.Effectiveness = await EvaluateAdaptationEffectivenessAsync(adaptation, context);
            adaptation.Success = adaptation.Effectiveness > 0.5;

            return adaptation;
        }

        private static async Task<PolicyAdaptation> AdaptBasedOnRiskAsync(string operationType, ThreatSituation threatSituation)
        {
            var policy = GetOrCreateDynamicPolicy(operationType);
            var riskAdjustment = (int)threatSituation.OverallThreatLevel * 0.1;

            return new PolicyAdaptation
            {
                AdaptationId = Guid.NewGuid().ToString(),
                OperationType = operationType,
                Timestamp = DateTime.UtcNow,
                OldThreshold = policy.CurrentRiskThreshold,
                NewThreshold = Math.Max(0.1, Math.Min(0.9, policy.CurrentRiskThreshold + riskAdjustment)),
                Reason = $"Risk-based adaptation for threat level {threatSituation.OverallThreatLevel}",
                Level = AdaptationLevel.Moderate,
                ThreatLevel = threatSituation.OverallThreatLevel,
                Success = true,
                Effectiveness = 0.7
            };
        }

        private static async Task<PolicyAdaptation> AdaptBasedOnPatternsAsync(string operationType, Dictionary<string, object> context)
        {
            // パターン分析に基づく適応（簡易実装）
            var anomalyScore = context.ContainsKey("anomaly_score") ?
                Convert.ToDouble(context["anomaly_score"]) : 0.0;

            if (anomalyScore > 0.7)
            {
                var policy = GetOrCreateDynamicPolicy(operationType);
                return new PolicyAdaptation
                {
                    AdaptationId = Guid.NewGuid().ToString(),
                    OperationType = operationType,
                    Timestamp = DateTime.UtcNow,
                    OldThreshold = policy.CurrentRiskThreshold,
                    NewThreshold = Math.Max(0.1, policy.CurrentRiskThreshold - 0.1),
                    Reason = $"Pattern-based adaptation for anomaly score {anomalyScore}",
                    Level = AdaptationLevel.Conservative,
                    Success = true,
                    Effectiveness = 0.6
                };
            }

            return null;
        }

        private static async Task<PolicyAdaptation> AdaptBasedOnResourcesAsync(string operationType)
        {
            // リソース使用量に基づく適応
            var memoryUsage = PerformanceMonitor.TakeMemorySnapshot("ResourceCheck");
            var cpuUsage = PerformanceMonitor.GetCpuUsage();

            if (memoryUsage.WorkingSet > 100 * 1024 * 1024 || cpuUsage > 80) // 100MB or 80% CPU
            {
                var policy = GetOrCreateDynamicPolicy(operationType);
                return new PolicyAdaptation
                {
                    AdaptationId = Guid.NewGuid().ToString(),
                    OperationType = operationType,
                    Timestamp = DateTime.UtcNow,
                    OldThreshold = policy.CurrentRiskThreshold,
                    NewThreshold = Math.Min(0.8, policy.CurrentRiskThreshold + 0.1),
                    Reason = $"Resource-based adaptation: Memory {memoryUsage.WorkingSet / 1024 / 1024}MB, CPU {cpuUsage}%",
                    Level = AdaptationLevel.Aggressive,
                    Success = true,
                    Effectiveness = 0.8
                };
            }

            return null;
        }

        private static DynamicPolicy GetOrCreateDynamicPolicy(string operationType)
        {
            lock (_policyLock)
            {
                if (!_dynamicPolicies.TryGetValue(operationType, out var policy))
                {
                    policy = new DynamicPolicy
                    {
                        OperationType = operationType,
                        OriginalRiskThreshold = 0.3,
                        CurrentRiskThreshold = 0.3,
                        AdaptabilityFactor = 0.7,
                        LastAdapted = DateTime.MinValue,
                        AdaptationCount = 0
                    };
                    _dynamicPolicies[operationType] = policy;
                }
                return policy;
            }
        }

        private static void RecordAdaptation(PolicyAdaptation adaptation, Dictionary<string, object> context, string evaluationId)
        {
            lock (_historyLock)
            {
                _adaptationHistory.Add(adaptation);

                // 履歴サイズを制限（最新500件）
                if (_adaptationHistory.Count > 500)
                {
                    _adaptationHistory.RemoveRange(0, _adaptationHistory.Count - 500);
                }
            }
        }

        private static void AdaptPoliciesCallback(object state)
        {
            Task.Run(async () =>
            {
                try
                {
                    await PerformGlobalPolicyAdaptationAsync();
                }
                catch (Exception ex)
                {
                    await Logger.LogError($"Policy adaptation callback failed: {ex.Message}",
                        nameof(DynamicPolicyManager), null, ex);
                }
            });
        }

        private static async Task PerformGlobalPolicyAdaptationAsync()
        {
            // グローバルなポリシー適応を実行
            var threatSituation = await EvaluateGlobalThreatSituationAsync();

            if (threatSituation.OverallThreatLevel >= ThreatLevel.Medium)
            {
                // すべてのポリシーを適応
                foreach (var policy in _dynamicPolicies.Values.ToList())
                {
                    var context = new Dictionary<string, object>
                    {
                        ["global_threat_level"] = threatSituation.OverallThreatLevel,
                        ["threat_duration"] = threatSituation.ThreatDuration.TotalMinutes
                    };

                    await EvaluateAndAdaptPolicyAsync(policy.OperationType, context, policy.CurrentRiskThreshold);
                }
            }
        }

        // ヘルパーメソッド
        private static async Task<ThreatLevel> CalculateThreatLevelAsync(Dictionary<string, object> context)
        {
            var threatScore = 0.0;

            if (context.TryGetValue("threat_level", out var threatLevelObj) &&
                Enum.TryParse<ThreatLevel>(threatLevelObj.ToString(), out var threatLevel))
            {
                threatScore += (int)threatLevel * 0.3;
            }

            if (context.TryGetValue("anomaly_score", out var anomalyObj) &&
                double.TryParse(anomalyObj.ToString(), out var anomalyScore))
            {
                threatScore += anomalyScore * 0.4;
            }

            if (context.TryGetValue("recent_failures", out var failuresObj) &&
                int.TryParse(failuresObj.ToString(), out var failures))
            {
                threatScore += Math.Min(failures * 0.1, 0.3);
            }

            return threatScore switch
            {
                >= 0.8 => ThreatLevel.Critical,
                >= 0.6 => ThreatLevel.High,
                >= 0.4 => ThreatLevel.Medium,
                >= 0.2 => ThreatLevel.Low,
                _ => ThreatLevel.None
            };
        }

        private static async Task<List<string>> IdentifyThreatCategoriesAsync(Dictionary<string, object> context)
        {
            var categories = new List<string>();

            if (context.ContainsKey("network_anomaly")) categories.Add("Network");
            if (context.ContainsKey("behavioral_anomaly")) categories.Add("Behavioral");
            if (context.ContainsKey("authentication_failure")) categories.Add("Authentication");
            if (context.ContainsKey("resource_abuse")) categories.Add("Resource");

            return await Task.FromResult(categories);
        }

        private static async Task<TimeSpan> EstimateThreatDurationAsync(Dictionary<string, object> context)
        {
            // 脅威の持続時間を推定（簡易実装）
            return TimeSpan.FromMinutes(30);
        }

        private static async Task<ImpactScope> AssessImpactScopeAsync(Dictionary<string, object> context)
        {
            // 影響範囲を評価（簡易実装）
            return ImpactScope.Local;
        }

        private static async Task<ThreatSituation> EvaluateGlobalThreatSituationAsync()
        {
            // グローバル脅威状況を評価
            return new ThreatSituation
            {
                OverallThreatLevel = ThreatLevel.Low,
                ThreatCategories = new List<string>(),
                ThreatDuration = TimeSpan.FromMinutes(0),
                ImpactScope = ImpactScope.None
            };
        }

        private static string GenerateAdaptationReason(ThreatSituation threatSituation)
        {
            return $"Threat level: {threatSituation.OverallThreatLevel}, Categories: {string.Join(", ", threatSituation.ThreatCategories)}";
        }

        private static AdaptationLevel DetermineAdaptationLevel(ThreatSituation threatSituation, Dictionary<string, object> context)
        {
            var threatScore = (int)threatSituation.OverallThreatLevel;

            if (threatScore >= 4) return AdaptationLevel.Aggressive;
            if (threatScore >= 3) return AdaptationLevel.Moderate;
            if (threatScore >= 2) return AdaptationLevel.Conservative;
            return AdaptationLevel.Minimal;
        }

        private static double CalculateThresholdAdjustment(DynamicPolicy policy, ThreatSituation threatSituation, AdaptationLevel level)
        {
            var baseAdjustment = (int)threatSituation.OverallThreatLevel * 0.05;
            var levelMultiplier = level switch
            {
                AdaptationLevel.Minimal => 0.5,
                AdaptationLevel.Conservative => 0.8,
                AdaptationLevel.Moderate => 1.0,
                AdaptationLevel.Aggressive => 1.5,
                _ => 1.0
            };

            return baseAdjustment * levelMultiplier * policy.AdaptabilityFactor;
        }

        private static double CalculateExpectedImprovement(DynamicPolicy policy, PolicyAdaptation adaptation)
        {
            var thresholdChange = Math.Abs(adaptation.NewThreshold - adaptation.OldThreshold);
            return Math.Min(thresholdChange * 2.0, 1.0); // 最大100%の改善を期待
        }

        private static async Task<double> EvaluateAdaptationEffectivenessAsync(PolicyAdaptation adaptation, Dictionary<string, object> context)
        {
            // 適応の有効性を評価（簡易実装）
            await Task.Delay(10); // シミュレーション
            return 0.7; // 70%の有効性
        }

        private static double CalculateAdaptationEffectiveness(List<PolicyAdaptation> adaptations, ThreatSituation threatSituation)
        {
            if (!adaptations.Any()) return 0;

            var averageEffectiveness = adaptations.Average(a => a.Effectiveness);
            var threatMultiplier = 1.0 + ((int)threatSituation.OverallThreatLevel * 0.1);

            return Math.Min(averageEffectiveness * threatMultiplier, 1.0);
        }
    }

    /// <summary>
    /// 動的ポリシー
    /// </summary>
    public class DynamicPolicy
    {
        public string OperationType { get; set; }
        public double OriginalRiskThreshold { get; set; }
        public double CurrentRiskThreshold { get; set; }
        public double AdaptabilityFactor { get; set; }
        public DateTime LastAdapted { get; set; }
        public int AdaptationCount { get; set; }
    }

    /// <summary>
    /// ポリシー適応結果
    /// </summary>
    public class PolicyAdaptationResult
    {
        public string EvaluationId { get; set; }
        public string OperationType { get; set; }
        public bool Adapted { get; set; }
        public double OriginalRiskThreshold { get; set; }
        public double NewRiskThreshold { get; set; }
        public string AdaptationReason { get; set; }
        public AdaptationLevel AdaptationLevel { get; set; }
        public double ExpectedImprovement { get; set; }
        public TimeSpan EvaluationTime { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// 高度な適応結果
    /// </summary>
    public class AdvancedAdaptationResult
    {
        public string OperationType { get; set; }
        public AdaptationStrategy Strategy { get; set; }
        public List<PolicyAdaptation> Adaptations { get; set; } = new();
        public AdaptationLevel OverallAdaptationLevel { get; set; }
        public double ExpectedEffectiveness { get; set; }
    }

    /// <summary>
    /// 適応戦略
    /// </summary>
    [Flags]
    public enum AdaptationStrategy
    {
        None = 0,
        RiskBased = 1,
        PatternBased = 2,
        ResourceBased = 4,
        Balanced = RiskBased | PatternBased,
        Comprehensive = RiskBased | PatternBased | ResourceBased
    }

    /// <summary>
    /// 適応レベル
    /// </summary>
    public enum AdaptationLevel
    {
        None,
        Minimal,
        Conservative,
        Moderate,
        Aggressive
    }

    /// <summary>
    /// 脅威状況
    /// </summary>
    public class ThreatSituation
    {
        public ThreatLevel OverallThreatLevel { get; set; }
        public List<string> ThreatCategories { get; set; } = new();
        public TimeSpan ThreatDuration { get; set; }
        public ImpactScope ImpactScope { get; set; }
    }

    /// <summary>
    /// 脅威レベル
    /// </summary>
    public enum ThreatLevel
    {
        None,
        Low,
        Medium,
        High,
        Critical
    }

    /// <summary>
    /// 影響範囲
    /// </summary>
    public enum ImpactScope
    {
        None,
        Local,
        Network,
        System,
        Global
    }

    /// <summary>
    /// ポリシー適応
    /// </summary>
    public class PolicyAdaptation
    {
        public string AdaptationId { get; set; }
        public string OperationType { get; set; }
        public DateTime Timestamp { get; set; }
        public double OldThreshold { get; set; }
        public double NewThreshold { get; set; }
        public string Reason { get; set; }
        public AdaptationLevel Level { get; set; }
        public ThreatLevel ThreatLevel { get; set; }
        public bool Success { get; set; }
        public double Effectiveness { get; set; }
        public bool RolledBack { get; set; }
        public DateTime? RollbackTime { get; set; }
    }

    /// <summary>
    /// ポリシー適応統計
    /// </summary>
    public class PolicyAdaptationStatistics
    {
        public TimeSpan TimeWindow { get; set; }
        public int TotalAdaptations { get; set; }
        public int SuccessfulAdaptations { get; set; }
        public int FailedAdaptations { get; set; }
        public double AverageEffectiveness { get; set; }
        public string MostAdaptedOperation { get; set; }
    }
}
