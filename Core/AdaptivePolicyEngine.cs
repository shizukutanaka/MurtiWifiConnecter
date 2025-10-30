using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// 適応型ポリシーエンジン - リアルタイム脅威状況に応じたセキュリティポリシー適応
    /// </summary>
    public class AdaptivePolicyEngine
    {
        private readonly Dictionary<string, AdaptivePolicy> _policies = new();
        private readonly object _lockObject = new();
        private readonly TimeSpan _policyRefreshInterval = TimeSpan.FromMinutes(5);
        private DateTime _lastPolicyRefresh = DateTime.MinValue;

        private readonly DynamicConfigurationManager _configManager = new();

        private readonly int _maxHistorySize = 10000;
        private readonly TimeSpan _learningInterval = TimeSpan.FromMinutes(30);

        public AdaptivePolicyEngine()
        {
            InitializeDefaultPolicies();
            InitializeLearningModels();

            // バッチ処理タイマーの初期化（100msごとにバッチ処理）
            _batchProcessingTimer = new Timer(ProcessEvaluationBatch, null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));

            _learningTimer = new Timer(LearnFromHistory, null, _learningInterval, _learningInterval);

            // テレメトリ収集を開始
            _telemetryCollector.StartCollecting();
            _performanceMonitor.StartMonitoring();
        }

        private void InitializeDefaultPolicies()
        {
            lock (_lockObject)
            {
                // ネットワーク接続ポリシー
                _policies["network_connect"] = new AdaptivePolicy
                {
                    Name = "Network Connection Policy",
                    BaseRiskThreshold = 0.3,
                    AdaptiveRules = new List<AdaptiveRule>
                    {
                        new AdaptiveRule
                        {
                            Condition = "threat_level_high",
                            Action = "require_additional_auth",
                            RiskAdjustment = 0.2
                        },
                        new AdaptiveRule
                        {
                            Condition = "unusual_location",
                            Action = "elevate_monitoring",
                            RiskAdjustment = 0.1
                        },
                        new AdaptiveRule
                        {
                            Condition = "multiple_failures",
                            Action = "temporary_block",
                            RiskAdjustment = 0.5
                        }
                    },
                    LastUpdated = DateTime.UtcNow
                };

                // 認証ポリシー
                _policies["credential_access"] = new AdaptivePolicy
                {
                    Name = "Credential Access Policy",
                    BaseRiskThreshold = 0.2,
                    AdaptiveRules = new List<AdaptiveRule>
                    {
                        new AdaptiveRule
                        {
                            Condition = "suspicious_pattern",
                            Action = "require_mfa",
                            RiskAdjustment = 0.3
                        },
                        new AdaptiveRule
                        {
                            Condition = "credential_rotation_overdue",
                            Action = "force_rotation",
                            RiskAdjustment = 0.4
                        }
                    },
                    LastUpdated = DateTime.UtcNow
                };

                // コマンド実行ポリシー
                _policies["command_execution"] = new AdaptivePolicy
                {
                    Name = "Command Execution Policy",
                    BaseRiskThreshold = 0.4,
                    AdaptiveRules = new List<AdaptiveRule>
                    {
                        new AdaptiveRule
                        {
                            Condition = "privileged_command",
                            Action = "require_justification",
                            RiskAdjustment = 0.15
                        },
                        new AdaptiveRule
                        {
                            Condition = "anomaly_detected",
                            Action = "log_enhanced",
                            RiskAdjustment = 0.25
                        }
                    },
                    LastUpdated = DateTime.UtcNow
                };
        private void InitializeLearningModels()
        {
            // 各操作タイプに対して学習モデルを初期化
            var operations = new[] { "network_connect", "credential_access", "command_execution" };
            foreach (var operation in operations)
            {
                _learningModels[operation] = new LearningModel
                {
                    Operation = operation,
                    Weights = new Dictionary<string, double>
                    {
                        ["threat_level"] = 0.3,
                        ["location_anomaly"] = 0.2,
                        ["recent_failures"] = 0.25,
                        ["pattern_score"] = 0.15,
                        ["time_of_day"] = 0.05,
                        ["device_trust"] = 0.3,
                        ["network_security"] = 0.2
                    },
                    Bias = 0.1,
                    LearningRate = 0.01,
                    LastUpdated = DateTime.UtcNow
                };
            }
        }


        private void LearnFromHistory(object state)
        {
            Task.Run(async () =>
            {
                try
                {
                    // 並行して複数の学習タスクを実行
                    var learningTasks = new[]
                    {
                        UpdateLearningModelsAsync(),
                        UpdateThreatIntelligenceAsync(),
                        OptimizePoliciesAsync()
                    };

                    await Task.WhenAll(learningTasks);
                }
                catch (Exception ex)
                {
                    // 学習中のエラーはログに記録するが、処理を継続
                    Console.WriteLine($"学習処理中にエラーが発生しました: {ex.Message}");
                }
            });
        }

        public async Task<AdaptivePolicyDecision> EvaluatePolicyAsync(string operation, Dictionary<string, object> context, double baseRiskScore)
        {
            await RefreshPoliciesIfNeededAsync();

            // 非同期評価のキューイング
            var tcs = new TaskCompletionSource<AdaptivePolicyDecision>();
            var evaluationId = Guid.NewGuid().ToString();

            _pendingEvaluations[evaluationId] = tcs;
            _evaluationQueue.Enqueue((operation, context, baseRiskScore, tcs));

            // タイムアウト設定（30秒）
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _pendingEvaluations.TryRemove(evaluationId, out _);
                throw new TimeoutException("Policy evaluation timed out");
            }

            return await tcs.Task;
        }

        /// <summary>
        /// 同期版のポリシー評価（既存の互換性維持）
        /// </summary>
        public AdaptivePolicyDecision EvaluatePolicy(string operation, Dictionary<string, object> context, double baseRiskScore)
        {
            return Task.Run(() => EvaluatePolicyAsync(operation, context, baseRiskScore)).GetAwaiter().GetResult();
        }

        /// <summary>
        /// バッチ評価処理
        /// </summary>
        private void ProcessEvaluationBatch(object state)
        {
            var batchSize = Math.Min(_evaluationQueue.Count, Environment.ProcessorCount * 2);
            if (batchSize == 0) return;

            var batch = new List<(string Operation, Dictionary<string, object> Context, double BaseRiskScore, TaskCompletionSource<AdaptivePolicyDecision> Tcs)>();

            for (int i = 0; i < batchSize && _evaluationQueue.TryDequeue(out var item); i++)
            {
                batch.Add(item);
            }

            if (batch.Any())
            {
                Task.Run(() => ProcessBatchAsync(batch));
            }
        }

        /// <summary>
        /// バッチを並行して処理
        /// </summary>
        private async Task ProcessBatchAsync(List<(string Operation, Dictionary<string, object> Context, double BaseRiskScore, TaskCompletionSource<AdaptivePolicyDecision> Tcs)> batch)
        {
            var tasks = batch.Select(async item =>
            {
                await _evaluationSemaphore.WaitAsync();
                try
                {
                    var decision = await EvaluatePolicyInternalAsync(item.Operation, item.Context, item.BaseRiskScore);
                    item.Tcs.SetResult(decision);
                }
                catch (Exception ex)
                {
                    item.Tcs.SetException(ex);
                }
                finally
                {
                    _evaluationSemaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// 内部ポリシー評価メソッド
        /// </summary>
        private async Task<AdaptivePolicyDecision> EvaluatePolicyInternalAsync(string operation, Dictionary<string, object> context, double baseRiskScore)
        {
            var evaluationStart = DateTime.UtcNow;

            try
            {
                // 機械学習ベースのリスク予測（簡略化）
                var mlPrediction = new MLPrediction
                {
                    RiskScore = baseRiskScore, // 基本リスクスコアを使用
                    Confidence = 0.5,
                    ModelPredictions = new List<ModelPrediction>(),
                    Features = new Dictionary<string, double>()
                };

                lock (_lockObject)
                {
                    if (!_policies.TryGetValue(operation, out var policy))
                    {
                        // デフォルトポリシーを使用
                        policy = CreateDefaultPolicy(operation);
                        _policies[operation] = policy;
                    }

                    var decision = new AdaptivePolicyDecision
                    {
                        Operation = operation,
                        BaseRiskScore = baseRiskScore,
                        AdjustedRiskScore = Math.Max(baseRiskScore, mlPrediction.RiskScore),
                        RequiredActions = new List<string>(),
                        PolicyName = policy.Name,
                        Timestamp = DateTime.UtcNow,
                        MLPrediction = mlPrediction
                    };

                    // 適応ルールを評価
                    foreach (var rule in policy.AdaptiveRules)
                    {
                        if (EvaluateCondition(rule.Condition, context))
                        {
                            decision.AdjustedRiskScore += rule.RiskAdjustment;
                            decision.RequiredActions.Add(rule.Action);
                        }
                    }

                    // 脅威インテリジェンスの考慮
                    var threatAdjustment = GetThreatAdjustment(operation, context);
                    decision.AdjustedRiskScore += threatAdjustment;

                    // 最終決定
                    decision.IsAllowed = decision.AdjustedRiskScore <= policy.BaseRiskThreshold;
                    decision.RiskLevel = DetermineRiskLevel(decision.AdjustedRiskScore);

                    // 決定履歴を記録
                    RecordDecisionHistory(decision, context);

                    // テレメトリを記録
                    var evaluationTime = DateTime.UtcNow - evaluationStart;
                    RecordTelemetry(operation, decision, context, evaluationTime);

                    // 異常検知を実行
                    var anomalyResult = _anomalyDetector.DetectAnomaly(operation, decision, context);
                    if (anomalyResult.IsAnomaly)
                    {
                        _telemetryCollector.RecordAnomaly(anomalyResult);
                    }

                    return decision;
                }
            }
            catch (Exception ex)
            {
                // エラーテレメトリを記録
                _telemetryCollector.RecordError(operation, ex, DateTime.UtcNow - evaluationStart);
                throw;
            }
        }

        /// <summary>
        /// テレメトリデータを記録
        /// </summary>
        private void RecordTelemetry(string operation, AdaptivePolicyDecision decision, Dictionary<string, object> context, TimeSpan evaluationTime)
        {
            var telemetryData = new TelemetryData
            {
                Operation = operation,
                Timestamp = decision.Timestamp,
                RiskScore = decision.AdjustedRiskScore,
                IsAllowed = decision.IsAllowed,
                RiskLevel = decision.RiskLevel,
                EvaluationTime = evaluationTime,
                ContextData = context,
                MLPrediction = decision.MLPrediction
            };

            _telemetryCollector.Record(telemetryData);

            // ポリシーメトリクスを更新
            var metrics = _policyMetrics.GetOrAdd(operation, op => new PolicyMetrics { Operation = op });
            metrics.TotalEvaluations++;
            metrics.TotalAllowed += decision.IsAllowed ? 1 : 0;
            metrics.TotalDenied += decision.IsAllowed ? 0 : 1;
            metrics.AverageRiskScore = (metrics.AverageRiskScore * (metrics.TotalEvaluations - 1) + decision.AdjustedRiskScore) / metrics.TotalEvaluations;
            metrics.LastEvaluationTime = decision.Timestamp;
        }

        /// <summary>
        /// テレメトリデータを取得
        /// </summary>
        public TelemetryReport GetTelemetryReport(TimeSpan timeWindow = default)
        {
            if (timeWindow == default)
            {
                timeWindow = TimeSpan.FromHours(1);
            }

            var cutoffTime = DateTime.UtcNow - timeWindow;

            return new TelemetryReport
            {
                TimeWindow = timeWindow,
                TotalEvaluations = _telemetryCollector.GetTotalEvaluations(cutoffTime),
                AverageEvaluationTime = _telemetryCollector.GetAverageEvaluationTime(cutoffTime),
                AnomalyCount = _telemetryCollector.GetAnomalyCount(cutoffTime),
                ErrorCount = _telemetryCollector.GetErrorCount(cutoffTime),
                PolicyMetrics = _policyMetrics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                PerformanceMetrics = _performanceMonitor.GetMetrics(cutoffTime)
            };
        }

        /// <summary>
        /// 動的設定を更新
        /// </summary>
        public async Task<ConfigurationUpdateResult> UpdateConfigurationAsync(string key, object value)
        {
            return await _configManager.UpdateConfigurationAsync(key, value);
        }

        /// <summary>
        /// 複数の設定を一括更新
        /// </summary>
        public async Task<ConfigurationUpdateResult> UpdateConfigurationsAsync(Dictionary<string, object> configurations)
        {
            return await _configManager.UpdateConfigurationsAsync(configurations);
        }

        /// <summary>
        /// 設定をデフォルト値にリセット
        /// </summary>
        public async Task<ConfigurationUpdateResult> ResetConfigurationAsync(string key)
        {
            return await _configManager.ResetConfigurationAsync(key);
        }

        /// <summary>
        /// 現在の設定を取得
        /// </summary>
        public Dictionary<string, object> GetCurrentConfiguration()
        {
            return _configManager.GetCurrentConfiguration();
        }

        /// <summary>
        /// 設定変更履歴を取得
        /// </summary>
        public List<ConfigurationChange> GetConfigurationHistory(DateTime? since = null)
        {
            return _configManager.GetConfigurationHistory(since);
        }

        /// <summary>
        /// 設定変更をロールバック
        /// </summary>
        public async Task<ConfigurationUpdateResult> RollbackConfigurationAsync(string changeId)
        {
            return await _configManager.RollbackConfigurationAsync(changeId);
        }

        /// <summary>
        /// 学習モデルを初期化（簡略化）
        /// </summary>
        private void InitializeLearningModels()
        {
            // 各操作タイプに対して学習モデルを初期化
            var operations = new[] { "network_connect", "credential_access", "command_execution" };
            foreach (var operation in operations)
            {
                _learningModels[operation] = new LearningModel
                {
                    Operation = operation,
                    Weights = new Dictionary<string, double>
                    {
                        ["threat_level"] = 0.3,
                        ["location_anomaly"] = 0.2,
                        ["recent_failures"] = 0.25,
                        ["pattern_score"] = 0.15,
                        ["time_of_day"] = 0.05,
                        ["device_trust"] = 0.3,
                        ["network_security"] = 0.2
                    },
                    Bias = 0.1,
                    LearningRate = 0.01,
                    LastUpdated = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// 特徴抽出（簡略化）
        /// </summary>
        private Dictionary<string, double> ExtractFeatures(Dictionary<string, object> context)
        {
            var features = new Dictionary<string, double>();

            // 基本的な特徴抽出
            if (context.TryGetValue("threat_level", out var threatLevel))
            {
                features["threat_level"] = threatLevel?.ToString() == "high" ? 1.0 :
                                         threatLevel?.ToString() == "medium" ? 0.6 : 0.2;
            }

            if (context.TryGetValue("location_anomaly", out var locationAnomaly))
            {
                features["location_anomaly"] = (bool?)locationAnomaly == true ? 1.0 : 0.0;
            }

            if (context.TryGetValue("recent_failures", out var recentFailures))
            {
                features["recent_failures"] = Math.Min(1.0, (int?)recentFailures ?? 0) / 10.0;
            }

            if (context.TryGetValue("pattern_score", out var patternScore))
            {
                features["pattern_score"] = (double?)patternScore ?? 0.0;
            }

            // 時間ベースの特徴
            var hour = DateTime.UtcNow.Hour;
            features["time_of_day"] = hour >= 22 || hour <= 6 ? 0.8 : 0.2; // 深夜はリスク高い

            // デバイストラストスコア
            features["device_trust"] = context.ContainsKey("known_device") &&
                                     (bool?)context["known_device"] == true ? 0.1 : 0.9;

            // ネットワークセキュリティ
            features["network_security"] = context.ContainsKey("encrypted") &&
                                         (bool?)context["encrypted"] == true ? 0.1 : 0.8;

            return features;
        }

        /// <summary>
        /// 予測計算（線形モデルのみ）
        /// </summary>
        private double CalculatePrediction(LearningModel model, Dictionary<string, double> features)
        {
            double prediction = model.Bias;
            foreach (var feature in features)
            {
                if (model.Weights.TryGetValue(feature.Key, out var weight))
                {
                    prediction += weight * feature.Value;
                }
            }
            return 1.0 / (1.0 + Math.Exp(-prediction)); // シグモイド
        }

        /// <summary>
        /// 信頼度計算
        /// </summary>
        private double CalculateConfidence(Dictionary<string, double> features, LearningModel model)
        {
            // 特徴の多さとモデルの更新頻度に基づいて信頼度を計算
            var featureCount = features.Count;
            var timeSinceUpdate = DateTime.UtcNow - model.LastUpdated;
            var recencyFactor = Math.Max(0.1, 1.0 - (timeSinceUpdate.TotalHours / 24.0));

            return Math.Min(1.0, (featureCount * 0.1) * recencyFactor);
        }

        /// <summary>
        /// 類似ケース検索
        /// </summary>
        private async Task<List<DecisionHistory>> FindSimilarCasesAsync(string operation, Dictionary<string, object> context)
        {
            var similarCases = new List<DecisionHistory>();
            var currentFeatures = ExtractFeatures(context);

            foreach (var history in _decisionHistory)
            {
                if (history.Operation != operation) continue;

                var historyFeatures = ExtractFeatures(history.Context);
                var similarity = CalculateSimilarity(currentFeatures, historyFeatures);

                if (similarity > 0.7) // 70%以上の類似度
                {
                    similarCases.Add(history);
                    if (similarCases.Count >= 5) break; // 最大5件
                }
            }

            return await Task.FromResult(similarCases);
        }

        /// <summary>
        /// 類似度計算
        /// </summary>
        private double CalculateSimilarity(Dictionary<string, double> features1, Dictionary<string, double> features2)
        {
            var allKeys = features1.Keys.Union(features2.Keys);
            double similarity = 0;
            int count = 0;

            foreach (var key in allKeys)
            {
                var val1 = features1.GetValueOrDefault(key, 0);
                var val2 = features2.GetValueOrDefault(key, 0);
                similarity += 1.0 - Math.Abs(val1 - val2); // 逆距離
                count++;
            }

            return count > 0 ? similarity / count : 0;
        }

        /// <summary>
        /// 脅威調整
        /// </summary>
        private double GetThreatAdjustment(string operation, Dictionary<string, object> context)
        {
            double adjustment = 0;

            foreach (var threat in _threatIntelligence.Values)
            {
                // 脅威の種類に応じたリスク調整
                var threatMultiplier = threat.Severity * threat.Confidence;

                switch (threat.ThreatId)
                {
                    case "malware_campaign_active":
                        if (operation.Contains("network") || operation.Contains("connect"))
                            adjustment += threatMultiplier * 0.3;
                        break;
                    case "phishing_attack_detected":
                        if (operation.Contains("credential"))
                            adjustment += threatMultiplier * 0.4;
                        break;
                    case "zero_day_exploit":
                        adjustment += threatMultiplier * 0.2;
                        break;
                    case "ransomware_activity":
                        if (operation.Contains("command"))
                            adjustment += threatMultiplier * 0.5;
                        break;
                }
            }

            return Math.Min(0.5, adjustment); // 最大調整を0.5に制限
        }

        /// <summary>
        /// 決定履歴記録
        /// </summary>
        private void RecordDecisionHistory(AdaptivePolicyDecision decision, Dictionary<string, object> context)
        {
            var history = new DecisionHistory
            {
                Operation = decision.Operation,
                Decision = decision,
                Context = new Dictionary<string, object>(context),
                Timestamp = decision.Timestamp,
                ActualRisk = decision.AdjustedRiskScore,
                WasSuccessful = true // 実際の実装では、結果を後から更新
            };

            _decisionHistory.Enqueue(history);

            // 履歴サイズを制限
            while (_decisionHistory.Count > _maxHistorySize && _decisionHistory.TryDequeue(out _)) { }
        }

        /// <summary>
        /// ポリシー更新確認
        /// </summary>
        private async Task RefreshPoliciesIfNeededAsync()
        {
            var now = DateTime.UtcNow;
            if (now - _lastPolicyRefresh > _policyRefreshInterval)
            {
                await RefreshPoliciesAsync();
                _lastPolicyRefresh = now;
            }
        }

        /// <summary>
        /// ポリシー更新
        /// </summary>
        private async Task RefreshPoliciesAsync()
        {
            // 実際の実装では、外部の脅威インテリジェンスやポリシー管理システムからポリシーを更新
            // ここでは基本的な適応ロジックを実装

            lock (_lockObject)
            {
                foreach (var policy in _policies.Values)
                {
                    // 時間経過による適応 - 機械学習ベースに置き換え
                    var timeSinceUpdate = DateTime.UtcNow - policy.LastUpdated;
                    if (timeSinceUpdate > TimeSpan.FromHours(1))
                    {
                        // ポリシーの適応はOptimizePoliciesAsyncで処理されるため、ここでは最小限の処理
                        policy.LastUpdated = DateTime.UtcNow;
                    }
                }
            }

            await Task.CompletedTask; // 非同期処理のプレースホルダー
        }

        /// <summary>
        /// 条件評価
        /// </summary>
        private bool EvaluateCondition(string condition, Dictionary<string, object> context)
        {
            // 条件評価の簡易実装
            // 実際の実装では、より複雑な条件評価ロジック
            switch (condition)
            {
                case "threat_level_high":
                    return context.ContainsKey("threat_level") &&
                           context["threat_level"]?.ToString() == "high";

                case "unusual_location":
                    return context.ContainsKey("location_anomaly") &&
                           (bool?)context["location_anomaly"] == true;

                case "multiple_failures":
                    return context.ContainsKey("recent_failures") &&
                           (int?)context["recent_failures"] > 3;

                case "suspicious_pattern":
                    return context.ContainsKey("pattern_score") &&
                           (double?)context["pattern_score"] > 0.7;

                case "credential_rotation_overdue":
                    return context.ContainsKey("days_since_rotation") &&
                           (int?)context["days_since_rotation"] > 90;

                case "privileged_command":
                    return context.ContainsKey("is_privileged") &&
                           (bool?)context["is_privileged"] == true;

                case "anomaly_detected":
                    return context.ContainsKey("anomaly_score") &&
                           (double?)context["anomaly_score"] > 0.8;

                default:
                    return false;
            }
        }

        /// <summary>
        /// デフォルトポリシー作成
        /// </summary>
        private AdaptivePolicy CreateDefaultPolicy(string operation)
        {
            return new AdaptivePolicy
            {
                Name = $"{operation} Policy",
                BaseRiskThreshold = 0.3,
                AdaptiveRules = new List<AdaptiveRule>(),
                LastUpdated = DateTime.UtcNow
            };
        }

        /// <summary>
        /// リスクレベル決定
        /// </summary>
        private RiskLevel DetermineRiskLevel(double riskScore)
        {
            if (riskScore >= 0.8) return RiskLevel.Critical;
            if (riskScore >= 0.6) return RiskLevel.High;
            if (riskScore >= 0.4) return RiskLevel.Medium;
            return RiskLevel.Low;
        }

        public void UpdatePolicy(string operation, AdaptivePolicy updatedPolicy)
        {
            lock (_lockObject)
            {
                _policies[operation] = updatedPolicy;
            }
        }

        public void Dispose()
        {
            _learningTimer?.Dispose();
            _batchProcessingTimer?.Dispose();
            _evaluationSemaphore?.Dispose();
        }

        public AdaptivePolicy GetPolicy(string operation)
        {
            lock (_lockObject)
            {
                return _policies.TryGetValue(operation, out var policy) ? policy : null;
            }
        }
    }

    public class AdaptivePolicy
    {
        public string Name { get; set; }
        public double BaseRiskThreshold { get; set; }
        public List<AdaptiveRule> AdaptiveRules { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public class AdaptiveRule
    {
        public string Condition { get; set; }
        public string Action { get; set; }
        public double RiskAdjustment { get; set; }
    }

    public class AdaptivePolicyDecision
    {
        public string Operation { get; set; }
        public double BaseRiskScore { get; set; }
        public double AdjustedRiskScore { get; set; }
        public bool IsAllowed { get; set; }
        public RiskLevel RiskLevel { get; set; }
        public List<string> RequiredActions { get; set; }
        public string PolicyName { get; set; }
        public DateTime Timestamp { get; set; }
        public MLPrediction MLPrediction { get; set; }
    }

    public enum RiskLevel
    {
        Low,
        Medium,
        High,
        Critical
    }

    public class LearningModel
    {
        public string Operation { get; set; }
        public Dictionary<string, double> Weights { get; set; }
        public double Bias { get; set; }
        public double LearningRate { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public class MLPrediction
    {
        public double RiskScore { get; set; }
        public double Confidence { get; set; }
        public Dictionary<string, double> Features { get; set; }
        public List<ModelPrediction> ModelPredictions { get; set; } = new();
    }

    public class ThreatIntelligence
    {
        public string ThreatId { get; set; }
        public double Severity { get; set; }
        public DateTime LastReported { get; set; }
        public double Confidence { get; set; }
    }

    public class DecisionHistory
    {
        public string Operation { get; set; }
        public AdaptivePolicyDecision Decision { get; set; }
        public Dictionary<string, object> Context { get; set; }
        public DateTime Timestamp { get; set; }
        public double ActualRisk { get; set; }
        public bool WasSuccessful { get; set; }
    }

    // 簡略化されたモデル予測クラス
    public class ModelPrediction
    {
            {
                var operation = modelGroup.Key;
                var learningData = modelGroup.Take(100).ToArray();

                // 各モデルの更新を並行して実行
                var modelUpdateTasks = new List<Task>
                {
                    Task.Run(() => UpdateLinearModelAsync(operation, learningData)),
                    Task.Run(() => UpdateXGBoostModelAsync(operation, learningData)),
                    Task.Run(() => UpdateNeuralNetworkModelAsync(operation, learningData)),
                    Task.Run(() => UpdateTimeSeriesModelAsync(operation, learningData)),
                    Task.Run(() => UpdateEnsembleModelAsync(operation, learningData))
                };

                updateTasks.AddRange(modelUpdateTasks);
            }

            await Task.WhenAll(updateTasks);
        }

        private void UpdateLinearModelAsync(string operation, DecisionHistory[] learningData)
        {
            if (_learningModels.TryGetValue(operation, out var model))
            {
                UpdateLinearModel(model, learningData);
            }
        }

        private async Task UpdateXGBoostModelAsync(string operation, DecisionHistory[] learningData)
        {
            if (_xgBoostModels.TryGetValue(operation, out var model))
            {
                await UpdateXGBoostModelAsync(model, learningData);
            }
        }

        private async Task UpdateNeuralNetworkModelAsync(string operation, DecisionHistory[] learningData)
        {
            if (_neuralNetworkModels.TryGetValue(operation, out var model))
            {
                await Task.Run(() => UpdateNeuralNetworkModel(model, learningData));
            }
        }

        private async Task UpdateTimeSeriesModelAsync(string operation, DecisionHistory[] learningData)
        {
            if (_timeSeriesModels.TryGetValue(operation, out var model))
            {
                await UpdateTimeSeriesModelAsync(model, learningData);
            }
        }

        private async Task UpdateEnsembleModelAsync(string operation, DecisionHistory[] learningData)
        {
            if (_ensembleModels.TryGetValue(operation, out var model))
            {
                await Task.Run(() => UpdateEnsembleModel(model, learningData));
            }
        }

        private async Task UpdateThreatIntelligenceAsync()
        {
            // 実際の実装では、外部の脅威インテリジェンスAPIからデータを取得
            // ここではシミュレーションとしてランダムな脅威データを生成

            var threats = new[]
            {
                "malware_campaign_active",
                "phishing_attack_detected",
                "zero_day_exploit",
                "ransomware_activity"
            };

            foreach (var threat in threats)
            {
                _threatIntelligence[threat] = new ThreatIntelligence
                {
                    ThreatId = threat,
                    Severity = new Random().NextDouble(),
                    LastReported = DateTime.UtcNow.AddMinutes(-new Random().Next(60)),
                    Confidence = 0.8 + new Random().NextDouble() * 0.2
                };
            }

            await Task.CompletedTask;
        }

        private async Task OptimizePoliciesAsync()
        {
            lock (_lockObject)
            {
                foreach (var policy in _policies.Values)
                {
                    // 成功率と拒否率に基づいて閾値を最適化
                    var recentDecisions = _decisionHistory
                        .Where(h => h.Operation == policy.Name && (DateTime.UtcNow - h.Timestamp) < TimeSpan.FromHours(1))
                        .Take(50)
                        .ToArray();

                    if (recentDecisions.Length >= 10)
                    {
                        var falsePositives = recentDecisions.Count(d => !d.WasSuccessful && d.Decision.IsAllowed);
                        var falseNegatives = recentDecisions.Count(d => d.WasSuccessful && !d.Decision.IsAllowed);

                        // 誤検知が多い場合、閾値を上げる
                        if (falsePositives > falseNegatives)
                        {
                            policy.BaseRiskThreshold = Math.Min(0.9, policy.BaseRiskThreshold + 0.05);
                        }
                        // 検知漏れが多い場合、閾値を下げる
                        else if (falseNegatives > falsePositives)
                        {
                            policy.BaseRiskThreshold = Math.Max(0.1, policy.BaseRiskThreshold - 0.05);
                        }
                    }
                }
            }

            await Task.CompletedTask;
        }

        private Dictionary<string, double> ExtractFeatures(Dictionary<string, object> context)
        {
            var features = new Dictionary<string, double>();

            // 基本的な特徴抽出
            if (context.TryGetValue("threat_level", out var threatLevel))
            {
                features["threat_level"] = threatLevel?.ToString() == "high" ? 1.0 :
                                         threatLevel?.ToString() == "medium" ? 0.6 : 0.2;
            }

            if (context.TryGetValue("location_anomaly", out var locationAnomaly))
            {
                features["location_anomaly"] = (bool?)locationAnomaly == true ? 1.0 : 0.0;
            }

            if (context.TryGetValue("recent_failures", out var recentFailures))
            {
                features["recent_failures"] = Math.Min(1.0, (int?)recentFailures ?? 0) / 10.0;
            }

            if (context.TryGetValue("pattern_score", out var patternScore))
            {
                features["pattern_score"] = (double?)patternScore ?? 0.0;
            }

            // 時間ベースの特徴
            var hour = DateTime.UtcNow.Hour;
            features["time_of_day"] = hour >= 22 || hour <= 6 ? 0.8 : 0.2; // 深夜はリスク高い

            // デバイストラストスコア
            features["device_trust"] = context.ContainsKey("known_device") &&
                                     (bool?)context["known_device"] == true ? 0.1 : 0.9;

            // ネットワークセキュリティ
            features["network_security"] = context.ContainsKey("encrypted") &&
                                         (bool?)context["encrypted"] == true ? 0.1 : 0.8;

            return features;
        }

        private double CalculatePrediction(LearningModel model, Dictionary<string, double> features)
        {
            double prediction = model.Bias;
            foreach (var feature in features)
            {
                if (model.Weights.TryGetValue(feature.Key, out var weight))
                {
                    prediction += weight * feature.Value;
                }
            }
            return 1.0 / (1.0 + Math.Exp(-prediction)); // シグモイド
        }

        private double CalculateConfidence(Dictionary<string, double> features, LearningModel model)
        {
            // 特徴の多さとモデルの更新頻度に基づいて信頼度を計算
            var featureCount = features.Count;
            var timeSinceUpdate = DateTime.UtcNow - model.LastUpdated;
            var recencyFactor = Math.Max(0.1, 1.0 - (timeSinceUpdate.TotalHours / 24.0));

            return Math.Min(1.0, (featureCount * 0.1) * recencyFactor);
        }

        private async Task<List<DecisionHistory>> FindSimilarCasesAsync(string operation, Dictionary<string, object> context)
        {
            var similarCases = new List<DecisionHistory>();
            var currentFeatures = ExtractFeatures(context);

            foreach (var history in _decisionHistory)
            {
                if (history.Operation != operation) continue;

                var historyFeatures = ExtractFeatures(history.Context);
                var similarity = CalculateSimilarity(currentFeatures, historyFeatures);

                if (similarity > 0.7) // 70%以上の類似度
                {
                    similarCases.Add(history);
                    if (similarCases.Count >= 5) break; // 最大5件
                }
            }

            return await Task.FromResult(similarCases);
        }

        private double CalculateSimilarity(Dictionary<string, double> features1, Dictionary<string, double> features2)
        {
            var allKeys = features1.Keys.Union(features2.Keys);
            double similarity = 0;
            int count = 0;

            foreach (var key in allKeys)
            {
                var val1 = features1.GetValueOrDefault(key, 0);
                var val2 = features2.GetValueOrDefault(key, 0);
                similarity += 1.0 - Math.Abs(val1 - val2); // 逆距離
                count++;
            }

            return count > 0 ? similarity / count : 0;
        }

        private double GetThreatAdjustment(string operation, Dictionary<string, object> context)
        {
            double adjustment = 0;

            foreach (var threat in _threatIntelligence.Values)
            {
                // 脅威の種類に応じたリスク調整
                var threatMultiplier = threat.Severity * threat.Confidence;

                switch (threat.ThreatId)
                {
                    case "malware_campaign_active":
                        if (operation.Contains("network") || operation.Contains("connect"))
                            adjustment += threatMultiplier * 0.3;
                        break;
                    case "phishing_attack_detected":
                        if (operation.Contains("credential"))
                            adjustment += threatMultiplier * 0.4;
                        break;
                    case "zero_day_exploit":
                        adjustment += threatMultiplier * 0.2;
                        break;
                    case "ransomware_activity":
                        if (operation.Contains("command"))
                            adjustment += threatMultiplier * 0.5;
                        break;
                }
            }

            return Math.Min(0.5, adjustment); // 最大調整を0.5に制限
        }

        private void RecordDecisionHistory(AdaptivePolicyDecision decision, Dictionary<string, object> context)
        {
            var history = new DecisionHistory
            {
                Operation = decision.Operation,
                Decision = decision,
                Context = new Dictionary<string, object>(context),
                Timestamp = decision.Timestamp,
                ActualRisk = decision.AdjustedRiskScore,
                WasSuccessful = true // 実際の実装では、結果を後から更新
            };

            _decisionHistory.Enqueue(history);

            // 履歴サイズを制限
            while (_decisionHistory.Count > _maxHistorySize && _decisionHistory.TryDequeue(out _)) { }
        }

        private async Task RefreshPoliciesIfNeededAsync()
        {
            var now = DateTime.UtcNow;
            if (now - _lastPolicyRefresh > _policyRefreshInterval)
            {
                await RefreshPoliciesAsync();
                _lastPolicyRefresh = now;
            }
        }

        private async Task RefreshPoliciesAsync()
        {
            // 実際の実装では、外部の脅威インテリジェンスやポリシー管理システムからポリシーを更新
            // ここでは基本的な適応ロジックを実装

            lock (_lockObject)
            {
                foreach (var policy in _policies.Values)
                {
                    // 時間経過による適応 - 機械学習ベースに置き換え
                    var timeSinceUpdate = DateTime.UtcNow - policy.LastUpdated;
                    if (timeSinceUpdate > TimeSpan.FromHours(1))
                    {
                        // ポリシーの適応はOptimizePoliciesAsyncで処理されるため、ここでは最小限の処理
                        policy.LastUpdated = DateTime.UtcNow;
                    }
                }
            }

            await Task.CompletedTask; // 非同期処理のプレースホルダー
        }

        private bool EvaluateCondition(string condition, Dictionary<string, object> context)
        {
            // 条件評価の簡易実装
            // 実際の実装では、より複雑な条件評価ロジック
            switch (condition)
            {
                case "threat_level_high":
                    return context.ContainsKey("threat_level") &&
                           context["threat_level"]?.ToString() == "high";

                case "unusual_location":
                    return context.ContainsKey("location_anomaly") &&
                           (bool?)context["location_anomaly"] == true;

                case "multiple_failures":
                    return context.ContainsKey("recent_failures") &&
                           (int?)context["recent_failures"] > 3;

                case "suspicious_pattern":
                    return context.ContainsKey("pattern_score") &&
                           (double?)context["pattern_score"] > 0.7;

                case "credential_rotation_overdue":
                    return context.ContainsKey("days_since_rotation") &&
                           (int?)context["days_since_rotation"] > 90;

                case "privileged_command":
                    return context.ContainsKey("is_privileged") &&
                           (bool?)context["is_privileged"] == true;

                case "anomaly_detected":
                    return context.ContainsKey("anomaly_score") &&
                           (double?)context["anomaly_score"] > 0.8;

                default:
                    return false;
            }
        }

        private AdaptivePolicy CreateDefaultPolicy(string operation)
        {
            return new AdaptivePolicy
            {
                Name = $"{operation} Policy",
                BaseRiskThreshold = 0.3,
                AdaptiveRules = new List<AdaptiveRule>(),
                LastUpdated = DateTime.UtcNow
            };
        }

        private RiskLevel DetermineRiskLevel(double riskScore)
        {
            if (riskScore >= 0.8) return RiskLevel.Critical;
            if (riskScore >= 0.6) return RiskLevel.High;
            if (riskScore >= 0.4) return RiskLevel.Medium;
            return RiskLevel.Low;
        }

        public void UpdatePolicy(string operation, AdaptivePolicy updatedPolicy)
        {
            lock (_lockObject)
            {
                _policies[operation] = updatedPolicy;
            }
        }

        public void Dispose()
        {
            _learningTimer?.Dispose();
            _batchProcessingTimer?.Dispose();
            _evaluationSemaphore?.Dispose();
        }

        public AdaptivePolicy GetPolicy(string operation)
        {
            lock (_lockObject)
            {
                return _policies.TryGetValue(operation, out var policy) ? policy : null;
            }
        }
    }

    public class AdaptivePolicy
    {
        public string Name { get; set; }
        public double BaseRiskThreshold { get; set; }
        public List<AdaptiveRule> AdaptiveRules { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public class AdaptiveRule
    {
        public string Condition { get; set; }
        public string Action { get; set; }
        public double RiskAdjustment { get; set; }
    }

    public class AdaptivePolicyDecision
    {
        public string Operation { get; set; }
        public double BaseRiskScore { get; set; }
        public double AdjustedRiskScore { get; set; }
        public bool IsAllowed { get; set; }
        public RiskLevel RiskLevel { get; set; }
        public List<string> RequiredActions { get; set; }
        public string PolicyName { get; set; }
        public DateTime Timestamp { get; set; }
        public MLPrediction MLPrediction { get; set; }
    }

    public enum RiskLevel
    {
        Low,
        Medium,
        High,
        Critical
    }

    public class LearningModel
    {
        public string Operation { get; set; }
        public Dictionary<string, double> Weights { get; set; }
        public double Bias { get; set; }
        public double LearningRate { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public class MLPrediction
    {
        public double RiskScore { get; set; }
        public double Confidence { get; set; }
        public Dictionary<string, double> Features { get; set; }
        public List<ModelPrediction> ModelPredictions { get; set; } = new();
    }

    public class ThreatIntelligence
    {
        public string ThreatId { get; set; }
        public double Severity { get; set; }
        public DateTime LastReported { get; set; }
        public double Confidence { get; set; }
    }

    public class DecisionHistory
    {
        public string Operation { get; set; }
        public AdaptivePolicyDecision Decision { get; set; }
        public Dictionary<string, object> Context { get; set; }
        public DateTime Timestamp { get; set; }
        public double ActualRisk { get; set; }
        public bool WasSuccessful { get; set; }
    }

    // 簡略化されたモデル予測クラス
    public class ModelPrediction
    {
            {
                var operation = modelGroup.Key;
                var learningData = modelGroup.Take(100).ToArray();

                // 各モデルの更新を並行して実行
                var modelUpdateTasks = new List<Task>
                {
                    Task.Run(() => UpdateLinearModelAsync(operation, learningData)),
                    Task.Run(() => UpdateXGBoostModelAsync(operation, learningData)),
                    Task.Run(() => UpdateNeuralNetworkModelAsync(operation, learningData)),
                    Task.Run(() => UpdateTimeSeriesModelAsync(operation, learningData)),
                    Task.Run(() => UpdateEnsembleModelAsync(operation, learningData))
                };

                updateTasks.AddRange(modelUpdateTasks);
            }

            await Task.WhenAll(updateTasks);
        }

        private void UpdateLinearModelAsync(string operation, DecisionHistory[] learningData)
        {
            if (_learningModels.TryGetValue(operation, out var model))
            {
                UpdateLinearModel(model, learningData);
            }
        }

        private async Task UpdateXGBoostModelAsync(string operation, DecisionHistory[] learningData)
        {
            if (_xgBoostModels.TryGetValue(operation, out var model))
            {
                await UpdateXGBoostModelAsync(model, learningData);
            }
        }

        private async Task UpdateNeuralNetworkModelAsync(string operation, DecisionHistory[] learningData)
        {
            if (_neuralNetworkModels.TryGetValue(operation, out var model))
            {
                await Task.Run(() => UpdateNeuralNetworkModel(model, learningData));
            }
        }

        private async Task UpdateTimeSeriesModelAsync(string operation, DecisionHistory[] learningData)
        {
            if (_timeSeriesModels.TryGetValue(operation, out var model))
            {
                await UpdateTimeSeriesModelAsync(model, learningData);
            }
        }

        private async Task UpdateEnsembleModelAsync(string operation, DecisionHistory[] learningData)
        {
            if (_ensembleModels.TryGetValue(operation, out var model))
            {
                await Task.Run(() => UpdateEnsembleModel(model, learningData));
            }
        }

        private async Task UpdateThreatIntelligenceAsync()
        {
            // 実際の実装では、外部の脅威インテリジェンスAPIからデータを取得
            // ここではシミュレーションとしてランダムな脅威データを生成

            var threats = new[]
            {
                "malware_campaign_active",
                "phishing_attack_detected",
                "zero_day_exploit",
                "ransomware_activity"
            };

            foreach (var threat in threats)
            {
                _threatIntelligence[threat] = new ThreatIntelligence
                {
                    ThreatId = threat,
                    Severity = new Random().NextDouble(),
                    LastReported = DateTime.UtcNow.AddMinutes(-new Random().Next(60)),
                    Confidence = 0.8 + new Random().NextDouble() * 0.2
                };
            }

            await Task.CompletedTask;
        }

        private async Task OptimizePoliciesAsync()
        {
            lock (_lockObject)
            {
                foreach (var policy in _policies.Values)
                {
                    // 成功率と拒否率に基づいて閾値を最適化
                    var recentDecisions = _decisionHistory
                        .Where(h => h.Operation == policy.Name && (DateTime.UtcNow - h.Timestamp) < TimeSpan.FromHours(1))
                        .Take(50)
                        .ToArray();

                    if (recentDecisions.Length >= 10)
                    {
                        var falsePositives = recentDecisions.Count(d => !d.WasSuccessful && d.Decision.IsAllowed);
                        var falseNegatives = recentDecisions.Count(d => d.WasSuccessful && !d.Decision.IsAllowed);

                        // 誤検知が多い場合、閾値を上げる
                        if (falsePositives > falseNegatives)
                        {
                            policy.BaseRiskThreshold = Math.Min(0.9, policy.BaseRiskThreshold + 0.05);
                        }
                        // 検知漏れが多い場合、閾値を下げる
                        else if (falseNegatives > falsePositives)
                        {
                            policy.BaseRiskThreshold = Math.Max(0.1, policy.BaseRiskThreshold - 0.05);
                        }
                    }
                }
            }

            await Task.CompletedTask;
        }

        private Dictionary<string, double> ExtractFeatures(Dictionary<string, object> context)
        {
            var features = new Dictionary<string, double>();

            // 基本的な特徴抽出
            if (context.TryGetValue("threat_level", out var threatLevel))
            {
                features["threat_level"] = threatLevel?.ToString() == "high" ? 1.0 :
                                         threatLevel?.ToString() == "medium" ? 0.6 : 0.2;
            }

            if (context.TryGetValue("location_anomaly", out var locationAnomaly))
            {
                features["location_anomaly"] = (bool?)locationAnomaly == true ? 1.0 : 0.0;
            }

            if (context.TryGetValue("recent_failures", out var recentFailures))
            {
                features["recent_failures"] = Math.Min(1.0, (int?)recentFailures ?? 0) / 10.0;
            }

            if (context.TryGetValue("pattern_score", out var patternScore))
            {
                features["pattern_score"] = (double?)patternScore ?? 0.0;
            }

            // 時間ベースの特徴
            var hour = DateTime.UtcNow.Hour;
            features["time_of_day"] = hour >= 22 || hour <= 6 ? 0.8 : 0.2; // 深夜はリスク高い

            // デバイストラストスコア
            features["device_trust"] = context.ContainsKey("known_device") &&
                                     (bool?)context["known_device"] == true ? 0.1 : 0.9;

            // ネットワークセキュリティ
            features["network_security"] = context.ContainsKey("encrypted") &&
                                         (bool?)context["encrypted"] == true ? 0.1 : 0.8;

            return features;
        }

        private double CalculatePrediction(LearningModel model, Dictionary<string, double> features)
        {
            double prediction = model.Bias;
            foreach (var feature in features)
            {
                if (model.Weights.TryGetValue(feature.Key, out var weight))
                {
                    prediction += weight * feature.Value;
                }
            }
            return 1.0 / (1.0 + Math.Exp(-prediction)); // シグモイド
        }

        private double CalculateConfidence(Dictionary<string, double> features, LearningModel model)
        {
            // 特徴の多さとモデルの更新頻度に基づいて信頼度を計算
            var featureCount = features.Count;
            var timeSinceUpdate = DateTime.UtcNow - model.LastUpdated;
            var recencyFactor = Math.Max(0.1, 1.0 - (timeSinceUpdate.TotalHours / 24.0));

            return Math.Min(1.0, (featureCount * 0.1) * recencyFactor);
        }

        private async Task<List<DecisionHistory>> FindSimilarCasesAsync(string operation, Dictionary<string, object> context)
        {
            var similarCases = new List<DecisionHistory>();
            var currentFeatures = ExtractFeatures(context);

            foreach (var history in _decisionHistory)
            {
                if (history.Operation != operation) continue;

                var historyFeatures = ExtractFeatures(history.Context);
                var similarity = CalculateSimilarity(currentFeatures, historyFeatures);

                if (similarity > 0.7) // 70%以上の類似度
                {
                    similarCases.Add(history);
                    if (similarCases.Count >= 5) break; // 最大5件
                }
            }

            return await Task.FromResult(similarCases);
        }

        private double CalculateSimilarity(Dictionary<string, double> features1, Dictionary<string, double> features2)
        {
            var allKeys = features1.Keys.Union(features2.Keys);
            double similarity = 0;
            int count = 0;

            foreach (var key in allKeys)
            {
                var val1 = features1.GetValueOrDefault(key, 0);
                var val2 = features2.GetValueOrDefault(key, 0);
                similarity += 1.0 - Math.Abs(val1 - val2); // 逆距離
                count++;
            }

            return count > 0 ? similarity / count : 0;
        }

        private double GetThreatAdjustment(string operation, Dictionary<string, object> context)
        {
            double adjustment = 0;

            foreach (var threat in _threatIntelligence.Values)
            {
                // 脅威の種類に応じたリスク調整
                var threatMultiplier = threat.Severity * threat.Confidence;

                switch (threat.ThreatId)
                {
                    case "malware_campaign_active":
                        if (operation.Contains("network") || operation.Contains("connect"))
                            adjustment += threatMultiplier * 0.3;
                        break;
                    case "phishing_attack_detected":
                        if (operation.Contains("credential"))
                            adjustment += threatMultiplier * 0.4;
                        break;
                    case "zero_day_exploit":
                        adjustment += threatMultiplier * 0.2;
                        break;
                    case "ransomware_activity":
                        if (operation.Contains("command"))
                            adjustment += threatMultiplier * 0.5;
                        break;
                }
            }

            return Math.Min(0.5, adjustment); // 最大調整を0.5に制限
        }

        private void RecordDecisionHistory(AdaptivePolicyDecision decision, Dictionary<string, object> context)
        {
            var history = new DecisionHistory
            {
                Operation = decision.Operation,
                Decision = decision,
                Context = new Dictionary<string, object>(context),
                Timestamp = decision.Timestamp,
                ActualRisk = decision.AdjustedRiskScore,
                WasSuccessful = true // 実際の実装では、結果を後から更新
            };

            _decisionHistory.Enqueue(history);

            // 履歴サイズを制限
            while (_decisionHistory.Count > _maxHistorySize && _decisionHistory.TryDequeue(out _)) { }
        }

        private async Task RefreshPoliciesIfNeededAsync()
        {
            var now = DateTime.UtcNow;
            if (now - _lastPolicyRefresh > _policyRefreshInterval)
            {
                await RefreshPoliciesAsync();
                _lastPolicyRefresh = now;
            }
        }

        private async Task RefreshPoliciesAsync()
        {
            // 実際の実装では、外部の脅威インテリジェンスやポリシー管理システムからポリシーを更新
            // ここでは基本的な適応ロジックを実装

            lock (_lockObject)
            {
                foreach (var policy in _policies.Values)
                {
                    // 時間経過による適応 - 機械学習ベースに置き換え
                    var timeSinceUpdate = DateTime.UtcNow - policy.LastUpdated;
                    if (timeSinceUpdate > TimeSpan.FromHours(1))
                    {
                        // ポリシーの適応はOptimizePoliciesAsyncで処理されるため、ここでは最小限の処理
                        policy.LastUpdated = DateTime.UtcNow;
                    }
                }
            }

            await Task.CompletedTask; // 非同期処理のプレースホルダー
        }

        private bool EvaluateCondition(string condition, Dictionary<string, object> context)
        {
            // 条件評価の簡易実装
            // 実際の実装では、より複雑な条件評価ロジック
            switch (condition)
            {
                case "threat_level_high":
                    return context.ContainsKey("threat_level") &&
                           context["threat_level"]?.ToString() == "high";

                case "unusual_location":
                    return context.ContainsKey("location_anomaly") &&
                           (bool?)context["location_anomaly"] == true;

                case "multiple_failures":
                    return context.ContainsKey("recent_failures") &&
                           (int?)context["recent_failures"] > 3;

                case "suspicious_pattern":
                    return context.ContainsKey("pattern_score") &&
                           (double?)context["pattern_score"] > 0.7;

                case "credential_rotation_overdue":
                    return context.ContainsKey("days_since_rotation") &&
                           (int?)context["days_since_rotation"] > 90;

                case "privileged_command":
                    return context.ContainsKey("is_privileged") &&
                           (bool?)context["is_privileged"] == true;

                case "anomaly_detected":
                    return context.ContainsKey("anomaly_score") &&
                           (double?)context["anomaly_score"] > 0.8;

                default:
                    return false;
            }
        }

        private AdaptivePolicy CreateDefaultPolicy(string operation)
        {
            return new AdaptivePolicy
            {
                Name = $"{operation} Policy",
                BaseRiskThreshold = 0.3,
                AdaptiveRules = new List<AdaptiveRule>(),
                LastUpdated = DateTime.UtcNow
            };
        }

        private RiskLevel DetermineRiskLevel(double riskScore)
        {
            if (riskScore >= 0.8) return RiskLevel.Critical;
            if (riskScore >= 0.6) return RiskLevel.High;
            if (riskScore >= 0.4) return RiskLevel.Medium;
            return RiskLevel.Low;
        }

        public void UpdatePolicy(string operation, AdaptivePolicy updatedPolicy)
        {
            lock (_lockObject)
            {
                _policies[operation] = updatedPolicy;
            }
        }

        public void Dispose()
        {
            _learningTimer?.Dispose();
            _batchProcessingTimer?.Dispose();
            _evaluationSemaphore?.Dispose();
        }

        public AdaptivePolicy GetPolicy(string operation)
        {
            lock (_lockObject)
            {
                return _policies.TryGetValue(operation, out var policy) ? policy : null;
            }
        }
    }

    public class AdaptivePolicy
    {
        public string Name { get; set; }
        public double BaseRiskThreshold { get; set; }
        public List<AdaptiveRule> AdaptiveRules { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public class AdaptiveRule
    {
        public string Condition { get; set; }
        public string Action { get; set; }
        public double RiskAdjustment { get; set; }
    }

    public class AdaptivePolicyDecision
    {
        public string Operation { get; set; }
        public double BaseRiskScore { get; set; }
        public double AdjustedRiskScore { get; set; }
        public bool IsAllowed { get; set; }
        public RiskLevel RiskLevel { get; set; }
        public List<string> RequiredActions { get; set; }
        public string PolicyName { get; set; }
        public DateTime Timestamp { get; set; }
        public MLPrediction MLPrediction { get; set; }
    }

    public enum RiskLevel
    {
        Low,
        Medium,
        High,
        Critical
    }

    public class LearningModel
    {
        public string Operation { get; set; }
        public Dictionary<string, double> Weights { get; set; }
        public double Bias { get; set; }
        public double LearningRate { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public class MLPrediction
    {
        public double RiskScore { get; set; }
        public double Confidence { get; set; }
        public Dictionary<string, double> Features { get; set; }
        public List<ModelPrediction> ModelPredictions { get; set; } = new();
    }

    public class ThreatIntelligence
    {
        public string ThreatId { get; set; }
        public double Severity { get; set; }
        public DateTime LastReported { get; set; }
        public double Confidence { get; set; }
    }

    public class DecisionHistory
    {
        public string Operation { get; set; }
        public AdaptivePolicyDecision Decision { get; set; }
        public Dictionary<string, object> Context { get; set; }
        public DateTime Timestamp { get; set; }
        public double ActualRisk { get; set; }
        public bool WasSuccessful { get; set; }
    }

    // 簡略化されたモデル予測クラス
    public class ModelPrediction
    {
        public string ModelType { get; set; }
        public double RiskScore { get; set; }
        public double Confidence { get; set; }
    }

    // ヘルパークラス
    public class TelemetryCollector
    {
        private readonly ConcurrentQueue<TelemetryData> _telemetryData = new();
        private readonly ConcurrentQueue<AnomalyResult> _anomalies = new();
        private readonly ConcurrentQueue<ErrorRecord> _errors = new();
        private readonly Timer _cleanupTimer;
        private const int MaxTelemetryItems = 10000;

        public TelemetryCollector()
        {
            _cleanupTimer = new Timer(CleanupOldData, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public void StartCollecting()
        {
            // テレメトリ収集を開始
        }

        public void Record(TelemetryData data)
        {
            _telemetryData.Enqueue(data);

            // 古いデータをクリーンアップ
            while (_telemetryData.Count > MaxTelemetryItems && _telemetryData.TryDequeue(out _)) { }
        }

        public void RecordAnomaly(AnomalyResult anomaly)
        {
            _anomalies.Enqueue(anomaly);
        }

        public void RecordError(string operation, Exception exception, TimeSpan duration)
        {
            _errors.Enqueue(new ErrorRecord
            {
                Operation = operation,
                Exception = exception,
                Timestamp = DateTime.UtcNow,
                Duration = duration
            });
        }

        public int GetTotalEvaluations(DateTime cutoffTime)
        {
            return _telemetryData.Count(data => data.Timestamp >= cutoffTime);
        }

        public TimeSpan GetAverageEvaluationTime(DateTime cutoffTime)
        {
            var relevantData = _telemetryData.Where(data => data.Timestamp >= cutoffTime).ToArray();
            if (!relevantData.Any()) return TimeSpan.Zero;

            return TimeSpan.FromTicks((long)relevantData.Average(data => data.EvaluationTime.Ticks));
        }

        public int GetAnomalyCount(DateTime cutoffTime)
        {
            return _anomalies.Count(anomaly => anomaly.Timestamp >= cutoffTime);
        }

        public int GetErrorCount(DateTime cutoffTime)
        {
            return _errors.Count(error => error.Timestamp >= cutoffTime);
        }

        public List<AnomalyResult> GetRecentAnomalies(int count)
        {
            return _anomalies.Reverse().Take(count).ToList();
        }

        private void CleanupOldData(object state)
        {
            var cutoffTime = DateTime.UtcNow - TimeSpan.FromHours(24);

            // 古いテレメトリデータをクリーンアップ
            while (_telemetryData.TryPeek(out var oldest) && oldest.Timestamp < cutoffTime)
            {
                _telemetryData.TryDequeue(out _);
            }

            // 古い異常データをクリーンアップ
            while (_anomalies.TryPeek(out var oldestAnomaly) && oldestAnomaly.Timestamp < cutoffTime)
            {
                _anomalies.TryDequeue(out _);
            }

            // 古いエラーデータをクリーンアップ
            while (_errors.TryPeek(out var oldestError) && oldestError.Timestamp < cutoffTime)
            {
                _errors.TryDequeue(out _);
            }
        }
    }

    public class PerformanceMonitor
    {
        private readonly ConcurrentQueue<PerformanceMetric> _metrics = new();
        private DateTime _lastHealthCheck = DateTime.MinValue;
        private HealthStatus _currentHealth = HealthStatus.Healthy;

        public void StartMonitoring()
        {
            // パフォーマンス監視を開始
        }

        public void RecordMetric(string operation, TimeSpan duration, long memoryUsage)
        {
            _metrics.Enqueue(new PerformanceMetric
            {
                Operation = operation,
                Timestamp = DateTime.UtcNow,
                Duration = duration,
                MemoryUsage = memoryUsage
            });
        }

        public HealthStatus GetCurrentHealth()
        {
            var now = DateTime.UtcNow;
            if (now - _lastHealthCheck > TimeSpan.FromMinutes(1))
            {
                _currentHealth = CalculateHealthStatus();
                _lastHealthCheck = now;
            }
            return _currentHealth;
        }

        public List<PerformanceMetric> GetMetrics(DateTime cutoffTime)
        {
            return _metrics.Where(m => m.Timestamp >= cutoffTime).ToList();
        }

        private HealthStatus CalculateHealthStatus()
        {
            var recentMetrics = GetMetrics(DateTime.UtcNow - TimeSpan.FromMinutes(5));

            if (!recentMetrics.Any())
                return HealthStatus.Healthy;

            var avgDuration = TimeSpan.FromTicks((long)recentMetrics.Average(m => m.Duration.Ticks));
            var maxMemory = recentMetrics.Max(m => m.MemoryUsage);

            // ヘルス状態を判定
            if (avgDuration > TimeSpan.FromSeconds(5) || maxMemory > 500 * 1024 * 1024)
                return HealthStatus.Critical;
            if (avgDuration > TimeSpan.FromSeconds(2) || maxMemory > 200 * 1024 * 1024)
                return HealthStatus.Warning;

            return HealthStatus.Healthy;
        }
    }

    public class AnomalyDetector
    {
        private readonly ConcurrentDictionary<string, List<double>> _baselineData = new();
        private const double AnomalyThreshold = 2.5; // 標準偏差の2.5倍

        public AnomalyResult DetectAnomaly(string operation, AdaptivePolicyDecision decision, Dictionary<string, object> context)
        {
            var features = ExtractFeatures(context);
            var riskScore = decision.AdjustedRiskScore;

            // ベースラインデータを取得または作成
            var baseline = _baselineData.GetOrAdd(operation, op => new List<double>());

            // 異常検知
            var isAnomaly = false;
            var confidence = 0.0;

            if (baseline.Count >= 10)
            {
                var mean = baseline.Average();
                var stdDev = Math.Sqrt(baseline.Sum(x => Math.Pow(x - mean, 2)) / baseline.Count);
                var zScore = stdDev > 0 ? Math.Abs(riskScore - mean) / stdDev : 0;

                isAnomaly = zScore > AnomalyThreshold;
                confidence = Math.Min(zScore / AnomalyThreshold, 1.0);
            }

            // ベースラインを更新
            baseline.Add(riskScore);
            if (baseline.Count > 100)
            {
                baseline.RemoveAt(0); // 古いデータを削除
            }

            return new AnomalyResult
            {
                Operation = operation,
                Timestamp = DateTime.UtcNow,
                IsAnomaly = isAnomaly,
                Confidence = confidence,
                RiskScore = riskScore,
                BaselineMean = baseline.Count > 0 ? baseline.Average() : 0,
                ContextData = context
            };
        }
    }

    // データクラス
    public class TelemetryData
    {
        public string Operation { get; set; }
        public DateTime Timestamp { get; set; }
        public double RiskScore { get; set; }
        public bool IsAllowed { get; set; }
        public RiskLevel RiskLevel { get; set; }
        public TimeSpan EvaluationTime { get; set; }
        public Dictionary<string, object> ContextData { get; set; }
        public MLPrediction MLPrediction { get; set; }
    }

    public class PolicyMetrics
    {
        public string Operation { get; set; }
        public int TotalEvaluations { get; set; }
        public int TotalAllowed { get; set; }
        public int TotalDenied { get; set; }
        public double AverageRiskScore { get; set; }
        public DateTime LastEvaluationTime { get; set; }
    }

    public class TelemetryReport
    {
        public TimeSpan TimeWindow { get; set; }
        public int TotalEvaluations { get; set; }
        public TimeSpan AverageEvaluationTime { get; set; }
        public int AnomalyCount { get; set; }
        public int ErrorCount { get; set; }
        public Dictionary<string, PolicyMetrics> PolicyMetrics { get; set; }
        public List<PerformanceMetric> PerformanceMetrics { get; set; }
    }

    public class DashboardData
    {
        public int ActiveEvaluations { get; set; }
        public int PendingEvaluations { get; set; }
        public int TotalPolicies { get; set; }
        public int ActiveModels { get; set; }
        public HealthStatus SystemHealth { get; set; }
        public List<AnomalyResult> RecentAnomalies { get; set; }
        public List<PolicyMetrics> PolicyPerformance { get; set; }
    }

    public class AnomalyResult
    {
        public string Operation { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsAnomaly { get; set; }
        public double Confidence { get; set; }
        public double RiskScore { get; set; }
        public double BaselineMean { get; set; }
        public Dictionary<string, object> ContextData { get; set; }
    }

    public class ErrorRecord
    {
        public string Operation { get; set; }
        public Exception Exception { get; set; }
        public DateTime Timestamp { get; set; }
        public TimeSpan Duration { get; set; }
    }

    public class PerformanceMetric
    {
        public string Operation { get; set; }
        public DateTime Timestamp { get; set; }
        public TimeSpan Duration { get; set; }
        public long MemoryUsage { get; set; }
    }

    public enum HealthStatus
    {
        Healthy,
        Warning,
        Critical
    }

    // 高度な機械学習モデルクラス
    public interface IMLModel
    {
        string Operation { get; set; }
        double Predict(Dictionary<string, double> features);
        void Update(Dictionary<string, double> features, double actualRisk);
    }

    public class XGBoostModel : IMLModel
    {
        public string Operation { get; set; }
        public List<DecisionTree> Trees { get; set; } = new();
        public double LearningRate { get; set; }
        public int MaxDepth { get; set; }
        public double MinChildWeight { get; set; }
        public double Lambda { get; set; }
        public double Gamma { get; set; }

        public double Predict(Dictionary<string, double> features)
        {
            double prediction = 0;
            foreach (var tree in Trees)
            {
                prediction += LearningRate * tree.Predict(features);
            }
            return Sigmoid(prediction);
        }

        public void Update(Dictionary<string, double> features, double actualRisk)
        {
            // 簡易XGBoost実装：新しいツリーを追加
            if (Trees.Count < 10) // 最大10本のツリー
            {
                var newTree = new DecisionTree(MaxDepth, MinChildWeight, Lambda, Gamma);
                Trees.Add(newTree);
            }
        }

        private double Sigmoid(double x) => 1.0 / (1.0 + Math.Exp(-x));
    }

    public class DecisionTree
    {
        public DecisionTree(int maxDepth, double minChildWeight, double lambda, double gamma)
        {
            // 簡易決定木実装
        }

        public double Predict(Dictionary<string, double> features)
        {
            // 簡易予測：平均値を使用
            return features.Values.Average();
        }
    }

    public class NeuralNetworkModel : IMLModel
    {
        public string Operation { get; set; }
        public List<NeuralLayer> Layers { get; set; } = new();
        public double LearningRate { get; set; }
        public List<double[,]> Weights { get; set; } = new();

        public double Predict(Dictionary<string, double> features)
        {
            var input = features.Values.ToArray();
            var output = ForwardPropagation(input);
            return output[0]; // 出力層の最初の値
        }

        public void Update(Dictionary<string, double> features, double actualRisk)
        {
            // バックプロパゲーションの実装（簡易版）
            var input = features.Values.ToArray();
            var target = new[] { actualRisk };

            // 順伝播
            var outputs = new List<double[]>();
            outputs.Add(input);

            foreach (var layer in Layers)
            {
                var prevOutput = outputs.Last();
                var layerOutput = new double[layer.Size];
                for (int i = 0; i < layer.Size; i++)
                {
                    layerOutput[i] = Activate(WeightedSum(prevOutput, Weights[Layers.IndexOf(layer)], i), layer.Activation);
                }
                outputs.Add(layerOutput);
            }

            // 逆伝播（簡易実装）
            // 実際の実装ではより複雑なバックプロパゲーションが必要
        }

        private double[] ForwardPropagation(double[] input)
        {
            var current = input;
            foreach (var layer in Layers)
            {
                var next = new double[layer.Size];
                for (int i = 0; i < layer.Size; i++)
                {
                    next[i] = Activate(WeightedSum(current, Weights[Layers.IndexOf(layer)], i), layer.Activation);
                }
                current = next;
            }
            return current;
        }

        private double WeightedSum(double[] input, double[,] weights, int outputIndex)
        {
            double sum = 0;
            for (int i = 0; i < input.Length; i++)
            {
                sum += input[i] * weights[i, outputIndex];
            }
            return sum;
        }

        private double Activate(double x, string activation)
        {
            return activation switch
            {
                "relu" => Math.Max(0, x),
                "sigmoid" => 1.0 / (1.0 + Math.Exp(-x)),
                "tanh" => Math.Tanh(x),
                _ => x
            };
        }
    }

    public class NeuralLayer
    {
        public int Size { get; set; }
        public string Activation { get; set; }
    }

    public class TimeSeriesModel : IMLModel
    {
        public string Operation { get; set; }
        public int SequenceLength { get; set; }
        public int HiddenSize { get; set; }
        public double LearningRate { get; set; }
        public List<double[,]> Weights { get; set; } = new();

        public double Predict(Dictionary<string, double> features)
        {
            // 簡易時系列予測：過去の値の平均を使用
            return features.Values.Average();
        }

        public void Update(Dictionary<string, double> features, double actualRisk)
        {
            // 時系列モデルの更新ロジック
            // 実際の実装ではLSTMやGRUの実装が必要
        }
    }

    public class EnsembleModel
    {
        public string Operation { get; set; }
        public List<IMLModel> Models { get; set; } = new();
        public double[] Weights { get; set; }

        public double Predict(Dictionary<string, double> features)
        {
            double prediction = 0;
            for (int i = 0; i < Models.Count && i < Weights.Length; i++)
            {
                prediction += Weights[i] * Models[i].Predict(features);
            }
            return prediction;
        }
    }

    // ヘルパークラス
    public class ModelPrediction
    {
        public string ModelType { get; set; }
        public double RiskScore { get; set; }
        public double Confidence { get; set; }
    }

    // ヘルパーメソッド
    private static List<double[,]> InitializeNeuralWeights(int inputSize, int[] layerSizes)
    {
        var weights = new List<double[,]>();
        var prevSize = inputSize;

        foreach (var size in layerSizes)
        {
            var weightMatrix = new double[prevSize, size];
            InitializeRandomWeights(weightMatrix);
            weights.Add(weightMatrix);
            prevSize = size;
        }

        return weights;
    }

    private static List<double[,]> InitializeTimeSeriesWeights(int sequenceLength, int hiddenSize, int outputSize)
    {
        var weights = new List<double[,]>();
        // 簡易的な重み初期化
        var inputWeights = new double[sequenceLength, hiddenSize];
        var outputWeights = new double[hiddenSize, outputSize];

        InitializeRandomWeights(inputWeights);
        InitializeRandomWeights(outputWeights);

        weights.Add(inputWeights);
        weights.Add(outputWeights);

        return weights;
    }

    private static void InitializeRandomWeights(double[,] matrix)
    {
        var random = new Random();
        for (int i = 0; i < matrix.GetLength(0); i++)
        {
            for (int j = 0; j < matrix.GetLength(1); j++)
            {
                matrix[i, j] = (random.NextDouble() - 0.5) * 0.1; // -0.05 to 0.05
            }
        }
    }

    private static double PredictWithXGBoost(XGBoostModel model, Dictionary<string, double> features)
    {
        return model.Predict(features);
    }

    private static double PredictWithNeuralNetwork(NeuralNetworkModel model, Dictionary<string, double> features)
    {
        return model.Predict(features);
    }

    private static async Task<double> PredictWithTimeSeriesAsync(TimeSeriesModel model, string operation, Dictionary<string, object> context)
    {
        // 時系列予測の実装
        await Task.Delay(1); // 非同期処理のシミュレーション
        return model.Predict(ExtractFeatures(context));
    }

    private static MLPrediction CalculateEnsemblePrediction(List<ModelPrediction> predictions)
    {
        if (!predictions.Any())
        {
            return new MLPrediction { RiskScore = 0.3, Confidence = 0.5 };
        }

        // 重み付き平均
        double weightedSum = 0;
        double totalWeight = 0;

        foreach (var prediction in predictions)
        {
            var weight = prediction.Confidence;
            weightedSum += prediction.RiskScore * weight;
            totalWeight += weight;
        }

        return new MLPrediction
        {
            RiskScore = Math.Max(0, Math.Min(1, weightedSum / totalWeight)),
            Confidence = totalWeight / predictions.Count
        };
    }

    private static void UpdateLinearModel(LearningModel model, DecisionHistory[] learningData)
    {
        foreach (var data in learningData)
        {
            var features = ExtractFeatures(data.Context);
            var prediction = CalculatePrediction(model, features);
            var error = data.ActualRisk - prediction;

            // 重み更新
            foreach (var feature in features)
            {
                if (model.Weights.ContainsKey(feature.Key))
                {
                    model.Weights[feature.Key] += model.LearningRate * error * feature.Value;
                }
            }

            // バイアス更新
            model.Bias += model.LearningRate * error;
        }

        model.LastUpdated = DateTime.UtcNow;
    }

    private static async Task UpdateXGBoostModelAsync(XGBoostModel model, DecisionHistory[] learningData)
    {
        // XGBoostモデルの更新
        await Task.Run(() =>
        {
            foreach (var data in learningData)
            {
                var features = ExtractFeatures(data.Context);
                model.Update(features, data.ActualRisk);
            }
        });
    }

    private static void UpdateNeuralNetworkModel(NeuralNetworkModel model, DecisionHistory[] learningData)
    {
        foreach (var data in learningData)
        {
            var features = ExtractFeatures(data.Context);
            model.Update(features, data.ActualRisk);
        }
    }

    private static async Task UpdateTimeSeriesModelAsync(TimeSeriesModel model, DecisionHistory[] learningData)
    {
        // 時系列モデルの更新
        await Task.Run(() =>
        {
            foreach (var data in learningData)
            {
                var features = ExtractFeatures(data.Context);
                model.Update(features, data.ActualRisk);
            }
        });
    }

    private static void UpdateEnsembleModel(EnsembleModel model, DecisionHistory[] learningData)
    {
        // アンサンブルモデルの更新
        // 各モデルの予測精度に基づいて重みを調整
        if (learningData.Length > 10)
        {
            var accuracyScores = new double[model.Models.Count];
            for (int i = 0; i < model.Models.Count; i++)
            {
                var modelAccuracy = CalculateModelAccuracy(model.Models[i], learningData);
                accuracyScores[i] = modelAccuracy;
            }

            // 正規化された重みを計算
            var totalAccuracy = accuracyScores.Sum();
            if (totalAccuracy > 0)
            {
                for (int i = 0; i < model.Weights.Length && i < accuracyScores.Length; i++)
                {
                    model.Weights[i] = accuracyScores[i] / totalAccuracy;
                }
            }
        }
    }

    private static double CalculateModelAccuracy(IMLModel model, DecisionHistory[] data)
    {
        if (data.Length == 0) return 0.5;

        double totalError = 0;
        foreach (var item in data)
        {
            var features = ExtractFeatures(item.Context);
            var prediction = model.Predict(features);
            var error = Math.Abs(prediction - item.ActualRisk);
            totalError += error;
        }

        return 1.0 - Math.Min(1.0, totalError / data.Length);
    }
}
            {
                return new ConfigurationUpdateResult
                {
                    Success = false,
                    ErrorMessage = $"Configuration key '{key}' not found"
                };
            }

            return await UpdateConfigurationAsync(key, configItem.DefaultValue);
        }

        public Dictionary<string, object> GetCurrentConfiguration()
        {
            return _configurations.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value);
        }

        public List<ConfigurationChange> GetConfigurationHistory(DateTime? since = null)
        {
            var cutoffTime = since ?? DateTime.UtcNow.AddDays(-7);
            return _changeHistory.Where(change => change.Timestamp >= cutoffTime).ToList();
        }

        public async Task<ConfigurationUpdateResult> RollbackConfigurationAsync(string changeId)
        {
            await _configLock.WaitAsync();
            try
            {
                var change = _changeHistory.FirstOrDefault(c => c.ChangeId == changeId);
                if (change == null)
                {
                    return new ConfigurationUpdateResult
                    {
                        Success = false,
                        ErrorMessage = $"Configuration change '{changeId}' not found"
                    };
                }

                if (!_configurations.TryGetValue(change.Key, out var configItem))
                {
                    return new ConfigurationUpdateResult
                    {
                        Success = false,
                        ErrorMessage = $"Configuration key '{change.Key}' not found"
                    };
                }

                // 値をロールバック
                configItem.Value = change.OldValue;
                configItem.LastModified = DateTime.UtcNow;

                // ロールバック変更を記録
                var rollbackChange = new ConfigurationChange
                {
                    ChangeId = Guid.NewGuid().ToString(),
                    Key = change.Key,
                    OldValue = change.NewValue,
                    NewValue = change.OldValue,
                    Timestamp = DateTime.UtcNow,
                    User = "system",
                    IsRollback = true,
                    OriginalChangeId = changeId
                };

                _changeHistory.Enqueue(rollbackChange);

                await OnConfigurationChangedAsync(change.Key, change.NewValue, change.OldValue);

                return new ConfigurationUpdateResult
                {
                    Success = true,
                    ChangeId = rollbackChange.ChangeId,
                    Message = $"Configuration '{change.Key}' rolled back successfully"
                };
            }
            catch (Exception ex)
            {
                return new ConfigurationUpdateResult
                {
                    Success = false,
                    ErrorMessage = $"Configuration rollback failed: {ex.Message}"
                };
            }
            finally
            {
                _configLock.Release();
            }
        }

        private ConfigurationValidationResult ValidateConfigurationValue(ConfigurationItem config, object value)
        {
            // 型チェック
            if (!config.Type.IsInstanceOfType(value))
            {
                return new ConfigurationValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Invalid type for configuration '{config.Key}'. Expected {config.Type.Name}, got {value.GetType().Name}"
                };
            }

            // バリデーションルールチェック
            foreach (var rule in config.ValidationRules)
            {
                var result = ValidateRule(rule, value);
                if (!result.IsValid)
                {
                    return result;
                }
            }

            return new ConfigurationValidationResult { IsValid = true };
        }

        private ConfigurationValidationResult ValidateRule(ValidationRule rule, object value)
        {
            switch (rule.Type)
            {
                case "range":
                    if (value is int intValue)
                    {
                        if (intValue < rule.Min || intValue > rule.Max)
                        {
                            return new ConfigurationValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = $"Value {intValue} is out of range [{rule.Min}, {rule.Max}]"
                            };
                        }
                    }
                    else if (value is double doubleValue)
                    {
                        if (doubleValue < rule.Min || doubleValue > rule.Max)
                        {
                            return new ConfigurationValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = $"Value {doubleValue} is out of range [{rule.Min}, {rule.Max}]"
                            };
                        }
                    }
                    break;
            }

            return new ConfigurationValidationResult { IsValid = true };
        }

        private async Task OnConfigurationChangedAsync(string key, object oldValue, object newValue)
        {
            // 設定変更時の処理（実際の実装ではイベントを発火したり、依存コンポーネントに通知）
            await Task.CompletedTask;
        }
    }

    // 設定関連のデータクラス
    public class ConfigurationItem
    {
        public string Key { get; set; }
        public object Value { get; set; }
        public object DefaultValue { get; set; }
        public Type Type { get; set; }
        public string Description { get; set; }
        public List<ValidationRule> ValidationRules { get; set; } = new();
        public DateTime? LastModified { get; set; }
    }

    public class ValidationRule
    {
        public string Type { get; set; } // "range", "regex", etc.
        public double Min { get; set; }
        public double Max { get; set; }
        public string Pattern { get; set; }
    }

    public class ConfigurationChange
    {
        public string ChangeId { get; set; }
        public string Key { get; set; }
        public object OldValue { get; set; }
        public object NewValue { get; set; }
        public DateTime Timestamp { get; set; }
        public string User { get; set; }
        public bool IsRollback { get; set; }
        public string OriginalChangeId { get; set; }
    }

    public class ConfigurationUpdateResult
    {
        public bool Success { get; set; }
        public string ChangeId { get; set; }
        public List<string> ChangeIds { get; set; } = new();
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class ConfigurationValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; }
    }
}
}
